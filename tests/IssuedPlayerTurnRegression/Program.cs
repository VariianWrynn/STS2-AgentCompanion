using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Runs;
using Sts2LlmCoop;

internal static class Program
{
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static AiTeammateDummyController Fixture(int round = 1, int turn = 1)
    {
        var controller = new AiTeammateDummyController();
        controller.Player.Creature.CombatState.RoundNumber = round;
        controller.Player.PlayerCombatState.TurnNumber = turn;
        return controller;
    }

    private static async Task<AiActionExecutionResult> Issue(AiTeammateAvailableAction action)
    {
        int before = RunManager.Instance.ActionQueueSynchronizer.Actions.Count;
        var result = await action.ExecuteAsync();
        Require(RunManager.Instance.ActionQueueSynchronizer.Actions.Count == before + 1,
            "The production producer must enqueue exactly one native action.");
        Require(ReferenceEquals(result.GameAction, RunManager.Instance.ActionQueueSynchronizer.Actions.Last()),
            "Settlement must track the action submitted to the native queue.");
        Require(result.GameAction is EndPlayerTurnAction, "Expected a native EndPlayerTurnAction.");
        Require(action.IssuedEndTurn != null && ReferenceEquals(action.IssuedEndTurn, result.IssuedEndTurn),
            "The result must retain the exact immutable identity captured by its producer.");
        return result;
    }

    private static async Task Complete(AiTeammateDummyController controller)
    {
        var offered = controller.DiscoverEndTurn(controller.Player);
        var result = await Issue(offered);
        controller.Track(offered, result);
        result.GameAction!.Finish();
        controller.Settle();
    }

    private static async Task ConstructorUsesNativeTurn(int round, int turn)
    {
        var controller = Fixture(round, turn);
        var result = await Issue(controller.DiscoverEndTurn(controller.Player));
        var native = (EndPlayerTurnAction)result.GameAction!;
        Require(ReferenceEquals(native.Player, controller.Player), "Native action must target the controlled player.");
        Require(native.TurnNumber == turn,
            $"Native EndPlayerTurnAction requires player turn {turn}; production submitted {native.TurnNumber} at global round {round}.");
    }

    private static async Task SameTurnDuplicateIsSuppressed()
    {
        var controller = Fixture();
        await Complete(controller);
        Require(controller.Suppresses(controller.DiscoverEndTurn(controller.Player)),
            "A completed EndTurn must suppress a duplicate in the same combat/player/turn.");
    }

    private static async Task NewPlayerTurnIsAllowed()
    {
        var controller = Fixture();
        await Complete(controller);
        controller.Player.PlayerCombatState.TurnNumber = 2;
        Require(!controller.Suppresses(controller.DiscoverEndTurn(controller.Player)),
            "Player turn 2 must be allowed even when global round remains 1.");
    }

    private static async Task LateCompletionDoesNotOwnNewTurn()
    {
        var controller = Fixture();
        var offered = controller.DiscoverEndTurn(controller.Player);
        var result = await Issue(offered);
        controller.Track(offered, result);
        result.GameAction!.Finish();
        controller.Player.Creature.CombatState.RoundNumber = 2;
        controller.Player.PlayerCombatState.TurnNumber = 2;
        controller.Settle();
        Require(!controller.Suppresses(controller.DiscoverEndTurn(controller.Player)),
            "Observing old completion in round 2 must not suppress the new turn.");
    }

    private static async Task NewCombatWithEqualCountersIsAllowed()
    {
        var controller = Fixture();
        await Complete(controller);
        controller.Player.Creature.CombatState = new CombatState { RoundNumber = 1 };
        Require(!controller.Suppresses(controller.DiscoverEndTurn(controller.Player)),
            "A new combat with the same numeric counters must permit EndTurn.");
    }

    private static async Task OtherPlayerIsAllowed()
    {
        var controller = Fixture();
        await Complete(controller);
        controller.Player.NetId = 3;
        Require(!controller.Suppresses(controller.DiscoverEndTurn(controller.Player)),
            "A different player must not inherit the previous player's marker.");
    }

    private static async Task TemporaryWindowGapPreservesMarker()
    {
        var controller = Fixture();
        await Complete(controller);
        controller.MissingWindow();
        Require(controller.Suppresses(controller.DiscoverEndTurn(controller.Player)),
            "A temporary missing window must preserve suppression for the same identity.");
    }

    private static async Task FailedAttemptDoesNotMark(string outcome)
    {
        var controller = Fixture();
        var offered = controller.DiscoverEndTurn(controller.Player);
        var result = await Issue(offered);
        controller.Track(offered, result);
        if (outcome == "cancel") result.GameAction!.Cancel();
        else if (outcome == "fault") result.GameAction!.Fault();
        else { result.GameAction!.Exception = new InvalidOperationException("native execution fault"); result.GameAction.Finish(); }
        controller.Settle();
        Require(!controller.HasPending, "Known terminal outcomes must release through ordinary settlement.");
        Require(!controller.Suppresses(controller.DiscoverEndTurn(controller.Player)),
            "A canceled, faulted or native-exception attempt must not mark the turn finished.");
    }

    private static async Task StaleOfferDoesNotEnqueue()
    {
        var controller = Fixture();
        var offered = controller.DiscoverEndTurn(controller.Player);
        controller.Player.PlayerCombatState.TurnNumber++;
        int before = RunManager.Instance.ActionQueueSynchronizer.Actions.Count;
        await controller.Execute(offered);
        Require(RunManager.Instance.ActionQueueSynchronizer.Actions.Count == before,
            "A stale offer must enqueue zero native actions.");
        Require(!controller.ExecutionFailed && !controller.HasPending,
            "A stale offer must allow replanning rather than permanent failure or settlement.");
    }

    private static async Task OldAttemptCannotBlockAdvancedTurn()
    {
        var controller = Fixture();
        var offered = controller.DiscoverEndTurn(controller.Player);
        var result = await Issue(offered);
        controller.Track(offered, result);
        controller.Player.PlayerCombatState.TurnNumber++;
        result.GameAction!.Finish();
        controller.Settle();
        var next = controller.DiscoverEndTurn(controller.Player);
        Require(!controller.Suppresses(next), "A normally finished old attempt cannot suppress an advanced native turn.");
        await Issue(next);
    }

    private static async Task OrdinaryNontrackedStillWorks()
    {
        var controller = Fixture();
        await controller.Execute(new AiTeammateAvailableAction(
            new AiLegalActionOption { ActionId = "event_option", ActionType = "ChooseEventOption" },
            () => Task.FromResult(AiActionExecutionResult.Completed), "event:one"));
        Require(!controller.ExecutionFailed && !controller.HasPending && controller.LastDeduplicationKey == "event:one",
            "Ordinary nontracked actions must complete and retain their deduplication behavior.");
    }

    private static async Task InvalidEndTurnCannotMark(bool tagged, bool tracked)
    {
        var controller = Fixture();
        var offered = controller.DiscoverEndTurn(controller.Player);
        var result = tracked ? new AiActionExecutionResult {
            GameAction = new EndPlayerTurnAction(controller.Player, 1), WaitForQueueSettle = true
        } : AiActionExecutionResult.Completed;
        var invalid = tagged
            ? AiTeammateAvailableAction.ForEndTurn(offered.Option, offered.IssuedEndTurn!, () => Task.FromResult(result))
            : new AiTeammateAvailableAction(offered.Option, () => Task.FromResult(result));
        await controller.Execute(invalid);
        Require(controller.ExecutionFailed && !controller.HasPending,
            "An invalid EndTurn contract must fail before pending settlement.");
        Require(!controller.Suppresses(offered), "An invalid EndTurn contract must never establish a marker.");
    }

    private static async Task IdentitySurvivesAwait()
    {
        var controller = Fixture();
        var offered = controller.DiscoverEndTurn(controller.Player);
        var result = await Issue(offered);
        var delayedResult = new TaskCompletionSource<AiActionExecutionResult>();
        var delayed = AiTeammateAvailableAction.ForEndTurn(offered.Option, offered.IssuedEndTurn!, () => delayedResult.Task);
        Task execution = controller.Execute(delayed);
        Require(!execution.IsCompleted && !controller.HasPending, "The fixture must suspend at the real production await.");
        controller.Player.PlayerCombatState.TurnNumber++;
        delayedResult.SetResult(result);
        await execution;
        Require(ReferenceEquals(controller.PendingIdentity, offered.IssuedEndTurn),
            "Pending settlement must retain the original exact identity across await and a turn change.");
        result.GameAction!.Finish();
        controller.Settle();
        Require(!controller.Suppresses(controller.DiscoverEndTurn(controller.Player)),
            "The async old result must not acquire the newer turn's ownership.");
    }

    private static async Task ReconstructedIdentityFailsHandoff()
    {
        var controller = Fixture();
        var offered = controller.DiscoverEndTurn(controller.Player);
        var reconstructed = IssuedPlayerTurn.Capture(controller.Player);
        Require(reconstructed.Equals(offered.IssuedEndTurn), "The fixture must reconstruct an equal identity.");
        var invalid = AiTeammateAvailableAction.ForEndTurn(offered.Option, offered.IssuedEndTurn!,
            () => Task.FromResult(AiActionExecutionResult.ForEndTurn(new EndPlayerTurnAction(controller.Player, 1), reconstructed)));
        await controller.Execute(invalid);
        Require(controller.ExecutionFailed && !controller.HasPending,
            "An equal replacement identity must not pass the exact-object handoff contract.");
    }

    private static async Task<int> Main()
    {
        Console.WriteLine("Production-source regression. Native constructor, engine and queue are explicit stubs; no gameplay claim.");
        (string Name, Func<Task> Run)[] tests = [
            ("normal round 1/player turn 1 constructor", () => ConstructorUsesNativeTurn(1, 1)),
            ("extra round 1/player turn 2 constructor", () => ConstructorUsesNativeTurn(1, 2)),
            ("persistent skew round 2/player turn 3 constructor", () => ConstructorUsesNativeTurn(2, 3)),
            ("same identity duplicate suppression", SameTurnDuplicateIsSuppressed),
            ("new player turn in same round", NewPlayerTurnIsAllowed),
            ("late completion after turn transition", LateCompletionDoesNotOwnNewTurn),
            ("new combat with matching counters", NewCombatWithEqualCountersIsAllowed),
            ("different player identity", OtherPlayerIsAllowed),
            ("temporary missing decision window", TemporaryWindowGapPreservesMarker),
            ("canceled native action", () => FailedAttemptDoesNotMark("cancel")),
            ("faulted completion task", () => FailedAttemptDoesNotMark("fault")),
            ("native exception despite successful Finished task", () => FailedAttemptDoesNotMark("native")),
            ("stale offer enqueues zero and replans", StaleOfferDoesNotEnqueue),
            ("old normally finished attempt after native turn advance", OldAttemptCannotBlockAdvancedTurn),
            ("ordinary nontracked action", OrdinaryNontrackedStillWorks),
            ("exact identity survives production await", IdentitySurvivesAwait),
            ("reconstructed identity fails exact handoff", ReconstructedIdentityFailsHandoff),
            ("tagged untracked EndTurn is invalid", () => InvalidEndTurnCannotMark(true, false)),
            ("untagged tracked EndTurn is invalid", () => InvalidEndTurnCannotMark(false, true)),
        ];
        int failed = 0;
        foreach (var test in tests)
        {
            try { await test.Run(); Console.WriteLine("PASS " + test.Name); }
            catch (Exception error) { failed++; Console.WriteLine("FAIL " + test.Name + ": " + error.Message); }
        }
        Console.WriteLine($"RESULT {tests.Length - failed} passed, {failed} failed");
        return failed == 0 ? 0 : 1;
    }
}

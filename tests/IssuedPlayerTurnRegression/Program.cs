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

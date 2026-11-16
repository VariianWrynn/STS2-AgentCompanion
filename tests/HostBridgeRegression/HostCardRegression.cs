using System.Diagnostics;
using Sts2LlmCoop.HostTest;

internal static class HostCardRegression
{
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
        Console.WriteLine("PASS " + message);
    }

    private sealed class Action : IHostTurnAction
    {
        public bool Completed { get; set; }
        public bool Failed { get; set; }
        internal int Disposals;
        public void Dispose() => Disposals++;
    }

    private sealed class Boundary : IHostTurnBoundary
    {
        internal HostTurnContext? Context = new(new object(), new object(), new object(), new object(), new object(),
            new object(), 3, true, false, true, "initial");
        internal HostCommand.PlayCard[] Offered = [new(new object(), new object(), new object(), new object(), "Play StrikeIronclad hand 1 on enemy 1"),
            new(new object(), null, new object(), new object(), "Play DefendIronclad hand 2 on self")];
        internal Action Action = new();
        internal HostCommand? Issued;
        internal int Calls;
        internal bool Legal = true;
        internal bool InHand = true;
        internal bool Throw;
        public HostTurnContext? Read() => Context;
        public HostCommand.PlayCard[] Cards(HostTurnContext context) => Offered;
        public bool CanPlay(HostTurnContext context, HostCommand.PlayCard card) => Legal;
        public bool InOriginalHand(HostCommand.PlayCard card) => InHand;
        public IHostTurnAction Dispatch(HostTurnContext context, HostCommand command)
        {
            Calls++;
            Issued = command;
            if (Throw) throw new InvalidOperationException("possible effect");
            return Action;
        }
    }

    private static HostSubmission Card(HostChoices offer, long sequence = 1, int index = 0) =>
        new(sequence, offer.Observation, offer.Choices.Where(x => x.Kind == "PlayCard").ElementAt(index).Key);

    internal static async Task Run()
    {
        var boundary = new Boundary();
        using var commands = new HostTurnCommands(boundary);
        var offered = commands.Observe(1);
        Check(offered.Choices.Length == 3 && offered.Choices.Count(x => x.Kind == "EndTurn") == 1 &&
            offered.Choices.Select(x => x.Key).Distinct().Count() == 3 && boundary.Calls == 0,
            "card observations expose opaque distinct card-target choices plus EndTurn without effects");
        var first = boundary.Offered[0];
        Check(commands.Submit(Card(offered)) == "submitted" && ReferenceEquals(boundary.Issued, first),
            "targeted card dispatch retains the exact offered card target holder and original hand command");
        Check(commands.Observe(2).Gate == "pending" && boundary.Action.Disposals == 0,
            "incomplete native card retains ownership and blocks all ordinary choices");
        boundary.Action.Completed = true;
        var noOp = commands.Observe(2);
        Check(noOp.Gate == "pending" && noOp.Diagnostic == "completed_card_in_original_hand" && noOp.Choices.Length == 0,
            "successful native no-op remains blocked with explicit original-hand diagnostic");
        boundary.InHand = false;
        boundary.Context = boundary.Context! with { Eligible = false, InSelection = true };
        Check(commands.Observe(2).Diagnostic == "host_choice_required" && boundary.Action.Disposals == 0,
            "host nested selection keeps native card ownership and requires an explicit future choice command");
        boundary.Context = boundary.Context with { InSelection = false };
        Check(commands.Observe(2).Diagnostic == "awaiting_host_readiness",
            "departed card waits for selection drag and executing-action readiness to settle");
        boundary.Context = boundary.Context with { Eligible = true, StateStamp = "card resolved" };
        offered = commands.Observe(2);
        Check(offered.Gate == "available" && offered.Diagnostic is null && boundary.Action.Disposals == 1 &&
            boundary.Context.Turn == 3, "successful departed card permits a fresh offer in the same native turn");
        boundary.Action = new Action();
        Check(commands.Submit(Card(offered, 2, 1)) == "submitted" && ReferenceEquals(boundary.Issued, boundary.Offered[1]) &&
            ((HostCommand.PlayCard)boundary.Issued).Target is null && boundary.Calls == 2,
            "same-turn chained Self card dispatch preserves its null native target");

        foreach (var change in new[] { "legality", "revision", "selection", "expiry", "refresh" })
        {
            var current = new Boundary();
            long clock = Stopwatch.GetTimestamp();
            using var guarded = new HostTurnCommands(current, () => clock);
            var payload = Card(guarded.Observe(1));
            if (change == "legality") current.Legal = false;
            if (change == "revision") current.Context = current.Context! with { StateStamp = "AI changed" };
            if (change == "selection") current.Context = current.Context! with { InSelection = true, Eligible = false };
            if (change == "expiry") clock += Stopwatch.Frequency * 3;
            if (change == "refresh") guarded.Observe(1);
            Check(guarded.Submit(payload) == "stale" && current.Calls == 0,
                "card " + change + " revalidation denies dispatch and any effect");
        }

        foreach (var replacement in new[] { "combat", "run", "session" })
        {
            var current = new Boundary();
            using var guarded = new HostTurnCommands(current);
            guarded.Submit(Card(guarded.Observe(1)));
            current.Action.Completed = true;
            var original = current.Context!;
            current.Context = null;
            Check(guarded.Observe(2).Gate == "pending" && current.Action.Disposals == 0,
                "card " + replacement + " replacement cannot settle while identity is unknown");
            current.Context = replacement == "combat" ? original with { Combat = new object() } :
                replacement == "run" ? original with { Run = new object() } : original with { Session = new object() };
            Check(guarded.Observe(2).Gate == "available" && current.Action.Disposals == 1 && current.Calls == 1,
                "valid " + replacement + " replacement settles only a completed successful card");
        }

        foreach (var outcome in new[] { "incomplete", "cancelled", "fault", "ambiguous" })
        {
            var current = new Boundary { Throw = outcome == "ambiguous" };
            long clock = Stopwatch.GetTimestamp();
            using var guarded = new HostTurnCommands(current, () => clock);
            guarded.Submit(Card(guarded.Observe(1)));
            current.Action.Completed = outcome != "incomplete";
            current.Action.Failed = outcome is "cancelled" or "fault";
            current.InHand = false;
            clock += Stopwatch.Frequency * 600;
            current.Context = null;
            var blocked = guarded.Observe(2);
            current.Context = new Boundary().Context;
            Check(blocked.Gate == (outcome == "incomplete" ? "pending" : "indeterminate") &&
                guarded.Observe(2).Gate == blocked.Gate && current.Calls == 1,
                "card " + outcome + " never releases on timeout null context or context replacement");
            Check(outcome == "incomplete" || blocked.Diagnostic == (outcome == "ambiguous" ? "enqueue_ambiguous" : "native_failure"),
                "card " + outcome + " exposes the conservative failure diagnostic");
        }

        var replayBoundary = new Boundary();
        using var replayCommands = new HostTurnCommands(replayBoundary);
        using (var mailbox = new HostSubmissionMailbox(replayCommands.Submit))
        {
            var payload = Card(replayCommands.Observe(mailbox.NextSequence));
            var queued = mailbox.SubmitAsync(payload, TimeSpan.FromSeconds(2));
            mailbox.Drain();
            var receipt = await queued;
            replayBoundary.Action.Completed = true;
            replayBoundary.InHand = false;
            replayCommands.Observe(mailbox.NextSequence);
            Check(ReferenceEquals(receipt, await mailbox.SubmitAsync(payload, TimeSpan.FromSeconds(1))) && replayBoundary.Calls == 1,
                "card identical sequence replay survives settlement and newer observations without duplicate play");
        }

        var bounded = new Boundary { Offered = Enumerable.Range(0, 300).Select(_ => first).ToArray() };
        using var boundedCommands = new HostTurnCommands(bounded);
        Check(boundedCommands.Observe(1).Choices.Length == 256 && bounded.Calls == 0,
            "host choices cap at one EndTurn plus 255 card-target pairs without effects");
        var disposed = new Boundary();
        var disposableCommands = new HostTurnCommands(disposed);
        disposableCommands.Submit(Card(disposableCommands.Observe(1)));
        disposableCommands.Dispose();
        disposableCommands.Dispose();
        Check(disposed.Action.Disposals == 1, "command shutdown releases pending native tracker exactly once");
    }
}

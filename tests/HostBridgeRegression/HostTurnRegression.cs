using System.Diagnostics;
using Sts2LlmCoop.HostTest;

internal static class HostTurnRegression
{
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
        Console.WriteLine("PASS " + message);
    }

    private sealed class NativeAction : IHostTurnAction
    {
        public bool Completed { get; set; }
        public bool Failed { get; set; }
    }

    private sealed class NativeBoundary : IHostTurnBoundary
    {
        internal HostTurnContext? Context = new(new object(), new object(), new object(), new object(), new object(), new object(),
            17, true, false, true, "host and AI revision 1");
        internal NativeAction Action = new();
        internal int Calls;
        internal int? IssuedPlayerTurn;
        internal object? IssuedActor;
        internal bool Throw;
        public HostTurnContext? Read() => Context;
        public IHostTurnAction EndTurn(HostTurnContext context)
        {
            Calls++;
            IssuedPlayerTurn = context.Turn;
            IssuedActor = context.Host;
            if (Throw) throw new InvalidOperationException("private native failure after possible effect");
            return Action;
        }
    }

    private static HostSubmission Payload(HostChoices choices, long sequence = 1) =>
        new(sequence, choices.Observation, choices.Choices.Single().Key);

    internal static async Task Run()
    {
        var readiness = new HostTurnReadiness(true, true, true, true, false, false, true, true, true, true, false, false, true);
        Check(readiness.Allows, "native EndTurn readiness admits an alive local play window");
        HostTurnReadiness[] denied = [readiness with { Alive = false }, readiness with { Combat = false },
            readiness with { Play = false }, readiness with { CorrectSide = false }, readiness with { Ready = true },
            readiness with { ActionsDisabled = true }, readiness with { ActiveRoom = false },
            readiness with { CurrentScreen = false }, readiness with { HandExists = false },
            readiness with { HandPlay = false }, readiness with { InCardPlay = true },
            readiness with { InSelection = true }, readiness with { ExtraTurnAllowed = false }];
        Check(denied.All(x => !x.Allows), "dead phase side ready disabled scene modal hand selection and extra-turn guards all deny");

        var boundary = new NativeBoundary();
        var commands = new HostTurnCommands(boundary);
        var first = commands.Observe(1);
        Check(boundary.Calls == 0 && first.Choices.Single().Kind == "EndTurn", "observation offers exactly native EndTurn without dispatch");
        var oldPayload = Payload(first);
        var fresh = commands.Observe(1);
        Check(commands.Submit(oldPayload) == "stale" && boundary.Calls == 0, "fresh observation invalidates older choice");
        var issuedHost = boundary.Context!.Host;
        Check(commands.Submit(Payload(fresh)) == "submitted" && boundary.Calls == 1 && boundary.IssuedPlayerTurn == 17 &&
            ReferenceEquals(boundary.IssuedActor, issuedHost), "dispatch uses the observed authoritative host and player turn 17");
        Check(commands.Submit(Payload(fresh)) == "stale" && boundary.Calls == 1, "dispatch consumes the offered choice");
        Check(commands.Observe(2).Gate == "pending", "native action remains gated while completion is pending");
        boundary.Action.Completed = true;
        Check(commands.Observe(2).Gate == "pending", "native completion alone does not imply readiness or whole-turn settlement");
        boundary.Context = boundary.Context with { Ready = true, Eligible = false };
        Check(commands.Observe(2).Gate == "unavailable", "completed native action plus observed readiness clears pending gate");
        boundary.Context = boundary.Context with { Ready = false, Turn = 18 };
        Check(commands.Observe(2).Choices.Length == 0, "ineligible ready transition does not manufacture an action");

        (string Name, Func<HostTurnContext, HostTurnContext?> Change)[] changes = [("identity denial", _ => null),
            ("session replacement", x => x with { Session = new object() }),
            ("run replacement", x => x with { Run = new object() }),
            ("host replacement", x => x with { Host = new object() }),
            ("combat replacement", x => x with { Combat = new object() }),
            ("hand replacement", x => x with { Hand = new object() }),
            ("scene replacement", x => x with { Scene = new object() }),
            ("player-turn change", x => x with { Turn = 99 }),
            ("readiness denial", x => x with { Eligible = false }),
            ("shared AI progress", x => x with { StateStamp = "AI progressed" })];
        foreach (var change in changes)
        {
            boundary = new NativeBoundary();
            commands = new HostTurnCommands(boundary);
            var payload = Payload(commands.Observe(1));
            boundary.Context = change.Change(boundary.Context!);
            Check(commands.Submit(payload) == "stale" && boundary.Calls == 0,
                change.Name + " invalidates the offered choice before native dispatch");
        }

        VerifyCombatSettlement();

        long clock = Stopwatch.GetTimestamp();
        boundary = new NativeBoundary();
        commands = new HostTurnCommands(boundary, () => clock);
        var expiring = Payload(commands.Observe(1));
        clock += Stopwatch.Frequency * 3;
        Check(commands.Submit(expiring) == "stale" && boundary.Calls == 0, "monotonic offer deadline rejects stale native choice");
        boundary.Throw = true;
        var throwing = Payload(commands.Observe(1));
        Check(commands.Submit(throwing) == "indeterminate" && commands.Observe(2).Gate == "indeterminate" && boundary.Calls == 1,
            "possible native effect followed by exception blocks future host mutations and preserves reads");

        boundary = new NativeBoundary();
        commands = new HostTurnCommands(boundary);
        commands.Submit(Payload(commands.Observe(1)));
        boundary.Action.Failed = true;
        Check(commands.Observe(2).Gate == "indeterminate", "native cancellation or failure blocks uncertain follow-up");

        boundary = new NativeBoundary();
        commands = new HostTurnCommands(boundary);
        using (var mailbox = new HostSubmissionMailbox(commands.Submit))
        {
            var payload = Payload(commands.Observe(mailbox.NextSequence));
            var admitted = mailbox.SubmitAsync(payload, TimeSpan.FromSeconds(2));
            mailbox.Drain();
            var receipt = await admitted;
            commands.Observe(mailbox.NextSequence);
            boundary.Action.Completed = true;
            foreach (var replacement in new[] { "combat", "run", "session" })
            {
                if (replacement == "combat") boundary.Context = boundary.Context! with { Combat = new object() };
                if (replacement == "run") boundary.Context = boundary.Context! with { Run = new object() };
                if (replacement == "session") boundary.Context = boundary.Context! with { Session = new object() };
                Check(commands.Observe(mailbox.NextSequence).Gate == "available", replacement + " replacement offers after completed action");
                Check(ReferenceEquals(receipt, await mailbox.SubmitAsync(payload, TimeSpan.FromSeconds(1))) && boundary.Calls == 1,
                    "original receipt survives newer observations and " + replacement + " replacement before freshness checks");
                Check(mailbox.NextSequence == 2, replacement + " replacement preserves process high-water sequence");
            }
        }

        var payloadTemplate = new HostSubmission(1, new string('a', 32), new string('b', 32));
        int dispatches = 0;
        using (var mailbox = new HostSubmissionMailbox(_ => { dispatches++; return "submitted"; }))
        {
            var admitted = mailbox.SubmitAsync(payloadTemplate, TimeSpan.FromSeconds(2));
            Check((await mailbox.SubmitAsync(payloadTemplate with { Sequence = 2 }, TimeSpan.FromSeconds(1))).Status == "busy",
                "single driver admission returns Busy without consuming the next sequence");
            Check((await mailbox.SubmitAsync(payloadTemplate with { Choice = new string('c', 32) }, TimeSpan.FromSeconds(1))).Status == "conflict",
                "conflicting latest sequence cannot dispatch");
            mailbox.Drain();
            var receipt = await admitted;
            Check(ReferenceEquals(receipt, await mailbox.SubmitAsync(payloadTemplate, TimeSpan.FromSeconds(1))) && dispatches == 1,
                "identical latest retry returns the original immutable receipt before freshness checks");
            Check((await mailbox.SubmitAsync(payloadTemplate with { Sequence = 3 }, TimeSpan.FromSeconds(1))).Status == "sequence_rejected",
                "sequence gaps reject without consuming sequence");
            for (long sequence = 2; sequence <= 24; sequence++)
            {
                var task = mailbox.SubmitAsync(payloadTemplate with { Sequence = sequence }, TimeSpan.FromSeconds(2));
                mailbox.Drain();
                if ((await task).Status != "submitted") throw new InvalidOperationException("Ordered submission was lost.");
            }
            Check(dispatches == 24, "bounded latest receipt supports more than ten ordered submissions without loss");
            Check(mailbox.NextSequence == 25 && dispatches == 24 &&
                (await mailbox.SubmitAsync(payloadTemplate, TimeSpan.FromSeconds(1))).Status == "sequence_rejected",
                "high-water replay protection rejects older sequences without retaining an unbounded history");
        }

        dispatches = 0;
        using (var mailbox = new HostSubmissionMailbox(_ => { dispatches++; return "submitted"; }))
        {
            var expired = await mailbox.SubmitAsync(payloadTemplate, TimeSpan.FromMilliseconds(30));
            mailbox.Drain();
            Check(expired.Status == "expired" && dispatches == 0 && mailbox.NextSequence == 2,
                "no-frame timeout consumes sequence and guarantees no late native dispatch");
            Check(ReferenceEquals(expired, await mailbox.SubmitAsync(payloadTemplate, TimeSpan.FromSeconds(1))),
                "expired duplicate retains its original terminal receipt");
        }

        using (var entered = new ManualResetEventSlim())
        using (var release = new ManualResetEventSlim())
        using (var mailbox = new HostSubmissionMailbox(_ => { entered.Set(); release.Wait(); dispatches++; return "submitted"; }))
        {
            var admitted = mailbox.SubmitAsync(payloadTemplate, TimeSpan.FromMilliseconds(100));
            var drain = Task.Run(mailbox.Drain);
            Check(entered.Wait(TimeSpan.FromSeconds(2)), "native submission work atomically claims before caller timeout");
            Check((await admitted).Status == "in_flight", "claimed caller timeout returns explicit in-flight uncertainty");
            Check((await mailbox.SubmitAsync(payloadTemplate, TimeSpan.FromMilliseconds(30))).Status == "in_flight",
                "identical retry during native work never claims a second dispatch");
            release.Set();
            await drain;
            Check((await mailbox.SubmitAsync(payloadTemplate, TimeSpan.FromSeconds(1))).Status == "submitted" && dispatches == 1,
                "in-flight retry resolves to original result after one native dispatch");
        }

        using (var mailbox = new HostSubmissionMailbox(_ => "submitted"))
        {
            Check((await mailbox.SubmitAsync(payloadTemplate with { Observation = "bad" }, TimeSpan.FromSeconds(1))).Status == "malformed" &&
                mailbox.NextSequence == 1, "malformed domain input cannot consume a sequence");
            var queued = mailbox.SubmitAsync(payloadTemplate, TimeSpan.FromSeconds(2));
            mailbox.Dispose();
            mailbox.Drain();
            Check((await queued).Status == "shutdown", "shutdown expires queued commands without late execution");
        }

        using (var entered = new ManualResetEventSlim())
        using (var release = new ManualResetEventSlim())
        using (var mailbox = new HostSubmissionMailbox(_ => { entered.Set(); release.Wait(); return "submitted"; }))
        {
            var claimed = mailbox.SubmitAsync(payloadTemplate, TimeSpan.FromSeconds(2));
            var drain = Task.Run(mailbox.Drain);
            Check(entered.Wait(TimeSpan.FromSeconds(2)), "shutdown test owns a claimed native request");
            mailbox.Dispose();
            var receipt = await claimed;
            release.Set();
            await drain;
            Check(receipt.Status == "indeterminate" &&
                ReferenceEquals(receipt, await mailbox.SubmitAsync(payloadTemplate, TimeSpan.FromSeconds(1))),
                "shutdown cannot report a claimed possible native effect as safely canceled");
        }

        var nativeSource = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../../src/LocalAiTeammate/HostTest/HostTestBridge.cs"));
        Check(nativeSource.Contains("new EndPlayerTurnAction(host, host.PlayerCombatState!.TurnNumber)") &&
            nativeSource.Contains("ActionQueueSynchronizer.RequestEnqueue(action)") && !nativeSource.Contains("new EndPlayerTurnAction(host, host.Creature.CombatState"),
            "native adapter source binds the authoritative player turn to the native synchronized action");
        Check(nativeSource.Contains("var combatState = host.Creature.CombatState;") &&
            nativeSource.Contains("combat is null || combatState is null") &&
            nativeSource.Contains("new HostTurnContext(session, run, host, combatState, hand, room"),
            "native adapter captures authoritative combat identity and denies unknown combat state");
    }

    private static void VerifyCombatSettlement()
    {
        (string Name, Func<HostTurnContext, HostTurnContext> Replace)[] replacements = [
            ("new combat at turn one", x => x with { Combat = new object(), Hand = new object(), Scene = new object() }),
            ("new combat with reused UI", x => x with { Combat = new object() }),
            ("new run", x => x with { Run = new object() }),
            ("new session", x => x with { Session = new object() })];
        foreach (var replacement in replacements)
        {
            var boundary = new NativeBoundary();
            boundary.Context = boundary.Context! with { Turn = 1 };
            var original = boundary.Context;
            var commands = new HostTurnCommands(boundary);
            Check(commands.Submit(Payload(commands.Observe(1))) == "submitted", replacement.Name + " starts with one native submission");
            boundary.Action.Completed = true;
            boundary.Context = null;
            Check(commands.Observe(2).Gate == "pending", replacement.Name + " keeps completed action pending during unknown context");
            boundary.Context = replacement.Replace(original);
            Check(commands.Observe(2).Gate == "available" && commands.Observe(2).Gate == "available" && boundary.Calls == 1,
                replacement.Name + " settles a completed action without dispatch");
        }

        var uiBoundary = new NativeBoundary();
        var uiCommands = new HostTurnCommands(uiBoundary);
        uiCommands.Submit(Payload(uiCommands.Observe(1)));
        uiBoundary.Action.Completed = true;
        uiBoundary.Context = uiBoundary.Context! with { Hand = new object(), Scene = new object(), StateStamp = "UI replaced" };
        Check(uiCommands.Observe(2).Gate == "pending", "UI replacement within the same combat cannot settle a completed action");

        foreach (var settlement in new[] { "phase", "turn" })
        {
            var boundary = new NativeBoundary();
            var commands = new HostTurnCommands(boundary);
            commands.Submit(Payload(commands.Observe(1)));
            boundary.Action.Completed = true;
            boundary.Context = settlement == "phase" ? boundary.Context! with { Play = false, Eligible = false } :
                boundary.Context! with { Turn = boundary.Context!.Turn + 1 };
            Check(commands.Observe(2).Gate == (settlement == "phase" ? "unavailable" : "available"),
                "same combat " + settlement + " transition settles a completed action");
        }

        foreach (var outcome in new[] { "incomplete", "failed", "indeterminate" })
        {
            long clock = Stopwatch.GetTimestamp();
            var boundary = new NativeBoundary { Throw = outcome == "indeterminate" };
            var commands = new HostTurnCommands(boundary, () => clock);
            commands.Submit(Payload(commands.Observe(1)));
            boundary.Action.Completed = outcome != "incomplete";
            boundary.Action.Failed = outcome == "failed";
            var expected = outcome == "incomplete" ? "pending" : "indeterminate";
            var original = boundary.Context!;
            boundary.Context = null;
            clock += Stopwatch.Frequency * 60;
            Check(commands.Observe(2).Gate == expected, outcome + " action stays blocked during unknown context after timeout");
            boundary.Context = original with { Session = new object(), Run = new object(), Combat = new object(), Ready = true };
            Check(commands.Observe(2).Gate == expected && commands.Observe(2).Gate == expected && boundary.Calls == 1,
                outcome + " action stays blocked across authoritative context replacement");
        }
    }
}

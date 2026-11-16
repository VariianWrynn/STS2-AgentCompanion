using System.Diagnostics;

namespace Sts2LlmCoop.HostTest;

internal sealed record HostChoice(string Key, string Kind, string Label);
internal sealed record HostChoices(string Observation, long NextSequence, string Gate, HostChoice[] Choices);
internal sealed record HostSubmission(long Sequence, string Observation, string Choice);
internal sealed record HostReceipt(long Sequence, string Status);

// References in this record stay on the game thread. Equality binds an offer to the exact native context.
internal sealed record HostTurnContext(object Session, object Run, object Host, object Combat, object Hand, object Scene,
    int Turn, bool Play, bool Ready, bool Eligible, string StateStamp)
{
    internal bool Matches(HostTurnContext other) => ReferenceEquals(Session, other.Session) &&
        ReferenceEquals(Run, other.Run) && ReferenceEquals(Host, other.Host) && ReferenceEquals(Combat, other.Combat) &&
        ReferenceEquals(Hand, other.Hand) &&
        ReferenceEquals(Scene, other.Scene) && Turn == other.Turn && Play == other.Play && Ready == other.Ready &&
        Eligible == other.Eligible && StateStamp == other.StateStamp;
}

internal interface IHostTurnAction
{
    bool Completed { get; }
    bool Failed { get; }
}

internal interface IHostTurnBoundary
{
    HostTurnContext? Read();
    IHostTurnAction EndTurn(HostTurnContext context);
}

internal sealed record HostTurnReadiness(bool Alive, bool Combat, bool Play, bool CorrectSide, bool Ready,
    bool ActionsDisabled, bool ActiveRoom, bool CurrentScreen, bool HandExists, bool HandPlay,
    bool InCardPlay, bool InSelection, bool ExtraTurnAllowed)
{
    internal bool Allows => Alive && Combat && Play && CorrectSide && !Ready && !ActionsDisabled &&
        ActiveRoom && CurrentScreen && HandExists && HandPlay && !InCardPlay && !InSelection && ExtraTurnAllowed;
}

internal sealed class HostTurnCommands(IHostTurnBoundary boundary, Func<long>? clock = null)
{
    private readonly Func<long> _clock = clock ?? Stopwatch.GetTimestamp;
    private sealed record Offer(string Token, string Key, HostTurnContext Context, long Deadline);
    private Offer? _offer;
    private (HostTurnContext Context, IHostTurnAction Action)? _pending;
    private bool _indeterminate;

    internal HostChoices Observe(long nextSequence)
    {
        _offer = null;
        var context = boundary.Read();
        RefreshPending(context);
        var gate = _indeterminate ? "indeterminate" : _pending is not null ? "pending" :
            context?.Eligible == true && nextSequence < long.MaxValue ? "available" : "unavailable";
        var token = Guid.NewGuid().ToString("N");
        if (gate != "available") return new HostChoices(token, nextSequence, gate, []);
        var key = Guid.NewGuid().ToString("N");
        _offer = new Offer(token, key, context!, _clock() + Stopwatch.Frequency * 2);
        return new HostChoices(token, nextSequence, gate, [new HostChoice(key, "EndTurn", "End turn")]);
    }

    internal string Submit(HostSubmission submission)
    {
        var offer = _offer;
        if (offer is null || submission.Observation != offer.Token || submission.Choice != offer.Key ||
            _clock() >= offer.Deadline) return "stale";
        _offer = null;
        try
        {
            var context = boundary.Read();
            RefreshPending(context);
            if (_indeterminate) return "indeterminate";
            if (_pending is not null) return "denied";
            if (context is null || !context.Eligible || !context.Matches(offer.Context)) return "stale";
            _pending = (context, boundary.EndTurn(context));
            return "submitted";
        }
        catch
        {
            // Enqueue can throw after a native effect. Only a new test process clears this uncertainty.
            _indeterminate = true;
            return "indeterminate";
        }
    }

    private void RefreshPending(HostTurnContext? current)
    {
        if (_indeterminate || _pending is not { } pending) return;
        if (pending.Action.Failed) { _indeterminate = true; return; }
        if (!pending.Action.Completed || current is null) return;
        if (!ReferenceEquals(current.Session, pending.Context.Session) ||
            !ReferenceEquals(current.Run, pending.Context.Run) ||
            !ReferenceEquals(current.Combat, pending.Context.Combat) ||
            current.Ready || !current.Play || current.Turn != pending.Context.Turn) _pending = null;
    }
}

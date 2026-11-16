using System.Diagnostics;

namespace Sts2LlmCoop.HostTest;

internal sealed record HostChoice(string Key, string Kind, string Label);
internal sealed record HostChoices(string Observation, long NextSequence, string Gate, HostChoice[] Choices, string? Diagnostic = null);
internal sealed record HostSubmission(long Sequence, string Observation, string Choice);
internal sealed record HostReceipt(long Sequence, string Status);

// References in this record stay on the game thread. Equality binds an offer to the exact native context.
internal sealed record HostTurnContext(object Session, object Run, object Host, object Combat, object Hand, object Scene,
    int Turn, bool Play, bool Ready, bool Eligible, string StateStamp, bool InSelection = false)
{
    internal bool Matches(HostTurnContext other) => ReferenceEquals(Session, other.Session) &&
        ReferenceEquals(Run, other.Run) && ReferenceEquals(Host, other.Host) && ReferenceEquals(Combat, other.Combat) &&
        ReferenceEquals(Hand, other.Hand) &&
        ReferenceEquals(Scene, other.Scene) && Turn == other.Turn && Play == other.Play && Ready == other.Ready &&
        Eligible == other.Eligible && StateStamp == other.StateStamp && InSelection == other.InSelection;
}

internal abstract record HostCommand
{
    internal sealed record EndTurn : HostCommand;
    internal sealed record PlayCard(object Card, object? Target, object Holder, object OriginalHand, string Label, uint? TargetCombatId = null) : HostCommand;
}

internal interface IHostTurnAction : IDisposable
{
    bool Completed { get; }
    bool Failed { get; }
}

internal interface IHostTurnBoundary
{
    HostTurnContext? Read();
    HostCommand.PlayCard[] Cards(HostTurnContext context);
    bool CanPlay(HostTurnContext context, HostCommand.PlayCard card);
    bool InOriginalHand(HostCommand.PlayCard card);
    IHostTurnAction Dispatch(HostTurnContext context, HostCommand command);
}

internal sealed record HostTurnReadiness(bool Alive, bool Combat, bool Play, bool CorrectSide, bool Ready,
    bool ActionsDisabled, bool ActiveRoom, bool CurrentScreen, bool HandExists, bool HandPlay,
    bool InCardPlay, bool InSelection, bool ExtraTurnAllowed)
{
    internal bool Allows => Alive && Combat && Play && CorrectSide && !Ready && !ActionsDisabled &&
        ActiveRoom && CurrentScreen && HandExists && HandPlay && !InCardPlay && !InSelection && ExtraTurnAllowed;
}

internal sealed class HostTurnCommands(IHostTurnBoundary boundary, Func<long>? clock = null) : IDisposable
{
    private readonly Func<long> _clock = clock ?? Stopwatch.GetTimestamp;
    private sealed record Offer(string Token, Dictionary<string, HostCommand> Commands, HostTurnContext Context, long Deadline);
    private Offer? _offer;
    private (HostTurnContext Context, HostCommand Command, IHostTurnAction Action)? _pending;
    private bool _indeterminate;
    private string? _diagnostic;

    internal HostChoices Observe(long nextSequence)
    {
        _offer = null;
        var context = boundary.Read();
        RefreshPending(context);
        var gate = _indeterminate ? "indeterminate" : _pending is not null ? "pending" :
            context?.Eligible == true && nextSequence < long.MaxValue ? "available" : "unavailable";
        var token = Guid.NewGuid().ToString("N");
        if (gate != "available") return new HostChoices(token, nextSequence, gate, [], _diagnostic);
        var commands = new Dictionary<string, HostCommand> { [Guid.NewGuid().ToString("N")] = new HostCommand.EndTurn() };
        foreach (var card in boundary.Cards(context!).Take(255))
            commands.Add(Guid.NewGuid().ToString("N"), card);
        _offer = new Offer(token, commands, context!, _clock() + Stopwatch.Frequency * 2);
        return new HostChoices(token, nextSequence, gate, commands.Select(pair => new HostChoice(pair.Key,
            pair.Value is HostCommand.PlayCard ? "PlayCard" : "EndTurn",
            pair.Value is HostCommand.PlayCard play ? play.Label : "End turn")).ToArray());
    }

    internal string Submit(HostSubmission submission)
    {
        var offer = _offer;
        if (offer is null || submission.Observation != offer.Token || !offer.Commands.ContainsKey(submission.Choice) ||
            _clock() >= offer.Deadline) return "stale";
        _offer = null;
        try
        {
            var context = boundary.Read();
            RefreshPending(context);
            if (_indeterminate) return "indeterminate";
            if (_pending is not null) return "denied";
            if (context is null || !context.Eligible || !context.Matches(offer.Context)) return "stale";
            var command = offer.Commands[submission.Choice];
            if (command is HostCommand.PlayCard card && !boundary.CanPlay(context, card)) return "stale";
            _pending = (context, command, boundary.Dispatch(context, command));
            return "submitted";
        }
        catch
        {
            // Enqueue can throw after a native effect. Only a new test process clears this uncertainty.
            _indeterminate = true;
            _diagnostic = "enqueue_ambiguous";
            return "indeterminate";
        }
    }

    public void Dispose()
    {
        _offer = null;
        _pending?.Action.Dispose();
        _pending = null;
    }

    private void RefreshPending(HostTurnContext? current)
    {
        if (_indeterminate || _pending is not { } pending) return;
        if (pending.Action.Failed)
        {
            _indeterminate = true;
            _diagnostic = "native_failure";
            pending.Action.Dispose();
            _pending = null;
            return;
        }
        _diagnostic = current?.InSelection == true ? "host_choice_required" : "native_pending";
        if (!pending.Action.Completed || current is null) return;
        if (!ReferenceEquals(current.Session, pending.Context.Session) ||
            !ReferenceEquals(current.Run, pending.Context.Run) ||
            !ReferenceEquals(current.Combat, pending.Context.Combat) ||
            (pending.Command is HostCommand.EndTurn &&
                (current.Ready || !current.Play || current.Turn != pending.Context.Turn)) ||
            (pending.Command is HostCommand.PlayCard card && current.Eligible && !current.InSelection && !boundary.InOriginalHand(card)))
        {
            pending.Action.Dispose();
            _pending = null;
            _diagnostic = null;
        }
        else if (pending.Command is HostCommand.PlayCard pendingCard)
            _diagnostic = current.InSelection ? "host_choice_required" :
                boundary.InOriginalHand(pendingCard) ? "completed_card_in_original_hand" : "awaiting_host_readiness";
    }
}

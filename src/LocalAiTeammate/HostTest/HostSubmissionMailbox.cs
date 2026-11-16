using System.Diagnostics;

namespace Sts2LlmCoop.HostTest;

internal sealed class HostSubmissionMailbox(Func<HostSubmission, string> dispatch, Func<long>? clock = null) : IDisposable
{
    private enum WorkState { Queued, Claimed, Done, Expired }
    private sealed class Work(HostSubmission payload, long deadline)
    {
        internal readonly HostSubmission Payload = payload;
        internal readonly long Deadline = deadline;
        internal WorkState State;
        internal HostReceipt? Receipt;
        internal readonly TaskCompletionSource<HostReceipt> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private readonly object _gate = new();
    private readonly Func<long> _clock = clock ?? Stopwatch.GetTimestamp;
    private Work? _latest;
    private bool _stopped;
    internal long NextSequence { get { lock (_gate) return (_latest?.Payload.Sequence ?? 0) + 1; } }

    internal Task<HostReceipt> SubmitAsync(HostSubmission payload, TimeSpan timeout)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        Work work;
        lock (_gate)
        {
            if (payload.Sequence <= 0 || payload.Sequence == long.MaxValue || !Token(payload.Observation) || !Token(payload.Choice))
                return Task.FromResult(new HostReceipt(payload.Sequence, "malformed"));
            if (_latest is { } latest && payload.Sequence == latest.Payload.Sequence)
            {
                if (payload != latest.Payload) return Task.FromResult(new HostReceipt(payload.Sequence, "conflict"));
                if (latest.Receipt is not null) return Task.FromResult(latest.Receipt);
                work = latest;
            }
            else
            {
                var next = (_latest?.Payload.Sequence ?? 0) + 1;
                if (payload.Sequence != next) return Task.FromResult(new HostReceipt(payload.Sequence, "sequence_rejected"));
                if (_stopped) return Task.FromResult(new HostReceipt(payload.Sequence, "shutdown"));
                if (_latest?.State is WorkState.Queued or WorkState.Claimed)
                    return Task.FromResult(new HostReceipt(payload.Sequence, "busy"));
                work = new Work(payload, _clock() + (long)(timeout.TotalSeconds * Stopwatch.Frequency));
                _latest = work;
            }
        }
        return WaitAsync(work, timeout);
    }

    internal static bool Token(string? value) => value is { Length: 32 } && value.All(char.IsAsciiHexDigit);

    private async Task<HostReceipt> WaitAsync(Work work, TimeSpan timeout)
    {
        try { return await work.Completion.Task.WaitAsync(timeout).ConfigureAwait(false); }
        catch (TimeoutException)
        {
            lock (_gate)
            {
                if (work.State == WorkState.Queued) Finish(work, "expired", WorkState.Expired);
                return work.Receipt ?? new HostReceipt(work.Payload.Sequence, "in_flight");
            }
        }
    }

    internal void Drain()
    {
        Work work;
        lock (_gate)
        {
            if (_stopped || _latest is not { State: WorkState.Queued } queued) return;
            work = queued;
            if (_clock() >= work.Deadline) { Finish(work, "expired", WorkState.Expired); return; }
            work.State = WorkState.Claimed;
        }
        string status;
        try { status = dispatch(work.Payload); }
        catch { status = "indeterminate"; }
        lock (_gate) { if (work.Receipt is null) Finish(work, status, WorkState.Done); }
    }

    private static void Finish(Work work, string status, WorkState state)
    {
        work.State = state;
        work.Receipt = new HostReceipt(work.Payload.Sequence, status);
        work.Completion.TrySetResult(work.Receipt);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _stopped = true;
            if (_latest is { Receipt: null } work)
                Finish(work, work.State == WorkState.Claimed ? "indeterminate" : "shutdown", WorkState.Done);
        }
    }
}

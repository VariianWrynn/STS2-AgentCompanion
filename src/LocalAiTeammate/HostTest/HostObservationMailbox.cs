using System.Diagnostics;

namespace Sts2LlmCoop.HostTest;

internal sealed record ObservationReply(string Status, object? Observation = null);

internal sealed class HostObservationMailbox : IDisposable
{
    private enum RequestState { Queued, Claimed, Done, Expired }

    private sealed class Request(long deadline)
    {
        internal readonly long Deadline = deadline;
        internal RequestState State;
        internal readonly TaskCompletionSource<ObservationReply> Completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private readonly object _gate = new();
    private readonly Queue<Request> _queue = new();
    private readonly HashSet<Request> _claimed = [];
    private readonly Func<object> _capture;
    private readonly Func<long> _clock;
    private readonly int _capacity;
    private bool _stopped;

    internal HostObservationMailbox(Func<object> capture, int capacity = 64, Func<long>? clock = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _capture = capture;
        _capacity = capacity;
        _clock = clock ?? Stopwatch.GetTimestamp;
    }

    internal Task<ObservationReply> ObserveAsync(TimeSpan timeout)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        Request request;
        lock (_gate)
        {
            if (_stopped) return Task.FromResult(new ObservationReply("shutdown"));
            if (_queue.Count >= _capacity) return Task.FromResult(new ObservationReply("busy"));
            request = new Request(_clock() + (long)(timeout.TotalSeconds * Stopwatch.Frequency));
            _queue.Enqueue(request);
        }
        return WaitAsync(request, timeout);
    }

    private async Task<ObservationReply> WaitAsync(Request request, TimeSpan timeout)
    {
        try { return await request.Completion.Task.WaitAsync(timeout).ConfigureAwait(false); }
        catch (TimeoutException)
        {
            lock (_gate)
            {
                if (request.State == RequestState.Queued)
                {
                    request.State = RequestState.Expired;
                    request.Completion.TrySetResult(new ObservationReply("expired"));
                }
                // Claimed reads cannot be interrupted. The caller still has a bounded wait.
                if (request.Completion.Task.IsCompleted) return request.Completion.Task.GetAwaiter().GetResult();
                return new ObservationReply("timeout");
            }
        }
    }

    internal void Drain(int budget = 10)
    {
        for (int processed = 0; processed < budget; processed++)
        {
            Request request;
            lock (_gate)
            {
                if (_stopped || !_queue.TryDequeue(out request!)) return;
                if (request.State != RequestState.Queued) continue;
                if (_clock() >= request.Deadline)
                {
                    request.State = RequestState.Expired;
                    request.Completion.TrySetResult(new ObservationReply("expired"));
                    continue;
                }
                request.State = RequestState.Claimed;
                _claimed.Add(request);
            }

            ObservationReply reply;
            try { reply = new ObservationReply("ok", _capture()); }
            catch { reply = new ObservationReply("capture_failed"); }
            lock (_gate)
            {
                request.State = RequestState.Done;
                _claimed.Remove(request);
                request.Completion.TrySetResult(reply);
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_stopped) return;
            _stopped = true;
            while (_queue.TryDequeue(out var request))
            {
                if (request.State != RequestState.Queued) continue;
                request.State = RequestState.Expired;
                request.Completion.TrySetResult(new ObservationReply("shutdown"));
            }
            foreach (var request in _claimed)
            {
                request.State = RequestState.Done;
                request.Completion.TrySetResult(new ObservationReply("shutdown"));
            }
            _claimed.Clear();
        }
    }
}

using Sts2LlmCoop.HostTest;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Entities.Actions;

internal static class NativeActionRegression
{
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
        Console.WriteLine("PASS " + message);
    }

    internal static void Run()
    {
        var native = new GameAction();
        int removals = 0;
        var tracked = new NativeAction(native, _ => removals++);
        Check(native.ListenerCount == 1 && !tracked.Completed && !tracked.Failed,
            "production native tracker subscribes before enqueue and starts unresolved");
        native.CancelThenFinish();
        Check(tracked.Completed && tracked.Failed,
            "production native tracker latches cancellation overwritten by native Finished and successful task");
        tracked.Dispose();
        tracked.Dispose();
        Check(native.ListenerCount == 0 && removals == 1,
            "production native tracker removes event and bridge ownership idempotently");
        native = new GameAction { State = GameActionState.Finished };
        using (tracked = new NativeAction(native, _ => { }))
        {
            Check(!tracked.Completed, "native Finished without successful completion task cannot settle a card");
            native.Completion.SetResult();
            Check(tracked.Completed && !tracked.Failed, "native Finished plus successful task settles without failure");
            native.Exception = new InvalidOperationException();
            Check(tracked.Failed, "native execution exception remains failure despite successful completion task");
        }
        foreach (var cancelled in new[] { true, false })
        {
            native = new GameAction { State = GameActionState.Finished };
            using (tracked = new NativeAction(native, _ => { }))
            {
                if (cancelled) native.Completion.SetCanceled();
                else native.Completion.SetException(new InvalidOperationException());
                Check(tracked.Failed && !tracked.Completed, "native completion " + (cancelled ? "cancellation" : "fault") + " is never success");
            }
        }
        native = new GameAction();
        using (tracked = new NativeAction(native, _ => { }))
        {
            native.Finish();
            Check(tracked.Completed && native.ListenerCount == 1, "completion retains cancellation listener until command settlement");
            native.CancelThenFinish();
            Check(tracked.Failed, "cancellation arriving after completion but before settlement stays visible");
        }
        Check(native.ListenerCount == 0, "shutdown releases unresolved or completed tracker subscriptions");
    }
}

namespace MegaCrit.Sts2.Core.Entities.Actions
{
    internal enum GameActionState { None, Finished, Canceled }
}

namespace MegaCrit.Sts2.Core.GameActions
{
    internal sealed class GameAction
    {
        internal TaskCompletionSource Completion = new();
        public Task CompletionTask => Completion.Task;
        public GameActionState State { get; set; }
        public Exception? Exception { get; set; }
        public event Action<GameAction>? BeforeCancelled;
        internal int ListenerCount => BeforeCancelled?.GetInvocationList().Length ?? 0;
        internal void Finish() { State = GameActionState.Finished; Completion.TrySetResult(); }
        internal void CancelThenFinish()
        {
            State = GameActionState.Canceled;
            BeforeCancelled?.Invoke(this);
            Finish();
        }
    }
}

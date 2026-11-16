using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Entities.Actions;

namespace Sts2LlmCoop.HostTest;

internal sealed class NativeAction : IHostTurnAction
{
    private readonly GameAction _action;
    private bool _cancelled;
    private Action<NativeAction>? _onDispose;
    internal NativeAction(GameAction action, Action<NativeAction> onDispose)
    {
        _action = action;
        _onDispose = onDispose;
        action.BeforeCancelled += Cancelled;
    }
    private void Cancelled(GameAction _) => _cancelled = true;
    public bool Completed => _action.CompletionTask.IsCompletedSuccessfully && _action.State == GameActionState.Finished;
    public bool Failed => _cancelled || _action.Exception is not null || _action.CompletionTask.IsFaulted ||
        _action.CompletionTask.IsCanceled || _action.State == GameActionState.Canceled;
    public void Dispose()
    {
        _action.BeforeCancelled -= Cancelled;
        var onDispose = _onDispose;
        _onDispose = null;
        onDispose?.Invoke(this);
    }
}

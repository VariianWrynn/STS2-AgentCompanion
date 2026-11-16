using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;

// Explicit engine substitutes record the native constructor and enqueue boundaries.
// No game, Godot or queue scheduling runs in this regression.
namespace MegaCrit.Sts2.Core.Combat
{
    public sealed class CombatState
    {
        public int RoundNumber { get; set; } = 1;
        // Deliberately equal by value so production must use reference identity.
        public override bool Equals(object? other) => other is CombatState;
        public override int GetHashCode() => 0;
    }
}
namespace MegaCrit.Sts2.Core.Entities.Players
{
    public sealed class Player
    {
        public ulong NetId { get; set; } = 2;
        public Creature Creature { get; } = new();
        public PlayerCombatState PlayerCombatState { get; } = new();
    }
    public sealed class Creature
    {
        public MegaCrit.Sts2.Core.Combat.CombatState CombatState { get; set; } = new();
    }
    public sealed class PlayerCombatState { public int TurnNumber { get; set; } = 1; }
}
namespace MegaCrit.Sts2.Core.GameActions
{
    public enum GameActionState { Running, Finished, Canceled }
    public class GameAction
    {
        private readonly TaskCompletionSource _completion = new();
        public Task CompletionTask => _completion.Task;
        public GameActionState State { get; private set; } = GameActionState.Running;
        public Exception? Exception { get; set; }
        public void Finish() { State = GameActionState.Finished; _completion.SetResult(); }
        public void Cancel() { State = GameActionState.Canceled; _completion.SetCanceled(); }
        public void Fault() { State = GameActionState.Finished; _completion.SetException(new InvalidOperationException("test fault")); }
    }
}
namespace MegaCrit.Sts2.Core.GameActions.Multiplayer { }
namespace MegaCrit.Sts2.Core.GameActions
{
    public sealed class EndPlayerTurnAction(Player player, int turnNumber) : GameAction
    {
        public Player Player { get; } = player;
        public int TurnNumber { get; } = turnNumber;
    }
}
namespace MegaCrit.Sts2.Core.Runs
{
    public sealed class RunManager
    {
        public static RunManager Instance { get; } = new();
        public RecordingQueue ActionQueueSynchronizer { get; } = new();
    }
    public sealed class RecordingQueue
    {
        public List<GameAction> Actions { get; } = [];
        public void RequestEnqueue(GameAction action) => Actions.Add(action);
    }
}
namespace MegaCrit.Sts2.Core.Logging
{
    public static class Log
    {
        public static void Info(string message) => Console.WriteLine(message);
        public static void Warn(string message) => Console.WriteLine(message);
    }
}
namespace Sts2LlmCoop
{
    internal sealed class AiLegalActionOption
    {
        public string ActionId { get; init; } = "";
        public string ActionType { get; init; } = "";
        public string Description { get; init; } = "";
        public string Label { get; init; } = "";
        public string Summary { get; init; } = "";
        public string? CardId { get; init; }
        public string? CardInstanceId { get; init; }
        public string? TargetId { get; init; }
        public int? EnergyCost { get; init; }
    }
    internal static class AiTeammateStatus { public static void Set(string message) { } }
    internal sealed partial class AiTeammateDummyController
    {
        private static readonly TimeSpan IdleTickInterval = TimeSpan.FromMilliseconds(250);
        private static readonly TimeSpan ActionSettleTimeout = TimeSpan.FromSeconds(5);
        // Ownership tests use zero grace rather than a wall-clock delay.
        private static readonly TimeSpan PostSettleGraceInterval = TimeSpan.Zero;
        public Player Player { get; set; } = new();
        public ulong PlayerId => Player.NetId;
        private static string BuildEndTurnActionId(Player player) => $"end_turn_player_{player.NetId}";
        private bool TryGetControlledPlayer(out Player player, out object state)
        { player = Player; state = new(); return true; }
        private static bool IsQueueSettledForReplan(PendingIssuedActionSettlement settlement) => true;
        private static void WriteAudit(string kind, string id, string type) { }
        public Task Execute(AiTeammateAvailableAction action) => ExecuteResolvedActionAsync(action);
        public bool ExecutionFailed => _executionFailed;
        public IssuedPlayerTurn? PendingIdentity => _pendingIssuedActionSettlement?.IssuedEndTurn;
        public bool HasPending => _pendingIssuedActionSettlement != null;
        public string? LastDeduplicationKey => _lastDeduplicationKey;
        public void MissingWindow() => ResetCompletedEndTurnTrackingIfNeeded(null, false);
        public void Track(AiTeammateAvailableAction action, AiActionExecutionResult result)
            => BeginIssuedActionSettlement(action, result);
        public void Settle()
        {
            TryWaitForIssuedActionSettlement();
            TryWaitForIssuedActionSettlement();
        }
        public bool Suppresses(AiTeammateAvailableAction offered)
        {
            ResetCompletedEndTurnTrackingIfNeeded(Player, true);
            return ShouldSuppressRepeatedEndTurn([offered], Player, true);
        }
    }
}

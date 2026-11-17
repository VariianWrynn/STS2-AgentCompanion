using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;

namespace Godot
{
    internal class GodotObject
    {
        public bool Valid { get; set; } = true;
        public static bool IsInstanceValid(GodotObject? value) => value?.Valid == true;
    }
    internal class Node : GodotObject
    {
        public bool InsideTree { get; set; } = true;
        public bool IsInsideTree() => InsideTree;
        public event Action? TreeExiting { add => throw new NotSupportedException(); remove => throw new NotSupportedException(); }
    }
    internal sealed class SceneTree
    {
        public Node Root => throw new NotSupportedException();
        public event Action? ProcessFrame { add => throw new NotSupportedException(); remove => throw new NotSupportedException(); }
    }
    internal static class Engine { public static object GetMainLoop() => throw new NotSupportedException(); }
}
namespace MegaCrit.Sts2.Core.Entities.Cards
{
    internal enum TargetType { AnyEnemy, Self, AllEnemies, AnyAlly, RandomEnemy }
    internal sealed class CardPile { public List<CardModel> Cards { get; } = []; }
    internal sealed class EnergyCost { public bool CostsX { get; set; } }
}
namespace MegaCrit.Sts2.Core.Models
{
    internal abstract class CardModel(TargetType targetType)
    {
        public TargetType TargetType { get; set; } = targetType;
        public Player Owner { get; set; } = null!;
        public object? Enchantment { get; set; }
        public object? Affliction { get; set; }
        public EnergyCost EnergyCost { get; } = new();
        public bool HasStarCostX { get; set; }
        public bool Upgraded { get; set; }
        public string Id { get; set; } = "";
        public bool NativeAllows { get; set; } = true;
        public List<Creature?> NativeTargets { get; } = [];
        public bool CanPlayTargeting(Creature? target) { NativeTargets.Add(target); return NativeAllows; }
        public Task OnEnqueuePlayVfx(Creature? target) => throw new NotSupportedException();
    }
}
namespace MegaCrit.Sts2.Core.Models.Cards
{
    internal class StrikeIronclad() : CardModel(TargetType.AnyEnemy);
    internal class Bash() : CardModel(TargetType.AnyEnemy);
    internal class DefendIronclad() : CardModel(TargetType.Self);
    internal class TwinStrike() : CardModel(TargetType.AnyEnemy);
    internal class Uppercut() : CardModel(TargetType.AnyEnemy);
    internal class PommelStrike() : CardModel(TargetType.AnyEnemy);
    internal class Inflame() : CardModel(TargetType.Self);
    internal class ShrugItOff() : CardModel(TargetType.Self);
    internal class Thunderclap() : CardModel(TargetType.AllEnemies);
    internal sealed class Armaments() : CardModel(TargetType.Self);
}
namespace MegaCrit.Sts2.Core.Entities.Creatures
{
    internal sealed class Creature
    {
        public bool IsAlive { get; set; } = true;
        public uint? CombatId { get; set; }
        public CombatState? CombatState { get; set; }
        public int Side { get; set; }
        public decimal CurrentHp { get; set; }
        public decimal MaxHp { get; set; }
    }
}
namespace MegaCrit.Sts2.Core.Entities.Players
{
    internal enum PlayerTurnPhase { Play }
    internal sealed class PlayerCombatState
    {
        public CardPile Hand { get; set; } = new();
        public int TurnNumber { get; set; }
        public PlayerTurnPhase Phase { get; set; }
        public decimal Energy { get; set; }
    }
    internal sealed class Player
    {
        public PlayerCombatState? PlayerCombatState { get; set; } = new();
        public Creature Creature { get; } = new();
        public ulong NetId { get; set; }
        public decimal Gold { get; set; }
    }
}
namespace MegaCrit.Sts2.Core.Nodes.Cards.Holders
{
    internal sealed class NCardHolder : Godot.Node
    {
        public CardModel CardModel { get; set; } = null!;
        public Godot.Node CardNode { get; set; } = new();
    }
}
namespace MegaCrit.Sts2.Core.Nodes.Combat
{
    internal sealed class PeekButton { public bool IsPeeking { get; set; } }
    internal sealed class NPlayerHand
    {
        public enum Mode { Play }
        public Dictionary<CardModel, NCardHolder> Holders { get; } = [];
        public NCardHolder? GetCardHolder(CardModel card) => Holders.GetValueOrDefault(card);
        public PeekButton PeekButton { get; } = new();
        public Mode CurrentMode => throw new NotSupportedException();
        public bool InCardPlay => throw new NotSupportedException();
        public bool IsInCardSelection => throw new NotSupportedException();
    }
}
namespace MegaCrit.Sts2.Core.Combat
{
    internal sealed class CombatState
    {
        public List<Creature> HittableEnemies { get; } = [];
        public int CurrentSide { get; set; }
        public int RoundNumber { get; set; }
    }
    internal sealed class StateTracker
    {
        public event Action<CombatState> CombatStateChanged { add => throw new NotSupportedException(); remove => throw new NotSupportedException(); }
    }
    internal sealed class CombatManager
    {
        public static CombatManager Instance => throw new NotSupportedException();
        public StateTracker StateTracker => throw new NotSupportedException();
        public bool IsInProgress => throw new NotSupportedException();
        public bool PlayerActionsDisabled => throw new NotSupportedException();
        public List<Player> PlayersTakingExtraTurn => throw new NotSupportedException();
        public bool IsPlayerReadyToEndTurn(Player host) => throw new NotSupportedException();
    }
}
namespace MegaCrit.Sts2.Core.Multiplayer.Game
{
    internal enum NetGameType { Host }
    internal class NetService
    {
        public bool IsConnected => throw new NotSupportedException();
        public NetGameType Type => throw new NotSupportedException();
        public ulong NetId => throw new NotSupportedException();
    }
}
namespace MegaCrit.Sts2.Core.Runs
{
    internal sealed class RunState
    {
        public List<Player> Players => throw new NotSupportedException();
        public Player? GetPlayer(ulong id) => throw new NotSupportedException();
        public object? CurrentRoom => throw new NotSupportedException();
        public int TotalFloor => throw new NotSupportedException();
    }
    internal sealed class ActionQueueSet
    {
        public event Action ActionQueueChanged { add => throw new NotSupportedException(); remove => throw new NotSupportedException(); }
        public bool AreAnyPlayerDrivenActionsBeingExecuted() => throw new NotSupportedException();
    }
    internal sealed class ActionQueueSynchronizer
    {
        public void RequestEnqueue(MegaCrit.Sts2.Core.GameActions.GameAction action) => throw new NotSupportedException();
    }
    internal sealed class RunManager
    {
        public static RunManager Instance => throw new NotSupportedException();
        public ActionQueueSet ActionQueueSet => throw new NotSupportedException();
        public ActionQueueSynchronizer ActionQueueSynchronizer => throw new NotSupportedException();
        public bool IsInProgress => throw new NotSupportedException();
        public RunState? DebugOnlyGetState() => throw new NotSupportedException();
        public MegaCrit.Sts2.Core.Multiplayer.Game.NetService NetService => throw new NotSupportedException();
    }
}
namespace MegaCrit.Sts2.Core.Context
{
    internal static class LocalContext
    {
        public static ulong NetId => throw new NotSupportedException();
        public static Player? GetMe(MegaCrit.Sts2.Core.Runs.RunState run) => throw new NotSupportedException();
    }
}
namespace MegaCrit.Sts2.Core.Nodes.Rooms
{
    internal sealed class NCombatRoom
    {
        public static NCombatRoom? Instance => throw new NotSupportedException();
        public CombatUi? Ui => throw new NotSupportedException();
        public MegaCrit.Sts2.Core.Rooms.CombatRoomMode Mode => throw new NotSupportedException();
    }
    internal sealed class CombatUi { public MegaCrit.Sts2.Core.Nodes.Combat.NPlayerHand Hand => throw new NotSupportedException(); }
}
namespace MegaCrit.Sts2.Core.Rooms { internal enum CombatRoomMode { ActiveCombat } }
namespace MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext
{
    internal sealed class ActiveScreenContext
    {
        public static ActiveScreenContext Instance => throw new NotSupportedException();
        public bool IsCurrent(object room) => throw new NotSupportedException();
    }
}
namespace MegaCrit.Sts2.Core.Logging
{
    internal static class Log
    {
        public static void Info(string text) => throw new NotSupportedException();
        public static void Error(string text) => throw new NotSupportedException();
    }
}
namespace MegaCrit.Sts2.Core.Helpers
{
    internal static class TaskHelper { public static void RunSafely(Task task) => throw new NotSupportedException(); }
}
namespace MegaCrit.Sts2.Core.Entities.Actions { internal enum GameActionState { None, Finished, Canceled } }
namespace MegaCrit.Sts2.Core.GameActions
{
    internal class GameAction
    {
        public Task CompletionTask => throw new NotSupportedException();
        public MegaCrit.Sts2.Core.Entities.Actions.GameActionState State => throw new NotSupportedException();
        public Exception? Exception => throw new NotSupportedException();
        public event Action<GameAction> BeforeCancelled { add => throw new NotSupportedException(); remove => throw new NotSupportedException(); }
    }
    internal sealed class EndPlayerTurnAction : GameAction
    {
        public EndPlayerTurnAction(Player player, int turn) => throw new NotSupportedException();
    }
    internal sealed class PlayCardAction : GameAction
    {
        public PlayCardAction(CardModel card, Creature? target) => throw new NotSupportedException();
    }
}
namespace Sts2LlmCoop
{
    internal sealed record Participant(bool IsHost, ulong PlayerId);
    internal sealed record AiController(ulong PlayerId);
    internal sealed class AiSession
    {
        public List<Participant> Participants => throw new NotSupportedException();
        public Dictionary<ulong, AiController> AiControllers => throw new NotSupportedException();
        public ulong HostPlayerId => throw new NotSupportedException();
    }
    internal static class AiTeammateSessionRegistry { public static AiSession? Current => throw new NotSupportedException(); }
    internal sealed class AiTeammateLoopbackHostGameService : MegaCrit.Sts2.Core.Multiplayer.Game.NetService;
}

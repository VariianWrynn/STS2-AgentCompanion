using System.Diagnostics;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Helpers;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using Environment = System.Environment;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Rooms;
using System.Runtime.CompilerServices;

namespace Sts2LlmCoop.HostTest;

internal sealed class HostTestBridge : IDisposable
{
    private readonly SceneTree _tree;
    private readonly int _threadId;
    private readonly HostProcessIdentity _process;
    private readonly HostObservationMailbox _mailbox;
    private readonly HostSubmissionMailbox _submissions;
    private readonly HostTurnCommands _commands;
    private readonly Action _removeNativeListeners;
    private Action? _removeQueueListener;
    private object? _queueSet;
    private long _nativeRevision;
    private readonly List<NativeAction> _actions = [];
    private readonly HostTestTransport _transport;
    private bool _disposed;

    private HostTestBridge(SceneTree tree, HostBridgeConfig config, HostProcessIdentity identity)
    {
        _tree = tree;
        _threadId = Environment.CurrentManagedThreadId;
        _process = identity;
        _commands = new HostTurnCommands(new NativeBoundary(this));
        _submissions = new HostSubmissionMailbox(_commands.Submit);
        _mailbox = new HostObservationMailbox(Capture);
        _transport = new HostTestTransport(config, _mailbox, _submissions);
        var tracker = CombatManager.Instance.StateTracker;
        tracker.CombatStateChanged += CombatChanged;
        _removeNativeListeners = () =>
        {
            tracker.CombatStateChanged -= CombatChanged;
            _removeQueueListener?.Invoke();
        };
        _tree.ProcessFrame += OnFrame;
        _tree.Root.TreeExiting += Dispose;
    }

    internal static void StartIfRequested()
    {
        try
        {
            var config = HostBridgeConfig.FromArguments(Environment.GetCommandLineArgs());
            if (config is null) return;
            var gameAssembly = typeof(RunManager).Assembly.Location;
            var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(gameAssembly)));
            if (!hash.Equals(config.ExpectedAssemblySha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Game assembly hash mismatch.");
            var releasePath = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(gameAssembly))!, "release_info.json");
            using var release = JsonDocument.Parse(File.ReadAllText(releasePath));
            var version = release.RootElement.GetProperty("version").GetString();
            if (version != "v0.113.1") throw new InvalidDataException("Unsupported installed release.");
            using var process = Process.GetCurrentProcess();
            var identity = new HostProcessIdentity(process.Id, process.StartTime.ToUniversalTime(),
                process.MainModule?.FileName, gameAssembly, hash, version);
            if (Engine.GetMainLoop() is not SceneTree tree) throw new InvalidOperationException("No scene tree.");
            _ = new HostTestBridge(tree, config, identity);
            Log.Info("[HostTest] Host observation and bounded native command bridge enabled. Runtime compatibility remains unverified.");
        }
        catch (Exception exception)
        {
            // The token and the configuration path must not enter game logs.
            Log.Error($"[HostTest] Bridge activation refused ({exception.GetType().Name}).");
        }
    }

    private void OnFrame()
    {
        RefreshQueueListener();
        _submissions.Drain();
        _mailbox.Drain();
    }

    private void CombatChanged(CombatState _) => _nativeRevision++;
    private void QueueChanged() => _nativeRevision++;

    private void RefreshQueueListener()
    {
        var queues = RunManager.Instance.ActionQueueSet;
        if (ReferenceEquals(queues, _queueSet)) return;
        _removeQueueListener?.Invoke();
        _removeQueueListener = null;
        _queueSet = queues;
        _nativeRevision++;
        if (queues is null) return;
        queues.ActionQueueChanged += QueueChanged;
        _removeQueueListener = () => queues.ActionQueueChanged -= QueueChanged;
    }

    private object Capture()
    {
        var observation = CaptureCore();
        return observation with { Submission = _commands.Observe(_submissions.NextSequence) };
    }

    private HostObservation CaptureCore()
    {
        if (Environment.CurrentManagedThreadId != _threadId)
            throw new InvalidOperationException("Native observation requires the main thread.");
        HostObservation Unavailable(string reason) => new("observe_host", "runtime_unverified", _process, reason);
        var session = AiTeammateSessionRegistry.Current;
        var manager = RunManager.Instance;
        if (session is null || !manager.IsInProgress) return Unavailable("no_companion_run");
        var run = manager.DebugOnlyGetState();
        if (run is null) return Unavailable("no_run_state");
        var ai = session.Participants.Where(p => !p.IsHost).ToArray();
        var hostParticipant = session.Participants.Where(p => p.IsHost).ToArray();
        var local = LocalContext.GetMe(run);
        var service = manager.NetService;
        var facts = new HostIdentityFacts(
            service is AiTeammateLoopbackHostGameService { IsConnected: true, Type: NetGameType.Host },
            session.HostPlayerId, LocalContext.NetId, local?.NetId, service.NetId,
            session.Participants.Count, hostParticipant.Length, ai.Length, session.AiControllers.Count,
            hostParticipant.Length == 1 && hostParticipant[0].PlayerId == session.HostPlayerId,
            ai.Length == 1 && ai[0].PlayerId != session.HostPlayerId &&
                session.AiControllers.TryGetValue(ai[0].PlayerId, out var controller) && controller.PlayerId == ai[0].PlayerId,
            session.AiControllers.ContainsKey(session.HostPlayerId),
            run.Players.Count == 2 && run.Players.Select(p => p.NetId).Distinct().Count() == 2 &&
            run.Players.Select(p => p.NetId).Order().SequenceEqual(session.Participants.Select(p => p.PlayerId).Order()));
        if (!HostIdentityPolicy.Allows(facts)) return Unavailable("host_scope_denied");
        return new HostObservation("observe_host", "runtime_unverified", _process, "host",
            run.CurrentRoom?.GetType().Name, run.TotalFloor,
            Project(run.GetPlayer(session.HostPlayerId)!), Project(run.GetPlayer(ai[0].PlayerId)!));
    }

    private HostTurnContext? ReadTurn()
    {
        var observation = CaptureCore();
        if (observation.Scope != "host") return null;
        var session = AiTeammateSessionRegistry.Current!;
        var run = RunManager.Instance.DebugOnlyGetState()!;
        var host = run.GetPlayer(session.HostPlayerId)!;
        var combat = host.PlayerCombatState;
        var combatState = host.Creature.CombatState;
        var room = NCombatRoom.Instance;
        var hand = room?.Ui?.Hand;
        if (combat is null || combatState is null || room is null || hand is null) return null;
        var manager = CombatManager.Instance;
        var ready = manager.IsPlayerReadyToEndTurn(host);
        var play = combat.Phase == PlayerTurnPhase.Play;
        var readiness = new HostTurnReadiness(host.Creature.IsAlive, manager.IsInProgress, play,
            combatState.CurrentSide == host.Creature.Side, ready,
            manager.PlayerActionsDisabled || RunManager.Instance.ActionQueueSet.AreAnyPlayerDrivenActionsBeingExecuted(),
            room.Mode == CombatRoomMode.ActiveCombat, ActiveScreenContext.Instance.IsCurrent(room), true,
            hand.CurrentMode == NPlayerHand.Mode.Play, hand.InCardPlay, hand.IsInCardSelection,
            manager.PlayersTakingExtraTurn.Count == 0 || manager.PlayersTakingExtraTurn.Contains(host));
        var stamp = JsonSerializer.Serialize(new { observation.Host, observation.Ai, readiness, Revision = _nativeRevision,
            Cards = combat.Hand.Cards.Select(RuntimeHelpers.GetHashCode).ToArray(),
            AiTurns = run.Players.Select(p => p.PlayerCombatState?.TurnNumber).ToArray() });
        return new HostTurnContext(session, run, host, combatState, hand, room, combat.TurnNumber, play, ready, readiness.Allows, stamp, readiness.InSelection);
    }

    private sealed class NativeBoundary(HostTestBridge bridge) : IHostTurnBoundary
    {
        public HostTurnContext? Read() => bridge.ReadTurn();

        public HostCommand.PlayCard[] Cards(HostTurnContext context)
        {
            var host = (Player)context.Host;
            var hand = (NPlayerHand)context.Hand;
            var result = new List<HostCommand.PlayCard>();
            var cards = host.PlayerCombatState!.Hand.Cards;
            for (var index = 0; index < cards.Count && result.Count < 255; index++)
            {
                var card = cards[index];
                if (!Supported(card)) continue;
                var holder = hand.GetCardHolder(card);
                if (holder is null) continue;
                IEnumerable<Creature?> targets = card.TargetType == TargetType.Self ? new Creature?[] { null } :
                    host.Creature.CombatState!.HittableEnemies.Cast<Creature?>();
                foreach (var target in targets)
                {
                    var label = $"Play {card.GetType().Name} hand {index + 1}" +
                        (target is null ? " on self" : $" on enemy {target.CombatId}");
                    var command = new HostCommand.PlayCard(card, target, holder, host.PlayerCombatState.Hand, label, target?.CombatId);
                    if (CanPlay(context, command)) result.Add(command);
                    if (result.Count == 255) break;
                }
            }
            return result.ToArray();
        }

        private static bool Supported(CardModel card) =>
            (card.GetType() == typeof(StrikeIronclad) || card.GetType() == typeof(DefendIronclad) || card.GetType() == typeof(Bash)) &&
            card.Enchantment is null && card.Affliction is null && !card.EnergyCost.CostsX && !card.HasStarCostX &&
            card.TargetType is TargetType.AnyEnemy or TargetType.Self;

        public bool CanPlay(HostTurnContext context, HostCommand.PlayCard command)
        {
            var host = (Player)context.Host;
            var hand = (NPlayerHand)context.Hand;
            var card = (CardModel)command.Card;
            var holder = (NCardHolder)command.Holder;
            var target = (Creature?)command.Target;
            if (!context.Eligible || hand.PeekButton.IsPeeking || !Supported(card) ||
                !ReferenceEquals(card.Owner, host) || !ReferenceEquals(command.OriginalHand, host.PlayerCombatState?.Hand) ||
                !InOriginalHand(command) || !GodotObject.IsInstanceValid(holder) || !holder.IsInsideTree() ||
                !GodotObject.IsInstanceValid(holder.CardNode) || !holder.CardNode.IsInsideTree() || !ReferenceEquals(holder.CardModel, card) ||
                !ReferenceEquals(hand.GetCardHolder(card), holder)) return false;
            if (card.TargetType == TargetType.Self ? target is not null || command.TargetCombatId is not null :
                target is null || !target.IsAlive || target.CombatId is null || target.CombatId != command.TargetCombatId ||
                !ReferenceEquals(target.CombatState, context.Combat) ||
                !host.Creature.CombatState!.HittableEnemies.Any(enemy => ReferenceEquals(enemy, target))) return false;
            return card.CanPlayTargeting(target);
        }

        public bool InOriginalHand(HostCommand.PlayCard command) =>
            ((CardPile)command.OriginalHand).Cards.Any(card => ReferenceEquals(card, command.Card));

        public IHostTurnAction Dispatch(HostTurnContext context, HostCommand command)
        {
            var host = (Player)context.Host;
            GameAction action = command switch
            {
                HostCommand.EndTurn => new EndPlayerTurnAction(host, host.PlayerCombatState!.TurnNumber),
                HostCommand.PlayCard card => new PlayCardAction((CardModel)card.Card, (Creature?)card.Target),
                _ => throw new InvalidOperationException("Unsupported host command.")
            };
            var tracked = new NativeAction(action, item => bridge._actions.Remove(item));
            bridge._actions.Add(tracked);
            try
            {
                if (command is HostCommand.PlayCard play)
                    TaskHelper.RunSafely(((CardModel)play.Card).OnEnqueuePlayVfx((Creature?)play.Target));
                RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(action);
                return tracked;
            }
            catch
            {
                tracked.Dispose();
                throw;
            }
        }
    }

    private static HostPlayerObservation Project(Player player) => new(player.NetId,
        player.PlayerCombatState?.Phase.ToString(),
        player.PlayerCombatState is null ? null : CombatManager.Instance.IsPlayerReadyToEndTurn(player),
        player.Creature.CombatState?.RoundNumber, player.Creature.CurrentHp, player.Creature.MaxHp,
        player.Gold, player.PlayerCombatState?.Energy, player.PlayerCombatState?.Hand.Cards.Count);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _tree.ProcessFrame -= OnFrame;
        _tree.Root.TreeExiting -= Dispose;
        _removeNativeListeners();
        _commands.Dispose();
        foreach (var action in _actions.ToArray()) action.Dispose();
        _actions.Clear();
        _transport.Dispose();
    }
}

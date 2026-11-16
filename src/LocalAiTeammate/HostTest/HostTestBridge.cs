using System.Diagnostics;
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

namespace Sts2LlmCoop.HostTest;

internal sealed class HostTestBridge : IDisposable
{
    private readonly SceneTree _tree;
    private readonly int _threadId;
    private readonly HostProcessIdentity _process;
    private readonly HostObservationMailbox _mailbox;
    private readonly HostTestTransport _transport;
    private bool _disposed;

    private HostTestBridge(SceneTree tree, HostBridgeConfig config, HostProcessIdentity identity)
    {
        _tree = tree;
        _threadId = Environment.CurrentManagedThreadId;
        _process = identity;
        _mailbox = new HostObservationMailbox(Capture);
        _transport = new HostTestTransport(config, _mailbox);
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
            Log.Info("[HostTest] Read-only observation bridge enabled. Runtime compatibility remains unverified.");
        }
        catch (Exception exception)
        {
            // The token and the configuration path must not enter game logs.
            Log.Error($"[HostTest] Bridge activation refused ({exception.GetType().Name}).");
        }
    }

    private void OnFrame() => _mailbox.Drain();

    private object Capture()
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
        _transport.Dispose();
    }
}

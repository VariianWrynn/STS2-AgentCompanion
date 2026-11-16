using System;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;

namespace Sts2LlmCoop;

internal enum AiTeammateActionKind
{
    PlayCard,
    UsePotion,
    EndTurn,
    ChooseMapNode,
    ChooseEventOption,
    ChooseRestSiteOption,
    ClaimReward,
    InspectShopState,
    ExecuteShopStep,
}

internal sealed class IssuedPlayerTurn : IEquatable<IssuedPlayerTurn>
{
    private IssuedPlayerTurn(CombatState combat, ulong playerId, int nativeTurnNumber)
    {
        Combat = combat;
        PlayerId = playerId;
        NativeTurnNumber = nativeTurnNumber;
    }

    public CombatState Combat { get; }
    public ulong PlayerId { get; }
    public int NativeTurnNumber { get; }

    public static IssuedPlayerTurn Capture(Player player)
    {
        return new IssuedPlayerTurn(
            player.Creature.CombatState as CombatState ?? throw new InvalidOperationException("EndTurn requires a combat."),
            player.NetId,
            player.PlayerCombatState?.TurnNumber ?? throw new InvalidOperationException("EndTurn requires a player turn."));
    }

    public bool Matches(Player player)
    {
        return ReferenceEquals(Combat, player.Creature.CombatState)
            && PlayerId == player.NetId
            && NativeTurnNumber == player.PlayerCombatState?.TurnNumber;
    }

    public bool Equals(IssuedPlayerTurn? other)
    {
        return other != null && ReferenceEquals(Combat, other.Combat)
            && PlayerId == other.PlayerId && NativeTurnNumber == other.NativeTurnNumber;
    }

    public override bool Equals(object? other) => other is IssuedPlayerTurn turn && Equals(turn);
    public override int GetHashCode() => HashCode.Combine(RuntimeHelpers.GetHashCode(Combat), PlayerId, NativeTurnNumber);
}

internal sealed class AiTeammateAvailableAction
{
    public AiTeammateAvailableAction(
        AiLegalActionOption option,
        Func<Task<AiActionExecutionResult>> executeAsync,
        string? deduplicationKey = null)
    {
        Option = option;
        ExecuteAsync = executeAsync;
        DeduplicationKey = deduplicationKey;
    }

    public IssuedPlayerTurn? IssuedEndTurn { get; private init; }

    public static AiTeammateAvailableAction ForEndTurn(
        AiLegalActionOption option, IssuedPlayerTurn issued,
        Func<Task<AiActionExecutionResult>> executeAsync)
    {
        return new AiTeammateAvailableAction(option, executeAsync) { IssuedEndTurn = issued };
    }

    public AiLegalActionOption Option { get; }

    public string ActionId => Option.ActionId;

    public string ActionType => Option.ActionType;

    public string Description => Option.Description;

    public string? CardId => Option.CardId;

    public string? CardInstanceId => Option.CardInstanceId;

    public string? TargetId => Option.TargetId;

    public int? EnergyCost => Option.EnergyCost;

    public Func<Task<AiActionExecutionResult>> ExecuteAsync { get; }

    public string? DeduplicationKey { get; }
}

internal sealed class AiActionExecutionResult
{
    public static AiActionExecutionResult Completed { get; } = new();

    public static AiActionExecutionResult Stale { get; } = new() { WasStale = true };

    public bool WasStale { get; private init; }

    public IssuedPlayerTurn? IssuedEndTurn { get; private init; }

    public static AiActionExecutionResult ForEndTurn(EndPlayerTurnAction action, IssuedPlayerTurn issued)
    {
        return new AiActionExecutionResult { GameAction = action, WaitForQueueSettle = true, IssuedEndTurn = issued };
    }

    public GameAction? GameAction { get; init; }

    public bool WaitForQueueSettle { get; init; }

    public bool HasTrackedGameAction => GameAction != null;
}

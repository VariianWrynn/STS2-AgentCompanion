using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Rooms;

namespace Sts2LlmCoop;

internal static class ShopInventoryResolver
{
    private sealed class VirtualMerchantVisitState
    {
        public required string RoomVisitKey { get; init; }

        public required Player Player { get; init; }

        public required MerchantInventory Inventory { get; init; }

        public bool VisitCompleted { get; set; }
    }

    private static readonly Dictionary<ulong, VirtualMerchantVisitState> VirtualMerchantStates = new();

    public static ResolvedMerchantInventory? Resolve(Player player)
    {
        if (player.RunState.CurrentRoom is not MerchantRoom)
        {
            VirtualMerchantStates.Remove(player.NetId);
            return null;
        }

        string roomVisitKey = BuildRoomVisitKey(player);
        MerchantInventory inventory = MegaCrit.Sts2.Core.Runs.RunManager.Instance.MerchantSynchronizer.GetInventoryFor(player);
        if (LocalContext.IsMe(player))
        {
            VirtualMerchantStates.Remove(player.NetId);
            return new ResolvedMerchantInventory
            {
                Inventory = inventory,
                ExecutionMode = ShopExecutionMode.LocalSharedUi,
                RoomVisitKey = roomVisitKey,
                VisitCompleted = false
            };
        }

        if (!AiTeammateDummyController.IsAiPlayer(player))
        {
            return null;
        }

        if (!VirtualMerchantStates.TryGetValue(player.NetId, out VirtualMerchantVisitState? state) ||
            !ReferenceEquals(state.Player, player) ||
            !ReferenceEquals(state.Inventory, inventory) ||
            !string.Equals(state.RoomVisitKey, roomVisitKey, StringComparison.Ordinal))
        {
            state = new VirtualMerchantVisitState
            {
                RoomVisitKey = roomVisitKey,
                Player = player,
                Inventory = inventory,
                VisitCompleted = false
            };
            VirtualMerchantStates[player.NetId] = state;
            Log.Info($"[AITeammate][Shop] Resolved native merchant inventory player={player.NetId} roomKey={roomVisitKey}");
        }

        return new ResolvedMerchantInventory
        {
            Inventory = inventory,
            ExecutionMode = ShopExecutionMode.VirtualAiDirect,
            RoomVisitKey = roomVisitKey,
            VisitCompleted = state.VisitCompleted
        };
    }

    public static bool MarkVisitCompleted(Player player, string roomVisitKey, string reason)
    {
        if (player.RunState.CurrentRoom is MerchantRoom &&
            VirtualMerchantStates.TryGetValue(player.NetId, out VirtualMerchantVisitState? state) &&
            ReferenceEquals(state.Player, player) &&
            ReferenceEquals(state.Inventory, MegaCrit.Sts2.Core.Runs.RunManager.Instance.MerchantSynchronizer.GetInventoryFor(player)) &&
            string.Equals(state.RoomVisitKey, roomVisitKey, StringComparison.Ordinal))
        {
            if (!state.VisitCompleted)
            {
                state.VisitCompleted = true;
                Log.Info($"[AITeammate][Shop] Marked virtual merchant visit complete player={player.NetId} roomKey={roomVisitKey} reason={reason}");
            }

            return true;
        }

        return false;
    }

    private static string BuildRoomVisitKey(Player player)
    {
        string mapCoord = player.RunState.CurrentMapCoord.HasValue
            ? $"{player.RunState.CurrentMapCoord.Value.col},{player.RunState.CurrentMapCoord.Value.row}"
            : "none";
        return $"act={player.RunState.CurrentActIndex};roomCount={player.RunState.CurrentRoomCount};coord={mapCoord};room={player.RunState.CurrentRoom?.GetType().Name ?? "null"}";
    }
}

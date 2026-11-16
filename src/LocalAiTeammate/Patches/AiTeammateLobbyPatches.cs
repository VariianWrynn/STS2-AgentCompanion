// ReSharper disable InconsistentNaming
using System;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Unlocks;

namespace Sts2LlmCoop;

[HarmonyPatch(typeof(PlatformUtil), nameof(PlatformUtil.GetPlayerName))]
[SuppressMessage("ReSharper", "UnusedType.Global")]
internal static class AiTeammatePlatformUtilGetPlayerNamePatch
{
    [HarmonyPrefix]
    private static bool Prefix(PlatformType platformType, ulong playerId, ref string __result)
    {
        if (!AiTeammateSessionRegistry.TryGetDisplayName(playerId, out string displayName))
        {
            return true;
        }

        __result = displayName;
        return false;
    }
}

[HarmonyPatch(typeof(SaveManager), nameof(SaveManager.SaveRun))]
[SuppressMessage("ReSharper", "UnusedType.Global")]
internal static class AiTeammateSaveManagerSaveRunPatch
{
    public static void Prefix()
    {
        AiTeammateSaveSupport.MarkCurrentRunIfNeeded();
    }
}

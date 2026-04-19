using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Unlocks;

namespace AITeammate.Scripts;

[HarmonyPatch(typeof(PlatformUtil), nameof(PlatformUtil.GetPlayerName))]
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

// 4.16: StartRunLobby's private begin-run entry point renamed from `BeginRun` to `BeginRunForAllPlayers`
// (and the inner local-execution half was extracted into `BeginRunLocally`). We continue to intercept the
// outer entry point so we still own the message send plus the listener dispatch.
[HarmonyPatch(typeof(StartRunLobby), "BeginRunForAllPlayers")]
internal static class AiTeammateStartRunLobbyBeginRunPatch
{
    [HarmonyPrefix]
    private static bool Prefix(StartRunLobby __instance, string seed, List<ModifierModel> modifiers)
    {
        if (__instance.NetService is not AiTeammateLoopbackHostGameService)
        {
            return true;
        }

        Log.Info("[AITeammate] Intercepting StartRunLobby.BeginRunForAllPlayers for local AI teammate loopback.");

        MethodInfo? updatePreferredAscensionMethod = AccessTools.Method(typeof(StartRunLobby), "UpdatePreferredAscension");
        updatePreferredAscensionMethod?.Invoke(__instance, Array.Empty<object>());

        LobbyBeginRunMessage beginRunMessage = new()
        {
            playersInLobby = __instance.Players,
            seed = seed,
            modifiers = modifiers.Select((modifier) => modifier.ToSerializable()).ToList(),
            act1 = __instance.Act1
        };

        __instance.NetService.SendMessage(beginRunMessage);

        UnlockState unlockState = GetUnlockState(__instance);
        // 4.16: ActModel.GetRandomList now takes an Rng (constructed from the seed) instead of the raw seed string.
        // This mirrors the construction previously done inside GetRandomList itself.
        Rng rng = new Rng((uint)StringHelper.GetDeterministicHashCode(seed));
        List<ActModel> acts = ActModel.GetRandomList(rng, unlockState, __instance.NetService.Type.IsMultiplayer()).ToList();
        ActModel? act1Override = GetAct1(__instance.Act1);
        if (act1Override != null)
        {
            acts[0] = act1Override;
        }

        // 4.16: StartRunLobby renamed the private field `_beginningRun` to `_isBeginningRun`.
        AccessTools.Field(typeof(StartRunLobby), "_isBeginningRun")?.SetValue(__instance, true);
        __instance.LobbyListener.BeginRun(seed, acts, modifiers);
        return false;
    }

    private static UnlockState GetUnlockState(StartRunLobby lobby)
    {
        MethodInfo? getUnlockStateMethod = AccessTools.Method(typeof(StartRunLobby), "GetUnlockState");
        UnlockState? unlockState = getUnlockStateMethod?.Invoke(lobby, Array.Empty<object>()) as UnlockState;
        if (unlockState == null)
        {
            throw new InvalidOperationException("Could not resolve UnlockState for AI teammate lobby begin-run flow.");
        }

        return unlockState;
    }

    private static ActModel? GetAct1(string act1Key)
    {
        MethodInfo? getActMethod = AccessTools.Method(typeof(StartRunLobby), "GetAct", new[] { typeof(string) });
        return getActMethod?.Invoke(null, new object[] { act1Key }) as ActModel;
    }
}

[HarmonyPatch(typeof(SaveManager), nameof(SaveManager.SaveRun))]
internal static class AiTeammateSaveManagerSaveRunPatch
{
    public static void Prefix()
    {
        AiTeammateSaveSupport.MarkCurrentRunIfNeeded();
    }
}

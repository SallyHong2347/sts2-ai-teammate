using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace AITeammate.Scripts;

internal static class AiTeammateTestMapPatches
{
    [HarmonyPatch(typeof(ActModel), nameof(ActModel.CreateMap))]
    private static class ActModelCreateMapPatch
    {
        private static void Postfix(RunState runState, ref ActMap __result)
        {
            if (!AiTeammateSessionRegistry.ShouldUseTestMap(runState))
            {
                return;
            }

            __result = new AiTeammateTestActMap();
            Log.Info("[AITeammate] Replaced generated Act 1 map with AI teammate test map.");
        }
    }

    [HarmonyPatch(typeof(RunManager), "RollRoomTypeFor")]
    private static class RunManagerRollRoomTypeForPatch
    {
        private static bool Prefix(MapPointType pointType, ref RoomType __result)
        {
            RunState? runState = RunManager.Instance.DebugOnlyGetState();
            if (pointType != MapPointType.Unknown || !AiTeammateSessionRegistry.ShouldUseTestMap(runState))
            {
                return true;
            }

            __result = RoomType.Event;
            Log.Info("[AITeammate] Forced test-map unknown room to resolve as an Event room.");
            return false;
        }
    }
}

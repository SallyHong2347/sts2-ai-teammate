using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;

namespace AITeammate.Scripts;

internal static class AiTeammateRewardPatches
{
    [HarmonyPatch(typeof(RewardsSet), nameof(RewardsSet.Offer))]
    private static class RewardsSetOfferPatch
    {
        private static bool Prefix(RewardsSet __instance, ref Task __result)
        {
            if (!AiTeammateDummyController.IsAiPlayer(__instance.Player))
            {
                return true;
            }

            __result = AiTeammateDummyController.ExecuteDeterministicRewardSetAsync(__instance);
            return false;
        }
    }

    [HarmonyPatch(typeof(RewardsCmd), nameof(RewardsCmd.OfferForRoomEnd))]
    private static class RewardsCmdOfferForRoomEndPatch
    {
        private static void Postfix(Player player, AbstractRoom room, ref Task __result)
        {
            __result = OfferForAiTeammatesAfterHostAsync(__result, player, room);
        }

        private static async Task OfferForAiTeammatesAfterHostAsync(Task originalTask, Player player, AbstractRoom room)
        {
            AiTeammateSessionState? session = AiTeammateSessionRegistry.Current;
            if (session == null || player.NetId != session.HostPlayerId)
            {
                await originalTask;
                return;
            }

            if (room is TreasureRoom)
            {
                await originalTask;
                return;
            }

            Task[] aiRewardTasks = session.Participants
                .Where(static participant => !participant.IsHost)
                .Select(participant => OfferRoomEndRewardsForAiParticipantAsync(player, room, participant))
                .ToArray();

            Log.Info($"[AITeammate] Starting room-end AI reward fanout room={room.GetType().Name} roomCount={player.RunState.CurrentRoomCount} aiCount={aiRewardTasks.Length}");
            await Task.WhenAll(aiRewardTasks.Prepend(originalTask));
            Log.Info($"[AITeammate] Finished room-end AI reward fanout room={room.GetType().Name} roomCount={player.RunState.CurrentRoomCount} currentRoom={player.RunState.CurrentRoom?.GetType().Name ?? "null"}");
        }

        private static async Task OfferRoomEndRewardsForAiParticipantAsync(
            Player hostPlayer,
            AbstractRoom room,
            AiTeammateSessionParticipant participant)
        {
            Player? aiPlayer = hostPlayer.RunState.GetPlayer(participant.PlayerId);
            if (aiPlayer == null)
            {
                return;
            }

            Log.Info($"[AITeammate] Offering room-end rewards to AI player={aiPlayer.NetId} room={room.GetType().Name} roomCount={hostPlayer.RunState.CurrentRoomCount} currentRoom={hostPlayer.RunState.CurrentRoom?.GetType().Name ?? "null"}");
            await RewardsCmd.OfferForRoomEnd(aiPlayer, room);
            Log.Info($"[AITeammate] Finished room-end rewards for AI player={aiPlayer.NetId} room={room.GetType().Name} roomCount={hostPlayer.RunState.CurrentRoomCount} currentRoom={hostPlayer.RunState.CurrentRoom?.GetType().Name ?? "null"}");
        }
    }

    [HarmonyPatch(typeof(CardReward), "OnSelect")]
    private static class CardRewardOnSelectPatch
    {
        private static bool Prefix(CardReward __instance, ref Task<bool> __result)
        {
            if (!AiTeammateDummyController.IsAiPlayer(__instance.Player))
            {
                return true;
            }

            __result = AiTeammateDummyController.ExecuteDeterministicCardRewardAsync(__instance);
            return false;
        }
    }
}

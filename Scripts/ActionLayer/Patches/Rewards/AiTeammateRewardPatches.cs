using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
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
            await originalTask;

            AiTeammateSessionState? session = AiTeammateSessionRegistry.Current;
            if (session == null || player.NetId != session.HostPlayerId)
            {
                return;
            }

            if (room is TreasureRoom)
            {
                return;
            }

            foreach (AiTeammateSessionParticipant participant in session.Participants.Where(static participant => !participant.IsHost))
            {
                Player? aiPlayer = player.RunState.GetPlayer(participant.PlayerId);
                if (aiPlayer != null)
                {
                    await RewardsCmd.OfferForRoomEnd(aiPlayer, room);
                }
            }
        }
    }
}

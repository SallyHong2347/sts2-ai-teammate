using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace AITeammate.Scripts;

/// <summary>
/// Patches to handle player death gracefully during AI teammate sessions.
/// The game's NPlayerHand.Remove throws InvalidOperationException when a card's
/// visual holder doesn't exist. This can happen when a player dies mid-turn and
/// HandlePlayerDeath tries to remove cards from combat while some holders are
/// already cleaned up (e.g. during an in-flight card play animation).
/// </summary>
internal static class AiTeammateCombatDeathPatches
{
    [HarmonyPatch(typeof(NPlayerHand), nameof(NPlayerHand.Remove))]
    private static class NPlayerHandRemoveSafetyPatch
    {
        private static bool Prefix(NPlayerHand __instance, CardModel card)
        {
            if (AiTeammateSessionRegistry.Current == null)
            {
                return true;
            }

            if (__instance.GetCardHolder(card) != null)
            {
                return true;
            }

            Log.Warn($"[AITeammate] NPlayerHand.Remove: no holder for card {card.Id}, skipping to avoid crash during player death.");
            return false;
        }
    }
}

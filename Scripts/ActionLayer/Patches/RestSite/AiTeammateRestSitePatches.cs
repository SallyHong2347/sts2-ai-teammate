using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;

namespace AITeammate.Scripts;

internal static class AiTeammateRestSitePatches
{
    private static readonly MethodInfo? RestSiteOwnerGetter =
        AccessTools.PropertyGetter(typeof(RestSiteOption), "Owner");

    [HarmonyPatch(typeof(MendRestSiteOption), nameof(MendRestSiteOption.OnSelect))]
    private static class MendRestSiteOptionPatch
    {
        private static bool Prefix(MendRestSiteOption __instance, ref Task<bool> __result)
        {
            Player? owner = RestSiteOwnerGetter?.Invoke(__instance, null) as Player;
            if (!AiTeammateDummyController.IsAiPlayer(owner) || owner == null)
            {
                return true;
            }

            __result = ExecuteAiMendAsync(owner);
            return false;
        }

        private static async Task<bool> ExecuteAiMendAsync(Player owner)
        {
            AiEventTuning tuning = AiCharacterCombatConfigLoader.LoadForPlayer(owner).Events;
            AiTeammateDummyController.MendTargetEvaluation? targetEvaluation =
                AiTeammateDummyController.SelectBestMendTarget(owner, tuning);
            if (targetEvaluation == null)
            {
                Log.Info($"[AITeammate][RestSite] player={owner.NetId} mend execution aborted reason=no_valid_target");
                return false;
            }

            Player target = targetEvaluation.Target;
            decimal healAmount = MendRestSiteOption.GetHealAmount(target);
            Log.Info($"[AITeammate][RestSite] player={owner.NetId} executing MEND target={target.NetId} heal={healAmount} score={targetEvaluation.Score:F1} reasons=[{string.Join("; ", targetEvaluation.Reasons)}]");
            await CreatureCmd.Heal(target.Creature, healAmount);
            await Hook.AfterRestSiteHeal(target.RunState, target, isMimicked: false);
            return true;
        }
    }
}

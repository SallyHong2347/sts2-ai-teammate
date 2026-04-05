using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Runs;

namespace AITeammate.Scripts;

internal static class AiTeammateTestCombatHelper
{
    private static readonly HashSet<string> PatchedCombatKeys = new(StringComparer.Ordinal);

    public static void PreparePotionTestCombatIfNeeded(Player player, RunState runState)
    {
        if (!AiTeammateSessionRegistry.ShouldUseTestMap(runState) ||
            player.Creature?.CombatState == null ||
            runState.CurrentRoom is not MegaCrit.Sts2.Core.Rooms.CombatRoom)
        {
            return;
        }

        string combatKey = BuildCombatKey(runState);
        bool firstPatchForCombat = PatchedCombatKeys.Add(combatKey);

        if (firstPatchForCombat)
        {
            string enemySummary = string.Join(
                ", ",
                player.Creature.CombatState.HittableEnemies.Select(static enemy =>
                    $"{enemy.GetType().Name}(hp={enemy.CurrentHp},block={enemy.Block},attacking={enemy.Monster?.NextMove?.Intents?.Any() == true})"));
            string potionSummary = string.Join(
                ", ",
                player.PotionSlots.Select(static potion => potion?.Id.Entry ?? "empty"));
            int overclockCopies = player.Deck.Cards.Count(static card => card.Id.Entry == ModelDb.Card<Overclock>().Id.Entry);
            string overclockCatalogStatus = CardCatalogRepository.Shared.TryGetStatus(ModelDb.Card<Overclock>().Id.Entry, out CardCatalogBuildStatus overclockStatus)
                ? overclockStatus.ToString()
                : "Missing";
            string burnCatalogStatus = CardCatalogRepository.Shared.TryGetStatus(ModelDb.Card<Burn>().Id.Entry, out CardCatalogBuildStatus burnStatus)
                ? burnStatus.ToString()
                : "Missing";
            Log.Info($"[AITeammate] Prepared test-map combat key={combatKey} player={player.NetId} enemies=[{enemySummary}] potions=[{potionSummary}] overclockCopies={overclockCopies} overclockCatalogStatus={overclockCatalogStatus} burnCatalogStatus={burnCatalogStatus}");
        }
    }

    private static string BuildCombatKey(RunState runState)
    {
        string coord = runState.CurrentMapCoord.HasValue
            ? $"{runState.CurrentMapCoord.Value.col},{runState.CurrentMapCoord.Value.row}"
            : "none";
        return $"act={runState.CurrentActIndex};roomCount={runState.CurrentRoomCount};coord={coord}";
    }
}

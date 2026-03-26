using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace AITeammate.Scripts;

internal sealed partial class AiTeammateDummyController
{
    private IReadOnlyList<AiTeammateAvailableAction> DiscoverCombatActions(Player player)
    {
        List<AiTeammateAvailableAction> actions = [];

        foreach (CardModel card in PileType.Hand.GetPile(player).Cards)
        {
            UnplayableReason reason;
            MegaCrit.Sts2.Core.Models.AbstractModel? preventer;
            if (!card.CanPlay(out reason, out preventer))
            {
                continue;
            }

            foreach (Creature? target in GetOrderedTargets(card.TargetType, player))
            {
                if (target == null || card.CanPlayTargeting(target))
                {
                    string targetName = target?.ToString() ?? "none";
                    actions.Add(new AiTeammateAvailableAction(
                        AiTeammateActionKind.PlayCard,
                        $"Play {card.Id.Entry} -> {targetName}",
                        () =>
                        {
                            card.TryManualPlay(target);
                            return Task.CompletedTask;
                        }));
                    break;
                }
            }
        }

        foreach (PotionModel potion in player.Potions.Where(static potion => !potion.IsQueued))
        {
            Creature? target = GetOrderedTargets(potion.TargetType, player).FirstOrDefault();
            if (potion.TargetType.IsSingleTarget() && target == null)
            {
                continue;
            }

            string targetName = target?.ToString() ?? "none";
            actions.Add(new AiTeammateAvailableAction(
                AiTeammateActionKind.UsePotion,
                $"Use potion {potion.Id.Entry} -> {targetName}",
                () =>
                {
                    potion.EnqueueManualUse(target);
                    return Task.CompletedTask;
                }));
        }

        actions.Add(new AiTeammateAvailableAction(
            AiTeammateActionKind.EndTurn,
            "End turn",
            () =>
            {
                int roundNumber = player.Creature.CombatState?.RoundNumber ?? 0;
                RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(new EndPlayerTurnAction(player, roundNumber));
                return Task.CompletedTask;
            }));

        return actions;
    }

    private static IEnumerable<Creature?> GetOrderedTargets(TargetType targetType, Player player)
    {
        CombatState? combatState = player.Creature.CombatState;
        if (combatState == null)
        {
            return new Creature?[] { null };
        }

        return targetType switch
        {
            TargetType.AnyEnemy => combatState.HittableEnemies.OrderBy(static creature => creature.CombatId ?? uint.MaxValue).Cast<Creature?>(),
            TargetType.AnyAlly => combatState.PlayerCreatures.Where(static creature => creature.IsAlive).OrderBy(static creature => creature.Player?.NetId ?? 0UL).Cast<Creature?>(),
            TargetType.AnyPlayer => combatState.PlayerCreatures.Where(static creature => creature.IsAlive).OrderBy(static creature => creature.Player?.NetId ?? 0UL).Cast<Creature?>(),
            TargetType.Self => new Creature?[] { player.Creature },
            _ => new Creature?[] { null },
        };
    }
}

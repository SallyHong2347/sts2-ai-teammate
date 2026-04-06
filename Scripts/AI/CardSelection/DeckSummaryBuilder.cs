using System;
using System.Collections.Generic;
using System.Linq;

namespace AITeammate.Scripts;

internal static class DeckSummaryBuilder
{
    public static DeckSummary Build(IReadOnlyList<ResolvedCardView> cards)
    {
        if (cards.Count == 0)
        {
            return new DeckSummary();
        }

        int totalCost = 0;
        int costedCards = 0;
        int totalDamage = 0;
        int totalBlock = 0;

        foreach (ResolvedCardView card in cards)
        {
            if (card.EffectiveCost >= 0)
            {
                totalCost += card.EffectiveCost;
                costedCards++;
            }

            totalDamage += card.GetEstimatedDamage();
            totalBlock += card.GetEstimatedBlock();
        }

        return new DeckSummary
        {
            CardCount = cards.Count,
            UpgradedCardCount = cards.Count(static card => card.IsUpgraded),
            AttackCount = cards.Count(static card => card.Type == MegaCrit.Sts2.Core.Entities.Cards.CardType.Attack),
            SkillCount = cards.Count(static card => card.Type == MegaCrit.Sts2.Core.Entities.Cards.CardType.Skill),
            PowerCount = cards.Count(static card => card.Type == MegaCrit.Sts2.Core.Entities.Cards.CardType.Power),
            FrontloadDamageSources = cards.Count(static card => card.GetEstimatedDamage() > 0),
            BlockSources = cards.Count(static card => card.GetEstimatedBlock() > 0),
            DrawSources = cards.Count(static card => card.GetCardsDrawn() > 0),
            EnergySources = cards.Count(static card => card.GetEnergyGain() > 0),
            VulnerableSources = cards.Count(static card => card.GetEnemyVulnerableAmount() > 0),
            WeakSources = cards.Count(static card => card.GetEnemyWeakAmount() > 0),
            ScalingSources = cards.Count(IsScalingCard),
            RetainCards = cards.Count(static card => card.Retain),
            ExhaustCards = cards.Count(static card => card.Exhaust),
            ZeroCostCards = cards.Count(static card => card.EffectiveCost == 0),
            HighCostCards = cards.Count(static card => card.EffectiveCost >= 2),
            AverageCost = costedCards > 0 ? (double)totalCost / costedCards : 0d,
            AverageDamage = (double)totalDamage / cards.Count,
            AverageBlock = (double)totalBlock / cards.Count
        };
    }

    private static bool IsScalingCard(ResolvedCardView card)
    {
        int persistentStrength = Math.Max(0, card.GetTotalStrengthAmount() - card.GetTotalTemporaryStrengthAmount());
        int persistentDexterity = Math.Max(0, card.GetTotalDexterityAmount() - card.GetTotalTemporaryDexterityAmount());
        return card.Type == MegaCrit.Sts2.Core.Entities.Cards.CardType.Power ||
               persistentStrength > 0 ||
               persistentDexterity > 0 ||
               (card.GetEnergyGain() > 0 && card.Effects.Any(static effect => effect.ValueTiming != ValueTiming.Immediate));
    }
}

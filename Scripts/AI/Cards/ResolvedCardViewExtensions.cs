using System;
using System.Collections.Generic;
using System.Linq;

namespace AITeammate.Scripts;

internal static class ResolvedCardViewExtensions
{
    public static bool HasEffect(this ResolvedCardView? card, EffectKind kind)
    {
        return card?.Effects.Any(effect => effect.Kind == kind) == true;
    }

    public static int GetEstimatedDamage(this ResolvedCardView? card)
    {
        if (card == null)
        {
            return 0;
        }

        return card.Effects
            .Where(static effect => effect.Kind == EffectKind.DealDamage)
            .Sum(effect => Math.Max(effect.Amount, 0) * Math.Max(effect.RepeatCount, 1));
    }

    public static int GetEstimatedBlock(this ResolvedCardView? card)
    {
        if (card == null)
        {
            return 0;
        }

        return card.Effects
            .Where(static effect => effect.Kind == EffectKind.GainBlock)
            .Sum(static effect => Math.Max(effect.Amount, 0));
    }

    public static int GetEffectAmount(this ResolvedCardView? card, EffectKind kind)
    {
        if (card == null)
        {
            return 0;
        }

        return card.Effects
            .Where(effect => effect.Kind == kind)
            .Sum(static effect => Math.Max(effect.Amount, 0));
    }

    public static int GetAppliedPowerAmount(this ResolvedCardView? card, string powerId, TargetScope? targetScope = null, DurationHint? durationHint = null)
    {
        if (card == null)
        {
            return 0;
        }

        return card.Effects
            .Where(effect => effect.Kind == EffectKind.ApplyPower &&
                             string.Equals(effect.AppliedPowerId, powerId, StringComparison.Ordinal) &&
                             (!targetScope.HasValue || effect.TargetScope == targetScope.Value) &&
                             (!durationHint.HasValue || effect.DurationHint == durationHint.Value))
            .Sum(static effect => Math.Max(effect.Amount, 0));
    }

    public static bool AppliesPower(this ResolvedCardView? card, string powerId)
    {
        return card.GetAppliedPowerAmount(powerId) > 0;
    }

    public static int GetEnemyVulnerableAmount(this ResolvedCardView? card)
    {
        return card.GetAppliedPowerAmount("Vulnerable", TargetScope.SingleEnemy) +
               card.GetAppliedPowerAmount("Vulnerable", TargetScope.AllEnemies);
    }

    public static int GetEnemyWeakAmount(this ResolvedCardView? card)
    {
        return card.GetAppliedPowerAmount("Weak", TargetScope.SingleEnemy) +
               card.GetAppliedPowerAmount("Weak", TargetScope.AllEnemies);
    }

    public static int GetSelfStrengthAmount(this ResolvedCardView? card)
    {
        return card.GetAppliedPowerAmount("Strength", TargetScope.Self);
    }

    public static int GetSelfDexterityAmount(this ResolvedCardView? card)
    {
        return card.GetAppliedPowerAmount("Dexterity", TargetScope.Self);
    }

    public static int GetSelfTemporaryStrengthAmount(this ResolvedCardView? card)
    {
        return card.GetAppliedPowerAmount("Strength", TargetScope.Self, DurationHint.ThisTurn);
    }

    public static int GetSelfTemporaryDexterityAmount(this ResolvedCardView? card)
    {
        return card.GetAppliedPowerAmount("Dexterity", TargetScope.Self, DurationHint.ThisTurn);
    }

    public static int GetTotalStrengthAmount(this ResolvedCardView? card)
    {
        return card.GetSelfStrengthAmount() + card.GetAllyStrengthAmount();
    }

    public static int GetTotalDexterityAmount(this ResolvedCardView? card)
    {
        return card.GetSelfDexterityAmount() + card.GetAllyDexterityAmount();
    }

    public static int GetTotalTemporaryStrengthAmount(this ResolvedCardView? card)
    {
        return card.GetSelfTemporaryStrengthAmount() + card.GetAllyTemporaryStrengthAmount();
    }

    public static int GetTotalTemporaryDexterityAmount(this ResolvedCardView? card)
    {
        return card.GetSelfTemporaryDexterityAmount() + card.GetAllyTemporaryDexterityAmount();
    }

    public static int GetAllyStrengthAmount(this ResolvedCardView? card)
    {
        return card.GetAppliedPowerAmount("Strength", TargetScope.SingleAlly) +
               card.GetAppliedPowerAmount("Strength", TargetScope.AllAllies);
    }

    public static int GetAllyDexterityAmount(this ResolvedCardView? card)
    {
        return card.GetAppliedPowerAmount("Dexterity", TargetScope.SingleAlly) +
               card.GetAppliedPowerAmount("Dexterity", TargetScope.AllAllies);
    }

    public static int GetAllyTemporaryStrengthAmount(this ResolvedCardView? card)
    {
        return card.GetAppliedPowerAmount("Strength", TargetScope.SingleAlly, DurationHint.ThisTurn) +
               card.GetAppliedPowerAmount("Strength", TargetScope.AllAllies, DurationHint.ThisTurn);
    }

    public static int GetAllyTemporaryDexterityAmount(this ResolvedCardView? card)
    {
        return card.GetAppliedPowerAmount("Dexterity", TargetScope.SingleAlly, DurationHint.ThisTurn) +
               card.GetAppliedPowerAmount("Dexterity", TargetScope.AllAllies, DurationHint.ThisTurn);
    }

    public static int GetCardsDrawn(this ResolvedCardView? card)
    {
        return card.GetEffectAmount(EffectKind.DrawCards);
    }

    public static int GetEnergyGain(this ResolvedCardView? card)
    {
        return card.GetEffectAmount(EffectKind.GainEnergy);
    }
}

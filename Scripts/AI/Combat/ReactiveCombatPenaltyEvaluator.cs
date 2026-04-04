using System;
using System.Linq;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Logging;

namespace AITeammate.Scripts;

internal static class ReactiveCombatPenaltyEvaluator
{
    public static ReactiveCombatPenaltyEvaluation Evaluate(
        DeterministicCombatContext context,
        AiLegalActionOption action,
        ResolvedCardView? card,
        int estimatedDamage,
        int damageHits,
        int availableBlockBeforeAction)
    {
        ReactiveCombatPenaltyEvaluation evaluation = new();
        bool isPlayCard = string.Equals(action.ActionType, AiTeammateActionKind.PlayCard.ToString(), StringComparison.Ordinal);
        bool isAttackLike = card != null && (card.Type == CardType.Attack || estimatedDamage > 0);

        if (isPlayCard && card != null)
        {
            foreach (DeterministicEnemyState enemy in context.EnemiesById.Values)
            {
                foreach (DeterministicEnemyReactiveState reactiveState in enemy.ReactiveStates)
                {
                    foreach (DeterministicEnemyReactiveEffectEntry effect in reactiveState.Effects)
                    {
                        if (!IsCardPlayTrigger(effect))
                        {
                            continue;
                        }

                        if (!MatchesCardCondition(effect, card, isAttackLike))
                        {
                            continue;
                        }

                        AddEffectPenalty(context, action.ActionId, effect, reactiveState, estimatedDamage, damageHits, ref availableBlockBeforeAction, ref evaluation);
                    }
                }
            }
        }

        if (estimatedDamage > 0 &&
            !string.IsNullOrWhiteSpace(action.TargetId) &&
            context.EnemiesById.TryGetValue(action.TargetId, out DeterministicEnemyState? targetEnemy))
        {
            foreach (DeterministicEnemyReactiveState reactiveState in targetEnemy.ReactiveStates)
            {
                foreach (DeterministicEnemyReactiveEffectEntry effect in reactiveState.Effects)
                {
                    if (!IsOnHitTrigger(effect))
                    {
                        continue;
                    }

                    if (!MatchesOnHitCondition(effect, card, isAttackLike))
                    {
                        continue;
                    }

                    AddEffectPenalty(context, action.ActionId, effect, reactiveState, estimatedDamage, damageHits, ref availableBlockBeforeAction, ref evaluation);
                }
            }
        }

        return evaluation;
    }

    private static void AddEffectPenalty(
        DeterministicCombatContext context,
        string actionId,
        DeterministicEnemyReactiveEffectEntry effect,
        DeterministicEnemyReactiveState reactiveState,
        int estimatedDamage,
        int damageHits,
        ref int availableBlock,
        ref ReactiveCombatPenaltyEvaluation evaluation)
    {
        AiCharacterCombatTuning tuning = context.CombatConfig.Combat;
        AiCombatCoreWeights core = tuning.CoreWeights;
        AiCombatStatusWeights status = tuning.StatusWeights;
        AiCombatResourceWeights resource = tuning.ResourceWeights;
        AiCombatRiskProfile risk = tuning.RiskProfile;
        int liveAmount = Math.Max(Math.Abs(effect.CurrentDisplayAmount), Math.Abs(effect.CurrentPowerAmount));
        int magnitude = EstimateMagnitude(effect, reactiveState, estimatedDamage, damageHits, liveAmount);
        if (magnitude <= 0 && effect.OutcomeKind != EnemyReactiveOutcomeKind.RestrictCardPlay)
        {
            magnitude = 1;
        }

        bool uncertaintyApplied = effect.RequiresRuntimeResolution || effect.StaticMagnitudeKind is EnemyReactiveMagnitudeKind.Conditional or EnemyReactiveMagnitudeKind.Unknown;
        evaluation.TriggeredEffectsCount++;
        if (uncertaintyApplied)
        {
            evaluation.ReactiveUncertaintyPenalty += Math.Max(1, core.UtilityValueWhenThreatened / 2);
        }

        switch (effect.OutcomeKind)
        {
            case EnemyReactiveOutcomeKind.RetaliateDamage:
            {
                int damage = magnitude;
                int blocked = effect.Blockability == EnemyReactiveBlockability.Unblockable
                    ? 0
                    : Math.Min(availableBlock, damage);
                availableBlock -= blocked;
                int hpLoss = effect.Blockability == EnemyReactiveBlockability.Unblockable
                    ? damage
                    : Math.Max(0, damage - blocked);

                evaluation.ReactiveDamageBlocked += blocked;
                evaluation.ReactiveDamageTaken += hpLoss;
                evaluation.RetaliationPenalty += risk.ApplySurvivalWeight(hpLoss * Math.Max(1, risk.DamageTakenPenaltyPerPoint / 3));
                break;
            }
            case EnemyReactiveOutcomeKind.AddCurseCard:
                evaluation.ReactiveCursePenalty += magnitude * (resource.DrawPenaltyWhenNotPlayable + core.UtilityValueWhenThreatened);
                break;
            case EnemyReactiveOutcomeKind.AddStatusCard:
                evaluation.ReactiveStatusPenalty += magnitude * Math.Max(resource.DrawPenaltyWhenNotPlayable, core.UtilityValueWhenThreatened);
                break;
            case EnemyReactiveOutcomeKind.AfflictCards:
                evaluation.ReactiveAfflictionPenalty += magnitude * Math.Max(resource.DrawPenaltyWhenNotPlayable, core.UtilityValueWhenSafe / 2);
                break;
            case EnemyReactiveOutcomeKind.ApplyKeyword:
                evaluation.ReactiveAfflictionPenalty += magnitude * Math.Max(resource.DrawPenaltyWhenNotPlayable, core.UtilityValueWhenThreatened / 2);
                break;
            case EnemyReactiveOutcomeKind.RestrictCardPlay:
                evaluation.CardPlayPunishmentPenalty += core.UtilityValueWhenThreatened + resource.SetupActionBonus;
                break;
            case EnemyReactiveOutcomeKind.ModifyCardCost:
                evaluation.CardPlayPunishmentPenalty += magnitude * Math.Max(1, resource.EnergyGainValue / 2);
                break;
            case EnemyReactiveOutcomeKind.ApplyDebuff:
            case EnemyReactiveOutcomeKind.ApplyPower:
            case EnemyReactiveOutcomeKind.GainStrength:
            case EnemyReactiveOutcomeKind.GainDexterity:
                evaluation.ReactiveDebuffPenalty += ScorePowerLikeReactivePenalty(effect, status, reactiveState);
                break;
            case EnemyReactiveOutcomeKind.GainBlock:
                evaluation.EnemyReactiveBuffPenalty += magnitude * core.DirectDamageValuePerPoint;
                break;
            case EnemyReactiveOutcomeKind.Stun:
                evaluation.CardPlayPunishmentPenalty += core.UtilityValueWhenThreatened + resource.SetupActionBonus;
                break;
            case EnemyReactiveOutcomeKind.ModifyIncomingDamage:
            case EnemyReactiveOutcomeKind.SpecialPunishment:
                evaluation.CardPlayPunishmentPenalty += Math.Max(1, core.UtilityValueWhenThreatened / 2);
                break;
        }

        if (ShouldLogMagnitudeUsage(effect, liveAmount, magnitude))
        {
            string payload = effect.AppliedPowerId ?? effect.AfflictionId ?? effect.AddedCardId ?? effect.AppliedKeyword ?? string.Empty;
            Log.Debug(
                $"[AITeammate][ReactivePenalty] actionId={actionId} power={reactiveState.PowerId} outcome={effect.OutcomeKind} trigger={effect.TriggerKind} staticMagnitude={effect.StaticMagnitude?.ToString() ?? "?"} staticKind={effect.StaticMagnitudeKind} currentAmount={effect.CurrentPowerAmount} currentDisplayAmount={effect.CurrentDisplayAmount} usedMagnitude={magnitude} uncertaintyApplied={uncertaintyApplied} payload={payload}");
        }
    }

    private static int ScorePowerLikeReactivePenalty(
        DeterministicEnemyReactiveEffectEntry effect,
        AiCombatStatusWeights status,
        DeterministicEnemyReactiveState reactiveState)
    {
        int magnitude = EstimateMagnitude(effect, reactiveState, estimatedDamage: 0, damageHits: 1, liveAmount: Math.Max(Math.Abs(effect.CurrentDisplayAmount), Math.Abs(effect.CurrentPowerAmount)));
        bool affectsEnemySelf = effect.TargetScope is EnemyReactiveTargetScope.Owner or EnemyReactiveTargetScope.Mixed;
        bool affectsPlayer = effect.TargetScope is EnemyReactiveTargetScope.Attacker or EnemyReactiveTargetScope.ActingPlayer or EnemyReactiveTargetScope.OpposingSide or EnemyReactiveTargetScope.Mixed;

        if (string.Equals(effect.AppliedPowerId, "StrengthPower", StringComparison.Ordinal))
        {
            if (affectsEnemySelf)
            {
                return magnitude * status.PersistentStrengthMinimumValue;
            }

            if (affectsPlayer)
            {
                return magnitude * Math.Max(status.PersistentStrengthMinimumValue, status.TemporaryStrengthMinimumValue);
            }
        }

        if (string.Equals(effect.AppliedPowerId, "DexterityPower", StringComparison.Ordinal))
        {
            if (affectsEnemySelf)
            {
                return magnitude * status.PersistentDexterityMinimumValue;
            }

            if (affectsPlayer)
            {
                return magnitude * Math.Max(status.PersistentDexterityMinimumValue, status.TemporaryDexterityMinimumValue);
            }
        }

        return magnitude * Math.Max(status.WeakImmediateDefenseValue / 2, 6);
    }

    private static int EstimateMagnitude(
        DeterministicEnemyReactiveEffectEntry effect,
        DeterministicEnemyReactiveState reactiveState,
        int estimatedDamage,
        int damageHits,
        int liveAmount)
    {
        if (effect.Descriptor.Notes.Contains("BlockedDamage", StringComparison.Ordinal))
        {
            return Math.Max(0, Math.Min(estimatedDamage, reactiveState.CreatureBlock()));
        }

        int? staticMagnitude = effect.StaticMagnitude.HasValue ? Math.Abs(effect.StaticMagnitude.Value) : null;
        if (ShouldPreferLiveAmount(effect, liveAmount))
        {
            if (effect.OutcomeKind is EnemyReactiveOutcomeKind.AddStatusCard or EnemyReactiveOutcomeKind.AddCurseCard && effect.Descriptor.Notes.Contains("*", StringComparison.Ordinal))
            {
                return Math.Max(1, liveAmount) * Math.Max(1, damageHits);
            }

            return Math.Max(1, liveAmount);
        }

        if (staticMagnitude.HasValue)
        {
            return staticMagnitude.Value;
        }

        if (effect.OutcomeKind is EnemyReactiveOutcomeKind.AddStatusCard or EnemyReactiveOutcomeKind.AddCurseCard && effect.Descriptor.Notes.Contains("*", StringComparison.Ordinal))
        {
            return Math.Max(1, liveAmount) * Math.Max(1, damageHits);
        }

        return Math.Max(1, liveAmount);
    }

    private static bool ShouldPreferLiveAmount(DeterministicEnemyReactiveEffectEntry effect, int liveAmount)
    {
        if (liveAmount <= 0)
        {
            return false;
        }

        if (effect.OutcomeKind is EnemyReactiveOutcomeKind.AddStatusCard or
            EnemyReactiveOutcomeKind.AddCurseCard or
            EnemyReactiveOutcomeKind.AfflictCards or
            EnemyReactiveOutcomeKind.ApplyKeyword)
        {
            return true;
        }

        if (effect.StaticMagnitudeKind is EnemyReactiveMagnitudeKind.RuntimeComputed or EnemyReactiveMagnitudeKind.Conditional or EnemyReactiveMagnitudeKind.ChoiceDependent)
        {
            return !effect.StaticMagnitude.HasValue || effect.StaticMagnitude.Value <= 1;
        }

        return false;
    }

    private static bool ShouldLogMagnitudeUsage(DeterministicEnemyReactiveEffectEntry effect, int liveAmount, int usedMagnitude)
    {
        if (effect.OutcomeKind is EnemyReactiveOutcomeKind.AddStatusCard or
            EnemyReactiveOutcomeKind.AddCurseCard or
            EnemyReactiveOutcomeKind.AfflictCards or
            EnemyReactiveOutcomeKind.ApplyKeyword)
        {
            return liveAmount > 0;
        }

        return effect.StaticMagnitudeKind is EnemyReactiveMagnitudeKind.RuntimeComputed or EnemyReactiveMagnitudeKind.Conditional
            && liveAmount > 0
            && usedMagnitude > 1;
    }

    private static bool IsCardPlayTrigger(DeterministicEnemyReactiveEffectEntry effect)
    {
        return effect.TriggerKind is EnemyReactiveTriggerKind.BeforeCardPlayed or EnemyReactiveTriggerKind.AfterCardPlayed or EnemyReactiveTriggerKind.ShouldPlay or EnemyReactiveTriggerKind.TryModifyEnergyCostInCombat;
    }

    private static bool IsOnHitTrigger(DeterministicEnemyReactiveEffectEntry effect)
    {
        return effect.TriggerKind is EnemyReactiveTriggerKind.BeforeDamageReceived or EnemyReactiveTriggerKind.AfterDamageReceived or EnemyReactiveTriggerKind.AfterAttack;
    }

    private static bool MatchesCardCondition(DeterministicEnemyReactiveEffectEntry effect, ResolvedCardView card, bool isAttackLike)
    {
        string notes = effect.Descriptor.Notes;
        if (notes.Contains("CardType.Skill", StringComparison.Ordinal))
        {
            return card.Type == CardType.Skill;
        }

        if (notes.Contains("CardType.Power", StringComparison.Ordinal))
        {
            return card.Type == CardType.Power;
        }

        if (notes.Contains("CardType.Attack", StringComparison.Ordinal))
        {
            return card.Type == CardType.Attack;
        }

        if (notes.Contains("IsPoweredAttack", StringComparison.Ordinal) || notes.Contains("props.IsPoweredAttack", StringComparison.Ordinal))
        {
            return isAttackLike;
        }

        return true;
    }

    private static bool MatchesOnHitCondition(DeterministicEnemyReactiveEffectEntry effect, ResolvedCardView? card, bool isAttackLike)
    {
        string notes = effect.Descriptor.Notes;
        if (notes.Contains("CardType.Skill", StringComparison.Ordinal) && card != null)
        {
            return card.Type == CardType.Skill;
        }

        if (notes.Contains("CardType.Power", StringComparison.Ordinal) && card != null)
        {
            return card.Type == CardType.Power;
        }

        if (notes.Contains("CardType.Attack", StringComparison.Ordinal) && card != null)
        {
            return card.Type == CardType.Attack;
        }

        if (notes.Contains("IsPoweredAttack", StringComparison.Ordinal) || notes.Contains("props.IsPoweredAttack", StringComparison.Ordinal))
        {
            return isAttackLike;
        }

        return true;
    }

    private static int CreatureBlock(this DeterministicEnemyReactiveState reactiveState)
    {
        return reactiveState.Power.Owner?.Block ?? 0;
    }
}

internal struct ReactiveCombatPenaltyEvaluation
{
    public int RetaliationPenalty;

    public int ReactiveStatusPenalty;

    public int ReactiveCursePenalty;

    public int ReactiveAfflictionPenalty;

    public int ReactiveDebuffPenalty;

    public int CardPlayPunishmentPenalty;

    public int EnemyReactiveBuffPenalty;

    public int ReactiveUncertaintyPenalty;

    public int ReactiveDamageTaken;

    public int ReactiveDamageBlocked;

    public int TriggeredEffectsCount;

    public int TotalScorePenalty =>
        RetaliationPenalty +
        ReactiveStatusPenalty +
        ReactiveCursePenalty +
        ReactiveAfflictionPenalty +
        ReactiveDebuffPenalty +
        CardPlayPunishmentPenalty +
        EnemyReactiveBuffPenalty +
        ReactiveUncertaintyPenalty;
}

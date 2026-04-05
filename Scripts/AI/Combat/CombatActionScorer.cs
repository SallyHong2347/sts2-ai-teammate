using System;
using System.Linq;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;

namespace AITeammate.Scripts;

internal sealed class CombatActionScorer
{
    public CombatActionScore Score(DeterministicCombatContext context, AiLegalActionOption action)
    {
        AiCharacterCombatTuning tuning = context.CombatConfig.Combat;
        AiCombatRiskProfile risk = tuning.RiskProfile;

        if (string.Equals(action.ActionType, AiTeammateActionKind.EndTurn.ToString(), StringComparison.Ordinal))
        {
            return new CombatActionScore
            {
                ActionId = action.ActionId,
                Category = CombatActionCategory.EndTurn,
                TotalScore = ScoreEndTurn(context)
            };
        }

        if (string.Equals(action.ActionType, AiTeammateActionKind.UsePotion.ToString(), StringComparison.Ordinal))
        {
            return new CombatActionScore
            {
                ActionId = action.ActionId,
                Category = CombatActionCategory.Potion,
                TotalScore = ScorePotion(context, action)
            };
        }

        ResolvedCardView? card = ResolveCard(context, action);
        if (card == null)
        {
            return new CombatActionScore
            {
                ActionId = action.ActionId,
                Category = CombatActionCategory.Utility,
                TotalScore = ScoreUtility(context, action)
            };
        }

        int immediateDamageScore = ScoreImmediateDamage(context, action, card);
        int immediateDefenseScore = ScoreImmediateDefense(context, action, card);
        int enemyDebuffScore = ScoreEnemyDebuff(context, action, card);
        int selfBuffScore = ScoreSelfBuff(context, action, card);
        int resourceSetupScore = ScoreResourceSetup(context, action, card);
        int killPotentialScore = ScoreKillPotential(context, action, card);
        ReactiveCombatPenaltyEvaluation reactivePenalty = ReactiveCombatPenaltyEvaluator.Evaluate(
            context,
            action,
            card,
            immediateDamageScore > 0 ? card.GetEstimatedDamage() : 0,
            GetDamageHits(card),
            context.CurrentBlock);
        int totalScore = risk.ApplyAttackWeight(immediateDamageScore) +
                         risk.ApplyDefenseWeight(immediateDefenseScore) +
                         enemyDebuffScore +
                         selfBuffScore +
                         resourceSetupScore +
                         killPotentialScore +
                         ScoreEnergyEfficiency(context, action) -
                         reactivePenalty.TotalScorePenalty;

        CombatActionCategory category = Classify(card, immediateDamageScore, immediateDefenseScore, selfBuffScore, resourceSetupScore);
        Log.Debug(
            $"[AITeammate] Semantic score actionId={action.ActionId} category={category} damage={immediateDamageScore} defense={immediateDefenseScore} debuff={enemyDebuffScore} buff={selfBuffScore} setup={resourceSetupScore} kill={killPotentialScore} retaliationPenalty={reactivePenalty.RetaliationPenalty} reactiveStatusPenalty={reactivePenalty.ReactiveStatusPenalty} reactiveCursePenalty={reactivePenalty.ReactiveCursePenalty} reactiveAfflictionPenalty={reactivePenalty.ReactiveAfflictionPenalty} reactiveDebuffPenalty={reactivePenalty.ReactiveDebuffPenalty} cardPlayPunishmentPenalty={reactivePenalty.CardPlayPunishmentPenalty} enemyReactiveBuffPenalty={reactivePenalty.EnemyReactiveBuffPenalty} reactiveUncertaintyPenalty={reactivePenalty.ReactiveUncertaintyPenalty} reactiveDamagePenalty={reactivePenalty.ReactiveDamageTaken} total={totalScore}");

        return new CombatActionScore
        {
            ActionId = action.ActionId,
            Category = category,
            TotalScore = totalScore
        };
    }

    private static CombatActionCategory Classify(
        ResolvedCardView card,
        int immediateDamageScore,
        int immediateDefenseScore,
        int selfBuffScore,
        int resourceSetupScore)
    {
        if (immediateDefenseScore >= Math.Max(immediateDamageScore, selfBuffScore) && card.HasEffect(EffectKind.GainBlock))
        {
            return CombatActionCategory.Block;
        }

        if (immediateDamageScore > 0 && (card.HasEffect(EffectKind.DealDamage) || card.Type == CardType.Attack))
        {
            return CombatActionCategory.Attack;
        }

        if (selfBuffScore >= resourceSetupScore && card.Type == CardType.Power)
        {
            return CombatActionCategory.PowerSetup;
        }

        if (resourceSetupScore > 0)
        {
            return CombatActionCategory.Utility;
        }

        return CombatActionCategory.Utility;
    }

    private static ResolvedCardView? ResolveCard(DeterministicCombatContext context, AiLegalActionOption action)
    {
        if (!string.IsNullOrEmpty(action.CardInstanceId) &&
            context.HandCardsByInstanceId.TryGetValue(action.CardInstanceId, out ResolvedCardView? liveCard))
        {
            return liveCard;
        }

        return null;
    }

    private static int ScoreImmediateDamage(DeterministicCombatContext context, AiLegalActionOption action, ResolvedCardView card)
    {
        AiCombatCoreWeights core = context.CombatConfig.Combat.CoreWeights;
        AiCombatStatusWeights status = context.CombatConfig.Combat.StatusWeights;
        int damage = card.GetEstimatedDamage();
        if (damage <= 0)
        {
            return 0;
        }

        int score = damage * core.DirectDamageValuePerPoint;
        int uncoveredDamage = Math.Max(0, context.IncomingDamage - context.CurrentBlock);
        if (uncoveredDamage > 0 && HasPlayableBlockAction(context))
        {
            score -= core.AttackWhileDefenseNeededPenalty;
        }

        if (!string.IsNullOrEmpty(action.TargetId) &&
            context.EnemiesById.TryGetValue(action.TargetId, out DeterministicEnemyState? enemy))
        {
            int effectiveHp = enemy.CurrentHp + enemy.Block;
            score += Math.Max(0, core.TargetLowHealthBiasThreshold - effectiveHp) * core.TargetLowHealthBiasValuePerPoint;
            if (enemy.IsAttacking)
            {
                score += core.AttackingTargetBonus;
            }
        }

        score += GetActorPowerAmount(context, "STRENGTH") * Math.Max(1, GetDamageHits(card)) * status.StrengthPerHitValue;
        score += card.GetSelfTemporaryStrengthAmount() * status.SelfTemporaryStrengthValue;
        return score;
    }

    private static int ScoreImmediateDefense(DeterministicCombatContext context, AiLegalActionOption action, ResolvedCardView card)
    {
        AiCombatStatusWeights status = context.CombatConfig.Combat.StatusWeights;
        AiCombatRiskProfile risk = context.CombatConfig.Combat.RiskProfile;
        int uncoveredDamage = Math.Max(0, context.IncomingDamage - context.CurrentBlock);
        int block = card.GetEstimatedBlock();
        int weakAmount = card.GetEnemyWeakAmount();
        int temporaryDexterity = card.GetSelfTemporaryDexterityAmount();
        int dexterity = Math.Max(0, card.GetSelfDexterityAmount() - temporaryDexterity);
        int weakPrevention = EstimateWeakPrevention(context, action, weakAmount);
        int blockedDamage = Math.Min(block, uncoveredDamage);

        int score = 0;
        if (block > 0)
        {
            score += blockedDamage * risk.BlockedDamageValuePerPoint;
            score += Math.Max(0, block - uncoveredDamage) * risk.ExcessBlockValuePerPoint;
            if (uncoveredDamage > 0 && block >= uncoveredDamage)
            {
                score += risk.FullBlockCoverageBonus;
            }
        }

        if (weakPrevention > 0)
        {
            score += weakPrevention * status.WeakImmediateDefenseValue;
        }

        if (temporaryDexterity > 0)
        {
            int nearTermBlockValue = HasAffordableBlockFollowUp(context, action)
                ? status.TemporaryDexterityWithFollowUpBlockValue
                : (uncoveredDamage > 0 ? status.TemporaryDexterityThreatenedBlockValue : status.TemporaryDexteritySafeBlockValue);
            score += temporaryDexterity * nearTermBlockValue;
        }

        if (dexterity > 0)
        {
            int futureBlockValue = HasPlayableBlockAction(context)
                ? status.PersistentDexterityWithBlockValue
                : status.PersistentDexterityWithoutBlockValue;
            score += dexterity * futureBlockValue;
        }

        if (context.CurrentHp <= Math.Max(risk.LowHealthEmergencyThreshold, context.IncomingDamage))
        {
            score += risk.LowHealthEmergencyDefenseBonus;
        }

        return score;
    }

    private static int ScoreEnemyDebuff(DeterministicCombatContext context, AiLegalActionOption action, ResolvedCardView card)
    {
        AiCombatStatusWeights status = context.CombatConfig.Combat.StatusWeights;
        int score = 0;
        int vulnerable = card.GetEnemyVulnerableAmount();
        int weak = card.GetEnemyWeakAmount();

        if (vulnerable > 0)
        {
            int followUpAttacks = CountAffordableAttackActions(context, action);
            score += vulnerable * (followUpAttacks > 0 ? status.VulnerableWithFollowUpValue : status.VulnerableWithoutFollowUpValue);
        }

        if (weak > 0)
        {
            score += EstimateWeakPrevention(context, action, weak) * status.WeakDebuffValue;
        }

        return score;
    }

    private static int ScoreSelfBuff(DeterministicCombatContext context, AiLegalActionOption action, ResolvedCardView card)
    {
        AiCombatStatusWeights status = context.CombatConfig.Combat.StatusWeights;
        int temporaryStrength = card.GetSelfTemporaryStrengthAmount();
        int totalStrength = card.GetSelfStrengthAmount();
        int persistentStrength = Math.Max(0, totalStrength - temporaryStrength);
        int temporaryDexterity = card.GetSelfTemporaryDexterityAmount();
        int totalDexterity = card.GetSelfDexterityAmount();
        int persistentDexterity = Math.Max(0, totalDexterity - temporaryDexterity);

        int score = 0;
        if (temporaryStrength > 0)
        {
            score += temporaryStrength * Math.Max(
                status.TemporaryStrengthMinimumValue,
                CountAffordableAttackActions(context, action) * status.TemporaryStrengthPerAffordableAttackValue);
        }

        if (persistentStrength > 0)
        {
            score += persistentStrength * Math.Max(
                status.PersistentStrengthMinimumValue,
                CountAffordableAttackActions(context, action) * status.PersistentStrengthPerAffordableAttackValue);
        }

        if (temporaryDexterity > 0)
        {
            score += temporaryDexterity * Math.Max(
                status.TemporaryDexterityMinimumValue,
                CountAffordableBlockActions(context, action) * status.TemporaryDexterityPerAffordableBlockValue);
        }

        if (persistentDexterity > 0)
        {
            score += persistentDexterity * Math.Max(
                status.PersistentDexterityMinimumValue,
                CountAffordableBlockActions(context, action) * status.PersistentDexterityPerAffordableBlockValue);
        }

        return score;
    }

    private static int ScoreResourceSetup(DeterministicCombatContext context, AiLegalActionOption action, ResolvedCardView card)
    {
        AiCombatResourceWeights resource = context.CombatConfig.Combat.ResourceWeights;
        int cardsDrawn = card.GetCardsDrawn();
        int energyGain = card.GetEnergyGain();
        int score = 0;

        if (cardsDrawn > 0)
        {
            bool hasSpendableFollowUp = CountAffordablePlayableActions(context, action, extraEnergy: energyGain) > 0;
            score += hasSpendableFollowUp
                ? cardsDrawn * resource.DrawValueWhenPlayable
                : -cardsDrawn * resource.DrawPenaltyWhenNotPlayable;
        }

        if (energyGain > 0)
        {
            score += energyGain * resource.EnergyGainValue;
        }

        return score;
    }

    private static int ScoreKillPotential(DeterministicCombatContext context, AiLegalActionOption action, ResolvedCardView card)
    {
        AiCombatRiskProfile risk = context.CombatConfig.Combat.RiskProfile;
        if (string.IsNullOrEmpty(action.TargetId) ||
            !context.EnemiesById.TryGetValue(action.TargetId, out DeterministicEnemyState? enemy))
        {
            return 0;
        }

        int estimatedDamage = card.GetEstimatedDamage();
        int effectiveEnemyHp = enemy.CurrentHp + enemy.Block;
        if (estimatedDamage >= effectiveEnemyHp)
        {
            return risk.LethalPriorityBonus + enemy.IncomingDamage * risk.LethalIncomingDamageValue;
        }

        return 0;
    }

    private static int ScoreUtility(DeterministicCombatContext context, AiLegalActionOption action)
    {
        AiCombatCoreWeights core = context.CombatConfig.Combat.CoreWeights;
        int uncoveredDamage = Math.Max(0, context.IncomingDamage - context.CurrentBlock);
        int score = uncoveredDamage > 0 ? core.UtilityValueWhenThreatened : core.UtilityValueWhenSafe;
        score += ScoreEnergyEfficiency(context, action);
        return score;
    }

    private static int ScorePotion(DeterministicCombatContext context, AiLegalActionOption action)
    {
        AiPotionCombatUseWeights potionUse = context.CombatConfig.Potions.CombatUse;
        AiCombatCoreWeights core = context.CombatConfig.Combat.CoreWeights;
        AiCombatStatusWeights status = context.CombatConfig.Combat.StatusWeights;
        AiCombatResourceWeights resource = context.CombatConfig.Combat.ResourceWeights;
        AiCombatRiskProfile risk = context.CombatConfig.Combat.RiskProfile;
        bool graveDanger = IsGraveDanger(context);
        PotionCombatScoreBreakdown breakdown = BuildPotionScoreBreakdown(context, action, potionUse, core, status, resource, risk, graveDanger);

        string targetSummary = string.IsNullOrEmpty(action.TargetId)
            ? "none"
            : (context.EnemiesById.TryGetValue(action.TargetId, out DeterministicEnemyState? targetEnemy)
                ? $"{targetEnemy.Id}(hp={targetEnemy.CurrentHp},block={targetEnemy.Block},incoming={targetEnemy.IncomingDamage},attacking={targetEnemy.IsAttacking})"
                : (context.AlliesById.TryGetValue(action.TargetId, out DeterministicAllyState? targetAlly)
                    ? $"{targetAlly.Id}(hp={targetAlly.CurrentHp},block={targetAlly.Block},incoming={targetAlly.IncomingDamage},actor={targetAlly.IsActor})"
                    : action.TargetId));
        Log.Info(
            $"[AITeammate][Potion] Score actor={context.Actor.NetId} actionId={action.ActionId} potion={breakdown.PotionId} target={targetSummary} metadata={breakdown.MetadataSource} graveDanger={graveDanger} baseScore={breakdown.BaseScore} consumptionCostPenalty={breakdown.ConsumptionCostPenalty} enemyDamageValue={breakdown.EnemyDamageValue} aoeEnemyValue={breakdown.AoeEnemyValue} lethalValue={breakdown.LethalValue} selfDamagePenalty={breakdown.SelfDamagePenalty} teammateDamagePenalty={breakdown.TeammateDamagePenalty} allyDamagePenalty={breakdown.AllyDamagePenalty} selfDebuffPenalty={breakdown.SelfDebuffPenalty} allyDebuffPenalty={breakdown.AllyDebuffPenalty} healValue={breakdown.HealValue} blockValue={breakdown.BlockValue} buffValue={breakdown.BuffValue} utilityValue={breakdown.UtilityValue} overkillPenalty={breakdown.OverkillPenalty} uncertaintyPenalty={breakdown.UncertaintyPenalty} graveDangerAdjustment={breakdown.GraveDangerAdjustment} followUpBonus={breakdown.FollowUpBonus} attackingTargetBonus={breakdown.AttackingTargetBonus} finalScore={breakdown.FinalScore}");

        return breakdown.FinalScore;
    }

    private static int ScoreEndTurn(DeterministicCombatContext context)
    {
        AiCombatResourceWeights resource = context.CombatConfig.Combat.ResourceWeights;
        if (ShouldPreferEndTurnOverRemainingPotions(context))
        {
            return resource.EndTurnWhenSkippingPotionsBonus;
        }

        return 0;
    }

    private static int ScoreEnergyEfficiency(DeterministicCombatContext context, AiLegalActionOption action)
    {
        if (!action.EnergyCost.HasValue)
        {
            return 0;
        }

        return Math.Max(0, 4 - action.EnergyCost.Value) * context.CombatConfig.Combat.ResourceWeights.EnergyEfficiencyValue;
    }

    private static int GetActorPowerAmount(DeterministicCombatContext context, string powerId)
    {
        return context.ActorPowerAmounts.TryGetValue(powerId, out int amount) ? amount : 0;
    }

    private static int GetDamageHits(ResolvedCardView card)
    {
        return card.Effects
            .Where(static effect => effect.Kind == EffectKind.DealDamage)
            .Sum(static effect => Math.Max(effect.RepeatCount, 1));
    }

    private static int EstimateWeakPrevention(DeterministicCombatContext context, AiLegalActionOption action, int weakAmount)
    {
        if (weakAmount <= 0)
        {
            return 0;
        }

        if (!string.IsNullOrEmpty(action.TargetId) &&
            context.EnemiesById.TryGetValue(action.TargetId, out DeterministicEnemyState? enemy))
        {
            return Math.Max(1, enemy.IncomingDamage / 4);
        }

        return Math.Max(1, context.IncomingDamage / 6);
    }

    private static bool HasPlayableBlockAction(DeterministicCombatContext context)
    {
        foreach (AiLegalActionOption action in context.LegalActions)
        {
            ResolvedCardView? card = ResolveCard(context, action);
            if (card?.HasEffect(EffectKind.GainBlock) == true)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasAffordableBlockFollowUp(DeterministicCombatContext context, AiLegalActionOption currentAction)
    {
        return CountAffordableBlockActions(context, currentAction) > 0;
    }

    private static int CountAffordableAttackActions(DeterministicCombatContext context, AiLegalActionOption currentAction)
    {
        return context.LegalActions.Count(candidate =>
        {
            if (string.Equals(candidate.ActionId, currentAction.ActionId, StringComparison.Ordinal))
            {
                return false;
            }

            if ((candidate.EnergyCost ?? 0) > Math.Max(0, context.Energy - (currentAction.EnergyCost ?? 0)))
            {
                return false;
            }

            ResolvedCardView? card = ResolveCard(context, candidate);
            return card?.HasEffect(EffectKind.DealDamage) == true || card?.Type == CardType.Attack;
        });
    }

    private static int CountAffordableBlockActions(DeterministicCombatContext context, AiLegalActionOption currentAction)
    {
        return context.LegalActions.Count(candidate =>
        {
            if (string.Equals(candidate.ActionId, currentAction.ActionId, StringComparison.Ordinal))
            {
                return false;
            }

            if ((candidate.EnergyCost ?? 0) > Math.Max(0, context.Energy - (currentAction.EnergyCost ?? 0)))
            {
                return false;
            }

            ResolvedCardView? card = ResolveCard(context, candidate);
            return card?.HasEffect(EffectKind.GainBlock) == true;
        });
    }

    private static int CountAffordablePlayableActions(DeterministicCombatContext context, AiLegalActionOption currentAction, int extraEnergy)
    {
        int remainingEnergy = Math.Max(0, context.Energy - (currentAction.EnergyCost ?? 0) + extraEnergy);
        return context.LegalActions.Count(candidate =>
            !string.Equals(candidate.ActionId, currentAction.ActionId, StringComparison.Ordinal) &&
            !string.Equals(candidate.ActionType, AiTeammateActionKind.EndTurn.ToString(), StringComparison.Ordinal) &&
            (candidate.EnergyCost ?? 0) <= remainingEnergy);
    }

    private static int CountNonPotionAttackActions(DeterministicCombatContext context)
    {
        int count = 0;
        foreach (AiLegalActionOption candidate in context.LegalActions)
        {
            if (string.Equals(candidate.ActionType, AiTeammateActionKind.UsePotion.ToString(), StringComparison.Ordinal))
            {
                continue;
            }

            ResolvedCardView? card = ResolveCard(context, candidate);
            if (card?.HasEffect(EffectKind.DealDamage) == true || card?.Type == CardType.Attack)
            {
                count++;
            }
        }

        return count;
    }

    private static PotionCombatScoreBreakdown BuildPotionScoreBreakdown(
        DeterministicCombatContext context,
        AiLegalActionOption action,
        AiPotionCombatUseWeights potionUse,
        AiCombatCoreWeights core,
        AiCombatStatusWeights status,
        AiCombatResourceWeights resource,
        AiCombatRiskProfile risk,
        bool graveDanger)
    {
        string potionId = action.CardId ?? "unknown";
        bool hasMetadata = PotionMetadataRepository.Shared.TryGet(potionId, out PotionMetadata? metadata) && metadata != null;
        PotionMetadata effectiveMetadata = metadata ?? new PotionMetadata
        {
            PotionId = potionId,
            DisplayName = potionId,
            SourceTypeName = "fallback",
            Usage = "Unknown",
            DeclaredTargetType = "Unknown",
            TargetKinds = [],
            Effects = [],
            HasPartialUnknowns = true,
            RequiresRuntimeResolution = true,
            Offensive = false,
            Defensive = false,
            Utility = true,
            Notes = "Potion metadata unavailable during combat scoring."
        };

        PotionCombatScoreBreakdown breakdown = new()
        {
            PotionId = potionId,
            MetadataSource = hasMetadata ? effectiveMetadata.SourceTypeName : "metadata-missing",
            BaseScore = context.IsEliteOrBossCombat ? potionUse.EliteBossBaseScore + potionUse.EliteBossBonus : potionUse.NormalFightBaseScore,
            ConsumptionCostPenalty = context.IsEliteOrBossCombat ? 10 : 18,
            UncertaintyPenalty = GetMetadataUncertaintyPenalty(effectiveMetadata)
        };

        if (graveDanger)
        {
            if (effectiveMetadata.Defensive)
            {
                breakdown.GraveDangerAdjustment += potionUse.GraveDangerDefensiveBonus;
            }
            else if (effectiveMetadata.Offensive)
            {
                breakdown.GraveDangerAdjustment += effectiveMetadata.HarmsSelf || effectiveMetadata.HarmsTeammate || effectiveMetadata.HarmsAllAllies
                    ? Math.Max(0, potionUse.GraveDangerOffensiveBonus / 3)
                    : potionUse.GraveDangerOffensiveBonus;
            }
        }

        if (effectiveMetadata.Offensive && !effectiveMetadata.HarmsSelf && !effectiveMetadata.HarmsTeammate && CountNonPotionAttackActions(context) > 0)
        {
            breakdown.FollowUpBonus += context.IsEliteOrBossCombat
                ? potionUse.EliteBossOffensiveFollowUpBonus
                : potionUse.NormalFightOffensiveFollowUpBonus;
        }

        if (!string.IsNullOrEmpty(action.TargetId) &&
            context.EnemiesById.TryGetValue(action.TargetId, out DeterministicEnemyState? targetEnemy) &&
            targetEnemy.IsAttacking)
        {
            breakdown.AttackingTargetBonus += potionUse.AttackingTargetBonus;
        }
        else if (effectiveMetadata.TargetKinds.Contains(PotionMetadataTargetKind.AllEnemies))
        {
            int attackingEnemies = context.EnemiesById.Values.Count(static enemy => enemy.IsAttacking);
            breakdown.AttackingTargetBonus += Math.Min(attackingEnemies, 2) * Math.Max(1, potionUse.AttackingTargetBonus / 2);
        }

        foreach (PotionEffectDescriptor effect in effectiveMetadata.Effects)
        {
            int magnitude = ResolveEffectMagnitude(effect);
            switch (effect.Kind)
            {
                case PotionEffectKind.DealDamage:
                    ScoreDamageEffect(context, action, effect, magnitude, core, risk, breakdown);
                    break;
                case PotionEffectKind.Heal:
                    breakdown.HealValue += ScoreHealingEffect(context, action, effect, magnitude, risk);
                    break;
                case PotionEffectKind.GainBlock:
                    breakdown.BlockValue += ScoreBlockEffect(context, action, effect, magnitude, risk);
                    break;
                case PotionEffectKind.ApplyPower:
                    ScorePowerEffect(context, action, effect, magnitude, status, breakdown);
                    break;
                case PotionEffectKind.GainEnergy:
                    if (PotionEffectHitsActor(context, action, effect))
                    {
                        breakdown.UtilityValue += Math.Max(1, magnitude) * resource.EnergyGainValue;
                    }
                    break;
                case PotionEffectKind.DrawCards:
                    if (PotionEffectHitsActor(context, action, effect))
                    {
                        breakdown.UtilityValue += magnitude > 0
                            ? magnitude * resource.DrawValueWhenPlayable
                            : 0;
                    }
                    break;
                case PotionEffectKind.GainGold:
                    breakdown.UtilityValue += context.IsEliteOrBossCombat ? 0 : core.UtilityValueWhenSafe;
                    break;
                case PotionEffectKind.UpgradeCards:
                case PotionEffectKind.AddCards:
                case PotionEffectKind.ObtainPotion:
                case PotionEffectKind.DiscardCards:
                case PotionEffectKind.ManipulateDrawPile:
                case PotionEffectKind.Utility:
                    breakdown.UtilityValue += ScoreGenericUtilityEffect(effect, resource, core);
                    break;
                case PotionEffectKind.GainMaxHp:
                    breakdown.BuffValue += Math.Max(0, magnitude) * Math.Max(1, risk.BlockedDamageValuePerPoint / 2);
                    break;
            }
        }

        if (effectiveMetadata.HarmsAllAllies)
        {
            int affectedAllies = Math.Max(1, context.AlliesById.Values.Count(static ally => !ally.IsActor));
            breakdown.AllyDamagePenalty += affectedAllies * potionUse.AllyDamagePenaltyPerAlly;
        }

        return breakdown;
    }

    private static bool IsGraveDanger(DeterministicCombatContext context)
    {
        AiCombatRiskProfile risk = context.CombatConfig.Combat.RiskProfile;
        int uncoveredDamage = Math.Max(0, context.IncomingDamage - context.CurrentBlock);
        int hpFractionThreshold = (int)(context.CurrentHp * risk.GraveDangerHpFraction);
        return uncoveredDamage >= Math.Max(risk.GraveDangerFloor, hpFractionThreshold) || uncoveredDamage >= context.CurrentHp;
    }

    private static bool ShouldPreferEndTurnOverRemainingPotions(DeterministicCombatContext context)
    {
        return context.LegalActions
            .Where(action => string.Equals(action.ActionType, AiTeammateActionKind.UsePotion.ToString(), StringComparison.Ordinal))
            .All(action => ScorePotion(context, action) <= 0);
    }

    private static bool PotionEffectHitsActor(DeterministicCombatContext context, AiLegalActionOption action, PotionEffectDescriptor effect)
    {
        if (effect.CanAffectSelf)
        {
            return true;
        }

        string actorTargetId = $"player_{context.Actor.NetId}";
        return effect.TargetKind == PotionMetadataTargetKind.SingleAlly &&
               string.Equals(action.TargetId, actorTargetId, StringComparison.Ordinal);
    }

    private static IEnumerable<DeterministicAllyState> GetPotionAffectedTeammates(DeterministicCombatContext context, AiLegalActionOption action, PotionEffectDescriptor effect)
    {
        IEnumerable<DeterministicAllyState> teammates = context.AlliesById.Values.Where(static ally => !ally.IsActor);
        if (effect.CanAffectAllAllies)
        {
            return teammates;
        }

        if (effect.CanAffectTeammate)
        {
            return teammates.Where(ally => string.Equals(ally.Id, action.TargetId, StringComparison.Ordinal));
        }

        return [];
    }

    private static void ScoreDamageEffect(
        DeterministicCombatContext context,
        AiLegalActionOption action,
        PotionEffectDescriptor effect,
        int magnitude,
        AiCombatCoreWeights core,
        AiCombatRiskProfile risk,
        PotionCombatScoreBreakdown breakdown)
    {
        if (magnitude <= 0)
        {
            return;
        }

        if (effect.CanAffectSingleEnemy && !string.IsNullOrEmpty(action.TargetId) && context.EnemiesById.TryGetValue(action.TargetId, out DeterministicEnemyState? targetEnemy))
        {
            ScoreEnemyDamageAgainstTarget(targetEnemy, magnitude, core, risk, breakdown, includeAoeTerm: false);
        }

        if (effect.CanAffectAllEnemies || effect.TargetKind is PotionMetadataTargetKind.AllCreatures or PotionMetadataTargetKind.AllCreaturesExceptPets)
        {
            int enemyIndex = 0;
            foreach (DeterministicEnemyState enemy in context.EnemiesById.Values.OrderBy(static enemy => enemy.Id, StringComparer.Ordinal))
            {
                ScoreEnemyDamageAgainstTarget(enemy, magnitude, core, risk, breakdown, includeAoeTerm: enemyIndex > 0);
                enemyIndex++;
            }
        }

        if (PotionEffectHitsActor(context, action, effect))
        {
            breakdown.SelfDamagePenalty += ScoreAllyDamagePenalty(context.Actor.NetId, context.CurrentHp, context.CurrentBlock, context.IncomingDamage, magnitude, risk, isActor: true);
        }

        foreach (DeterministicAllyState ally in GetPotionAffectedTeammates(context, action, effect))
        {
            breakdown.TeammateDamagePenalty += ScoreAllyDamagePenalty(ally.Player.NetId, ally.CurrentHp, ally.Block, ally.IncomingDamage, magnitude, risk, isActor: false);
        }
    }

    private static void ScoreEnemyDamageAgainstTarget(
        DeterministicEnemyState enemy,
        int magnitude,
        AiCombatCoreWeights core,
        AiCombatRiskProfile risk,
        PotionCombatScoreBreakdown breakdown,
        bool includeAoeTerm)
    {
        int effectiveHp = enemy.CurrentHp + enemy.Block;
        int damageApplied = Math.Min(magnitude, effectiveHp);
        int overkill = Math.Max(0, magnitude - effectiveHp);

        if (includeAoeTerm)
        {
            breakdown.AoeEnemyValue += risk.ApplyAttackWeight(damageApplied * Math.Max(1, core.DirectDamageValuePerPoint / 2));
        }
        else
        {
            breakdown.EnemyDamageValue += risk.ApplyAttackWeight(damageApplied * core.DirectDamageValuePerPoint);
        }

        if (enemy.IsAttacking)
        {
            breakdown.EnemyDamageValue += core.AttackingTargetBonus;
        }

        if (magnitude >= effectiveHp)
        {
            breakdown.LethalValue += risk.LethalPriorityBonus + enemy.IncomingDamage * risk.LethalIncomingDamageValue;
        }

        if (overkill > 0)
        {
            breakdown.OverkillPenalty += overkill * Math.Max(1, core.TargetLowHealthBiasValuePerPoint);
        }
    }

    private static int ScoreAllyDamagePenalty(ulong playerId, int hp, int block, int incomingDamage, int magnitude, AiCombatRiskProfile risk, bool isActor)
    {
        int healthAfterBlock = Math.Max(1, hp + block);
        int directPenalty = risk.ApplySurvivalWeight(magnitude * risk.DamageTakenPenaltyPerPoint);
        if (magnitude >= healthAfterBlock)
        {
            directPenalty += risk.LethalPriorityBonus + incomingDamage * risk.LethalIncomingDamageValue;
        }
        else if (hp <= Math.Max(risk.LowHealthEmergencyThreshold, incomingDamage) || magnitude >= Math.Max(1, hp / 2))
        {
            directPenalty += risk.LowHealthEmergencyDefenseBonus;
        }

        if (!isActor)
        {
            directPenalty += Math.Max(10, risk.DeadEnemyReward / 2);
        }

        return directPenalty;
    }

    private static int ScoreHealingEffect(DeterministicCombatContext context, AiLegalActionOption action, PotionEffectDescriptor effect, int magnitude, AiCombatRiskProfile risk)
    {
        if (magnitude <= 0)
        {
            return 0;
        }

        int score = 0;
        if (PotionEffectHitsActor(context, action, effect))
        {
            score += ScoreHealingForActor(context.CurrentHp, context.IncomingDamage, magnitude, risk);
        }

        foreach (DeterministicAllyState ally in GetPotionAffectedTeammates(context, action, effect))
        {
            score += ScoreHealingForActor(ally.CurrentHp, ally.IncomingDamage, magnitude, risk);
        }

        return score;
    }

    private static int ScoreHealingForActor(int hp, int incomingDamage, int magnitude, AiCombatRiskProfile risk)
    {
        int urgencyMultiplier = hp <= Math.Max(risk.LowHealthEmergencyThreshold, incomingDamage) ? 2 : 1;
        return magnitude * Math.Max(1, risk.BlockedDamageValuePerPoint) * urgencyMultiplier;
    }

    private static int ScoreBlockEffect(DeterministicCombatContext context, AiLegalActionOption action, PotionEffectDescriptor effect, int magnitude, AiCombatRiskProfile risk)
    {
        if (magnitude <= 0)
        {
            return 0;
        }

        int score = 0;
        if (PotionEffectHitsActor(context, action, effect))
        {
            int prevented = Math.Min(magnitude, Math.Max(0, context.IncomingDamage - context.CurrentBlock));
            score += risk.ApplyDefenseWeight(prevented * risk.BlockedDamageValuePerPoint);
            if (magnitude >= Math.Max(0, context.IncomingDamage - context.CurrentBlock) && context.IncomingDamage > context.CurrentBlock)
            {
                score += risk.FullBlockCoverageBonus;
            }
        }

        foreach (DeterministicAllyState ally in GetPotionAffectedTeammates(context, action, effect))
        {
            int prevented = Math.Min(magnitude, Math.Max(0, ally.IncomingDamage - ally.Block));
            score += risk.ApplyDefenseWeight(prevented * Math.Max(1, risk.BlockedDamageValuePerPoint / 2));
        }

        return score;
    }

    private static void ScorePowerEffect(
        DeterministicCombatContext context,
        AiLegalActionOption action,
        PotionEffectDescriptor effect,
        int magnitude,
        AiCombatStatusWeights status,
        PotionCombatScoreBreakdown breakdown)
    {
        int stacks = Math.Max(1, magnitude);
        if (effect.IsDebuff)
        {
            int penaltyPerStack = GetDebuffPenaltyPerStack(effect.AppliedPowerId, status);
            if (PotionEffectHitsActor(context, action, effect))
            {
                breakdown.SelfDebuffPenalty += stacks * penaltyPerStack;
            }

            int affectedTeammates = GetPotionAffectedTeammates(context, action, effect).Count();
            if (affectedTeammates > 0)
            {
                breakdown.AllyDebuffPenalty += affectedTeammates * stacks * penaltyPerStack;
            }

            if (effect.CanAffectSingleEnemy || effect.CanAffectAllEnemies)
            {
                breakdown.BuffValue += ScoreEnemyDebuffValue(context, effect, stacks, status);
            }

            return;
        }

        int buffPerStack = GetBuffValuePerStack(effect.AppliedPowerId, status);
        int affectedAllies = 0;
        if (PotionEffectHitsActor(context, action, effect))
        {
            affectedAllies++;
        }

        affectedAllies += GetPotionAffectedTeammates(context, action, effect).Count();

        breakdown.BuffValue += Math.Max(1, affectedAllies) * stacks * buffPerStack;
    }

    private static int ScoreEnemyDebuffValue(DeterministicCombatContext context, PotionEffectDescriptor effect, int stacks, AiCombatStatusWeights status)
    {
        int targetCount = effect.CanAffectAllEnemies
            ? Math.Max(1, context.EnemiesById.Count)
            : 1;
        int perStack = GetDebuffValuePerStack(effect.AppliedPowerId, status, context);
        return targetCount * stacks * perStack;
    }

    private static int GetDebuffPenaltyPerStack(string? powerId, AiCombatStatusWeights status)
    {
        if (string.IsNullOrWhiteSpace(powerId))
        {
            return 12;
        }

        if (powerId.Contains("Weak", StringComparison.OrdinalIgnoreCase))
        {
            return status.WeakImmediateDefenseValue;
        }

        if (powerId.Contains("Vulnerable", StringComparison.OrdinalIgnoreCase))
        {
            return Math.Max(status.VulnerableWithoutFollowUpValue, 14);
        }

        if (powerId.Contains("Poison", StringComparison.OrdinalIgnoreCase))
        {
            return 18;
        }

        return 12;
    }

    private static int GetDebuffValuePerStack(string? powerId, AiCombatStatusWeights status, DeterministicCombatContext context)
    {
        if (string.IsNullOrWhiteSpace(powerId))
        {
            return 8;
        }

        if (powerId.Contains("Weak", StringComparison.OrdinalIgnoreCase))
        {
            return status.WeakDebuffValue;
        }

        if (powerId.Contains("Vulnerable", StringComparison.OrdinalIgnoreCase))
        {
            return CountNonPotionAttackActions(context) > 0 ? status.VulnerableWithFollowUpValue : status.VulnerableWithoutFollowUpValue;
        }

        if (powerId.Contains("Poison", StringComparison.OrdinalIgnoreCase))
        {
            return 10;
        }

        return 8;
    }

    private static int GetBuffValuePerStack(string? powerId, AiCombatStatusWeights status)
    {
        if (string.IsNullOrWhiteSpace(powerId))
        {
            return 10;
        }

        if (powerId.Contains("Strength", StringComparison.OrdinalIgnoreCase))
        {
            return status.PersistentStrengthMinimumValue;
        }

        if (powerId.Contains("Dexterity", StringComparison.OrdinalIgnoreCase))
        {
            return status.PersistentDexterityMinimumValue;
        }

        return 10;
    }

    private static int ScoreGenericUtilityEffect(PotionEffectDescriptor effect, AiCombatResourceWeights resource, AiCombatCoreWeights core)
    {
        return effect.MagnitudeKind switch
        {
            PotionMagnitudeKind.Static => core.UtilityValueWhenSafe,
            PotionMagnitudeKind.RuntimeComputed => Math.Max(1, core.UtilityValueWhenSafe / 2),
            PotionMagnitudeKind.ChoiceDependent => Math.Max(1, resource.SetupActionBonus),
            PotionMagnitudeKind.Randomized => Math.Max(1, core.UtilityValueWhenThreatened / 2),
            _ => Math.Max(1, core.UtilityValueWhenThreatened / 3)
        };
    }

    private static int GetMetadataUncertaintyPenalty(PotionMetadata metadata)
    {
        int penalty = 0;
        if (metadata.RequiresRuntimeResolution)
        {
            penalty += 18;
        }

        if (metadata.HasPartialUnknowns)
        {
            penalty += 12;
        }

        penalty += metadata.Effects.Sum(effect => effect.MagnitudeKind switch
        {
            PotionMagnitudeKind.RuntimeComputed => 4,
            PotionMagnitudeKind.ChoiceDependent => 8,
            PotionMagnitudeKind.Randomized => 10,
            PotionMagnitudeKind.Unknown => 12,
            _ => 0
        });

        return penalty;
    }

    private static int ResolveEffectMagnitude(PotionEffectDescriptor effect)
    {
        int magnitude = Math.Max(0, effect.Magnitude ?? 0);
        return effect.MagnitudeKind switch
        {
            PotionMagnitudeKind.Static => magnitude,
            PotionMagnitudeKind.RuntimeComputed => magnitude,
            PotionMagnitudeKind.ChoiceDependent => Math.Max(0, (int)Math.Round(magnitude * 0.75d, MidpointRounding.AwayFromZero)),
            PotionMagnitudeKind.Randomized => Math.Max(0, (int)Math.Round(magnitude * 0.60d, MidpointRounding.AwayFromZero)),
            _ => 0
        };
    }
}

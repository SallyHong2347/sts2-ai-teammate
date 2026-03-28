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
        int totalScore = immediateDamageScore +
                         immediateDefenseScore +
                         enemyDebuffScore +
                         selfBuffScore +
                         resourceSetupScore +
                         killPotentialScore +
                         ScoreEnergyEfficiency(action);

        CombatActionCategory category = Classify(card, immediateDamageScore, immediateDefenseScore, selfBuffScore, resourceSetupScore);
        Log.Debug(
            $"[AITeammate] Semantic score actionId={action.ActionId} category={category} damage={immediateDamageScore} defense={immediateDefenseScore} debuff={enemyDebuffScore} buff={selfBuffScore} setup={resourceSetupScore} kill={killPotentialScore} total={totalScore}");

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
        int damage = card.GetEstimatedDamage();
        if (damage <= 0)
        {
            return 0;
        }

        int score = damage * 5;
        int uncoveredDamage = Math.Max(0, context.IncomingDamage - context.CurrentBlock);
        if (uncoveredDamage > 0 && HasPlayableBlockAction(context))
        {
            score -= 18;
        }

        if (!string.IsNullOrEmpty(action.TargetId) &&
            context.EnemiesById.TryGetValue(action.TargetId, out DeterministicEnemyState? enemy))
        {
            int effectiveHp = enemy.CurrentHp + enemy.Block;
            score += Math.Max(0, 24 - effectiveHp);
            if (enemy.IsAttacking)
            {
                score += 8;
            }
        }

        score += GetActorPowerAmount(context, "STRENGTH") * Math.Max(1, GetDamageHits(card));
        score += card.GetSelfTemporaryStrengthAmount() * 8;
        return score;
    }

    private static int ScoreImmediateDefense(DeterministicCombatContext context, AiLegalActionOption action, ResolvedCardView card)
    {
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
            score += blockedDamage * 10;
            score += Math.Max(0, block - uncoveredDamage) * 2;
            if (uncoveredDamage > 0 && block >= uncoveredDamage)
            {
                score += 50;
            }
        }

        if (weakPrevention > 0)
        {
            score += weakPrevention * 12;
        }

        if (temporaryDexterity > 0)
        {
            int nearTermBlockValue = HasAffordableBlockFollowUp(context, action) ? 18 : (uncoveredDamage > 0 ? 12 : 6);
            score += temporaryDexterity * nearTermBlockValue;
        }

        if (dexterity > 0)
        {
            int futureBlockValue = HasPlayableBlockAction(context) ? 10 : 4;
            score += dexterity * futureBlockValue;
        }

        if (context.CurrentHp <= Math.Max(12, context.IncomingDamage))
        {
            score += 35;
        }

        return score;
    }

    private static int ScoreEnemyDebuff(DeterministicCombatContext context, AiLegalActionOption action, ResolvedCardView card)
    {
        int score = 0;
        int vulnerable = card.GetEnemyVulnerableAmount();
        int weak = card.GetEnemyWeakAmount();

        if (vulnerable > 0)
        {
            int followUpAttacks = CountAffordableAttackActions(context, action);
            score += vulnerable * (followUpAttacks > 0 ? 16 : 6);
        }

        if (weak > 0)
        {
            score += EstimateWeakPrevention(context, action, weak) * 5;
        }

        return score;
    }

    private static int ScoreSelfBuff(DeterministicCombatContext context, AiLegalActionOption action, ResolvedCardView card)
    {
        int temporaryStrength = card.GetSelfTemporaryStrengthAmount();
        int totalStrength = card.GetSelfStrengthAmount();
        int persistentStrength = Math.Max(0, totalStrength - temporaryStrength);
        int temporaryDexterity = card.GetSelfTemporaryDexterityAmount();
        int totalDexterity = card.GetSelfDexterityAmount();
        int persistentDexterity = Math.Max(0, totalDexterity - temporaryDexterity);

        int score = 0;
        if (temporaryStrength > 0)
        {
            score += temporaryStrength * Math.Max(6, CountAffordableAttackActions(context, action) * 8);
        }

        if (persistentStrength > 0)
        {
            score += persistentStrength * Math.Max(4, CountAffordableAttackActions(context, action) * 5);
        }

        if (temporaryDexterity > 0)
        {
            score += temporaryDexterity * Math.Max(8, CountAffordableBlockActions(context, action) * 10);
        }

        if (persistentDexterity > 0)
        {
            score += persistentDexterity * Math.Max(4, CountAffordableBlockActions(context, action) * 6);
        }

        return score;
    }

    private static int ScoreResourceSetup(DeterministicCombatContext context, AiLegalActionOption action, ResolvedCardView card)
    {
        int cardsDrawn = card.GetCardsDrawn();
        int energyGain = card.GetEnergyGain();
        int score = 0;

        if (cardsDrawn > 0)
        {
            bool hasSpendableFollowUp = CountAffordablePlayableActions(context, action, extraEnergy: energyGain) > 0;
            score += hasSpendableFollowUp ? cardsDrawn * 10 : -cardsDrawn * 8;
        }

        if (energyGain > 0)
        {
            score += energyGain * 18;
        }

        return score;
    }

    private static int ScoreKillPotential(DeterministicCombatContext context, AiLegalActionOption action, ResolvedCardView card)
    {
        if (string.IsNullOrEmpty(action.TargetId) ||
            !context.EnemiesById.TryGetValue(action.TargetId, out DeterministicEnemyState? enemy))
        {
            return 0;
        }

        int estimatedDamage = card.GetEstimatedDamage();
        int effectiveEnemyHp = enemy.CurrentHp + enemy.Block;
        if (estimatedDamage >= effectiveEnemyHp)
        {
            return 55 + enemy.IncomingDamage * 8;
        }

        return 0;
    }

    private static int ScoreUtility(DeterministicCombatContext context, AiLegalActionOption action)
    {
        int uncoveredDamage = Math.Max(0, context.IncomingDamage - context.CurrentBlock);
        int score = uncoveredDamage > 0 ? 10 : 18;
        score += ScoreEnergyEfficiency(action);
        return score;
    }

    private static int ScorePotion(DeterministicCombatContext context, AiLegalActionOption action)
    {
        bool isOffensivePotion = IsOffensivePotion(action);
        bool isDefensivePotion = IsDefensivePotion(action);
        bool graveDanger = IsGraveDanger(context);
        bool canAmplifyAttacks = isOffensivePotion && CountNonPotionAttackActions(context) > 0;
        bool isHighValueTarget = IsHighValuePotionTarget(context, action);

        int score = context.IsEliteOrBossCombat ? 18 : -160;

        if (context.IsEliteOrBossCombat)
        {
            score += 12;
        }

        if (graveDanger)
        {
            score += isDefensivePotion ? 160 : 60;
        }

        if (isOffensivePotion)
        {
            if (context.IsEliteOrBossCombat && canAmplifyAttacks)
            {
                score += 95;
            }
            else if (!context.IsEliteOrBossCombat && canAmplifyAttacks && isHighValueTarget)
            {
                score += 25;
            }
        }

        if (!string.IsNullOrEmpty(action.TargetId) &&
            context.EnemiesById.TryGetValue(action.TargetId, out DeterministicEnemyState? enemy))
        {
            if (enemy.IsAttacking)
            {
                score += 8;
            }

            if (enemy.CurrentHp + enemy.Block <= 18)
            {
                score -= 18;
            }
        }

        return score;
    }

    private static int ScoreEndTurn(DeterministicCombatContext context)
    {
        if (ShouldPreferEndTurnOverRemainingPotions(context))
        {
            return 24;
        }

        return context.LegalActions.Count > 1 ? -10000 : 0;
    }

    private static int ScoreEnergyEfficiency(AiLegalActionOption action)
    {
        if (!action.EnergyCost.HasValue)
        {
            return 0;
        }

        return Math.Max(0, 4 - action.EnergyCost.Value) * 4;
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

    private static bool IsOffensivePotion(AiLegalActionOption action)
    {
        string potionId = action.CardId ?? string.Empty;
        return potionId.Contains("BINDING", StringComparison.OrdinalIgnoreCase)
               || potionId.Contains("VULNERABLE", StringComparison.OrdinalIgnoreCase)
               || potionId.Contains("WEAK", StringComparison.OrdinalIgnoreCase)
               || potionId.Contains("POISON", StringComparison.OrdinalIgnoreCase)
               || potionId.Contains("FIRE", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDefensivePotion(AiLegalActionOption action)
    {
        string potionId = action.CardId ?? string.Empty;
        return potionId.Contains("BLOCK", StringComparison.OrdinalIgnoreCase)
               || potionId.Contains("ARMOR", StringComparison.OrdinalIgnoreCase)
               || potionId.Contains("DEXTERITY", StringComparison.OrdinalIgnoreCase)
               || potionId.Contains("WEAK", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsGraveDanger(DeterministicCombatContext context)
    {
        int uncoveredDamage = Math.Max(0, context.IncomingDamage - context.CurrentBlock);
        return uncoveredDamage >= Math.Max(10, context.CurrentHp / 3) || uncoveredDamage >= context.CurrentHp;
    }

    private static bool IsHighValuePotionTarget(DeterministicCombatContext context, AiLegalActionOption action)
    {
        if (string.IsNullOrEmpty(action.TargetId) ||
            !context.EnemiesById.TryGetValue(action.TargetId, out DeterministicEnemyState? enemy))
        {
            return false;
        }

        return enemy.IsAttacking || enemy.CurrentHp + enemy.Block >= 24;
    }

    private static bool ShouldPreferEndTurnOverRemainingPotions(DeterministicCombatContext context)
    {
        return context.LegalActions
            .Where(action => string.Equals(action.ActionType, AiTeammateActionKind.UsePotion.ToString(), StringComparison.Ordinal))
            .All(action => ScorePotion(context, action) <= 0);
    }
}

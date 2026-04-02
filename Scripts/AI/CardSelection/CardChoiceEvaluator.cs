using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;

namespace AITeammate.Scripts;

internal sealed class CardChoiceEvaluator
{
    private readonly CardEvaluationContextFactory _contextFactory = new();

    public CardEvaluationContextFactory ContextFactory => _contextFactory;

    public CardChoiceDecision EvaluateCandidates(
        IEnumerable<CardModel> candidates,
        CardEvaluationContext context)
    {
        AiCardRewardTuning tuning = AiCharacterCombatConfigLoader.LoadForPlayer(context.Player).CardRewards;
        List<CardEvaluationResult> ranked = candidates
            .Select((card, index) => EvaluateCard(card, index, context))
            .OrderByDescending(static result => result.FinalScore)
            .ThenBy(result => result.Candidate.Name, StringComparer.Ordinal)
            .ToList();

        double skipThreshold = GetSkipThreshold(context, tuning);
        bool shouldTake = ranked.Count > 0 && (!context.SkipAllowed || ranked[0].FinalScore >= skipThreshold);

        return new CardChoiceDecision
        {
            RankedResults = ranked,
            SkipThreshold = skipThreshold,
            ShouldTakeCard = shouldTake
        };
    }

    private CardEvaluationResult EvaluateCard(CardModel cardModel, int index, CardEvaluationContext context)
    {
        AiCardRewardTuning tuning = AiCharacterCombatConfigLoader.LoadForPlayer(context.Player).CardRewards;
        ResolvedCardView card = _contextFactory.ResolveCandidate(cardModel, index);
        CardFeatureVector features = CardFeatureVector.From(card);

        double intrinsic = ScoreIntrinsic(card, features, tuning);
        double deckFit = ScoreDeckFit(card, features, context, tuning);
        double needs = ScoreDeckNeeds(card, features, context, tuning);
        double redundancy = ScoreRedundancy(card, features, context, tuning);
        double contextAdjustment = ScoreContext(card, features, context, tuning);
        double final = intrinsic + deckFit + needs + contextAdjustment - redundancy;

        List<string> reasons = [];
        if (intrinsic > 0)
        {
            reasons.Add($"intrinsic +{intrinsic:F1}");
        }

        if (deckFit > 0)
        {
            reasons.Add($"fit +{deckFit:F1}");
        }

        if (needs > 0)
        {
            reasons.Add($"needs +{needs:F1}");
        }

        if (redundancy > 0)
        {
            reasons.Add($"redundancy -{redundancy:F1}");
        }

        if (contextAdjustment != 0)
        {
            reasons.Add($"context {(contextAdjustment > 0 ? "+" : string.Empty)}{contextAdjustment:F1}");
        }

        return new CardEvaluationResult
        {
            CandidateCard = cardModel,
            Candidate = card,
            FinalScore = final,
            IntrinsicScore = intrinsic,
            DeckFitScore = deckFit,
            NeedCoverageScore = needs,
            RedundancyPenalty = redundancy,
            ContextAdjustmentScore = contextAdjustment,
            Reasons = reasons
        };
    }

    private static double ScoreIntrinsic(ResolvedCardView card, CardFeatureVector features, AiCardRewardTuning tuning)
    {
        AiCardRewardIntrinsicWeights intrinsic = tuning.IntrinsicWeights;
        double score = 0d;
        score += features.Damage * intrinsic.DamageValuePerPoint;
        score += features.Block * intrinsic.BlockValuePerPoint;
        score += features.Draw * intrinsic.DrawValue;
        score += features.Energy * intrinsic.EnergyValue;
        score += features.Vulnerable * intrinsic.VulnerableValue;
        score += features.Weak * intrinsic.WeakValue;
        score += features.PersistentStrength * intrinsic.PersistentStrengthValue;
        score += features.PersistentDexterity * intrinsic.PersistentDexterityValue;
        score += features.TemporaryStrength * intrinsic.TemporaryStrengthValue;
        score += features.TemporaryDexterity * intrinsic.TemporaryDexterityValue;
        score += features.RepeatCount * intrinsic.RepeatValue;
        score += GetRarityBonus(card.Rarity, intrinsic);

        if (card.Type == CardType.Power)
        {
            score += intrinsic.PowerBonus;
        }

        if (card.EffectiveCost == 0)
        {
            score += intrinsic.ZeroCostBonus;
        }
        else if (card.EffectiveCost > 1)
        {
            score -= (card.EffectiveCost - 1) * intrinsic.HighCostPenaltyPerExtraEnergy;
        }

        if (card.Retain)
        {
            score += intrinsic.RetainBonus;
        }

        if (card.Exhaust)
        {
            score += score >= 18d ? intrinsic.GoodExhaustBonus : -intrinsic.BadExhaustPenalty;
        }

        if (card.Ethereal)
        {
            score -= intrinsic.EtherealPenalty;
        }

        if (features.TotalKnownValue <= 0 &&
            card.Type is CardType.Attack or CardType.Skill or CardType.Power)
        {
            score -= intrinsic.UnknownValuePenalty;
        }

        return score;
    }

    private static double ScoreDeckFit(ResolvedCardView card, CardFeatureVector features, CardEvaluationContext context, AiCardRewardTuning tuning)
    {
        AiCardRewardSynergyWeights synergy = tuning.SynergyWeights;
        DeckSummary deck = context.DeckSummary;
        double score = 0d;

        if (features.Draw > 0 && (deck.HighCostCards >= 4 || deck.AverageCost >= 1.35d))
        {
            score += features.Draw * synergy.DrawWithHighCurveValue;
        }

        if (features.Energy > 0 && (deck.HighCostCards >= 5 || deck.DrawSources >= 2))
        {
            score += features.Energy * synergy.EnergyWithHeavyCurveValue;
        }

        if (features.PersistentStrength > 0 || features.TemporaryStrength > 0 || features.Vulnerable > 0)
        {
            score += Math.Min(deck.AttackCount, 8) * synergy.AttackScalingSynergyPerAttack;
        }

        if (features.PersistentDexterity > 0 || features.TemporaryDexterity > 0)
        {
            score += Math.Min(deck.BlockSources, 8) * synergy.DefenseScalingSynergyPerBlockSource;
        }

        if (card.Type == CardType.Power && deck.DrawSources > 0)
        {
            score += synergy.PowerWithDrawBonus;
        }

        if (card.Retain && deck.HighCostCards > 0)
        {
            score += synergy.RetainWithHighCostBonus;
        }

        if (card.Exhaust && (features.Draw > 0 || features.Energy > 0 || features.TotalKnownValue >= 18))
        {
            score += synergy.ExhaustSynergyBonus;
        }

        return score;
    }

    private static double ScoreDeckNeeds(ResolvedCardView card, CardFeatureVector features, CardEvaluationContext context, AiCardRewardTuning tuning)
    {
        AiCardRewardSynergyWeights synergy = tuning.SynergyWeights;
        DeckSummary deck = context.DeckSummary;
        double score = 0d;

        if (deck.FrontloadDamageSources < DesiredDamageSources(deck))
        {
            score += Math.Min(features.Damage / synergy.DamageNeedScale, synergy.DamageNeedCap);
            score += features.Vulnerable * synergy.VulnerableNeedValue;
        }

        if (deck.BlockSources < DesiredBlockSources(deck))
        {
            score += Math.Min(features.Block / synergy.BlockNeedScale, synergy.BlockNeedCap);
            score += features.Weak * synergy.WeakNeedValue;
            score += features.PersistentDexterity * synergy.DexterityNeedValue;
        }

        if (deck.DrawSources < DesiredDrawSources(deck))
        {
            score += features.Draw * synergy.DrawNeedValue;
        }

        if (deck.EnergySources < DesiredEnergySources(deck))
        {
            score += features.Energy * synergy.EnergyNeedValue;
        }

        if (deck.ScalingSources < DesiredScalingSources(deck))
        {
            score += (features.PersistentStrength + features.PersistentDexterity) * synergy.ScalingNeedValue;
            if (card.Type == CardType.Power)
            {
                score += synergy.PowerScalingBonus;
            }
        }

        if (deck.CardCount <= 15 && card.EffectiveCost <= 1)
        {
            score += Math.Min(features.Damage + features.Block, synergy.EarlyCheapCardTempoCap) * synergy.EarlyCheapCardTempoScale;
        }

        return score;
    }

    private static double ScoreRedundancy(ResolvedCardView card, CardFeatureVector features, CardEvaluationContext context, AiCardRewardTuning tuning)
    {
        AiCardRewardDisciplineWeights discipline = tuning.DisciplineWeights;
        DeckSummary deck = context.DeckSummary;
        int copiesInDeck = context.DeckCards.Count(deckCard =>
            string.Equals(deckCard.CardId, card.CardId, StringComparison.Ordinal));

        double penalty = copiesInDeck * discipline.DuplicatePenaltyPerCopy;

        if (deck.DrawSources >= DesiredDrawSources(deck) + 1 && features.Draw > 0)
        {
            penalty += features.Draw * discipline.ExcessDrawPenalty;
        }

        if (deck.EnergySources >= DesiredEnergySources(deck) + 1 && features.Energy > 0)
        {
            penalty += features.Energy * discipline.ExcessEnergyPenalty;
        }

        if (deck.FrontloadDamageSources >= DesiredDamageSources(deck) + 2 && features.Damage > 0)
        {
            penalty += Math.Min(features.Damage / discipline.ExcessDamagePenaltyScale, discipline.ExcessDamagePenaltyCap);
        }

        if (deck.BlockSources >= DesiredBlockSources(deck) + 2 && features.Block > 0)
        {
            penalty += Math.Min(features.Block / discipline.ExcessBlockPenaltyScale, discipline.ExcessBlockPenaltyCap);
        }

        if (deck.ScalingSources >= DesiredScalingSources(deck) + 2 &&
            (features.PersistentStrength > 0 || features.PersistentDexterity > 0 || card.Type == CardType.Power))
        {
            penalty += discipline.ExcessScalingPenalty;
        }

        if (deck.PowerCount >= 5 && card.Type == CardType.Power)
        {
            penalty += discipline.ExcessPowerPenalty;
        }

        if (card.Ethereal && card.EffectiveCost >= 2)
        {
            penalty += discipline.EtherealHighCostPenalty;
        }

        return penalty;
    }

    private static double ScoreContext(ResolvedCardView card, CardFeatureVector features, CardEvaluationContext context, AiCardRewardTuning tuning)
    {
        AiCardRewardDisciplineWeights discipline = tuning.DisciplineWeights;
        AiCardRewardSynergyWeights synergy = tuning.SynergyWeights;
        double score = context.ChoiceSource switch
        {
            CardChoiceSource.Reward => discipline.RewardContextBonus,
            CardChoiceSource.ChooseScreen => discipline.ChooseScreenContextBonus,
            CardChoiceSource.Event => discipline.EventContextBonus,
            CardChoiceSource.Shop => ScoreShopContext(context, tuning),
            _ => 0d
        };

        if (context.CurrentActIndex == 0 && context.TotalFloor <= 10)
        {
            score += Math.Min(features.Damage + features.Block, synergy.EarlyActTempoCap) * synergy.EarlyActTempoScale;
        }

        if (context.AscensionLevel >= 10 && features.Block > 0)
        {
            score += synergy.HighAscensionBlockBonus;
        }

        return score;
    }

    private static double ScoreShopContext(CardEvaluationContext context, AiCardRewardTuning tuning)
    {
        AiCardRewardDisciplineWeights discipline = tuning.DisciplineWeights;
        if (!context.CandidateGoldCost.HasValue)
        {
            return -discipline.ShopMissingCostPenalty;
        }

        double gold = Math.Max(context.Gold, 1);
        double cost = context.CandidateGoldCost.Value;
        return -(cost / Math.Max(gold, 50d)) * discipline.ShopCostRatioPenaltyScale;
    }

    private static double GetSkipThreshold(CardEvaluationContext context, AiCardRewardTuning tuning)
    {
        AiCardRewardDisciplineWeights discipline = tuning.DisciplineWeights;
        if (!context.SkipAllowed || context.ChoiceSource == CardChoiceSource.ForcedChoice)
        {
            return double.NegativeInfinity;
        }

        return context.ChoiceSource switch
        {
            CardChoiceSource.Reward => discipline.RewardSkipThreshold,
            CardChoiceSource.ChooseScreen => discipline.ChooseScreenSkipThreshold,
            CardChoiceSource.Event => discipline.EventSkipThreshold,
            CardChoiceSource.Shop => discipline.ShopSkipThresholdBase + (context.CandidateGoldCost ?? 0) * discipline.ShopSkipThresholdCostFactor,
            _ => discipline.EventSkipThreshold
        };
    }

    private static int DesiredDamageSources(DeckSummary deck)
    {
        return deck.CardCount < 15 ? 6 : 8;
    }

    private static int DesiredBlockSources(DeckSummary deck)
    {
        return deck.CardCount < 15 ? 5 : 7;
    }

    private static int DesiredDrawSources(DeckSummary deck)
    {
        return deck.CardCount < 18 ? 2 : 3;
    }

    private static int DesiredEnergySources(DeckSummary deck)
    {
        return deck.CardCount < 18 ? 1 : 2;
    }

    private static int DesiredScalingSources(DeckSummary deck)
    {
        return deck.CardCount < 15 ? 1 : 2;
    }

    private static double GetRarityBonus(string rarity, AiCardRewardIntrinsicWeights intrinsic)
    {
        return rarity switch
        {
            "Rare" => intrinsic.RareBonus,
            "Uncommon" => intrinsic.UncommonBonus,
            "Common" => 0d,
            "Basic" => intrinsic.BasicBonus,
            "Curse" => intrinsic.CursePenalty,
            "Status" => intrinsic.StatusPenalty,
            "Quest" => intrinsic.QuestPenalty,
            "Event" => intrinsic.EventBonus,
            "Ancient" => intrinsic.AncientBonus,
            _ => 0d
        };
    }

    private readonly record struct CardFeatureVector(
        int Damage,
        int Block,
        int Draw,
        int Energy,
        int Vulnerable,
        int Weak,
        int PersistentStrength,
        int PersistentDexterity,
        int TemporaryStrength,
        int TemporaryDexterity,
        int RepeatCount)
    {
        public int TotalKnownValue =>
            Damage +
            Block +
            (Draw * 4) +
            (Energy * 5) +
            (Vulnerable * 3) +
            (Weak * 3) +
            (PersistentStrength * 3) +
            (PersistentDexterity * 3) +
            (TemporaryStrength * 2) +
            (TemporaryDexterity * 2);

        public static CardFeatureVector From(ResolvedCardView card)
        {
            int temporaryStrength = card.GetSelfTemporaryStrengthAmount();
            int temporaryDexterity = card.GetSelfTemporaryDexterityAmount();

            return new CardFeatureVector(
                card.GetEstimatedDamage(),
                card.GetEstimatedBlock(),
                card.GetCardsDrawn(),
                card.GetEnergyGain(),
                card.GetEnemyVulnerableAmount(),
                card.GetEnemyWeakAmount(),
                Math.Max(0, card.GetSelfStrengthAmount() - temporaryStrength),
                Math.Max(0, card.GetSelfDexterityAmount() - temporaryDexterity),
                temporaryStrength,
                temporaryDexterity,
                Math.Max(card.ReplayCount, 1));
        }
    }
}

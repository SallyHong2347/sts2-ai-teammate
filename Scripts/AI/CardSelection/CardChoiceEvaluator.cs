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
        List<CardEvaluationResult> ranked = candidates
            .Select((card, index) => EvaluateCard(card, index, context))
            .OrderByDescending(static result => result.FinalScore)
            .ThenBy(result => result.Candidate.Name, StringComparer.Ordinal)
            .ToList();

        double skipThreshold = GetSkipThreshold(context);
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
        ResolvedCardView card = _contextFactory.ResolveCandidate(cardModel, index);
        CardFeatureVector features = CardFeatureVector.From(card);

        double intrinsic = ScoreIntrinsic(card, features);
        double deckFit = ScoreDeckFit(card, features, context);
        double needs = ScoreDeckNeeds(card, features, context);
        double redundancy = ScoreRedundancy(card, features, context);
        double contextAdjustment = ScoreContext(card, features, context);
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

    private static double ScoreIntrinsic(ResolvedCardView card, CardFeatureVector features)
    {
        double score = 0d;
        score += features.Damage * 0.55d;
        score += features.Block * 0.45d;
        score += features.Draw * 8d;
        score += features.Energy * 12d;
        score += features.Vulnerable * 5d;
        score += features.Weak * 5.5d;
        score += features.PersistentStrength * 6d;
        score += features.PersistentDexterity * 6d;
        score += features.TemporaryStrength * 4d;
        score += features.TemporaryDexterity * 4d;
        score += features.RepeatCount * 0.75d;
        score += GetRarityBonus(card.Rarity);

        if (card.Type == CardType.Power)
        {
            score += 2d;
        }

        if (card.EffectiveCost == 0)
        {
            score += 4d;
        }
        else if (card.EffectiveCost > 1)
        {
            score -= (card.EffectiveCost - 1) * 2.25d;
        }

        if (card.Retain)
        {
            score += 2d;
        }

        if (card.Exhaust)
        {
            score += score >= 18d ? 1d : -2d;
        }

        if (card.Ethereal)
        {
            score -= 6d;
        }

        if (features.TotalKnownValue <= 0 &&
            card.Type is CardType.Attack or CardType.Skill or CardType.Power)
        {
            score -= 6d;
        }

        return score;
    }

    private static double ScoreDeckFit(ResolvedCardView card, CardFeatureVector features, CardEvaluationContext context)
    {
        DeckSummary deck = context.DeckSummary;
        double score = 0d;

        if (features.Draw > 0 && (deck.HighCostCards >= 4 || deck.AverageCost >= 1.35d))
        {
            score += features.Draw * 3.5d;
        }

        if (features.Energy > 0 && (deck.HighCostCards >= 5 || deck.DrawSources >= 2))
        {
            score += features.Energy * 5d;
        }

        if (features.PersistentStrength > 0 || features.TemporaryStrength > 0 || features.Vulnerable > 0)
        {
            score += Math.Min(deck.AttackCount, 8) * 0.9d;
        }

        if (features.PersistentDexterity > 0 || features.TemporaryDexterity > 0)
        {
            score += Math.Min(deck.BlockSources, 8) * 0.9d;
        }

        if (card.Type == CardType.Power && deck.DrawSources > 0)
        {
            score += 3d;
        }

        if (card.Retain && deck.HighCostCards > 0)
        {
            score += 3d;
        }

        if (card.Exhaust && (features.Draw > 0 || features.Energy > 0 || features.TotalKnownValue >= 18))
        {
            score += 2d;
        }

        return score;
    }

    private static double ScoreDeckNeeds(ResolvedCardView card, CardFeatureVector features, CardEvaluationContext context)
    {
        DeckSummary deck = context.DeckSummary;
        double score = 0d;

        if (deck.FrontloadDamageSources < DesiredDamageSources(deck))
        {
            score += Math.Min(features.Damage / 3d, 12d);
            score += features.Vulnerable * 2d;
        }

        if (deck.BlockSources < DesiredBlockSources(deck))
        {
            score += Math.Min(features.Block / 3d, 12d);
            score += features.Weak * 2d;
            score += features.PersistentDexterity * 4d;
        }

        if (deck.DrawSources < DesiredDrawSources(deck))
        {
            score += features.Draw * 6d;
        }

        if (deck.EnergySources < DesiredEnergySources(deck))
        {
            score += features.Energy * 8d;
        }

        if (deck.ScalingSources < DesiredScalingSources(deck))
        {
            score += (features.PersistentStrength + features.PersistentDexterity) * 5d;
            if (card.Type == CardType.Power)
            {
                score += 3d;
            }
        }

        if (deck.CardCount <= 15 && card.EffectiveCost <= 1)
        {
            score += Math.Min(features.Damage + features.Block, 16) * 0.35d;
        }

        return score;
    }

    private static double ScoreRedundancy(ResolvedCardView card, CardFeatureVector features, CardEvaluationContext context)
    {
        DeckSummary deck = context.DeckSummary;
        int copiesInDeck = context.DeckCards.Count(deckCard =>
            string.Equals(deckCard.CardId, card.CardId, StringComparison.Ordinal));

        double penalty = copiesInDeck * 4d;

        if (deck.DrawSources >= DesiredDrawSources(deck) + 1 && features.Draw > 0)
        {
            penalty += features.Draw * 4d;
        }

        if (deck.EnergySources >= DesiredEnergySources(deck) + 1 && features.Energy > 0)
        {
            penalty += features.Energy * 5d;
        }

        if (deck.FrontloadDamageSources >= DesiredDamageSources(deck) + 2 && features.Damage > 0)
        {
            penalty += Math.Min(features.Damage / 5d, 8d);
        }

        if (deck.BlockSources >= DesiredBlockSources(deck) + 2 && features.Block > 0)
        {
            penalty += Math.Min(features.Block / 5d, 8d);
        }

        if (deck.ScalingSources >= DesiredScalingSources(deck) + 2 &&
            (features.PersistentStrength > 0 || features.PersistentDexterity > 0 || card.Type == CardType.Power))
        {
            penalty += 6d;
        }

        if (deck.PowerCount >= 5 && card.Type == CardType.Power)
        {
            penalty += 4d;
        }

        if (card.Ethereal && card.EffectiveCost >= 2)
        {
            penalty += 4d;
        }

        return penalty;
    }

    private static double ScoreContext(ResolvedCardView card, CardFeatureVector features, CardEvaluationContext context)
    {
        double score = context.ChoiceSource switch
        {
            CardChoiceSource.Reward => 2d,
            CardChoiceSource.ChooseScreen => 1d,
            CardChoiceSource.Event => 0d,
            CardChoiceSource.Shop => ScoreShopContext(context),
            _ => 0d
        };

        if (context.CurrentActIndex == 0 && context.TotalFloor <= 10)
        {
            score += Math.Min(features.Damage + features.Block, 18d) * 0.15d;
        }

        if (context.AscensionLevel >= 10 && features.Block > 0)
        {
            score += 1d;
        }

        return score;
    }

    private static double ScoreShopContext(CardEvaluationContext context)
    {
        if (!context.CandidateGoldCost.HasValue)
        {
            return -6d;
        }

        double gold = Math.Max(context.Gold, 1);
        double cost = context.CandidateGoldCost.Value;
        return -(cost / Math.Max(gold, 50d)) * 8d;
    }

    private static double GetSkipThreshold(CardEvaluationContext context)
    {
        if (!context.SkipAllowed || context.ChoiceSource == CardChoiceSource.ForcedChoice)
        {
            return double.NegativeInfinity;
        }

        return context.ChoiceSource switch
        {
            CardChoiceSource.Reward => 12d,
            CardChoiceSource.ChooseScreen => 12d,
            CardChoiceSource.Event => 14d,
            CardChoiceSource.Shop => 22d + (context.CandidateGoldCost ?? 0) * 0.10d,
            _ => 14d
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

    private static double GetRarityBonus(string rarity)
    {
        return rarity switch
        {
            "Rare" => 6d,
            "Uncommon" => 3d,
            "Common" => 0d,
            "Basic" => -3d,
            "Curse" => -35d,
            "Status" => -25d,
            "Quest" => -10d,
            "Event" => 1d,
            "Ancient" => 8d,
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

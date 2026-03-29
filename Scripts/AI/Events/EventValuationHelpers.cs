using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;

namespace AITeammate.Scripts;

internal sealed class EventValuationHelpers
{
    public double EvaluateRelic(string? relicId, string? rarity, EventVisitState snapshot, List<string> reasons)
    {
        double totalScore = rarity switch
        {
            "Ancient" => 28d,
            "Rare" => 21d,
            "Uncommon" => 15d,
            "Common" => 10d,
            _ => 8d
        };

        reasons.Add($"relicBaseline={totalScore:F1}");
        if (string.IsNullOrEmpty(relicId))
        {
            return totalScore;
        }

        string upper = relicId.ToUpperInvariant();
        AddRelicPatternBonus("MEMBERSHIP", 18d, "membership discount scales future shops");
        AddRelicPatternBonus("COURIER", 15d, "courier discount/restock premium");
        AddRelicPatternBonus("BAG_OF_PREPARATION", 11d, "opening draw consistency");
        AddRelicPatternBonus("ANCHOR", 9d, "reliable early block");
        AddRelicPatternBonus("ORICHALCUM", 8d, "passive defense floor");
        AddRelicPatternBonus("PANTOGRAPH", 8d, "boss sustain value");
        AddRelicPatternBonus("VAJRA", 6d, "passive attack scaling");

        if (snapshot.RelicIds.Contains(upper))
        {
            totalScore -= 20d;
            reasons.Add("duplicate or already-owned effect penalty -20.0");
        }

        return totalScore;

        void AddRelicPatternBonus(string pattern, double bonus, string reason)
        {
            if (upper.Contains(pattern, StringComparison.Ordinal))
            {
                totalScore += bonus;
                reasons.Add($"{reason} +{bonus:F1}");
            }
        }
    }

    public double EvaluatePotion(
        string? potionId,
        string? rarity,
        int count,
        EventVisitState snapshot,
        List<string> reasons)
    {
        double singlePotionScore = rarity switch
        {
            "Event" => 14d,
            "Rare" => 12d,
            "Uncommon" => 8d,
            "Common" => 5d,
            _ => 4d
        };

        if (!snapshot.Player.HasOpenPotionSlots)
        {
            singlePotionScore -= 20d;
            reasons.Add("no open potion slots -20.0");
        }

        if (snapshot.RelicIds.Contains("SOZU"))
        {
            singlePotionScore -= 30d;
            reasons.Add("Sozu blocks procurement -30.0");
        }

        if (!string.IsNullOrEmpty(potionId))
        {
            string upper = potionId.ToUpperInvariant();
            if (MatchesAny(upper, "BLOCK", "ARMOR", "DEX", "GHOST"))
            {
                double defenseBonus = snapshot.DeckSummary.BlockSources < 6 ? 6d : 3d;
                singlePotionScore += defenseBonus;
                reasons.Add($"defensive coverage +{defenseBonus:F1}");
            }

            if (MatchesAny(upper, "FIRE", "EXPLOS", "ATTACK", "STRENGTH", "FEAR", "VULNERABLE"))
            {
                double offenseBonus = snapshot.DeckSummary.FrontloadDamageSources < 7 ? 6d : 3d;
                singlePotionScore += offenseBonus;
                reasons.Add($"offensive reach +{offenseBonus:F1}");
            }

            if (MatchesAny(upper, "ENERGY", "DRAW", "GAMBLER", "AMBROSIA", "LIQUID"))
            {
                double tempoBonus = snapshot.DeckSummary.DrawSources < 2 || snapshot.DeckSummary.EnergySources < 1 ? 7d : 4d;
                singlePotionScore += tempoBonus;
                reasons.Add($"tempo coverage +{tempoBonus:F1}");
            }
        }

        double total = singlePotionScore * Math.Max(1, count);
        reasons.Add($"potionTotal={total:F1}");
        return total;
    }

    public double EvaluateFixedCardGain(IEnumerable<string> cardIds, EventVisitState snapshot, List<string> reasons)
    {
        double total = 0d;
        foreach (string cardId in cardIds)
        {
            if (!CardCatalogRepository.Shared.TryGet(cardId, out CardCatalogEntry? entry) || entry == null)
            {
                total += 10d;
                reasons.Add($"fixedCard={cardId} conservativeBaseline +10.0");
                continue;
            }

            double score = EvaluateCatalogCard(entry);
            total += score;
            reasons.Add($"fixedCard={entry.CardId} cardEval={score:F1}");
        }

        return total;
    }

    public EventRemovalCandidate? SelectBestRemovalCandidate(EventVisitState snapshot)
    {
        Dictionary<string, int> copiesById = snapshot.Player.Deck.Cards
            .GroupBy(static card => card.Id.Entry, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal);

        List<EventRemovalCandidate> candidates = [];
        for (int index = 0; index < snapshot.Player.Deck.Cards.Count; index++)
        {
            CardModel deckCard = snapshot.Player.Deck.Cards[index];
            if (!deckCard.IsRemovable)
            {
                continue;
            }

            ResolvedCardView resolved = snapshot.DeckCards[index];
            double burden = 0d;
            List<string> reasons = [];

            switch (resolved.Rarity)
            {
                case "Curse":
                    burden += 40d;
                    reasons.Add("curse tax +40.0");
                    break;
                case "Status":
                    burden += 28d;
                    reasons.Add("status tax +28.0");
                    break;
                case "Basic":
                    burden += 9d;
                    reasons.Add("basic card tax +9.0");
                    break;
                case "Rare":
                    burden -= 6d;
                    reasons.Add("rare keep bias -6.0");
                    break;
                case "Uncommon":
                    burden -= 2d;
                    reasons.Add("uncommon keep bias -2.0");
                    break;
            }

            if (deckCard.Id.Entry.Contains("STRIKE", StringComparison.OrdinalIgnoreCase))
            {
                burden += 14d;
                reasons.Add("starter strike burden +14.0");
            }

            if (deckCard.Id.Entry.Contains("DEFEND", StringComparison.OrdinalIgnoreCase))
            {
                burden += 11d;
                reasons.Add("starter defend burden +11.0");
            }

            if (copiesById.TryGetValue(deckCard.Id.Entry, out int copies) && copies > 1)
            {
                double duplicatePenalty = (copies - 1) * 3d;
                burden += duplicatePenalty;
                reasons.Add($"duplicate copies +{duplicatePenalty:F1}");
            }

            int knownValue = resolved.GetEstimatedDamage() +
                             resolved.GetEstimatedBlock() +
                             (resolved.GetCardsDrawn() * 4) +
                             (resolved.GetEnergyGain() * 5) +
                             (resolved.GetEnemyVulnerableAmount() * 3) +
                             (resolved.GetEnemyWeakAmount() * 3) +
                             (resolved.GetSelfStrengthAmount() * 3) +
                             (resolved.GetSelfDexterityAmount() * 3);

            if (knownValue <= 0)
            {
                burden += 8d;
                reasons.Add("low known output +8.0");
            }
            else if (knownValue <= 8)
            {
                burden += 4d;
                reasons.Add("thin output +4.0");
            }
            else if (knownValue >= 18)
            {
                burden -= 4d;
                reasons.Add("strong output keep bias -4.0");
            }

            if (resolved.EffectiveCost >= 2 && knownValue <= 10)
            {
                burden += 7d;
                reasons.Add("expensive for output +7.0");
            }

            if (resolved.Ethereal)
            {
                burden += 6d;
                reasons.Add("ethereal reliability tax +6.0");
            }

            if (resolved.Exhaust && knownValue <= 12)
            {
                burden += 2d;
                reasons.Add("low-value exhaust tax +2.0");
            }

            if (resolved.IsUpgraded)
            {
                burden -= 6d;
                reasons.Add("upgraded keep bias -6.0");
            }

            if (resolved.Type == CardType.Power)
            {
                burden -= 3d;
                reasons.Add("power keep bias -3.0");
            }

            if (resolved.EffectiveCost == 0)
            {
                burden -= 2d;
                reasons.Add("zero-cost flexibility keep bias -2.0");
            }

            candidates.Add(new EventRemovalCandidate
            {
                CardId = deckCard.Id.Entry,
                Name = deckCard.Title?.ToString() ?? deckCard.Id.Entry,
                BurdenScore = burden,
                RuntimeCard = deckCard,
                Reasons = reasons
            });
        }

        return candidates
            .OrderByDescending(static candidate => candidate.BurdenScore)
            .ThenBy(candidate => candidate.Name, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    public double EvaluateBestUpgradeTarget(EventVisitState snapshot, int count, List<string> reasons)
    {
        List<(string CardId, string Name, double Score, List<string> Reasons)> candidates = [];
        foreach (CardModel card in snapshot.Player.Deck.Cards.Where(static card => card.IsUpgradable))
        {
            double score = 0d;
            List<string> candidateReasons = [];
            if (!CardCatalogRepository.Shared.TryGet(card.Id.Entry, out CardCatalogEntry? entry) || entry == null)
            {
                score = 8d;
                candidateReasons.Add("missing catalog entry fallback +8.0");
            }
            else
            {
                score += EvaluateUpgradeSpec(entry.UpgradeSpec, candidateReasons);
                if (entry.Rarity == "Basic")
                {
                    score += 2d;
                    candidateReasons.Add("basic card cleanup/value +2.0");
                }

                if (entry.Type == CardType.Power)
                {
                    score += 2d;
                    candidateReasons.Add("power upgrade bias +2.0");
                }
            }

            candidates.Add((card.Id.Entry, card.Title?.ToString() ?? card.Id.Entry, score, candidateReasons));
        }

        List<(string CardId, string Name, double Score, List<string> Reasons)> best = candidates
            .OrderByDescending(static candidate => candidate.Score)
            .ThenBy(candidate => candidate.Name, StringComparer.Ordinal)
            .Take(Math.Max(1, count))
            .ToList();

        double total = best.Sum(static candidate => candidate.Score);
        foreach (var candidate in best)
        {
            reasons.Add($"bestUpgradeTarget={candidate.CardId} score={candidate.Score:F1} detail=[{string.Join(", ", candidate.Reasons)}]");
        }

        return total;
    }

    public double EvaluateTransform(EventVisitState snapshot, int count, List<string> reasons)
    {
        EventRemovalCandidate? removalCandidate = SelectBestRemovalCandidate(snapshot);
        double removalValue = removalCandidate?.BurdenScore ?? 0d;
        double expectedReplacementValue = 11d * Math.Max(1, count);
        double total = (removalValue * 0.65d) + expectedReplacementValue;
        reasons.Add($"transformRemovalValue={(removalValue * 0.65d):F1}");
        reasons.Add($"transformReplacementBaseline={expectedReplacementValue:F1}");
        if (removalCandidate != null)
        {
            reasons.Add($"transformTarget={removalCandidate.CardId}");
        }

        return total;
    }

    public double EvaluateHpPenalty(EventVisitState snapshot, int hpLoss, bool willKillPlayer, List<string> reasons)
    {
        if (hpLoss <= 0)
        {
            return 0d;
        }

        double hpRatio = snapshot.MaxHp > 0 ? (double)snapshot.CurrentHp / snapshot.MaxHp : 0d;
        double perHpPenalty = hpRatio switch
        {
            <= 0.25d => 4.8d,
            <= 0.40d => 3.8d,
            <= 0.60d => 2.9d,
            _ => 2.0d
        };
        double total = hpLoss * perHpPenalty;
        reasons.Add($"hpPenaltyPerPoint={perHpPenalty:F1}");
        reasons.Add($"hpPenaltyTotal={-total:F1}");

        if (willKillPlayer)
        {
            total += 1000d;
            reasons.Add("lethal option penalty -1000.0");
        }

        return total;
    }

    public double EvaluateMaxHpPenalty(int maxHpDelta, List<string> reasons)
    {
        if (maxHpDelta >= 0)
        {
            return 0d;
        }

        double total = Math.Abs(maxHpDelta) * 4.5d;
        reasons.Add($"maxHpPenalty={-total:F1}");
        return total;
    }

    public double EvaluateGoldDelta(int goldDelta, List<string> reasons)
    {
        if (goldDelta == 0)
        {
            return 0d;
        }

        double total = goldDelta / 12d;
        reasons.Add($"goldDeltaScore={(total >= 0 ? "+" : string.Empty)}{total:F1}");
        return total;
    }

    public double EvaluateCursePenalty(IEnumerable<string> curseIds, List<string> reasons)
    {
        double total = 0d;
        foreach (string curseId in curseIds)
        {
            double penalty = curseId.ToUpperInvariant() switch
            {
                "REGRET" => 26d,
                "DOUBT" => 18d,
                "SHAME" => 18d,
                "WRITHE" => 20d,
                "PAIN" => 20d,
                "DECAY" => 22d,
                "DEBT" => 16d,
                "GUILTY" => 16d,
                _ => 14d
            };
            total += penalty;
            reasons.Add($"curse={curseId} penalty={-penalty:F1}");
        }

        return total;
    }

    public double EvaluateRandomnessDiscount(EventOutcomeSummary outcome, List<string> reasons)
    {
        if (!outcome.HasRandomness)
        {
            return 0d;
        }

        double discount = outcome.FixedCardCount > 0 || outcome.RelicIds.Count > 0 || outcome.PotionRewardCount > 0
            ? 6d
            : 10d;
        reasons.Add($"randomnessDiscount={-discount:F1}");
        return discount;
    }

    public double UnsupportedOptionPenalty(bool hasUnknownEffects, List<string> reasons)
    {
        double penalty = hasUnknownEffects ? 12d : 6d;
        reasons.Add($"unsupportedPenalty={-penalty:F1}");
        return penalty;
    }

    private static double EvaluateUpgradeSpec(CardUpgradeSpec spec, List<string> reasons)
    {
        double score = 4d;
        if (spec.CostOverride.HasValue)
        {
            score += Math.Max(0, 2 - spec.CostOverride.Value) * 4d;
            reasons.Add($"costOverride->{spec.CostOverride.Value}");
        }
        else if (spec.CostDelta < 0)
        {
            score += Math.Abs(spec.CostDelta) * 5d;
            reasons.Add($"costReduction +{Math.Abs(spec.CostDelta) * 5d:F1}");
        }

        foreach ((EffectAdjustmentKey _, int value) in spec.EffectAmountAdjustments)
        {
            score += Math.Max(0, value) * 1.5d;
        }

        if (spec.Retain == true)
        {
            score += 3d;
            reasons.Add("gains retain +3.0");
        }

        if (spec.Exhaust == false)
        {
            score += 2d;
            reasons.Add("removes exhaust +2.0");
        }

        if (spec.Ethereal == false)
        {
            score += 3d;
            reasons.Add("removes ethereal +3.0");
        }

        if (spec.ReplayCountOverride.HasValue && spec.ReplayCountOverride.Value > 1)
        {
            score += 4d;
            reasons.Add("replay increase +4.0");
        }

        reasons.Add($"upgradeSpecScore={score:F1}");
        return score;
    }

    private static double EvaluateCatalogCard(CardCatalogEntry entry)
    {
        double score = entry.Rarity switch
        {
            "Rare" => 18d,
            "Uncommon" => 14d,
            "Common" => 10d,
            "Basic" => 6d,
            "Curse" => -20d,
            _ => 8d
        };

        score += entry.Type switch
        {
            CardType.Power => 4d,
            CardType.Attack => 2d,
            CardType.Skill => 1d,
            _ => 0d
        };

        if (entry.BaseCost == 0)
        {
            score += 3d;
        }
        else if (entry.BaseCost >= 2)
        {
            score -= 2d;
        }

        score += entry.SemanticProfile.Effects.Sum(static effect =>
            effect.Kind switch
            {
                EffectKind.DealDamage => Math.Min(effect.Amount * effect.RepeatCount, 20) * 0.4d,
                EffectKind.GainBlock => Math.Min(effect.Amount * effect.RepeatCount, 20) * 0.35d,
                EffectKind.DrawCards => effect.Amount * 4d,
                EffectKind.GainEnergy => effect.Amount * 6d,
                EffectKind.ApplyPower => effect.Amount * 1.2d,
                _ => 0.5d
            });

        return score;
    }

    private static bool MatchesAny(string value, params string[] patterns)
    {
        foreach (string pattern in patterns)
        {
            if (value.Contains(pattern, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}

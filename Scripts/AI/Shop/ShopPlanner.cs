using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Entities.Cards;

namespace AITeammate.Scripts;

internal sealed class ShopPlanner
{
    private const int MaxSearchDepth = 6;
    private const int MaxStoredPlans = 24;
    private const int MaxChildrenPerState = 7;
    private const int MerchantFoulPotionGoldGain = 100;

    private readonly CardChoiceEvaluator _cardChoiceEvaluator = new();

    public ShopPlannerResult Evaluate(ShopVisitState snapshot)
    {
        Dictionary<string, ShopOffer> offersById = snapshot.Offers.ToDictionary(static offer => offer.OfferId, StringComparer.Ordinal);

        ShopRemovalCandidate? bestRemovalCandidate = SelectBestRemovalCandidate(snapshot);
        List<ShopOfferEvaluation> offerEvaluations = snapshot.Offers
            .Select(offer => EvaluateOffer(snapshot, offer, bestRemovalCandidate))
            .OrderByDescending(static evaluation => evaluation.TotalScore)
            .ThenBy(evaluation => evaluation.Name, StringComparer.Ordinal)
            .ToList();

        Dictionary<string, ShopOfferEvaluation> offerEvaluationsById = offerEvaluations.ToDictionary(
            static evaluation => evaluation.OfferId,
            StringComparer.Ordinal);

        List<ShopActionEvaluation> actionEvaluations = snapshot.Actions
            .Select(action => EvaluateAction(snapshot, action, offerEvaluationsById, bestRemovalCandidate))
            .OrderByDescending(static evaluation => evaluation.ImmediateScore)
            .ThenBy(evaluation => evaluation.Description, StringComparer.Ordinal)
            .ToList();

        Dictionary<string, ShopActionEvaluation> actionEvaluationsById = actionEvaluations.ToDictionary(
            static evaluation => evaluation.ActionId,
            StringComparer.Ordinal);

        List<ShopPlan> completedPlans = [];
        Dictionary<string, double> bestScoreByState = new(StringComparer.Ordinal);

        SimulatedShopState initialState = SimulatedShopState.Create(snapshot);
        SearchPlans(
            snapshot,
            offersById,
            actionEvaluationsById,
            initialState,
            [],
            0d,
            MaxSearchDepth,
            completedPlans,
            bestScoreByState);

        List<ShopPlan> rankedPlans = completedPlans
            .OrderByDescending(static plan => plan.TotalScore)
            .ThenBy(static plan => plan.Steps.Count)
            .ThenByDescending(static plan => plan.RemainingGold)
            .Take(MaxStoredPlans)
            .ToList();

        ShopPlan leavePlan = rankedPlans.FirstOrDefault(static plan => plan.PlanId == "leave_now")
            ?? BuildCompletedPlan(snapshot, initialState, [], 0d);
        ShopPlan bestPlan = rankedPlans.Count > 0 ? rankedPlans[0] : leavePlan;

        return new ShopPlannerResult
        {
            OfferEvaluations = offerEvaluations,
            ActionEvaluations = actionEvaluations,
            BestPlan = bestPlan,
            LeavePlan = leavePlan,
            ConsideredPlans = rankedPlans
        };
    }

    private ShopOfferEvaluation EvaluateOffer(
        ShopVisitState snapshot,
        ShopOffer offer,
        ShopRemovalCandidate? bestRemovalCandidate)
    {
        return offer.Kind switch
        {
            ShopOfferKind.CharacterCard or ShopOfferKind.ColorlessCard => EvaluateCardOffer(snapshot, offer),
            ShopOfferKind.Relic => EvaluateRelicOffer(snapshot, offer),
            ShopOfferKind.Potion => EvaluatePotionOffer(snapshot, offer),
            ShopOfferKind.CardRemoval => EvaluateRemovalOffer(snapshot, offer, bestRemovalCandidate),
            _ => BuildUnavailableOfferEvaluation(offer, "unknown offer kind")
        };
    }

    private ShopOfferEvaluation EvaluateCardOffer(ShopVisitState snapshot, ShopOffer offer)
    {
        if (offer.RuntimeCardModel == null || offer.ResolvedCard == null)
        {
            return BuildUnavailableOfferEvaluation(offer, "missing runtime card model");
        }

        CardEvaluationContext context = _cardChoiceEvaluator.ContextFactory.Create(
            snapshot.Player,
            CardChoiceSource.Shop,
            skipAllowed: true,
            candidateGoldCost: offer.Cost,
            debugSource: $"shop_offer_{offer.OfferId}");
        CardChoiceDecision decision = _cardChoiceEvaluator.EvaluateCandidates([offer.RuntimeCardModel], context);
        CardEvaluationResult? best = decision.BestEvaluation;
        if (best == null)
        {
            return BuildUnavailableOfferEvaluation(offer, "card evaluator returned no result");
        }

        double margin = best.FinalScore - decision.SkipThreshold;
        double totalScore = margin;
        List<string> reasons =
        [
            $"cardEval={best.FinalScore:F1}",
            $"shopThreshold={decision.SkipThreshold:F1}",
            $"margin={(margin >= 0 ? "+" : string.Empty)}{margin:F1}"
        ];

        if (decision.ShouldTakeCard)
        {
            totalScore += 4d;
            reasons.Add("above shop buy threshold");
        }
        else
        {
            totalScore -= 4d;
            reasons.Add("below shop buy threshold");
        }

        if (offer.IsOnSale)
        {
            totalScore += 2.5d;
            reasons.Add("sale pricing bonus");
        }

        if (!offer.IsAffordable)
        {
            totalScore -= 18d;
            reasons.Add("currently unaffordable");
        }

        if (!offer.IsStocked)
        {
            totalScore -= 30d;
            reasons.Add("not stocked");
        }
        else if (!offer.IsPurchaseLegalNow)
        {
            totalScore -= 5d;
            reasons.Add("not immediately legal");
        }

        if (offer.Kind == ShopOfferKind.ColorlessCard)
        {
            totalScore -= 1.5d;
            reasons.Add("colorless premium caution");
        }

        reasons.AddRange(best.Reasons);

        return new ShopOfferEvaluation
        {
            OfferId = offer.OfferId,
            Kind = offer.Kind,
            Name = offer.Name,
            TotalScore = totalScore,
            IsAffordable = offer.IsAffordable,
            IsLegalNow = offer.IsPurchaseLegalNow,
            Reasons = reasons
        };
    }

    private ShopOfferEvaluation EvaluateRelicOffer(ShopVisitState snapshot, ShopOffer offer)
    {
        if (offer.RuntimeRelicModel == null)
        {
            return BuildUnavailableOfferEvaluation(offer, "missing runtime relic model");
        }

        string relicId = offer.ModelId.ToUpperInvariant();
        double totalScore = GetRelicRarityBaseline(offer.Rarity) - (offer.Cost / 12d);
        List<string> reasons =
        [
            $"rarityBaseline={GetRelicRarityBaseline(offer.Rarity):F1}",
            $"costPenalty={-(offer.Cost / 12d):F1}"
        ];

        AddRelicPatternBonus("MEMBERSHIP", 18d, "membership discount scales future shops");
        AddRelicPatternBonus("COURIER", 15d, "courier discount and restock are premium");
        AddRelicPatternBonus("BAG_OF_PREPARATION", 11d, "opening draw consistency");
        AddRelicPatternBonus("ANCHOR", 9d, "reliable early block");
        AddRelicPatternBonus("ORICHALCUM", 8d, "passive defense floor");
        AddRelicPatternBonus("PANTOGRAPH", 8d, "boss sustain value");
        AddRelicPatternBonus("VAJRA", 6d, "passive attack scaling");

        if (relicId.Contains("STRIKE_DUMMY", StringComparison.Ordinal) ||
            relicId.Contains("STRIKEDUMMY", StringComparison.Ordinal))
        {
            int strikeCards = snapshot.DeckEntries.Count(static card =>
                card.CardId.Contains("STRIKE", StringComparison.OrdinalIgnoreCase));
            if (strikeCards >= 3)
            {
                double strikeBonus = 3d + strikeCards;
                totalScore += strikeBonus;
                reasons.Add($"strike synergy +{strikeBonus:F1}");
            }
        }

        if (snapshot.HasMembershipCard && relicId.Contains("MEMBERSHIP", StringComparison.Ordinal))
        {
            totalScore -= 20d;
            reasons.Add("already owns membership effect");
        }

        if (snapshot.HasCourier && relicId.Contains("COURIER", StringComparison.Ordinal))
        {
            totalScore -= 18d;
            reasons.Add("already owns courier effect");
        }

        if (!offer.IsAffordable)
        {
            totalScore -= 14d;
            reasons.Add("currently unaffordable");
        }

        if (!offer.IsStocked)
        {
            totalScore -= 30d;
            reasons.Add("not stocked");
        }

        return new ShopOfferEvaluation
        {
            OfferId = offer.OfferId,
            Kind = offer.Kind,
            Name = offer.Name,
            TotalScore = totalScore,
            IsAffordable = offer.IsAffordable,
            IsLegalNow = offer.IsPurchaseLegalNow,
            Reasons = reasons
        };

        void AddRelicPatternBonus(string pattern, double bonus, string reason)
        {
            if (relicId.Contains(pattern, StringComparison.Ordinal))
            {
                totalScore += bonus;
                reasons.Add($"{reason} +{bonus:F1}");
            }
        }
    }

    private ShopOfferEvaluation EvaluatePotionOffer(ShopVisitState snapshot, ShopOffer offer)
    {
        if (offer.RuntimePotionModel == null)
        {
            return BuildUnavailableOfferEvaluation(offer, "missing runtime potion model");
        }

        string potionId = offer.ModelId.ToUpperInvariant();
        double totalScore = GetPotionRarityBaseline(offer.Rarity) - (offer.Cost / 11d);
        List<string> reasons =
        [
            $"rarityBaseline={GetPotionRarityBaseline(offer.Rarity):F1}",
            $"costPenalty={-(offer.Cost / 11d):F1}"
        ];

        if (!snapshot.HasOpenPotionSlots)
        {
            totalScore -= 20d;
            reasons.Add("no open potion slots");
        }

        if (snapshot.HasSozu)
        {
            totalScore -= 30d;
            reasons.Add("Sozu blocks procurement");
        }

        if (MatchesAny(potionId, "BLOCK", "ARMOR", "DEX", "GHOST"))
        {
            double defenseBonus = snapshot.DeckSummary.BlockSources < 6 ? 6d : 3d;
            totalScore += defenseBonus;
            reasons.Add($"defensive coverage +{defenseBonus:F1}");
        }

        if (MatchesAny(potionId, "FIRE", "EXPLOS", "ATTACK", "STRENGTH", "FEAR", "VULNERABLE"))
        {
            double offenseBonus = snapshot.DeckSummary.FrontloadDamageSources < 7 ? 6d : 3d;
            totalScore += offenseBonus;
            reasons.Add($"offensive reach +{offenseBonus:F1}");
        }

        if (MatchesAny(potionId, "ENERGY", "DRAW", "GAMBLER", "AMBROSIA", "LIQUID"))
        {
            double tempoBonus = snapshot.DeckSummary.DrawSources < 2 || snapshot.DeckSummary.EnergySources < 1 ? 7d : 4d;
            totalScore += tempoBonus;
            reasons.Add($"tempo coverage +{tempoBonus:F1}");
        }

        if (MatchesAny(potionId, "FAIRY", "HEART", "ELIXIR"))
        {
            totalScore += 8d;
            reasons.Add("high leverage emergency value +8.0");
        }

        if (!offer.IsAffordable)
        {
            totalScore -= 10d;
            reasons.Add("currently unaffordable");
        }

        if (!offer.IsPurchaseLegalNow)
        {
            totalScore -= 18d;
            reasons.Add("not immediately legal");
        }

        return new ShopOfferEvaluation
        {
            OfferId = offer.OfferId,
            Kind = offer.Kind,
            Name = offer.Name,
            TotalScore = totalScore,
            IsAffordable = offer.IsAffordable,
            IsLegalNow = offer.IsPurchaseLegalNow,
            Reasons = reasons
        };
    }

    private ShopOfferEvaluation EvaluateRemovalOffer(
        ShopVisitState snapshot,
        ShopOffer offer,
        ShopRemovalCandidate? bestRemovalCandidate)
    {
        List<string> reasons = [];
        if (bestRemovalCandidate == null)
        {
            return new ShopOfferEvaluation
            {
                OfferId = offer.OfferId,
                Kind = offer.Kind,
                Name = offer.Name,
                TotalScore = -100d,
                IsAffordable = offer.IsAffordable,
                IsLegalNow = offer.IsPurchaseLegalNow,
                Reasons = ["no removable card candidate found"]
            };
        }

        int basicCards = snapshot.DeckEntries.Count(static card =>
            card.CardId.Contains("STRIKE", StringComparison.OrdinalIgnoreCase) ||
            card.CardId.Contains("DEFEND", StringComparison.OrdinalIgnoreCase));
        double deckSizeBonus = snapshot.DeckSummary.CardCount >= 20
            ? 7d
            : snapshot.DeckSummary.CardCount >= 15
                ? 4d
                : 2d;
        double consistencyBonus = (basicCards * 1.5d) +
                                  (snapshot.DeckSummary.AverageCost >= 1.4d ? 2.5d : 0d) +
                                  (snapshot.DeckSummary.ZeroCostCards == 0 ? 1d : 0d);
        double costPenalty = offer.Cost / 8.5d;
        double totalScore = bestRemovalCandidate.BurdenScore + deckSizeBonus + consistencyBonus - costPenalty;

        reasons.Add($"bestTarget={bestRemovalCandidate.Name}");
        reasons.Add($"targetBurden={bestRemovalCandidate.BurdenScore:F1}");
        reasons.Add($"deckSizeBonus={deckSizeBonus:F1}");
        reasons.Add($"consistencyBonus={consistencyBonus:F1}");
        reasons.Add($"costPenalty={-costPenalty:F1}");

        if (!offer.IsAffordable)
        {
            totalScore -= 14d;
            reasons.Add("currently unaffordable");
        }

        if (!snapshot.CardRemovalAvailable)
        {
            totalScore -= 40d;
            reasons.Add("card removal unavailable");
        }

        if (snapshot.HasHoarder)
        {
            totalScore -= 35d;
            reasons.Add("Hoarder prevents merchant removal");
        }

        return new ShopOfferEvaluation
        {
            OfferId = offer.OfferId,
            Kind = offer.Kind,
            Name = offer.Name,
            TotalScore = totalScore,
            IsAffordable = offer.IsAffordable,
            IsLegalNow = offer.IsPurchaseLegalNow,
            Reasons = reasons
        };
    }

    private ShopActionEvaluation EvaluateAction(
        ShopVisitState snapshot,
        ShopAction action,
        IReadOnlyDictionary<string, ShopOfferEvaluation> offerEvaluationsById,
        ShopRemovalCandidate? bestRemovalCandidate)
    {
        double score = 0d;
        List<string> reasons = [];
        ShopOfferEvaluation? offerEvaluation = null;

        switch (action.Kind)
        {
            case ShopActionKind.BuyOffer:
                if (action.OfferId != null && offerEvaluationsById.TryGetValue(action.OfferId, out ShopOfferEvaluation? matchedOfferEvaluation))
                {
                    offerEvaluation = matchedOfferEvaluation;
                    score = matchedOfferEvaluation.TotalScore;
                    reasons.Add($"offerScore={matchedOfferEvaluation.TotalScore:F1}");
                }
                else
                {
                    score = -100d;
                    reasons.Add("missing offer evaluation");
                }

                if (!action.IsCurrentlyLegal)
                {
                    reasons.Add(action.RequiresInventoryOpen
                        ? "currently blocked by inventory/gold/legality; planner may unlock later"
                        : "currently illegal");
                }
                break;

            case ShopActionKind.RemoveCard:
                if (action.OfferId != null && offerEvaluationsById.TryGetValue(action.OfferId, out ShopOfferEvaluation? removalEvaluation))
                {
                    offerEvaluation = removalEvaluation;
                    score = removalEvaluation.TotalScore;
                    reasons.Add($"removalScore={removalEvaluation.TotalScore:F1}");
                }
                else
                {
                    score = -100d;
                    reasons.Add("missing removal evaluation");
                }

                if (bestRemovalCandidate != null)
                {
                    reasons.Add($"target={bestRemovalCandidate.Name}");
                }
                break;

            case ShopActionKind.UseFoulPotionAtMerchant:
                if (!snapshot.HasUsableFoulPotion)
                {
                    score = -100d;
                    reasons.Add("no usable foul potion");
                    break;
                }

                score = 60d;
                reasons.Add("normal merchant policy: always use foul potion for +100 gold");
                reasons.Add($"+{MerchantFoulPotionGoldGain} gold before further shop planning");
                break;

            case ShopActionKind.OpenInventory:
                score = snapshot.InventoryIsOpen ? -2d : 0.5d;
                reasons.Add(snapshot.InventoryIsOpen ? "inventory already open" : "opens merchant inventory");
                break;

            case ShopActionKind.CloseInventory:
                score = snapshot.InventoryIsOpen ? 0d : -2d;
                reasons.Add(snapshot.InventoryIsOpen ? "needed before leaving" : "inventory already closed");
                break;

            case ShopActionKind.LeaveShop:
                score = 0d;
                reasons.Add("baseline leave option");
                break;
        }

        return new ShopActionEvaluation
        {
            ActionId = action.ActionId,
            Kind = action.Kind,
            Description = action.Description,
            ImmediateScore = score,
            IsLegalNow = action.IsCurrentlyLegal,
            IsConsideredByPlanner = action.Kind != ShopActionKind.LeaveShop || !snapshot.InventoryIsOpen,
            OfferEvaluation = offerEvaluation,
            RemovalCandidate = action.Kind == ShopActionKind.RemoveCard ? bestRemovalCandidate : null,
            Reasons = reasons
        };
    }

    private ShopRemovalCandidate? SelectBestRemovalCandidate(ShopVisitState snapshot)
    {
        Dictionary<string, int> copiesById = snapshot.DeckEntries
            .GroupBy(static card => card.CardId, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal);

        List<ShopRemovalCandidate> candidates = [];
        foreach (ShopDeckCard deckCard in snapshot.DeckEntries.Where(static card => card.IsRemovable))
        {
            double burden = 0d;
            List<string> reasons = [];
            ResolvedCardView resolved = deckCard.ResolvedCard;

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
                    reasons.Add("rare card keep bias -6.0");
                    break;
                case "Uncommon":
                    burden -= 2d;
                    reasons.Add("uncommon keep bias -2.0");
                    break;
            }

            if (deckCard.CardId.Contains("STRIKE", StringComparison.OrdinalIgnoreCase))
            {
                burden += 14d;
                reasons.Add("starter strike burden +14.0");
            }

            if (deckCard.CardId.Contains("DEFEND", StringComparison.OrdinalIgnoreCase))
            {
                burden += 11d;
                reasons.Add("starter defend burden +11.0");
            }

            if (copiesById.TryGetValue(deckCard.CardId, out int copies) && copies > 1)
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

            candidates.Add(new ShopRemovalCandidate
            {
                CardId = deckCard.CardId,
                Name = deckCard.Name,
                BurdenScore = burden,
                DeckCard = deckCard,
                Reasons = reasons
            });
        }

        return candidates
            .OrderByDescending(static candidate => candidate.BurdenScore)
            .ThenBy(candidate => candidate.Name, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private void SearchPlans(
        ShopVisitState snapshot,
        IReadOnlyDictionary<string, ShopOffer> offersById,
        IReadOnlyDictionary<string, ShopActionEvaluation> actionEvaluationsById,
        SimulatedShopState state,
        IReadOnlyList<ShopPlanStep> steps,
        double currentScore,
        int depthRemaining,
        List<ShopPlan> completedPlans,
        Dictionary<string, double> bestScoreByState)
    {
        string stateKey = state.BuildKey();
        if (bestScoreByState.TryGetValue(stateKey, out double bestKnownScore) &&
            bestKnownScore >= currentScore)
        {
            return;
        }

        bestScoreByState[stateKey] = currentScore;
        completedPlans.Add(BuildCompletedPlan(snapshot, state, steps, currentScore));

        if (depthRemaining <= 0)
        {
            return;
        }

        List<PlannableAction> nextActions = BuildPlannableActions(snapshot, offersById, actionEvaluationsById, state)
            .OrderByDescending(static candidate => candidate.SortScore)
            .ThenBy(candidate => candidate.Action.Description, StringComparer.Ordinal)
            .Take(MaxChildrenPerState)
            .ToList();

        foreach (PlannableAction nextAction in nextActions)
        {
            SimulatedShopState nextState = ApplyAction(state, nextAction.Action, offersById);
            ShopPlanStep step = new()
            {
                ActionId = nextAction.Action.ActionId,
                Kind = nextAction.Action.Kind,
                Description = nextAction.Action.Description,
                ScoreContribution = nextAction.Evaluation.ImmediateScore,
                GoldBefore = state.Gold,
                GoldAfter = nextState.Gold,
                Reasons = nextAction.Evaluation.Reasons
            };

            List<ShopPlanStep> nextSteps = steps.Concat([step]).ToList();
            SearchPlans(
                snapshot,
                offersById,
                actionEvaluationsById,
                nextState,
                nextSteps,
                currentScore + nextAction.Evaluation.ImmediateScore,
                depthRemaining - 1,
                completedPlans,
                bestScoreByState);
        }
    }

    private List<PlannableAction> BuildPlannableActions(
        ShopVisitState snapshot,
        IReadOnlyDictionary<string, ShopOffer> offersById,
        IReadOnlyDictionary<string, ShopActionEvaluation> actionEvaluationsById,
        SimulatedShopState state)
    {
        List<PlannableAction> candidates = [];

        foreach (ShopAction action in snapshot.Actions)
        {
            if (!actionEvaluationsById.TryGetValue(action.ActionId, out ShopActionEvaluation? evaluation))
            {
                continue;
            }

            if (!CanTakeAction(state, action, offersById))
            {
                continue;
            }

            double sortScore = evaluation.ImmediateScore;
            if (action.Kind == ShopActionKind.OpenInventory || action.Kind == ShopActionKind.CloseInventory)
            {
                sortScore += 0.25d;
            }

            if (action.Kind == ShopActionKind.UseFoulPotionAtMerchant)
            {
                sortScore += 2d;
            }

            candidates.Add(new PlannableAction
            {
                Action = action,
                Evaluation = evaluation,
                SortScore = sortScore
            });
        }

        return candidates;
    }

    private static bool CanTakeAction(
        SimulatedShopState state,
        ShopAction action,
        IReadOnlyDictionary<string, ShopOffer> offersById)
    {
        switch (action.Kind)
        {
            case ShopActionKind.OpenInventory:
                return !state.InventoryOpen;

            case ShopActionKind.CloseInventory:
                return state.InventoryOpen;

            case ShopActionKind.LeaveShop:
                return false;

            case ShopActionKind.UseFoulPotionAtMerchant:
                return state.FoulPotionAvailable;

            case ShopActionKind.RemoveCard:
                return state.InventoryOpen &&
                       state.RemovalAvailable &&
                       action.GoldCost.HasValue &&
                       state.Gold >= action.GoldCost.Value;

            case ShopActionKind.BuyOffer:
                if (!state.InventoryOpen ||
                    action.OfferId == null ||
                    !state.RemainingOfferIds.Contains(action.OfferId) ||
                    !offersById.TryGetValue(action.OfferId, out ShopOffer? offer) ||
                    !action.GoldCost.HasValue ||
                    state.Gold < action.GoldCost.Value)
                {
                    return false;
                }

                if (offer.Kind == ShopOfferKind.Potion && state.FilledPotionSlots >= state.MaxPotionSlots)
                {
                    return false;
                }

                return offer.IsStocked;

            default:
                return false;
        }
    }

    private static SimulatedShopState ApplyAction(
        SimulatedShopState state,
        ShopAction action,
        IReadOnlyDictionary<string, ShopOffer> offersById)
    {
        SimulatedShopState next = state.Clone();
        switch (action.Kind)
        {
            case ShopActionKind.OpenInventory:
                next.InventoryOpen = true;
                break;

            case ShopActionKind.CloseInventory:
                next.InventoryOpen = false;
                break;

            case ShopActionKind.UseFoulPotionAtMerchant:
                next.FoulPotionAvailable = false;
                next.Gold += MerchantFoulPotionGoldGain;
                next.FilledPotionSlots = Math.Max(0, next.FilledPotionSlots - 1);
                break;

            case ShopActionKind.RemoveCard:
                if (action.GoldCost.HasValue)
                {
                    next.Gold -= action.GoldCost.Value;
                }

                next.RemovalAvailable = false;
                if (action.OfferId != null)
                {
                    next.RemainingOfferIds.Remove(action.OfferId);
                }
                break;

            case ShopActionKind.BuyOffer:
                if (action.GoldCost.HasValue)
                {
                    next.Gold -= action.GoldCost.Value;
                }

                if (action.OfferId != null)
                {
                    next.RemainingOfferIds.Remove(action.OfferId);
                    if (offersById.TryGetValue(action.OfferId, out ShopOffer? offer) &&
                        offer.Kind == ShopOfferKind.Potion)
                    {
                        next.FilledPotionSlots = Math.Min(next.MaxPotionSlots, next.FilledPotionSlots + 1);
                    }
                }
                break;
        }

        return next;
    }

    private static ShopPlan BuildCompletedPlan(
        ShopVisitState snapshot,
        SimulatedShopState state,
        IReadOnlyList<ShopPlanStep> steps,
        double currentScore)
    {
        List<ShopPlanStep> completedSteps = steps.ToList();
        if (snapshot.ExecutionMode == ShopExecutionMode.LocalSharedUi && state.InventoryOpen)
        {
            completedSteps.Add(new ShopPlanStep
            {
                ActionId = "shop_close_inventory",
                Kind = ShopActionKind.CloseInventory,
                Description = "Close merchant inventory.",
                ScoreContribution = 0d,
                GoldBefore = state.Gold,
                GoldAfter = state.Gold,
                Reasons = ["close inventory before leaving"]
            });
        }

        completedSteps.Add(new ShopPlanStep
        {
            ActionId = "shop_leave",
            Kind = ShopActionKind.LeaveShop,
            Description = snapshot.ExecutionMode == ShopExecutionMode.VirtualAiDirect
                ? "Mark the virtual merchant visit complete."
                : "Leave the merchant room via proceed.",
            ScoreContribution = 0d,
            GoldBefore = state.Gold,
            GoldAfter = state.Gold,
            Reasons = [snapshot.ExecutionMode == ShopExecutionMode.VirtualAiDirect
                ? "finish virtual merchant visit"
                : "baseline leave action"]
        });

        string planId = completedSteps.Count == 1 && completedSteps[0].ActionId == "shop_leave"
            ? "leave_now"
            : $"plan_{string.Join("_", completedSteps.Select(static step => step.ActionId))}";
        string outcome = completedSteps.Count == 1
            ? "leave without spending"
            : $"take {completedSteps.Count - 1} shop step(s), then leave";

        if (snapshot.HasCourier)
        {
            outcome += " (Courier restock not simulated)";
        }

        return new ShopPlan
        {
            PlanId = planId,
            Steps = completedSteps,
            TotalScore = currentScore,
            RemainingGold = state.Gold,
            LeavesShop = true,
            OutcomeSummary = outcome
        };
    }

    private static ShopOfferEvaluation BuildUnavailableOfferEvaluation(ShopOffer offer, string reason)
    {
        return new ShopOfferEvaluation
        {
            OfferId = offer.OfferId,
            Kind = offer.Kind,
            Name = offer.Name,
            TotalScore = -100d,
            IsAffordable = offer.IsAffordable,
            IsLegalNow = false,
            Reasons = [reason]
        };
    }

    private static double GetRelicRarityBaseline(string? rarity)
    {
        return rarity switch
        {
            "Ancient" => 28d,
            "Rare" => 21d,
            "Uncommon" => 15d,
            "Common" => 10d,
            _ => 8d
        };
    }

    private static double GetPotionRarityBaseline(string? rarity)
    {
        return rarity switch
        {
            "Event" => 14d,
            "Rare" => 12d,
            "Uncommon" => 8d,
            "Common" => 5d,
            _ => 4d
        };
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

    private sealed class SimulatedShopState
    {
        public required int Gold { get; set; }

        public required bool InventoryOpen { get; set; }

        public required bool FoulPotionAvailable { get; set; }

        public required int FilledPotionSlots { get; set; }

        public required int MaxPotionSlots { get; set; }

        public required bool RemovalAvailable { get; set; }

        public required HashSet<string> RemainingOfferIds { get; init; }

        public static SimulatedShopState Create(ShopVisitState snapshot)
        {
            return new SimulatedShopState
            {
                Gold = snapshot.Gold,
                InventoryOpen = snapshot.InventoryIsOpen,
                FoulPotionAvailable = snapshot.HasUsableFoulPotion,
                FilledPotionSlots = snapshot.FilledPotionSlots,
                MaxPotionSlots = snapshot.MaxPotionSlots,
                RemovalAvailable = snapshot.CardRemovalAvailable,
                RemainingOfferIds = snapshot.Offers
                    .Where(static offer => offer.IsStocked)
                    .Select(static offer => offer.OfferId)
                    .ToHashSet(StringComparer.Ordinal)
            };
        }

        public SimulatedShopState Clone()
        {
            return new SimulatedShopState
            {
                Gold = Gold,
                InventoryOpen = InventoryOpen,
                FoulPotionAvailable = FoulPotionAvailable,
                FilledPotionSlots = FilledPotionSlots,
                MaxPotionSlots = MaxPotionSlots,
                RemovalAvailable = RemovalAvailable,
                RemainingOfferIds = new HashSet<string>(RemainingOfferIds, StringComparer.Ordinal)
            };
        }

        public string BuildKey()
        {
            return $"{Gold}|{InventoryOpen}|{FoulPotionAvailable}|{FilledPotionSlots}|{RemovalAvailable}|{string.Join(",", RemainingOfferIds.OrderBy(static id => id, StringComparer.Ordinal))}";
        }
    }

    private sealed class PlannableAction
    {
        public required ShopAction Action { get; init; }

        public required ShopActionEvaluation Evaluation { get; init; }

        public required double SortScore { get; init; }
    }
}

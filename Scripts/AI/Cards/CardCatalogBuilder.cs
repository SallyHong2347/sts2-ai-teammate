using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;

namespace AITeammate.Scripts;

internal sealed class CardCatalogBuilder
{
    private const int SummaryIdLimit = 12;

    public CardCatalogRepository Build()
    {
        CardCatalogRepository repository = new();
        List<string> partialCardIds = [];
        List<string> failedCardIds = [];
        int completeCount = 0;
        int partialCount = 0;

        foreach (CardModel card in ModelDb.AllCards.OrderBy(static card => card.Id.Entry, StringComparer.Ordinal))
        {
            try
            {
                CardCatalogEntry baselineEntry = BuildBaselineEntrySafe(card);
                CardCatalogEntry entry = TryEnrichEntry(baselineEntry, card, out bool isPartial);
                repository.Upsert(entry);

                if (isPartial)
                {
                    partialCount++;
                    partialCardIds.Add(entry.CardId);
                }
                else
                {
                    completeCount++;
                }
            }
            catch (Exception exception)
            {
                string cardId = GetCardIdSafe(card);
                repository.MarkFailed(cardId);
                failedCardIds.Add(cardId);
                Log.Warn($"[AITeammate] Failed to build even baseline card catalog entry for card={cardId}: {exception}");
            }
        }

        int totalProcessed = completeCount + partialCount + failedCardIds.Count;
        Log.Info($"[AITeammate] Built static card catalog entries total={totalProcessed} complete={completeCount} partial={partialCount} failed={failedCardIds.Count}.");
        LogBuildSummary("partial", partialCardIds);
        LogBuildSummary("failed", failedCardIds);
        return repository;
    }

    private static CardCatalogEntry BuildBaselineEntrySafe(CardModel canonicalCard)
    {
        string cardId = GetCardIdSafe(canonicalCard);
        IReadOnlyList<string> keywords = GetKeywordsSafe(canonicalCard);
        IReadOnlyList<string> tags = GetTagsSafe(canonicalCard);
        return new CardCatalogEntry
        {
            CardId = cardId,
            BuildStatus = CardCatalogBuildStatus.Partial,
            Name = GetTitleSafe(canonicalCard, cardId),
            PoolId = string.Empty,
            Type = GetCardTypeSafe(canonicalCard),
            Rarity = GetCardRaritySafe(canonicalCard),
            TargetType = GetTargetTypeSafe(canonicalCard),
            ShouldShowInCardLibrary = false,
            BaseCost = GetCanonicalCostSafe(canonicalCard),
            HasXCost = GetHasXCostSafe(canonicalCard),
            BaseDescription = string.Empty,
            UpgradeDescriptionPreview = string.Empty,
            Keywords = keywords,
            Tags = tags,
            HoverTipRefs = [],
            MaxUpgradeLevel = 0,
            BaseFlags = BuildFlagsFromKeywords(keywords, canonicalCard),
            BaseDynamicVars = new Dictionary<string, int>(StringComparer.Ordinal),
            UpgradeSpec = CardUpgradeSpec.Empty,
            SemanticProfile = new CardSemanticProfile()
        };
    }

    private static CardCatalogEntry TryEnrichEntry(CardCatalogEntry baselineEntry, CardModel canonicalCard, out bool isPartial)
    {
        List<string> enrichmentFailures = [];
        string poolId = TryGetOptional(
            () => canonicalCard.Pool.Id.Entry,
            string.Empty,
            canonicalCard,
            enrichmentFailures,
            "pool_id");
        bool shouldShowInCardLibrary = TryGetOptional(
            () => canonicalCard.ShouldShowInCardLibrary,
            baselineEntry.ShouldShowInCardLibrary,
            canonicalCard,
            enrichmentFailures,
            "card_library_visibility");

        string baseDescription = TryGetOptional(
            () => GetFormattedDescription(canonicalCard),
            string.Empty,
            canonicalCard,
            enrichmentFailures,
            "base_description");
        IReadOnlyDictionary<string, int> dynamicVars = TryGetOptional(
            () => GetDynamicVars(canonicalCard),
            new Dictionary<string, int>(StringComparer.Ordinal),
            canonicalCard,
            enrichmentFailures,
            "dynamic_vars");

        CardUpgradeSpec upgradeSpec = CardUpgradeSpec.Empty;
        string upgradeDescriptionPreview = string.Empty;
        int maxUpgradeLevel = TryGetOptional(
            () => canonicalCard.MaxUpgradeLevel,
            baselineEntry.MaxUpgradeLevel,
            canonicalCard,
            enrichmentFailures,
            "max_upgrade_level");

        if (maxUpgradeLevel > 0)
        {
            CardSnapshot? upgradedSnapshot = TryCaptureFirstUpgradeSnapshot(canonicalCard, maxUpgradeLevel, enrichmentFailures);
            if (upgradedSnapshot != null)
            {
                upgradeDescriptionPreview = upgradedSnapshot.Description;
                if (!string.IsNullOrEmpty(baseDescription))
                {
                    IReadOnlyList<NormalizedEffectDescriptor> baseEffects = TryGetOptional(
                        () => ExtractSemanticEffects(canonicalCard, baseDescription, dynamicVars),
                        baselineEntry.SemanticProfile.Effects,
                        canonicalCard,
                        enrichmentFailures,
                        "base_effects");
                    upgradeSpec = BuildUpgradeSpec(new CardSnapshot
                    {
                        Cost = baselineEntry.BaseCost,
                        HasXCost = baselineEntry.HasXCost,
                        Description = baseDescription,
                        Keywords = baselineEntry.Keywords,
                        Tags = baselineEntry.Tags,
                        HoverTipRefs = [],
                        Flags = baselineEntry.BaseFlags,
                        DynamicVars = dynamicVars,
                        Effects = baseEffects
                    }, upgradedSnapshot);
                }
            }
        }

        IReadOnlyList<NormalizedEffectDescriptor> enrichedEffects = string.IsNullOrEmpty(baseDescription)
            ? baselineEntry.SemanticProfile.Effects
            : TryGetOptional(
                () => ExtractSemanticEffects(canonicalCard, baseDescription, dynamicVars),
                baselineEntry.SemanticProfile.Effects,
                canonicalCard,
                enrichmentFailures,
                "semantic_effects");

        isPartial = enrichmentFailures.Count > 0;
        if (isPartial)
        {
            Log.Warn($"[AITeammate] Built partial catalog entry for card={baselineEntry.CardId} optionalFailures=[{string.Join(", ", enrichmentFailures)}]");
        }

        CardCatalogEntry entry = new()
        {
            CardId = baselineEntry.CardId,
            BuildStatus = isPartial ? CardCatalogBuildStatus.Partial : CardCatalogBuildStatus.Complete,
            Name = baselineEntry.Name,
            PoolId = poolId,
            Type = baselineEntry.Type,
            Rarity = baselineEntry.Rarity,
            TargetType = baselineEntry.TargetType,
            ShouldShowInCardLibrary = shouldShowInCardLibrary,
            BaseCost = baselineEntry.BaseCost,
            HasXCost = baselineEntry.HasXCost,
            BaseDescription = baseDescription,
            UpgradeDescriptionPreview = upgradeDescriptionPreview,
            Keywords = baselineEntry.Keywords,
            Tags = baselineEntry.Tags,
            HoverTipRefs = [],
            MaxUpgradeLevel = maxUpgradeLevel,
            BaseFlags = baselineEntry.BaseFlags,
            BaseDynamicVars = dynamicVars,
            UpgradeSpec = upgradeSpec,
            SemanticProfile = new CardSemanticProfile
            {
                Effects = enrichedEffects
            }
        };

        Log.Debug($"[AITeammate] Catalog card built id={entry.CardId} status={entry.BuildStatus} effects=[{string.Join(", ", entry.SemanticProfile.Effects.Select(static effect => effect.Describe()))}]");
        return entry;
    }

    private static CardSnapshot CaptureSnapshot(CardModel card, bool isUpgradePreview)
    {
        string description = isUpgradePreview
            ? card.GetDescriptionForUpgradePreview()
            : GetFormattedDescription(card);
        IReadOnlyDictionary<string, int> dynamicVars = GetDynamicVars(card);
        CardFlags flags = GetFlags(card);
        IReadOnlyList<NormalizedEffectDescriptor> effects = ExtractSemanticEffects(card, description, dynamicVars);

        return new CardSnapshot
        {
            Cost = card.EnergyCost.Canonical,
            HasXCost = card.EnergyCost.CostsX,
            Description = description,
            Keywords = card.Keywords.Select(static keyword => keyword.ToString()).OrderBy(static keyword => keyword, StringComparer.Ordinal).ToArray(),
            Tags = card.Tags.Select(static tag => tag.ToString()).OrderBy(static tag => tag, StringComparer.Ordinal).ToArray(),
            HoverTipRefs = [],
            Flags = flags,
            DynamicVars = dynamicVars,
            Effects = effects
        };
    }

    private static CardSnapshot? TryCaptureFirstUpgradeSnapshot(CardModel canonicalCard, int maxUpgradeLevel, List<string> enrichmentFailures)
    {
        if (maxUpgradeLevel <= 0)
        {
            return null;
        }

        try
        {
            CardModel upgraded = canonicalCard.ToMutable();
            upgraded.UpgradeInternal();
            upgraded.FinalizeUpgradeInternal();
            return CaptureSnapshot(upgraded, isUpgradePreview: true);
        }
        catch (Exception exception)
        {
            enrichmentFailures.Add($"upgrade_snapshot:{exception.GetType().Name}");
            Log.Warn($"[AITeammate] Skipping upgrade snapshot for card={canonicalCard.Id.Entry}: {exception}");
            return null;
        }
    }

    private static CardUpgradeSpec BuildUpgradeSpec(CardSnapshot baseSnapshot, CardSnapshot upgradedSnapshot)
    {
        Dictionary<EffectAdjustmentKey, int> effectAdjustments = new();

        foreach (NormalizedEffectDescriptor effect in baseSnapshot.Effects.Concat(upgradedSnapshot.Effects))
        {
            EffectAdjustmentKey key = new(effect.Kind, effect.AppliedPowerId);
            int upgradedAmount = upgradedSnapshot.Effects
                .Where(candidate => candidate.Kind == effect.Kind &&
                                    string.Equals(candidate.AppliedPowerId, effect.AppliedPowerId, StringComparison.Ordinal))
                .Sum(static candidate => Math.Max(candidate.Amount, 0));
            int baseAmount = baseSnapshot.Effects
                .Where(candidate => candidate.Kind == effect.Kind &&
                                    string.Equals(candidate.AppliedPowerId, effect.AppliedPowerId, StringComparison.Ordinal))
                .Sum(static candidate => Math.Max(candidate.Amount, 0));
            int delta = upgradedAmount - baseAmount;
            if (delta != 0)
            {
                effectAdjustments[key] = delta;
            }
        }

        int costDelta = upgradedSnapshot.Cost - baseSnapshot.Cost;
        int? costOverride = costDelta == 0 ? null : upgradedSnapshot.Cost;

        return new CardUpgradeSpec
        {
            CostDelta = 0,
            CostOverride = costOverride,
            Exhaust = baseSnapshot.Flags.Exhaust != upgradedSnapshot.Flags.Exhaust ? upgradedSnapshot.Flags.Exhaust : null,
            Ethereal = baseSnapshot.Flags.Ethereal != upgradedSnapshot.Flags.Ethereal ? upgradedSnapshot.Flags.Ethereal : null,
            Retain = baseSnapshot.Flags.Retain != upgradedSnapshot.Flags.Retain ? upgradedSnapshot.Flags.Retain : null,
            ReplayCountOverride = baseSnapshot.Flags.ReplayCount != upgradedSnapshot.Flags.ReplayCount ? upgradedSnapshot.Flags.ReplayCount : null,
            EffectAmountAdjustments = effectAdjustments
        };
    }

    private static string GetFormattedDescription(CardModel card)
    {
        LocString description = card.Description;
        card.DynamicVars.AddTo(description);
        return description.GetFormattedText();
    }

    private static IReadOnlyDictionary<string, int> GetDynamicVars(CardModel card)
    {
        Dictionary<string, int> values = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, DynamicVar> pair in card.DynamicVars)
        {
            values[pair.Key] = (int)pair.Value.BaseValue;
        }

        return values;
    }

    private static CardFlags GetFlags(CardModel card)
    {
        IReadOnlySet<CardKeyword> keywords = card.Keywords;
        return BuildFlagsFromKeywords(keywords.Select(static keyword => keyword.ToString()).ToArray(), card);
    }

    private static CardFlags BuildFlagsFromKeywords(IReadOnlyList<string> keywords, CardModel card)
    {
        HashSet<string> keywordSet = keywords.ToHashSet(StringComparer.Ordinal);
        return new CardFlags
        {
            Exhaust = keywordSet.Contains(CardKeyword.Exhaust.ToString()),
            Ethereal = keywordSet.Contains(CardKeyword.Ethereal.ToString()),
            Retain = keywordSet.Contains(CardKeyword.Retain.ToString()),
            Innate = keywordSet.Contains(CardKeyword.Innate.ToString()),
            Unplayable = keywordSet.Contains(CardKeyword.Unplayable.ToString()),
            ReplayCount = GetReplayCountSafe(card)
        };
    }

    private static T TryGetOptional<T>(
        Func<T> getter,
        T fallback,
        CardModel card,
        List<string> enrichmentFailures,
        string label)
    {
        try
        {
            return getter();
        }
        catch (Exception exception)
        {
            enrichmentFailures.Add($"{label}:{exception.GetType().Name}");
            Log.Warn($"[AITeammate] Optional card metadata failed card={GetCardIdSafe(card)} field={label}: {exception}");
            return fallback;
        }
    }

    private static string GetCardIdSafe(CardModel card)
    {
        try
        {
            return string.IsNullOrWhiteSpace(card.Id.Entry)
                ? card.GetType().Name
                : card.Id.Entry;
        }
        catch
        {
            return card.GetType().Name;
        }
    }

    private static string GetTitleSafe(CardModel card, string fallback)
    {
        try
        {
            return string.IsNullOrWhiteSpace(card.Title) ? fallback : card.Title;
        }
        catch
        {
            return fallback;
        }
    }

    private static CardType GetCardTypeSafe(CardModel card)
    {
        try
        {
            return card.Type;
        }
        catch
        {
            return CardType.Status;
        }
    }

    private static string GetCardRaritySafe(CardModel card)
    {
        try
        {
            return card.Rarity.ToString();
        }
        catch
        {
            return "Unknown";
        }
    }

    private static TargetType GetTargetTypeSafe(CardModel card)
    {
        try
        {
            return card.TargetType;
        }
        catch
        {
            return TargetType.None;
        }
    }

    private static int GetCanonicalCostSafe(CardModel card)
    {
        try
        {
            return card.EnergyCost.Canonical;
        }
        catch
        {
            return 0;
        }
    }

    private static bool GetHasXCostSafe(CardModel card)
    {
        try
        {
            return card.EnergyCost.CostsX;
        }
        catch
        {
            return false;
        }
    }

    private static IReadOnlyList<string> GetKeywordsSafe(CardModel card)
    {
        try
        {
            return card.Keywords
                .Select(static keyword => keyword.ToString())
                .OrderBy(static keyword => keyword, StringComparer.Ordinal)
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    private static IReadOnlyList<string> GetTagsSafe(CardModel card)
    {
        try
        {
            return card.Tags
                .Select(static tag => tag.ToString())
                .OrderBy(static tag => tag, StringComparer.Ordinal)
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    private static int GetReplayCountSafe(CardModel card)
    {
        try
        {
            return Math.Max(card.BaseReplayCount, 0);
        }
        catch
        {
            return 0;
        }
    }

    private static void LogBuildSummary(string label, IReadOnlyList<string> cardIds)
    {
        if (cardIds.Count == 0)
        {
            return;
        }

        string sample = string.Join(", ", cardIds.Take(SummaryIdLimit));
        string suffix = cardIds.Count > SummaryIdLimit ? ", ..." : string.Empty;
        Log.Info($"[AITeammate] Card catalog {label} ids=[{sample}{suffix}]");
    }

    private static HoverTipRefKind GetHoverTipKind(IHoverTip tip)
    {
        if (tip is CardHoverTip)
        {
            return HoverTipRefKind.Card;
        }

        return tip.CanonicalModel switch
        {
            PowerModel => HoverTipRefKind.Power,
            CardModel => HoverTipRefKind.Card,
            OrbModel => HoverTipRefKind.Orb,
            _ => InferHoverTipKindFromId(tip.Id)
        };
    }

    private static HoverTipRefKind InferHoverTipKindFromId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return HoverTipRefKind.Unknown;
        }

        if (id.Contains("static_hover_tips", StringComparison.OrdinalIgnoreCase))
        {
            return HoverTipRefKind.StaticConcept;
        }

        if (id.Contains("card_keywords", StringComparison.OrdinalIgnoreCase))
        {
            return HoverTipRefKind.Keyword;
        }

        if (id.Contains("POWER", StringComparison.OrdinalIgnoreCase))
        {
            return HoverTipRefKind.Power;
        }

        return HoverTipRefKind.Unknown;
    }

    private static string? GetHoverTipTitle(IHoverTip tip)
    {
        return tip switch
        {
            HoverTip hoverTip => hoverTip.Title,
            CardHoverTip cardHoverTip => cardHoverTip.Card.Title,
            _ => tip.CanonicalModel switch
            {
                CardModel cardModel => cardModel.Title,
                PowerModel powerModel => powerModel.Title.GetFormattedText(),
                OrbModel orbModel => orbModel.Title.GetFormattedText(),
                _ => null
            }
        };
    }

    private static string? GetHoverTipDescription(IHoverTip tip)
    {
        return tip switch
        {
            HoverTip hoverTip => hoverTip.Description,
            CardHoverTip cardHoverTip => GetFormattedDescription(cardHoverTip.Card),
            _ => tip.CanonicalModel switch
            {
                PowerModel powerModel => powerModel.Description.GetFormattedText(),
                _ => null
            }
        };
    }

    private static IReadOnlyList<NormalizedEffectDescriptor> ExtractSemanticEffects(
        CardModel card,
        string description,
        IReadOnlyDictionary<string, int> dynamicVars)
    {
        List<NormalizedEffectDescriptor> effects = [];
        int repeatCount = GetDynamicVar(dynamicVars, "Repeat", fallback: 1);
        int damage = Math.Max(0, GetDynamicVar(dynamicVars, "Damage") + GetDynamicVar(dynamicVars, "ExtraDamage"));
        if (damage > 0)
        {
            effects.Add(new NormalizedEffectDescriptor
            {
                Kind = EffectKind.DealDamage,
                TargetScope = MapTargetScope(card.TargetType),
                Amount = damage,
                RepeatCount = Math.Max(repeatCount, 1),
                DurationHint = DurationHint.Immediate,
                ValueTiming = ValueTiming.Immediate
            });
        }

        int block = GetDynamicVar(dynamicVars, "Block");
        if (block <= 0 && card.GainsBlock)
        {
            block = GetDynamicVar(dynamicVars, "CalculatedBlock");
        }

        if (block > 0)
        {
            effects.Add(new NormalizedEffectDescriptor
            {
                Kind = EffectKind.GainBlock,
                TargetScope = TargetScope.Self,
                Amount = block,
                DurationHint = DurationHint.Immediate,
                ValueTiming = ValueTiming.Immediate
            });
        }

        AddApplyPowerEffect(effects, card.TargetType, description, dynamicVars, "Vulnerable", "VulnerablePower");
        AddApplyPowerEffect(effects, card.TargetType, description, dynamicVars, "Weak", "WeakPower");
        AddApplyPowerEffect(effects, card.TargetType, description, dynamicVars, "Strength", "StrengthPower");
        AddApplyPowerEffect(effects, card.TargetType, description, dynamicVars, "Dexterity", "DexterityPower");

        int cardsDrawn = GetDynamicVar(dynamicVars, "Cards");
        if (cardsDrawn > 0)
        {
            effects.Add(new NormalizedEffectDescriptor
            {
                Kind = EffectKind.DrawCards,
                TargetScope = TargetScope.Self,
                Amount = cardsDrawn,
                DurationHint = DurationHint.Immediate,
                ValueTiming = ValueTiming.Setup
            });
        }

        int energyGain = GetDynamicVar(dynamicVars, "Energy");
        if (energyGain > 0)
        {
            effects.Add(new NormalizedEffectDescriptor
            {
                Kind = EffectKind.GainEnergy,
                TargetScope = TargetScope.Self,
                Amount = energyGain,
                DurationHint = DurationHint.Immediate,
                ValueTiming = ValueTiming.Immediate
            });
        }

        return effects;
    }

    private static void AddApplyPowerEffect(
        List<NormalizedEffectDescriptor> effects,
        TargetType targetType,
        string description,
        IReadOnlyDictionary<string, int> dynamicVars,
        string powerId,
        string dynamicVarName)
    {
        int amount = GetDynamicVar(dynamicVars, dynamicVarName);
        if (amount <= 0)
        {
            return;
        }

        bool isTemporaryBuff = (powerId is "Strength" or "Dexterity") &&
                               description.Contains("this turn", StringComparison.OrdinalIgnoreCase);
        effects.Add(new NormalizedEffectDescriptor
        {
            Kind = EffectKind.ApplyPower,
            TargetScope = MapTargetScope(targetType),
            Amount = amount,
            AppliedPowerId = powerId,
            DurationHint = isTemporaryBuff ? DurationHint.ThisTurn : DurationHint.Unknown,
            ValueTiming = powerId switch
            {
                "Weak" or "Vulnerable" => ValueTiming.Mixed,
                "Strength" or "Dexterity" when isTemporaryBuff => ValueTiming.Mixed,
                "Strength" or "Dexterity" => ValueTiming.Setup,
                _ => ValueTiming.Setup
            }
        });
    }

    private static int GetDynamicVar(IReadOnlyDictionary<string, int> dynamicVars, string name, int fallback = 0)
    {
        return dynamicVars.TryGetValue(name, out int value) ? Math.Max(value, 0) : fallback;
    }

    private static TargetScope MapTargetScope(TargetType targetType)
    {
        return targetType switch
        {
            TargetType.Self => TargetScope.Self,
            TargetType.AnyEnemy or TargetType.RandomEnemy => TargetScope.SingleEnemy,
            TargetType.AllEnemies => TargetScope.AllEnemies,
            TargetType.AnyAlly or TargetType.AnyPlayer or TargetType.Osty => TargetScope.SingleAlly,
            TargetType.AllAllies => TargetScope.AllAllies,
            _ => TargetScope.Any
        };
    }


    private sealed class CardSnapshot
    {
        public int Cost { get; init; }

        public bool HasXCost { get; init; }

        public string Description { get; init; } = string.Empty;

        public IReadOnlyList<string> Keywords { get; init; } = [];

        public IReadOnlyList<string> Tags { get; init; } = [];

        public IReadOnlyList<HoverTipRef> HoverTipRefs { get; init; } = [];

        public CardFlags Flags { get; init; } = new();

        public IReadOnlyDictionary<string, int> DynamicVars { get; init; } = new Dictionary<string, int>();

        public IReadOnlyList<NormalizedEffectDescriptor> Effects { get; init; } = [];
    }
}

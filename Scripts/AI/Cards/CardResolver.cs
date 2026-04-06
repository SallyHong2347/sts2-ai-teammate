using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;

namespace AITeammate.Scripts;

internal sealed class CardResolver : ICardResolver
{
    private static readonly string[] VulnerableKeys = ["VulnerablePower"];
    private static readonly string[] WeakKeys = ["WeakPower"];
    private static readonly string[] StrengthKeys = ["StrengthPower"];
    private static readonly string[] DexterityKeys = ["DexterityPower"];
    private static readonly HashSet<string> LoggedDegradedMetadataKeys = new(StringComparer.Ordinal);
    private static readonly object DegradedMetadataLogLock = new();

    private readonly CardCatalogRepository _catalogRepository;
    private readonly CardDefinitionRepository _fallbackRepository;
    private readonly RunCardStateStore _runStateStore;
    private readonly CombatCardStateStore _combatStateStore;

    public CardResolver(
        CardCatalogRepository catalogRepository,
        CardDefinitionRepository fallbackRepository,
        RunCardStateStore runStateStore,
        CombatCardStateStore combatStateStore)
    {
        _catalogRepository = catalogRepository;
        _fallbackRepository = fallbackRepository;
        _runStateStore = runStateStore;
        _combatStateStore = combatStateStore;
    }

    public ResolvedCardView Resolve(CardModel liveCard, string cardInstanceId)
    {
        string cardId = GetCardIdSafe(liveCard);
        int upgradeLevel = GetUpgradeLevel(liveCard);
        bool isUpgraded = upgradeLevel > 0;
        CardStateOverlay? runOverlay = _runStateStore.TryGet(cardInstanceId, out CardStateOverlay? storedRunOverlay)
            ? storedRunOverlay
            : null;
        CardStateOverlay? combatOverlay = _combatStateStore.TryGet(cardInstanceId, out CardStateOverlay? storedCombatOverlay)
            ? storedCombatOverlay
            : null;

        if (_catalogRepository.TryGet(cardId, out CardCatalogEntry? catalogEntry) && catalogEntry != null)
        {
            ResolvedCardView catalogResolved = ResolveFromCatalog(
                catalogEntry,
                cardInstanceId,
                upgradeLevel,
                isUpgraded,
                runOverlay,
                combatOverlay);
            Log.Debug($"[AITeammate] Resolved card instance={cardInstanceId} card={catalogResolved.CardId} source={catalogResolved.MetadataSource} status={catalogResolved.CatalogBuildStatus} effects=[{string.Join(", ", catalogResolved.Effects.Select(static effect => effect.Describe()))}]");
            return catalogResolved;
        }

        CardCatalogBuildStatus missingStatus = _catalogRepository.TryGetStatus(cardId, out CardCatalogBuildStatus knownStatus)
            ? knownStatus
            : CardCatalogBuildStatus.Failed;

        string fallbackReason = knownStatus == CardCatalogBuildStatus.Failed
            ? "catalog_failed"
            : "catalog_missing";
        ResolvedCardView fallbackResolved = ResolveFromFallback(liveCard, cardInstanceId, upgradeLevel, isUpgraded, runOverlay, combatOverlay, missingStatus, fallbackReason);
        Log.Debug($"[AITeammate] Resolved card instance={cardInstanceId} card={fallbackResolved.CardId} source={fallbackResolved.MetadataSource} status={fallbackResolved.CatalogBuildStatus} effects=[{string.Join(", ", fallbackResolved.Effects.Select(static effect => effect.Describe()))}]");
        return fallbackResolved;
    }

    private ResolvedCardView ResolveFromCatalog(
        CardCatalogEntry entry,
        string cardInstanceId,
        int upgradeLevel,
        bool isUpgraded,
        CardStateOverlay? runOverlay,
        CardStateOverlay? combatOverlay)
    {
        bool canTrustEnrichedMetadata = entry.BuildStatus == CardCatalogBuildStatus.Complete;
        if (!canTrustEnrichedMetadata)
        {
            LogDegradedCardMetadataOnce(entry.CardId, "catalog_partial", "using baseline-only catalog metadata; enriched semantics and upgrade data are skipped.");
        }

        CardUpgradeSpec trustedUpgradeSpec = canTrustEnrichedMetadata ? entry.UpgradeSpec : CardUpgradeSpec.Empty;
        int effectiveCost = entry.BaseCost;
        effectiveCost = ApplyCostUpgrade(effectiveCost, trustedUpgradeSpec, isUpgraded);
        effectiveCost = ApplyOverlayCost(effectiveCost, runOverlay);
        effectiveCost = ApplyOverlayCost(effectiveCost, combatOverlay);
        effectiveCost = Math.Max(0, effectiveCost);

        bool exhaust = ApplyFlag(entry.BaseFlags.Exhaust, trustedUpgradeSpec.Exhaust, isUpgraded, runOverlay?.Exhaust, combatOverlay?.Exhaust);
        bool ethereal = ApplyFlag(entry.BaseFlags.Ethereal, trustedUpgradeSpec.Ethereal, isUpgraded, runOverlay?.Ethereal, combatOverlay?.Ethereal);
        bool retain = ApplyFlag(entry.BaseFlags.Retain, trustedUpgradeSpec.Retain, isUpgraded, runOverlay?.Retain, combatOverlay?.Retain);

        int replayCount = entry.BaseFlags.ReplayCount;
        if (isUpgraded && trustedUpgradeSpec.ReplayCountOverride.HasValue)
        {
            replayCount = trustedUpgradeSpec.ReplayCountOverride.Value;
        }

        if (runOverlay?.ReplayCountOverride.HasValue == true)
        {
            replayCount = runOverlay.ReplayCountOverride.Value;
        }

        if (combatOverlay?.ReplayCountOverride.HasValue == true)
        {
            replayCount = combatOverlay.ReplayCountOverride.Value;
        }

        IReadOnlyList<NormalizedEffectDescriptor> baseEffects = canTrustEnrichedMetadata
            ? entry.SemanticProfile.Effects
            : [];
        IReadOnlyList<NormalizedEffectDescriptor> effects = ResolveEffects(baseEffects, trustedUpgradeSpec, isUpgraded, runOverlay, combatOverlay);
        return new ResolvedCardView
        {
            CardInstanceId = cardInstanceId,
            CardId = entry.CardId,
            CatalogBuildStatus = entry.BuildStatus,
            MetadataSource = canTrustEnrichedMetadata ? "CatalogComplete" : "CatalogPartialBaseline",
            UsesConservativeMetadata = !canTrustEnrichedMetadata,
            CanTrustEnrichedMetadata = canTrustEnrichedMetadata,
            Name = entry.Name,
            Type = entry.Type,
            Targeting = entry.TargetType,
            EffectiveCost = effectiveCost,
            Rarity = entry.Rarity,
            Keywords = entry.Keywords.ToArray(),
            Tags = entry.Tags.ToArray(),
            Exhaust = exhaust,
            Ethereal = ethereal,
            Retain = retain,
            ReplayCount = Math.Max(0, replayCount),
            IsUpgraded = isUpgraded,
            UpgradeLevel = upgradeLevel,
            Effects = effects
        };
    }

    private ResolvedCardView ResolveFromFallback(
        CardModel liveCard,
        string cardInstanceId,
        int upgradeLevel,
        bool isUpgraded,
        CardStateOverlay? runOverlay,
        CardStateOverlay? combatOverlay,
        CardCatalogBuildStatus catalogStatus,
        string fallbackReason)
    {
        CardDefinition definition = GetOrCreateFallbackDefinition(liveCard);
        LogDegradedCardMetadataOnce(definition.CardId, fallbackReason, "using conservative fallback card metadata.");
        int effectiveCost = definition.BaseCost;
        effectiveCost = ApplyCostUpgrade(effectiveCost, definition.UpgradeSpec, isUpgraded);
        effectiveCost = ApplyOverlayCost(effectiveCost, runOverlay);
        effectiveCost = ApplyOverlayCost(effectiveCost, combatOverlay);
        effectiveCost = Math.Max(0, effectiveCost);

        bool exhaust = ApplyFlag(definition.Exhaust, definition.UpgradeSpec.Exhaust, isUpgraded, runOverlay?.Exhaust, combatOverlay?.Exhaust);
        bool ethereal = ApplyFlag(definition.Ethereal, definition.UpgradeSpec.Ethereal, isUpgraded, runOverlay?.Ethereal, combatOverlay?.Ethereal);
        bool retain = ApplyFlag(definition.Retain, definition.UpgradeSpec.Retain, isUpgraded, runOverlay?.Retain, combatOverlay?.Retain);

        int replayCount = definition.ReplayCount;
        if (isUpgraded && definition.UpgradeSpec.ReplayCountOverride.HasValue)
        {
            replayCount = definition.UpgradeSpec.ReplayCountOverride.Value;
        }

        if (runOverlay?.ReplayCountOverride.HasValue == true)
        {
            replayCount = runOverlay.ReplayCountOverride.Value;
        }

        if (combatOverlay?.ReplayCountOverride.HasValue == true)
        {
            replayCount = combatOverlay.ReplayCountOverride.Value;
        }

        IReadOnlyList<NormalizedEffectDescriptor> effects = ResolveEffects(definition.Effects, definition.UpgradeSpec, isUpgraded, runOverlay, combatOverlay);
        return new ResolvedCardView
        {
            CardInstanceId = cardInstanceId,
            CardId = definition.CardId,
            CatalogBuildStatus = catalogStatus,
            MetadataSource = "FallbackLiveBaseline",
            UsesConservativeMetadata = true,
            CanTrustEnrichedMetadata = false,
            Name = definition.Name,
            Type = definition.Type,
            Targeting = definition.Targeting,
            EffectiveCost = effectiveCost,
            Rarity = definition.Rarity,
            Keywords = definition.Keywords.ToArray(),
            Tags = [],
            Exhaust = exhaust,
            Ethereal = ethereal,
            Retain = retain,
            ReplayCount = Math.Max(0, replayCount),
            IsUpgraded = isUpgraded,
            UpgradeLevel = upgradeLevel,
            Effects = effects
        };
    }

    private static IReadOnlyList<NormalizedEffectDescriptor> ResolveEffects(
        IReadOnlyList<NormalizedEffectDescriptor> baseEffects,
        CardUpgradeSpec upgradeSpec,
        bool isUpgraded,
        CardStateOverlay? runOverlay,
        CardStateOverlay? combatOverlay)
    {
        List<NormalizedEffectDescriptor> resolved = baseEffects
            .Select(static effect => new NormalizedEffectDescriptor
            {
                Kind = effect.Kind,
                TargetScope = effect.TargetScope,
                Amount = effect.Amount,
                RepeatCount = effect.RepeatCount,
                AppliedPowerId = effect.AppliedPowerId,
                DurationHint = effect.DurationHint,
                ValueTiming = effect.ValueTiming
            })
            .ToList();

        if (isUpgraded)
        {
            ApplyEffectAdjustments(resolved, upgradeSpec.EffectAmountAdjustments);
        }

        if (runOverlay != null)
        {
            ApplyEffectAdjustments(resolved, runOverlay.EffectAmountAdjustments);
        }

        if (combatOverlay != null)
        {
            ApplyEffectAdjustments(resolved, combatOverlay.EffectAmountAdjustments);
        }

        return resolved;
    }

    private static void ApplyEffectAdjustments(
        List<NormalizedEffectDescriptor> effects,
        IReadOnlyDictionary<EffectAdjustmentKey, int> adjustments)
    {
        foreach ((EffectAdjustmentKey key, int delta) in adjustments)
        {
            for (int i = 0; i < effects.Count; i++)
            {
                NormalizedEffectDescriptor effect = effects[i];
                if (effect.Kind != key.Kind ||
                    !string.Equals(effect.AppliedPowerId, key.AppliedPowerId, StringComparison.Ordinal))
                {
                    continue;
                }

                effects[i] = new NormalizedEffectDescriptor
                {
                    Kind = effect.Kind,
                    TargetScope = effect.TargetScope,
                    Amount = Math.Max(0, effect.Amount + delta),
                    RepeatCount = effect.RepeatCount,
                    AppliedPowerId = effect.AppliedPowerId,
                    DurationHint = effect.DurationHint,
                    ValueTiming = effect.ValueTiming
                };
            }
        }
    }

    private static int ApplyCostUpgrade(int baseCost, CardUpgradeSpec upgradeSpec, bool isUpgraded)
    {
        if (!isUpgraded)
        {
            return baseCost;
        }

        int cost = upgradeSpec.CostOverride ?? baseCost;
        return cost + upgradeSpec.CostDelta;
    }

    private static int ApplyOverlayCost(int currentCost, CardStateOverlay? overlay)
    {
        if (overlay == null)
        {
            return currentCost;
        }

        int effective = overlay.CostOverride ?? currentCost;
        return effective + overlay.CostDelta;
    }

    private static bool ApplyFlag(bool baseValue, bool? upgradeValue, bool isUpgraded, bool? runValue, bool? combatValue)
    {
        bool effective = isUpgraded && upgradeValue.HasValue ? upgradeValue.Value : baseValue;
        if (runValue.HasValue)
        {
            effective = runValue.Value;
        }

        if (combatValue.HasValue)
        {
            effective = combatValue.Value;
        }

        return effective;
    }

    private CardDefinition GetOrCreateFallbackDefinition(CardModel liveCard)
    {
        string cardId = GetCardIdSafe(liveCard);
        if (_fallbackRepository.TryGet(cardId, out CardDefinition? definition) && definition != null)
        {
            return definition;
        }

        definition = CreateDefinitionFromLiveCardSafe(liveCard);
        _fallbackRepository.Upsert(definition);
        Log.Debug($"[AITeammate] Card definition fallback extracted from live card data for {cardId}.");
        return definition;
    }

    private static CardDefinition CreateDefinitionFromLiveCardSafe(CardModel liveCard)
    {
        string cardId = GetCardIdSafe(liveCard);
        IReadOnlyList<string> keywords = GetKeywordStringsSafe(liveCard);
        return new CardDefinition
        {
            CardId = cardId,
            Name = GetStringPropertySafe(liveCard, "Name", "DisplayName") ?? GetTitleSafe(liveCard, cardId),
            Type = TryGetLiveValue(() => liveCard.Type, default(CardType)),
            Targeting = TryGetLiveValue(() => liveCard.TargetType, default(TargetType)),
            BaseCost = Math.Max(0, TryGetLiveValue(() => liveCard.EnergyCost.GetAmountToSpend(), 0)),
            Rarity = GetObjectStringSafe(liveCard, "Rarity") ?? "Unknown",
            Keywords = keywords,
            Exhaust = HasKeywordSafe(liveCard, CardKeyword.Exhaust),
            Ethereal = HasKeywordSafe(liveCard, CardKeyword.Ethereal),
            Retain = HasKeywordSafe(liveCard, CardKeyword.Retain),
            ReplayCount = Math.Max(TryGetLiveValue(() => liveCard.BaseReplayCount, 0), 0),
            Effects = ExtractEffectsSafe(liveCard),
            UpgradeSpec = CardUpgradeSpec.Empty
        };
    }

    private static IReadOnlyList<string> GetKeywordStringsSafe(CardModel liveCard)
    {
        HashSet<string> keywords = new(StringComparer.Ordinal);
        TryAddValues(liveCard, keywords, "Keywords");
        TryAddValues(liveCard, keywords, "Tags");
        return keywords.ToArray();
    }

    private static void TryAddValues(CardModel liveCard, HashSet<string> target, string propertyName)
    {
        object? rawValues = TryGetLiveValue(
            () => liveCard.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)?.GetValue(liveCard),
            null);
        if (rawValues is not IEnumerable values || values is string)
        {
            return;
        }

        foreach (object? value in values)
        {
            if (value == null)
            {
                continue;
            }

            string text = value.ToString() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(text))
            {
                target.Add(text);
            }
        }
    }

    private static IReadOnlyList<NormalizedEffectDescriptor> ExtractEffectsSafe(CardModel liveCard)
    {
        List<NormalizedEffectDescriptor> effects = [];
        int damage = GetEstimatedDamage(liveCard, out int repeatCount);
        if (damage > 0)
        {
            effects.Add(new NormalizedEffectDescriptor
            {
                Kind = EffectKind.DealDamage,
                TargetScope = MapTargetScope(liveCard.TargetType),
                Amount = damage,
                RepeatCount = repeatCount,
                DurationHint = DurationHint.Immediate,
                ValueTiming = ValueTiming.Immediate
            });
        }

        int block = GetDynamicVarValue(liveCard, "CalculatedBlock");
        if (block <= 0)
        {
            block = GetDynamicVarValue(liveCard, "Block");
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

        AddPowerEffects(effects, liveCard, VulnerableKeys, "Vulnerable");
        AddPowerEffects(effects, liveCard, WeakKeys, "Weak");
        AddPowerEffects(effects, liveCard, StrengthKeys, "Strength");
        AddPowerEffects(effects, liveCard, DexterityKeys, "Dexterity");

        int cardsDrawn = GetDynamicVarValue(liveCard, "Cards");
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

        int energy = GetDynamicVarValue(liveCard, "Energy");
        if (energy > 0)
        {
            effects.Add(new NormalizedEffectDescriptor
            {
                Kind = EffectKind.GainEnergy,
                TargetScope = TargetScope.Self,
                Amount = energy,
                DurationHint = DurationHint.Immediate,
                ValueTiming = ValueTiming.Immediate
            });
        }

        return effects;
    }

    private static void AddPowerEffects(
        List<NormalizedEffectDescriptor> effects,
        CardModel liveCard,
        IEnumerable<string> dynamicVarKeys,
        string powerId)
    {
        int amount = 0;
        foreach (string key in dynamicVarKeys)
        {
            amount = Math.Max(amount, GetDynamicVarValue(liveCard, key));
        }

        if (amount <= 0)
        {
            return;
        }

        effects.Add(new NormalizedEffectDescriptor
        {
            Kind = EffectKind.ApplyPower,
            TargetScope = MapTargetScope(liveCard.TargetType),
            Amount = amount,
            AppliedPowerId = powerId,
            DurationHint = DurationHint.Unknown,
            ValueTiming = ValueTiming.Mixed
        });
    }

    private static int GetEstimatedDamage(CardModel liveCard, out int repeatCount)
    {
        int damage = GetDynamicVarValue(liveCard, "CalculatedDamage");
        if (damage <= 0)
        {
            damage = GetDynamicVarValue(liveCard, "Damage");
        }

        repeatCount = Math.Max(GetDynamicVarValue(liveCard, "Repeat"), 1);
        int extraDamage = GetDynamicVarValue(liveCard, "ExtraDamage");
        return Math.Max(damage + extraDamage, 0);
    }

    private static int GetDynamicVarValue(CardModel liveCard, string key)
    {
        IReadOnlyDictionary<string, DynamicVar>? dynamicVars = TryGetLiveValue(() => liveCard.DynamicVars, null);
        if (dynamicVars == null || !dynamicVars.TryGetValue(key, out DynamicVar? value))
        {
            return 0;
        }

        return Math.Max(TryGetLiveValue(() => value.IntValue, 0), 0);
    }

    private static int GetUpgradeLevel(CardModel liveCard)
    {
        return Math.Max(TryGetLiveValue(() => liveCard.CurrentUpgradeLevel, 0), 0);
    }

    private static string? GetStringPropertySafe(CardModel liveCard, params string[] propertyNames)
    {
        foreach (string propertyName in propertyNames)
        {
            object? value = GetPropertyValueSafe(liveCard, propertyName);
            if (value is string text && !string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return null;
    }

    private static string? GetObjectStringSafe(CardModel liveCard, string propertyName)
    {
        object? value = GetPropertyValueSafe(liveCard, propertyName);
        return value?.ToString();
    }

    private static object? GetPropertyValueSafe(CardModel liveCard, string propertyName)
    {
        return TryGetLiveValue(
            () => liveCard.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)?.GetValue(liveCard),
            null);
    }

    private static bool HasKeywordSafe(CardModel liveCard, CardKeyword keyword)
    {
        IReadOnlyCollection<CardKeyword>? keywords = TryGetLiveValue(() => liveCard.Keywords, null);
        return keywords?.Contains(keyword) == true;
    }

    private static string GetCardIdSafe(CardModel liveCard)
    {
        return TryGetLiveValue(() => liveCard.Id.Entry, "UNKNOWN_CARD");
    }

    private static string GetTitleSafe(CardModel liveCard, string fallback)
    {
        return TryGetLiveValue(() => liveCard.Title?.ToString(), null) ?? fallback;
    }

    private static T TryGetLiveValue<T>(Func<T> getter, T fallback)
    {
        try
        {
            return getter();
        }
        catch (Exception exception)
        {
            Log.Debug($"[AITeammate] Live card metadata access failed: {exception.GetType().Name}: {exception.Message}");
            return fallback;
        }
    }

    private static void LogDegradedCardMetadataOnce(string cardId, string mode, string message)
    {
        string key = $"{cardId}|{mode}";
        lock (DegradedMetadataLogLock)
        {
            if (!LoggedDegradedMetadataKeys.Add(key))
            {
                return;
            }
        }

        Log.Warn($"[AITeammate] Card metadata degraded for {cardId}: {message} mode={mode}");
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

}

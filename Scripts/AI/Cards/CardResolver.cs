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
        string cardId = liveCard.Id.Entry;
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
            ResolvedCardView catalogResolved = ResolveFromCatalog(catalogEntry, cardInstanceId, upgradeLevel, isUpgraded, runOverlay, combatOverlay);
            Log.Debug($"[AITeammate] Resolved card instance={cardInstanceId} card={catalogResolved.CardId} source=catalog effects=[{string.Join(", ", catalogResolved.Effects.Select(static effect => effect.Describe()))}]");
            return catalogResolved;
        }

        CardDefinition fallbackDefinition = GetOrCreateFallbackDefinition(liveCard);
        ResolvedCardView fallbackResolved = ResolveFromFallback(fallbackDefinition, cardInstanceId, upgradeLevel, isUpgraded, runOverlay, combatOverlay);
        Log.Debug($"[AITeammate] Resolved card instance={cardInstanceId} card={fallbackResolved.CardId} source=fallback effects=[{string.Join(", ", fallbackResolved.Effects.Select(static effect => effect.Describe()))}]");
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
        int effectiveCost = entry.BaseCost;
        effectiveCost = ApplyCostUpgrade(effectiveCost, entry.UpgradeSpec, isUpgraded);
        effectiveCost = ApplyOverlayCost(effectiveCost, runOverlay);
        effectiveCost = ApplyOverlayCost(effectiveCost, combatOverlay);
        effectiveCost = Math.Max(0, effectiveCost);

        bool exhaust = ApplyFlag(entry.BaseFlags.Exhaust, entry.UpgradeSpec.Exhaust, isUpgraded, runOverlay?.Exhaust, combatOverlay?.Exhaust);
        bool ethereal = ApplyFlag(entry.BaseFlags.Ethereal, entry.UpgradeSpec.Ethereal, isUpgraded, runOverlay?.Ethereal, combatOverlay?.Ethereal);
        bool retain = ApplyFlag(entry.BaseFlags.Retain, entry.UpgradeSpec.Retain, isUpgraded, runOverlay?.Retain, combatOverlay?.Retain);

        int replayCount = entry.BaseFlags.ReplayCount;
        if (isUpgraded && entry.UpgradeSpec.ReplayCountOverride.HasValue)
        {
            replayCount = entry.UpgradeSpec.ReplayCountOverride.Value;
        }

        if (runOverlay?.ReplayCountOverride.HasValue == true)
        {
            replayCount = runOverlay.ReplayCountOverride.Value;
        }

        if (combatOverlay?.ReplayCountOverride.HasValue == true)
        {
            replayCount = combatOverlay.ReplayCountOverride.Value;
        }

        IReadOnlyList<NormalizedEffectDescriptor> effects = ResolveEffects(entry.SemanticProfile.Effects, entry.UpgradeSpec, isUpgraded, runOverlay, combatOverlay);
        return new ResolvedCardView
        {
            CardInstanceId = cardInstanceId,
            CardId = entry.CardId,
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
        CardDefinition definition,
        string cardInstanceId,
        int upgradeLevel,
        bool isUpgraded,
        CardStateOverlay? runOverlay,
        CardStateOverlay? combatOverlay)
    {
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
        string cardId = liveCard.Id.Entry;
        if (_fallbackRepository.TryGet(cardId, out CardDefinition? definition) && definition != null)
        {
            return definition;
        }

        definition = CreateDefinitionFromLiveCard(liveCard);
        _fallbackRepository.Upsert(definition);
        Log.Debug($"[AITeammate] Card definition fallback extracted from live card data for {cardId}.");
        return definition;
    }

    private static CardDefinition CreateDefinitionFromLiveCard(CardModel liveCard)
    {
        return new CardDefinition
        {
            CardId = liveCard.Id.Entry,
            Name = GetStringProperty(liveCard, "Name", "DisplayName") ?? liveCard.Title,
            Type = liveCard.Type,
            Targeting = liveCard.TargetType,
            BaseCost = Math.Max(0, liveCard.EnergyCost.GetAmountToSpend()),
            Rarity = GetObjectString(liveCard, "Rarity") ?? "Unknown",
            Keywords = GetKeywordStrings(liveCard),
            Exhaust = liveCard.Keywords.Contains(CardKeyword.Exhaust),
            Ethereal = liveCard.Keywords.Contains(CardKeyword.Ethereal),
            Retain = liveCard.Keywords.Contains(CardKeyword.Retain),
            ReplayCount = Math.Max(liveCard.BaseReplayCount, 0),
            Effects = ExtractEffects(liveCard),
            UpgradeSpec = CardUpgradeSpec.Empty
        };
    }

    private static IReadOnlyList<string> GetKeywordStrings(CardModel liveCard)
    {
        HashSet<string> keywords = new(StringComparer.Ordinal);
        AddValues(liveCard, keywords, "Keywords");
        AddValues(liveCard, keywords, "Tags");
        return keywords.ToArray();
    }

    private static void AddValues(CardModel liveCard, HashSet<string> target, string propertyName)
    {
        PropertyInfo? property = liveCard.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        if (property?.GetValue(liveCard) is not IEnumerable values || values is string)
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

    private static IReadOnlyList<NormalizedEffectDescriptor> ExtractEffects(CardModel liveCard)
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

        bool isTemporaryBuff = (powerId is "Strength" or "Dexterity") &&
                               liveCard.Description.GetFormattedText().Contains("this turn", StringComparison.OrdinalIgnoreCase);
        effects.Add(new NormalizedEffectDescriptor
        {
            Kind = EffectKind.ApplyPower,
            TargetScope = GuessPowerTargetScope(liveCard.TargetType, powerId),
            Amount = amount,
            AppliedPowerId = powerId,
            DurationHint = isTemporaryBuff ? DurationHint.ThisTurn : DurationHint.Unknown,
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
        if (!liveCard.DynamicVars.TryGetValue(key, out DynamicVar? value))
        {
            return 0;
        }

        return Math.Max(value.IntValue, 0);
    }

    private static int GetUpgradeLevel(CardModel liveCard)
    {
        return Math.Max(liveCard.CurrentUpgradeLevel, 0);
    }

    private static string? GetStringProperty(CardModel liveCard, params string[] propertyNames)
    {
        foreach (string propertyName in propertyNames)
        {
            object? value = GetPropertyValue(liveCard, propertyName);
            if (value is string text && !string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return null;
    }

    private static string? GetObjectString(CardModel liveCard, string propertyName)
    {
        object? value = GetPropertyValue(liveCard, propertyName);
        return value?.ToString();
    }

    private static object? GetPropertyValue(CardModel liveCard, string propertyName)
    {
        return liveCard.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)?.GetValue(liveCard);
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

    private static TargetScope GuessPowerTargetScope(TargetType targetType, string powerId)
    {
        return powerId switch
        {
            "Strength" or "Dexterity" when targetType is TargetType.Self or TargetType.AnyPlayer or TargetType.AnyAlly => TargetScope.Self,
            "Strength" or "Dexterity" => TargetScope.Self,
            _ => MapTargetScope(targetType)
        };
    }
}

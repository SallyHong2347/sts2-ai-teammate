using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;

namespace AITeammate.Scripts;

internal sealed class EnemyReactiveMetadataBuilder
{
    private static readonly Dictionary<string, EnemyReactiveTriggerKind> HookTriggerKinds = new(StringComparer.Ordinal)
    {
        ["BeforeDamageReceived"] = EnemyReactiveTriggerKind.BeforeDamageReceived,
        ["AfterDamageReceived"] = EnemyReactiveTriggerKind.AfterDamageReceived,
        ["AfterAttack"] = EnemyReactiveTriggerKind.AfterAttack,
        ["BeforeCardPlayed"] = EnemyReactiveTriggerKind.BeforeCardPlayed,
        ["AfterCardPlayed"] = EnemyReactiveTriggerKind.AfterCardPlayed,
        ["AfterCardDrawn"] = EnemyReactiveTriggerKind.AfterCardDrawn,
        ["AfterCardEnteredCombat"] = EnemyReactiveTriggerKind.AfterCardEnteredCombat,
        ["BeforeCombatStart"] = EnemyReactiveTriggerKind.BeforeCombatStart,
        ["AfterApplied"] = EnemyReactiveTriggerKind.AfterApplied,
        ["AfterRemoved"] = EnemyReactiveTriggerKind.AfterRemoved,
        ["AfterDeath"] = EnemyReactiveTriggerKind.AfterDeath,
        ["BeforeTurnEnd"] = EnemyReactiveTriggerKind.BeforeTurnEnd,
        ["AfterTurnEnd"] = EnemyReactiveTriggerKind.AfterTurnEnd,
        ["BeforeSideTurnStart"] = EnemyReactiveTriggerKind.BeforeSideTurnStart,
        ["AfterSideTurnStart"] = EnemyReactiveTriggerKind.AfterSideTurnStart,
        ["ShouldPlay"] = EnemyReactiveTriggerKind.ShouldPlay,
        ["TryModifyEnergyCostInCombat"] = EnemyReactiveTriggerKind.TryModifyEnergyCostInCombat,
        ["ModifyDamageMultiplicative"] = EnemyReactiveTriggerKind.ModifyDamageMultiplicative,
        ["ModifyDamageAdditive"] = EnemyReactiveTriggerKind.ModifyDamageAdditive
    };

    private static readonly Regex MethodRegex = new(
        @"(?<signature>(?:public|private|protected|internal)\s+(?:override\s+)?(?:async\s+)?(?:[\w<>\[\]\?,\s\.]+)\s+(?<name>\w+)\s*\([^;\{]*\)\s*(?:=>\s*[^;]+;|\{))",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex CreatureDamageRegex = new(@"CreatureCmd\.Damage\s*\((?<args>.*?)\)", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex ApplyPowerRegex = new(@"PowerCmd\.Apply<(?<power>[\w\d_]+)>\s*\((?<args>.*?)\)", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex GainBlockRegex = new(@"CreatureCmd\.(GainBlock|GainArmor)\s*\((?<args>.*?)\)", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex AfflictRegex = new(@"CardCmd\.Afflict(?:AndPreview)?<(?<affliction>[\w\d_]+)>\s*\((?<args>.*?)\)", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex KeywordRegex = new(@"CardCmd\.ApplyKeyword\s*\((?<args>.*?)CardKeyword\.(?<keyword>\w+)\s*\)", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex RemoveKeywordRegex = new(@"CardCmd\.RemoveKeyword\s*\((?<args>.*?)CardKeyword\.(?<keyword>\w+)\s*\)", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex AddStatusRegex = new(@"CardPileCmd\.(?:AddToCombatAndPreview|AddGeneratedCardToCombat)<(?<card>[\w\d_]+)>\s*\((?<args>.*?)\)", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex CreateCardRegex = new(@"CreateCard<(?<card>[\w\d_]+)>\s*\(", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex StunRegex = new(@"CreatureCmd\.Stun\s*\((?<args>.*?)\)", RegexOptions.Singleline | RegexOptions.Compiled);

    public EnemyReactiveMetadataRepository Build()
    {
        EnemyReactiveMetadataRepository repository = new();
        string sourceRoot = ResolveSourceRoot();
        List<PowerModel> canonicalPowers = EnumerateCanonicalPowers();
        List<string> findings =
        [
            $"Power definitions discovered from reflected PowerModel types count={canonicalPowers.Count}.",
            "Reactive enemy truth is broadly extractable from power hook overrides plus command construction in decompiled source.",
            "Trigger kinds, retaliation scope, card-flow punishments, and many direct outcomes are statically discoverable from source method bodies.",
            "Some effects remain only partially knowable until runtime because they depend on history entries, blocked versus unblocked damage, dynamic vars, or conditional hook branches."
        ];

        int fullyStatic = 0;
        int runtimeResolved = 0;
        int partial = 0;
        int reactiveCount = 0;
        int filteredPassiveOrNonPunisher = 0;
        List<string> filteredExamples = [];

        foreach (PowerModel power in canonicalPowers.OrderBy(static power => power.Id.Entry, StringComparer.Ordinal))
        {
            EnemyReactiveMetadataBuildResult buildResult = BuildMetadata(power, sourceRoot);
            EnemyReactiveMetadata? metadata = buildResult.Metadata;
            if (metadata == null)
            {
                if (buildResult.WasFilteredAsNonReactive)
                {
                    filteredPassiveOrNonPunisher++;
                    if (filteredExamples.Count < 8)
                    {
                        filteredExamples.Add(power.Id.Entry);
                    }
                }

                continue;
            }

            repository.Upsert(metadata);
            reactiveCount++;

            if (metadata.RequiresRuntimeResolution)
            {
                runtimeResolved++;
            }

            if (!metadata.RequiresRuntimeResolution && !metadata.HasPartialUnknowns)
            {
                fullyStatic++;
            }

            if (metadata.HasPartialUnknowns)
            {
                partial++;
            }
        }

        findings.Add(sourceRoot.Length > 0
            ? $"Enemy reactive source root resolved: {sourceRoot}"
            : "Enemy reactive source root could not be resolved; repository is empty because source-driven normalization is required for this pass.");
        findings.Add("Validated source anchors include ThornsPower retaliation, TenderPower on-card-play stat reduction, and status-card insertion via PersonalHivePower and PainfulStabsPower.");
        findings.Add("Direct status/curse insertion exists in some powers, but many punishers use card afflictions or play restrictions instead of deck insertion.");
        findings.Add($"Filtered passive or non-punisher powers count={filteredPassiveOrNonPunisher} examples=[{string.Join(", ", filteredExamples)}].");

        repository.SetReport(new EnemyReactiveMetadataBuildReport
        {
            SourceRoot = sourceRoot,
            TotalPowerTypesScanned = canonicalPowers.Count,
            ReactivePowerCount = reactiveCount,
            FullyStaticPowers = fullyStatic,
            RuntimeResolvedPowers = runtimeResolved,
            PartialPowers = partial,
            Findings = findings
        });

        Log.Info($"[AITeammate] Built enemy reactive metadata entries={reactiveCount} static={fullyStatic} runtime={runtimeResolved} partial={partial} filteredPassive={filteredPassiveOrNonPunisher}.");
        return repository;
    }

    private static List<PowerModel> EnumerateCanonicalPowers()
    {
        List<PowerModel> powers = [];
        IEnumerable<Type> powerTypes = typeof(PowerModel).Assembly
            .GetTypes()
            .Where(static type =>
                typeof(PowerModel).IsAssignableFrom(type) &&
                !type.IsAbstract &&
                type.Namespace != null &&
                type.Namespace.Contains(".Models.Powers", StringComparison.Ordinal) &&
                !type.Namespace.Contains(".Mocks", StringComparison.Ordinal));

        foreach (Type powerType in powerTypes)
        {
            try
            {
                if (Activator.CreateInstance(powerType) is not PowerModel power)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(power.Id.Entry))
                {
                    continue;
                }

                powers.Add(power);
            }
            catch (Exception ex)
            {
                Log.Warn($"[AITeammate][EnemyReactiveMetadata] Skipped power type={powerType.FullName ?? powerType.Name} during reflection bootstrap: {ex.Message}");
            }
        }

        return powers;
    }

    private static EnemyReactiveMetadataBuildResult BuildMetadata(PowerModel power, string sourceRoot)
    {
        if (string.IsNullOrWhiteSpace(sourceRoot))
        {
            return EnemyReactiveMetadataBuildResult.Empty;
        }

        string sourcePath = ResolvePowerSourceFile(sourceRoot, power.GetType().Name);
        string sourceText = ReadSourceText(sourcePath);
        if (string.IsNullOrWhiteSpace(sourceText))
        {
            return EnemyReactiveMetadataBuildResult.Empty;
        }

        SourceMethodMap methodMap = SourceMethodMap.Parse(sourceText);
        Dictionary<string, int> dynamicVars = GetSafeDynamicVars(power);
        List<EnemyReactiveEffectDescriptor> effects = ExtractEffects(power, methodMap, dynamicVars);
        if (effects.Count == 0)
        {
            return EnemyReactiveMetadataBuildResult.Empty;
        }

        if (!IsReactivePunishmentPower(effects))
        {
            Log.Debug($"[AITeammate][EnemyReactiveMetadata] Filtered passive/non-punisher power={power.Id.Entry} sourceType={power.GetType().Name} effects=[{string.Join(", ", effects.Select(static effect => effect.Describe()))}]");
            return new EnemyReactiveMetadataBuildResult
            {
                WasFilteredAsNonReactive = true
            };
        }

        bool requiresRuntime = effects.Any(static effect => effect.RequiresRuntimeResolution || effect.MagnitudeKind is EnemyReactiveMagnitudeKind.RuntimeComputed or EnemyReactiveMagnitudeKind.Conditional or EnemyReactiveMagnitudeKind.Randomized or EnemyReactiveMagnitudeKind.ChoiceDependent)
                               || sourceText.Contains("History", StringComparison.Ordinal)
                               || sourceText.Contains("CurrentHp", StringComparison.Ordinal)
                               || sourceText.Contains("BlockedDamage", StringComparison.Ordinal)
                               || sourceText.Contains("UnblockedDamage", StringComparison.Ordinal)
                               || sourceText.Contains("Random", StringComparison.Ordinal);
        bool partialUnknowns = effects.Any(static effect => effect.OutcomeKind == EnemyReactiveOutcomeKind.Unknown || effect.MagnitudeKind == EnemyReactiveMagnitudeKind.Unknown);

        bool retaliates = effects.Any(static effect => effect.OutcomeKind == EnemyReactiveOutcomeKind.RetaliateDamage);
        bool appliesDebuffs = effects.Any(static effect =>
            effect.OutcomeKind == EnemyReactiveOutcomeKind.ApplyDebuff ||
            effect.OutcomeKind == EnemyReactiveOutcomeKind.ApplyPower && IsLikelyDebuffPower(effect.AppliedPowerId));
        bool addsStatusCards = effects.Any(static effect => effect.OutcomeKind == EnemyReactiveOutcomeKind.AddStatusCard);
        bool addsCurseCards = effects.Any(static effect => effect.OutcomeKind == EnemyReactiveOutcomeKind.AddCurseCard);
        bool afflictsCards = effects.Any(static effect => effect.OutcomeKind == EnemyReactiveOutcomeKind.AfflictCards);
        bool changesStrength = effects.Any(static effect => string.Equals(effect.AppliedPowerId, "StrengthPower", StringComparison.Ordinal));
        bool changesDexterity = effects.Any(static effect => string.Equals(effect.AppliedPowerId, "DexterityPower", StringComparison.Ordinal));
        bool grantsBlock = effects.Any(static effect => effect.OutcomeKind == EnemyReactiveOutcomeKind.GainBlock);
        bool modifiesIncomingDamage = effects.Any(static effect => effect.OutcomeKind == EnemyReactiveOutcomeKind.ModifyIncomingDamage);
        bool restrictsPlay = effects.Any(static effect => effect.OutcomeKind == EnemyReactiveOutcomeKind.RestrictCardPlay);
        bool modifiesCardCosts = effects.Any(static effect => effect.OutcomeKind == EnemyReactiveOutcomeKind.ModifyCardCost);
        bool cardFlowPunishment = effects.Any(static effect => effect.CardFlowPunishment);
        bool blockableRetaliation = effects.Any(static effect => effect.OutcomeKind == EnemyReactiveOutcomeKind.RetaliateDamage && effect.Blockability == EnemyReactiveBlockability.Blockable);
        bool unblockableRetaliation = effects.Any(static effect => effect.OutcomeKind == EnemyReactiveOutcomeKind.RetaliateDamage && effect.Blockability == EnemyReactiveBlockability.Unblockable);

        return new EnemyReactiveMetadataBuildResult
        {
            Metadata = new EnemyReactiveMetadata
            {
                PowerId = power.Id.Entry,
                DisplayName = GetSafeDisplayName(power),
                SourceTypeName = power.GetType().FullName ?? power.GetType().Name,
                SourceFilePath = sourcePath,
                PowerType = GetSafePowerType(power),
                DynamicVars = dynamicVars,
                Effects = effects,
                HasReactivePunishment = true,
                RetaliatesDamage = retaliates,
                CardFlowPunishment = cardFlowPunishment,
                AppliesDebuffs = appliesDebuffs,
                AddsStatusCards = addsStatusCards,
                AddsCurseCards = addsCurseCards,
                AfflictsCards = afflictsCards,
                ChangesStrength = changesStrength,
                ChangesDexterity = changesDexterity,
                GrantsBlock = grantsBlock,
                ModifiesIncomingDamage = modifiesIncomingDamage,
                RestrictsPlay = restrictsPlay,
                ModifiesCardCosts = modifiesCardCosts,
                IncludesBlockableRetaliation = blockableRetaliation,
                IncludesUnblockableRetaliation = unblockableRetaliation,
                RequiresRuntimeResolution = requiresRuntime,
                HasPartialUnknowns = partialUnknowns,
                Notes = BuildNotes(sourceText, effects, requiresRuntime, partialUnknowns)
            }
        };
    }

    private static bool IsReactivePunishmentPower(IReadOnlyList<EnemyReactiveEffectDescriptor> effects)
    {
        bool hasReactiveTrigger = effects.Any(static effect => IsMeaningfulReactiveTrigger(effect.TriggerKind));
        bool hasReactiveOutcome = effects.Any(static effect =>
            effect.ImmediateHpPunishment ||
            effect.CardFlowPunishment ||
            effect.OutcomeKind is EnemyReactiveOutcomeKind.RetaliateDamage or
                EnemyReactiveOutcomeKind.ApplyPower or
                EnemyReactiveOutcomeKind.ApplyDebuff or
                EnemyReactiveOutcomeKind.GainBlock or
                EnemyReactiveOutcomeKind.GainStrength or
                EnemyReactiveOutcomeKind.GainDexterity or
                EnemyReactiveOutcomeKind.ModifyIncomingDamage or
                EnemyReactiveOutcomeKind.ModifyCardCost or
                EnemyReactiveOutcomeKind.RestrictCardPlay or
                EnemyReactiveOutcomeKind.AddStatusCard or
                EnemyReactiveOutcomeKind.AddCurseCard or
                EnemyReactiveOutcomeKind.AfflictCards or
                EnemyReactiveOutcomeKind.ApplyKeyword or
                EnemyReactiveOutcomeKind.Stun or
                EnemyReactiveOutcomeKind.SpecialPunishment);

        return hasReactiveTrigger && hasReactiveOutcome;
    }

    private static bool IsMeaningfulReactiveTrigger(EnemyReactiveTriggerKind triggerKind)
    {
        return triggerKind is EnemyReactiveTriggerKind.BeforeDamageReceived or
            EnemyReactiveTriggerKind.AfterDamageReceived or
            EnemyReactiveTriggerKind.AfterAttack or
            EnemyReactiveTriggerKind.BeforeCardPlayed or
            EnemyReactiveTriggerKind.AfterCardPlayed or
            EnemyReactiveTriggerKind.AfterCardDrawn or
            EnemyReactiveTriggerKind.AfterCardEnteredCombat or
            EnemyReactiveTriggerKind.BeforeCombatStart or
            EnemyReactiveTriggerKind.ShouldPlay or
            EnemyReactiveTriggerKind.TryModifyEnergyCostInCombat;
    }

    private static List<EnemyReactiveEffectDescriptor> ExtractEffects(PowerModel power, SourceMethodMap methodMap, IReadOnlyDictionary<string, int> dynamicVars)
    {
        List<EnemyReactiveEffectDescriptor> effects = [];
        HashSet<string> dedupe = new(StringComparer.Ordinal);

        foreach ((string methodName, EnemyReactiveTriggerKind triggerKind) in HookTriggerKinds)
        {
            if (!methodMap.TryGetExpandedBody(methodName, out string body))
            {
                continue;
            }

            AddDerivedTriggerDescriptor(effects, dedupe, power, triggerKind, body, dynamicVars);
            AddDamageDescriptors(effects, dedupe, triggerKind, body, dynamicVars);
            AddApplyPowerDescriptors(effects, dedupe, triggerKind, body, dynamicVars);
            AddGainBlockDescriptors(effects, dedupe, triggerKind, body, dynamicVars);
            AddAfflictionDescriptors(effects, dedupe, triggerKind, body, dynamicVars);
            AddKeywordDescriptors(effects, dedupe, triggerKind, body);
            AddStatusCardDescriptors(effects, dedupe, triggerKind, body, dynamicVars);
            AddStunDescriptors(effects, dedupe, triggerKind, body);
        }

        return effects;
    }

    private static void AddDerivedTriggerDescriptor(
        List<EnemyReactiveEffectDescriptor> effects,
        ISet<string> dedupe,
        PowerModel power,
        EnemyReactiveTriggerKind triggerKind,
        string body,
        IReadOnlyDictionary<string, int> dynamicVars)
    {
        if (triggerKind == EnemyReactiveTriggerKind.ShouldPlay)
        {
            AddDescriptor(effects, dedupe, new EnemyReactiveEffectDescriptor
            {
                TriggerKind = triggerKind,
                OutcomeKind = EnemyReactiveOutcomeKind.RestrictCardPlay,
                TargetScope = InferTargetScope(triggerKind, body),
                MagnitudeKind = EnemyReactiveMagnitudeKind.Conditional,
                CardFlowPunishment = true,
                RequiresRuntimeResolution = true,
                Notes = TrimNotes(body)
            });
        }
        else if (triggerKind == EnemyReactiveTriggerKind.TryModifyEnergyCostInCombat)
        {
            AddDescriptor(effects, dedupe, new EnemyReactiveEffectDescriptor
            {
                TriggerKind = triggerKind,
                OutcomeKind = EnemyReactiveOutcomeKind.ModifyCardCost,
                TargetScope = InferTargetScope(triggerKind, body),
                Magnitude = TryResolveMagnitude(dynamicVars, body, "Energy"),
                MagnitudeKind = InferMagnitudeKind(body),
                CardFlowPunishment = true,
                RequiresRuntimeResolution = true,
                Notes = TrimNotes(body)
            });
        }
        else if (triggerKind is EnemyReactiveTriggerKind.ModifyDamageMultiplicative or EnemyReactiveTriggerKind.ModifyDamageAdditive)
        {
            AddDescriptor(effects, dedupe, new EnemyReactiveEffectDescriptor
            {
                TriggerKind = triggerKind,
                OutcomeKind = EnemyReactiveOutcomeKind.ModifyIncomingDamage,
                TargetScope = InferTargetScope(triggerKind, body),
                MagnitudeKind = InferMagnitudeKind(body),
                RequiresRuntimeResolution = true,
                Notes = TrimNotes(body)
            });
        }
        else if (triggerKind == EnemyReactiveTriggerKind.BeforeCombatStart &&
                 body.Contains("Afflict", StringComparison.Ordinal) &&
                 power.Type.ToString().Length > 0)
        {
            AddDescriptor(effects, dedupe, new EnemyReactiveEffectDescriptor
            {
                TriggerKind = triggerKind,
                OutcomeKind = EnemyReactiveOutcomeKind.AfflictCards,
                TargetScope = InferTargetScope(triggerKind, body),
                MagnitudeKind = InferMagnitudeKind(body),
                CardFlowPunishment = true,
                RequiresRuntimeResolution = true,
                Notes = TrimNotes(body)
            });
        }
    }

    private static void AddDamageDescriptors(List<EnemyReactiveEffectDescriptor> effects, ISet<string> dedupe, EnemyReactiveTriggerKind triggerKind, string body, IReadOnlyDictionary<string, int> dynamicVars)
    {
        foreach (Match match in CreatureDamageRegex.Matches(body))
        {
            string args = match.Groups["args"].Value;
            EnemyReactiveOutcomeKind outcomeKind = triggerKind is EnemyReactiveTriggerKind.BeforeDamageReceived or EnemyReactiveTriggerKind.AfterDamageReceived or EnemyReactiveTriggerKind.AfterAttack
                ? EnemyReactiveOutcomeKind.RetaliateDamage
                : EnemyReactiveOutcomeKind.SpecialPunishment;
            AddDescriptor(effects, dedupe, new EnemyReactiveEffectDescriptor
            {
                TriggerKind = triggerKind,
                OutcomeKind = outcomeKind,
                TargetScope = InferDamageTargetScope(args, body),
                Magnitude = TryResolveMagnitude(dynamicVars, args, "Damage", "Amount"),
                MagnitudeKind = InferMagnitudeKind(args),
                Blockability = InferBlockability(args),
                ImmediateHpPunishment = true,
                CardFlowPunishment = triggerKind is EnemyReactiveTriggerKind.AfterCardPlayed or EnemyReactiveTriggerKind.BeforeCardPlayed,
                RequiresRuntimeResolution = InferMagnitudeKind(args) != EnemyReactiveMagnitudeKind.Static || args.Contains("BlockedDamage", StringComparison.Ordinal) || args.Contains("UnblockedDamage", StringComparison.Ordinal),
                Notes = TrimNotes(args)
            });
        }
    }

    private static void AddApplyPowerDescriptors(List<EnemyReactiveEffectDescriptor> effects, ISet<string> dedupe, EnemyReactiveTriggerKind triggerKind, string body, IReadOnlyDictionary<string, int> dynamicVars)
    {
        foreach (Match match in ApplyPowerRegex.Matches(body))
        {
            string powerId = match.Groups["power"].Value;
            string args = match.Groups["args"].Value;
            EnemyReactiveOutcomeKind kind = IsLikelyDebuffPower(powerId) ? EnemyReactiveOutcomeKind.ApplyDebuff : EnemyReactiveOutcomeKind.ApplyPower;
            if (string.Equals(powerId, "StrengthPower", StringComparison.Ordinal))
            {
                kind = EnemyReactiveOutcomeKind.GainStrength;
            }
            else if (string.Equals(powerId, "DexterityPower", StringComparison.Ordinal))
            {
                kind = EnemyReactiveOutcomeKind.GainDexterity;
            }

            AddDescriptor(effects, dedupe, new EnemyReactiveEffectDescriptor
            {
                TriggerKind = triggerKind,
                OutcomeKind = kind,
                TargetScope = InferTargetScope(triggerKind, args),
                Magnitude = TryResolveMagnitude(dynamicVars, args, powerId, "Amount", "Value"),
                MagnitudeKind = InferMagnitudeKind(args),
                AppliedPowerId = powerId,
                RequiresRuntimeResolution = InferMagnitudeKind(args) != EnemyReactiveMagnitudeKind.Static,
                Notes = TrimNotes(args)
            });
        }
    }

    private static void AddGainBlockDescriptors(List<EnemyReactiveEffectDescriptor> effects, ISet<string> dedupe, EnemyReactiveTriggerKind triggerKind, string body, IReadOnlyDictionary<string, int> dynamicVars)
    {
        foreach (Match match in GainBlockRegex.Matches(body))
        {
            string args = match.Groups["args"].Value;
            AddDescriptor(effects, dedupe, new EnemyReactiveEffectDescriptor
            {
                TriggerKind = triggerKind,
                OutcomeKind = EnemyReactiveOutcomeKind.GainBlock,
                TargetScope = InferTargetScope(triggerKind, args),
                Magnitude = TryResolveMagnitude(dynamicVars, args, "Block", "Armor"),
                MagnitudeKind = InferMagnitudeKind(args),
                RequiresRuntimeResolution = InferMagnitudeKind(args) != EnemyReactiveMagnitudeKind.Static,
                Notes = TrimNotes(args)
            });
        }
    }

    private static void AddAfflictionDescriptors(List<EnemyReactiveEffectDescriptor> effects, ISet<string> dedupe, EnemyReactiveTriggerKind triggerKind, string body, IReadOnlyDictionary<string, int> dynamicVars)
    {
        foreach (Match match in AfflictRegex.Matches(body))
        {
            string affliction = match.Groups["affliction"].Value;
            string args = match.Groups["args"].Value;
            AddDescriptor(effects, dedupe, new EnemyReactiveEffectDescriptor
            {
                TriggerKind = triggerKind,
                OutcomeKind = EnemyReactiveOutcomeKind.AfflictCards,
                TargetScope = InferCardTargetScope(args, body),
                Magnitude = TryResolveMagnitude(dynamicVars, args, affliction, "Amount"),
                MagnitudeKind = InferMagnitudeKind(args),
                AfflictionId = affliction,
                CardFlowPunishment = true,
                DelayedPunishment = triggerKind is EnemyReactiveTriggerKind.AfterApplied or EnemyReactiveTriggerKind.BeforeCombatStart,
                GlobalPunishment = args.Contains("AllCards", StringComparison.Ordinal) || body.Contains("AllCards", StringComparison.Ordinal),
                RequiresRuntimeResolution = true,
                Notes = TrimNotes(args)
            });
        }
    }

    private static void AddKeywordDescriptors(List<EnemyReactiveEffectDescriptor> effects, ISet<string> dedupe, EnemyReactiveTriggerKind triggerKind, string body)
    {
        foreach (Match match in KeywordRegex.Matches(body))
        {
            string args = match.Groups["args"].Value;
            string keyword = match.Groups["keyword"].Value;
            AddDescriptor(effects, dedupe, new EnemyReactiveEffectDescriptor
            {
                TriggerKind = triggerKind,
                OutcomeKind = EnemyReactiveOutcomeKind.ApplyKeyword,
                TargetScope = InferCardTargetScope(args, body),
                AppliedKeyword = keyword,
                MagnitudeKind = EnemyReactiveMagnitudeKind.Static,
                CardFlowPunishment = true,
                DelayedPunishment = triggerKind is EnemyReactiveTriggerKind.AfterApplied or EnemyReactiveTriggerKind.BeforeCombatStart,
                RequiresRuntimeResolution = false,
                Notes = TrimNotes(args)
            });
        }

        foreach (Match match in RemoveKeywordRegex.Matches(body))
        {
            string args = match.Groups["args"].Value;
            string keyword = match.Groups["keyword"].Value;
            AddDescriptor(effects, dedupe, new EnemyReactiveEffectDescriptor
            {
                TriggerKind = triggerKind,
                OutcomeKind = EnemyReactiveOutcomeKind.RemoveKeyword,
                TargetScope = InferCardTargetScope(args, body),
                AppliedKeyword = keyword,
                MagnitudeKind = EnemyReactiveMagnitudeKind.Static,
                CardFlowPunishment = true,
                DelayedPunishment = true,
                RequiresRuntimeResolution = false,
                Notes = TrimNotes(args)
            });
        }
    }

    private static void AddStatusCardDescriptors(List<EnemyReactiveEffectDescriptor> effects, ISet<string> dedupe, EnemyReactiveTriggerKind triggerKind, string body, IReadOnlyDictionary<string, int> dynamicVars)
    {
        foreach (Match match in AddStatusRegex.Matches(body))
        {
            string cardId = match.Groups["card"].Value;
            string args = match.Groups["args"].Value;
            AddDescriptor(effects, dedupe, new EnemyReactiveEffectDescriptor
            {
                TriggerKind = triggerKind,
                OutcomeKind = InferAddedCardOutcome(cardId),
                TargetScope = InferCardTargetScope(args, body),
                Magnitude = TryResolveMagnitude(dynamicVars, args, "Amount", cardId),
                MagnitudeKind = InferMagnitudeKind(args),
                AddedCardId = cardId,
                CardFlowPunishment = true,
                DelayedPunishment = true,
                RequiresRuntimeResolution = true,
                Notes = TrimNotes(args)
            });
        }

        if (body.Contains("AddGeneratedCardToCombat", StringComparison.Ordinal))
        {
            foreach (Match match in CreateCardRegex.Matches(body))
            {
                string cardId = match.Groups["card"].Value;
                AddDescriptor(effects, dedupe, new EnemyReactiveEffectDescriptor
                {
                    TriggerKind = triggerKind,
                    OutcomeKind = InferAddedCardOutcome(cardId),
                    TargetScope = InferCardTargetScope(body, body),
                    Magnitude = TryResolveMagnitude(dynamicVars, body, "Amount", cardId),
                    MagnitudeKind = InferMagnitudeKind(body),
                    AddedCardId = cardId,
                    CardFlowPunishment = true,
                    DelayedPunishment = true,
                    RequiresRuntimeResolution = true,
                    Notes = TrimNotes(body)
                });
            }
        }
    }

    private static void AddStunDescriptors(List<EnemyReactiveEffectDescriptor> effects, ISet<string> dedupe, EnemyReactiveTriggerKind triggerKind, string body)
    {
        foreach (Match match in StunRegex.Matches(body))
        {
            string args = match.Groups["args"].Value;
            AddDescriptor(effects, dedupe, new EnemyReactiveEffectDescriptor
            {
                TriggerKind = triggerKind,
                OutcomeKind = EnemyReactiveOutcomeKind.Stun,
                TargetScope = InferTargetScope(triggerKind, args),
                MagnitudeKind = EnemyReactiveMagnitudeKind.Conditional,
                RequiresRuntimeResolution = true,
                Notes = TrimNotes(args)
            });
        }
    }

    private static void AddDescriptor(List<EnemyReactiveEffectDescriptor> effects, ISet<string> dedupe, EnemyReactiveEffectDescriptor descriptor)
    {
        string key = string.Join("|",
            descriptor.TriggerKind,
            descriptor.OutcomeKind,
            descriptor.TargetScope,
            descriptor.MagnitudeKind,
            descriptor.Blockability,
            descriptor.Magnitude?.ToString() ?? string.Empty,
            descriptor.AppliedPowerId ?? string.Empty,
            descriptor.AfflictionId ?? string.Empty,
            descriptor.AddedCardId ?? string.Empty,
            descriptor.AppliedKeyword ?? string.Empty);
        if (dedupe.Add(key))
        {
            effects.Add(descriptor);
        }
    }

    private static string GetSafeDisplayName(PowerModel power)
    {
        try
        {
            string? formatted = power.Title?.GetFormattedText();
            if (!string.IsNullOrWhiteSpace(formatted))
            {
                return formatted;
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"[AITeammate][EnemyReactiveMetadata] Failed to format display name for power={power.Id.Entry}: {ex.Message}");
        }

        return power.Id.Entry;
    }

    private static Dictionary<string, int> GetSafeDynamicVars(PowerModel power)
    {
        try
        {
            return power.DynamicVars.ToDictionary(static pair => pair.Key, static pair => (int)pair.Value.BaseValue, StringComparer.Ordinal);
        }
        catch (Exception ex)
        {
            Log.Warn($"[AITeammate][EnemyReactiveMetadata] Failed to resolve dynamic vars for power={power.Id.Entry}: {ex.Message}");
            return new Dictionary<string, int>(StringComparer.Ordinal);
        }
    }

    private static string GetSafePowerType(PowerModel power)
    {
        try
        {
            return power.Type.ToString();
        }
        catch (Exception ex)
        {
            Log.Warn($"[AITeammate][EnemyReactiveMetadata] Failed to resolve power type for power={power.Id.Entry}: {ex.Message}");
            return string.Empty;
        }
    }

    private static string ResolveSourceRoot()
    {
        string[] candidates =
        [
            Environment.GetEnvironmentVariable("STS2_SOURCE_DIR") ?? string.Empty,
            @"C:\Users\hongs\Desktop\sts2Code\sts2PckBeta402",
            @"C:\Users\hongs\Desktop\sts2PckBeta402"
        ];

        return candidates.FirstOrDefault(static candidate => !string.IsNullOrWhiteSpace(candidate) && Directory.Exists(candidate)) ?? string.Empty;
    }

    private static string ResolvePowerSourceFile(string sourceRoot, string typeName)
    {
        string direct = Path.Combine(sourceRoot, "src", "Core", "Models", "Powers", $"{typeName}.cs");
        if (File.Exists(direct))
        {
            return direct;
        }

        string[] matches = Directory.GetFiles(sourceRoot, $"{typeName}.cs", SearchOption.AllDirectories);
        return matches.FirstOrDefault() ?? string.Empty;
    }

    private static string ReadSourceText(string sourcePath)
    {
        return !string.IsNullOrWhiteSpace(sourcePath) && File.Exists(sourcePath)
            ? File.ReadAllText(sourcePath)
            : string.Empty;
    }

    private static EnemyReactiveTargetScope InferDamageTargetScope(string args, string body)
    {
        if (args.Contains("dealer", StringComparison.Ordinal))
        {
            return EnemyReactiveTargetScope.Attacker;
        }

        if (args.Contains("cardPlay.Card.Owner.Creature", StringComparison.Ordinal) || body.Contains("cardPlay.Card.Owner.Creature", StringComparison.Ordinal))
        {
            return EnemyReactiveTargetScope.ActingPlayer;
        }

        if (args.Contains("base.Owner", StringComparison.Ordinal))
        {
            return EnemyReactiveTargetScope.Owner;
        }

        return EnemyReactiveTargetScope.Unknown;
    }

    private static EnemyReactiveTargetScope InferCardTargetScope(string args, string body)
    {
        if (args.Contains("card", StringComparison.Ordinal) && !args.Contains("AllCards", StringComparison.Ordinal))
        {
            return EnemyReactiveTargetScope.PlayedCard;
        }

        if (args.Contains("AllCards", StringComparison.Ordinal) || body.Contains("AllCards", StringComparison.Ordinal))
        {
            if (body.Contains("Where((CardModel c)", StringComparison.Ordinal))
            {
                return EnemyReactiveTargetScope.MatchingCardsOfAffectedPlayer;
            }

            return EnemyReactiveTargetScope.AffectedPlayerCards;
        }

        if (body.Contains("base.Owner.Player", StringComparison.Ordinal))
        {
            return EnemyReactiveTargetScope.AffectedPlayerCards;
        }

        return EnemyReactiveTargetScope.Unknown;
    }

    private static EnemyReactiveTargetScope InferTargetScope(EnemyReactiveTriggerKind triggerKind, string body)
    {
        if (body.Contains("dealer", StringComparison.Ordinal))
        {
            return EnemyReactiveTargetScope.Attacker;
        }

        if (body.Contains("cardPlay.Card.Owner.Creature", StringComparison.Ordinal) || body.Contains("card.Owner == base.Owner.Player", StringComparison.Ordinal) || body.Contains("card.Owner != base.Owner.Player", StringComparison.Ordinal))
        {
            return EnemyReactiveTargetScope.ActingPlayer;
        }

        if (body.Contains("AllCards", StringComparison.Ordinal))
        {
            return EnemyReactiveTargetScope.AffectedPlayerCards;
        }

        if (body.Contains("base.Owner.CombatState.Allies", StringComparison.Ordinal))
        {
            return EnemyReactiveTargetScope.Allies;
        }

        if (body.Contains("base.Owner", StringComparison.Ordinal))
        {
            return triggerKind is EnemyReactiveTriggerKind.AfterApplied or EnemyReactiveTriggerKind.AfterRemoved or EnemyReactiveTriggerKind.AfterSideTurnStart or EnemyReactiveTriggerKind.BeforeSideTurnStart
                ? EnemyReactiveTargetScope.Owner
                : EnemyReactiveTargetScope.Mixed;
        }

        return EnemyReactiveTargetScope.Unknown;
    }

    private static EnemyReactiveMagnitudeKind InferMagnitudeKind(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return EnemyReactiveMagnitudeKind.Unknown;
        }

        if (text.Contains("Random", StringComparison.Ordinal))
        {
            return EnemyReactiveMagnitudeKind.Randomized;
        }

        if (text.Contains("Choose", StringComparison.Ordinal) || text.Contains("Select", StringComparison.Ordinal))
        {
            return EnemyReactiveMagnitudeKind.ChoiceDependent;
        }

        if (text.Contains("if (", StringComparison.Ordinal) || text.Contains("&&", StringComparison.Ordinal) || text.Contains("||", StringComparison.Ordinal))
        {
            if (text.Contains("result.", StringComparison.Ordinal) || text.Contains("History", StringComparison.Ordinal) || text.Contains("CurrentHp", StringComparison.Ordinal) || text.Contains("DynamicVars", StringComparison.Ordinal) || text.Contains("base.Amount", StringComparison.Ordinal))
            {
                return EnemyReactiveMagnitudeKind.RuntimeComputed;
            }

            return EnemyReactiveMagnitudeKind.Conditional;
        }

        if (text.Contains("result.", StringComparison.Ordinal) ||
            text.Contains("CurrentHp", StringComparison.Ordinal) ||
            text.Contains("History", StringComparison.Ordinal) ||
            text.Contains("DynamicVars", StringComparison.Ordinal) ||
            text.Contains("base.Amount", StringComparison.Ordinal) ||
            text.Contains("Count(", StringComparison.Ordinal))
        {
            return EnemyReactiveMagnitudeKind.RuntimeComputed;
        }

        return Regex.IsMatch(text, @"-?\d+m?") ? EnemyReactiveMagnitudeKind.Static : EnemyReactiveMagnitudeKind.Unknown;
    }

    private static int? TryResolveMagnitude(IReadOnlyDictionary<string, int> dynamicVars, string text, params string[] candidateKeys)
    {
        Match literal = Regex.Match(text, @"(?<![\w])(-?\d+)m?(?![\w])");
        if (literal.Success && int.TryParse(literal.Groups[1].Value, out int literalValue))
        {
            return literalValue;
        }

        foreach (string key in candidateKeys)
        {
            if (dynamicVars.TryGetValue(key, out int directValue))
            {
                return directValue;
            }
        }

        foreach (KeyValuePair<string, int> pair in dynamicVars)
        {
            if (candidateKeys.Any(key => text.Contains(key, StringComparison.OrdinalIgnoreCase) || pair.Key.Contains(key, StringComparison.OrdinalIgnoreCase)))
            {
                return pair.Value;
            }
        }

        return null;
    }

    private static EnemyReactiveBlockability InferBlockability(string args)
    {
        if (args.Contains("ValueProp.Unblockable", StringComparison.Ordinal))
        {
            return EnemyReactiveBlockability.Unblockable;
        }

        return EnemyReactiveBlockability.Blockable;
    }

    private static EnemyReactiveOutcomeKind InferAddedCardOutcome(string cardId)
    {
        return cardId switch
        {
            "Wound" or "Dazed" or "Burn" or "Slimed" or "VoidPowerCard" => EnemyReactiveOutcomeKind.AddStatusCard,
            "Regret" or "Pain" or "Decay" or "Doubt" or "Shame" or "CurseOfTheBell" or "Necronomicurse" => EnemyReactiveOutcomeKind.AddCurseCard,
            _ => EnemyReactiveOutcomeKind.AddStatusCard
        };
    }

    private static bool IsLikelyDebuffPower(string? powerId)
    {
        if (string.IsNullOrWhiteSpace(powerId))
        {
            return false;
        }

        return powerId.Contains("Weak", StringComparison.OrdinalIgnoreCase) ||
               powerId.Contains("Vulnerable", StringComparison.OrdinalIgnoreCase) ||
               powerId.Contains("Poison", StringComparison.OrdinalIgnoreCase) ||
               powerId.Contains("Shackle", StringComparison.OrdinalIgnoreCase) ||
               powerId.Contains("Frail", StringComparison.OrdinalIgnoreCase) ||
               powerId.Contains("Debuff", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildNotes(string sourceText, IReadOnlyList<EnemyReactiveEffectDescriptor> effects, bool requiresRuntime, bool partialUnknowns)
    {
        List<string> notes = [];
        if (requiresRuntime)
        {
            notes.Add("Contains runtime-conditioned, history-conditioned, or branch-dependent reactive behavior.");
        }

        if (partialUnknowns)
        {
            notes.Add("Some reactive fields could not be normalized statically.");
        }

        if (effects.Any(static effect => effect.OutcomeKind == EnemyReactiveOutcomeKind.AddStatusCard || effect.OutcomeKind == EnemyReactiveOutcomeKind.AddCurseCard))
        {
            notes.Add("Direct card insertion detected from source commands.");
        }

        if (!sourceText.Contains("CardPileCmd.Add", StringComparison.Ordinal) && effects.Any(static effect => effect.OutcomeKind == EnemyReactiveOutcomeKind.AfflictCards))
        {
            notes.Add("Punishment uses card afflictions or keyword changes rather than adding status/curse cards directly.");
        }

        return string.Join(" ", notes);
    }

    private static string TrimNotes(string text)
    {
        string compact = Regex.Replace(text, @"\s+", " ").Trim();
        return compact.Length <= 220 ? compact : compact[..220] + "...";
    }

    private sealed class SourceMethodMap
    {
        private readonly Dictionary<string, SourceMethod> _methods;

        private SourceMethodMap(Dictionary<string, SourceMethod> methods)
        {
            _methods = methods;
        }

        public static SourceMethodMap Parse(string sourceText)
        {
            Dictionary<string, SourceMethod> methods = new(StringComparer.Ordinal);
            foreach (Match match in MethodRegex.Matches(sourceText))
            {
                string methodName = match.Groups["name"].Value;
                string signature = match.Groups["signature"].Value;
                if (!signature.EndsWith("{", StringComparison.Ordinal))
                {
                    continue;
                }

                int absoluteBodyStart = match.Index + signature.Length - 1;
                int absoluteBodyEnd = FindMatchingBrace(sourceText, absoluteBodyStart);
                if (absoluteBodyEnd <= absoluteBodyStart)
                {
                    continue;
                }

                string body = sourceText.Substring(absoluteBodyStart + 1, absoluteBodyEnd - absoluteBodyStart - 1);
                methods[methodName] = new SourceMethod(methodName, body);
            }

            return new SourceMethodMap(methods);
        }

        public bool TryGetExpandedBody(string methodName, out string expandedBody)
        {
            expandedBody = string.Empty;
            if (!_methods.TryGetValue(methodName, out SourceMethod? root))
            {
                return false;
            }

            HashSet<string> visited = new(StringComparer.Ordinal) { root.Name };
            List<string> bodies = [root.Body];
            Expand(root.Body, visited, bodies, 0);
            expandedBody = string.Join(Environment.NewLine, bodies);
            return true;
        }

        private void Expand(string body, ISet<string> visited, ICollection<string> bodies, int depth)
        {
            if (depth >= 2)
            {
                return;
            }

            foreach (Match match in Regex.Matches(body, @"\b(?<name>[A-Za-z_]\w*)\s*\("))
            {
                string candidate = match.Groups["name"].Value;
                if (visited.Contains(candidate))
                {
                    continue;
                }

                if (!_methods.TryGetValue(candidate, out SourceMethod? child))
                {
                    continue;
                }

                visited.Add(candidate);
                bodies.Add(child.Body);
                Expand(child.Body, visited, bodies, depth + 1);
            }
        }

        private static int FindMatchingBrace(string sourceText, int openBraceIndex)
        {
            int depth = 0;
            for (int i = openBraceIndex; i < sourceText.Length; i++)
            {
                char c = sourceText[i];
                if (c == '{')
                {
                    depth++;
                }
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return i;
                    }
                }
            }

            return -1;
        }

        private sealed record SourceMethod(string Name, string Body);
    }

    private sealed class EnemyReactiveMetadataBuildResult
    {
        public static EnemyReactiveMetadataBuildResult Empty { get; } = new();

        public EnemyReactiveMetadata? Metadata { get; init; }

        public bool WasFilteredAsNonReactive { get; init; }
    }
}

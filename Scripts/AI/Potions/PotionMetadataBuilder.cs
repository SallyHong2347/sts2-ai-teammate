using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;

namespace AITeammate.Scripts;

internal sealed class PotionMetadataBuilder
{
    private static readonly Regex DamageRegex = new(@"CreatureCmd\.Damage\s*\((?<args>.*?)\)", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex HealRegex = new(@"CreatureCmd\.Heal\s*\((?<args>.*?)\)", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex BlockRegex = new(@"CreatureCmd\.(GainBlock|GainArmor)\s*\((?<args>.*?)\)", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex MaxHpRegex = new(@"CreatureCmd\.GainMaxHp\s*\((?<args>.*?)\)", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex ApplyPowerRegex = new(@"PowerCmd\.Apply<(?<power>[\w\d_]+)>\s*\((?<args>.*?)\)", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex GainEnergyRegex = new(@"PlayerCmd\.GainEnergy\s*\((?<args>.*?)\)", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex GainGoldRegex = new(@"PlayerCmd\.GainGold\s*\((?<args>.*?)\)", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex DrawRegex = new(@"CardPileCmd\.Draw\s*\((?<args>.*?)\)", RegexOptions.Singleline | RegexOptions.Compiled);

    public PotionMetadataRepository Build()
    {
        PotionMetadataRepository repository = new();
        string sourceRoot = ResolveSourceRoot();
        List<PotionModel> canonicalPotions = EnumerateCanonicalPotions();
        List<string> findings =
        [
            $"Potion definitions discovered from reflected PotionModel types count={canonicalPotions.Count}.",
            "Potion source includes canonical ids, display names, usage, target type, and dynamic vars via PotionModel.",
            "Most potion effects are statically discoverable from OnUse command construction, but some remain runtime/choice/random dependent.",
            "Runtime metadata bootstrap avoids ModelDb.AllPotions so retail public beta does not trip character-pool lookup during potion repository initialization."
        ];

        int fullyStatic = 0;
        int runtimeResolved = 0;
        int partial = 0;

        foreach (PotionModel potion in canonicalPotions.OrderBy(static potion => potion.Id.Entry, StringComparer.Ordinal))
        {
            PotionMetadata metadata = BuildMetadata(potion, sourceRoot);
            repository.Upsert(metadata);

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
            ? $"Potion source root resolved: {sourceRoot}"
            : "Potion source root could not be resolved; metadata fell back to PotionModel-only information.");
        findings.Add("Foul Potion source confirms all-creatures damage except pets during combat, which includes self and teammate.");

        repository.SetReport(new PotionMetadataBuildReport
        {
            SourceRoot = sourceRoot,
            TotalPotions = repository.All.Count(),
            FullyStaticPotions = fullyStatic,
            RuntimeResolvedPotions = runtimeResolved,
            PartialPotions = partial,
            Findings = findings
        });

        Log.Info($"[AITeammate] Built potion metadata entries={repository.All.Count()} static={fullyStatic} runtime={runtimeResolved} partial={partial}.");
        return repository;
    }

    private static List<PotionModel> EnumerateCanonicalPotions()
    {
        List<PotionModel> potions = [];
        IEnumerable<Type> potionTypes = typeof(PotionModel).Assembly
            .GetTypes()
            .Where(static type =>
                typeof(PotionModel).IsAssignableFrom(type) &&
                !type.IsAbstract &&
                type.Namespace != null &&
                type.Namespace.Contains(".Models.Potions", StringComparison.Ordinal));

        foreach (Type potionType in potionTypes)
        {
            try
            {
                if (Activator.CreateInstance(potionType) is not PotionModel potion)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(potion.Id.Entry))
                {
                    continue;
                }

                potions.Add(potion);
            }
            catch (Exception ex)
            {
                Log.Warn($"[AITeammate][PotionMetadata] Skipped potion type={potionType.FullName ?? potionType.Name} during reflection bootstrap: {ex.Message}");
            }
        }

        return potions;
    }

    private static PotionMetadata BuildMetadata(PotionModel potion, string sourceRoot)
    {
        string sourcePath = ResolvePotionSourceFile(sourceRoot, potion.GetType().Name);
        string sourceText = ReadSourceText(sourcePath);
        IReadOnlyDictionary<string, int> dynamicVars = potion.DynamicVars.ToDictionary(static pair => pair.Key, static pair => (int)pair.Value.BaseValue, StringComparer.Ordinal);
        List<PotionMetadataTargetKind> targetKinds = InferTargetKinds(potion, sourceText);
        List<PotionEffectDescriptor> effects = ExtractEffects(potion, sourceText, dynamicVars, targetKinds);
        bool requiresRuntime = effects.Any(static effect => effect.MagnitudeKind is PotionMagnitudeKind.RuntimeComputed or PotionMagnitudeKind.ChoiceDependent or PotionMagnitudeKind.Randomized)
                               || sourceText.Contains("InCombat", StringComparison.Ordinal)
                               || sourceText.Contains("CurrentRoomType", StringComparison.Ordinal)
                               || sourceText.Contains("Random", StringComparison.Ordinal)
                               || sourceText.Contains("Choose", StringComparison.Ordinal);
        bool partialUnknowns = string.IsNullOrWhiteSpace(sourceText) || effects.Any(static effect => effect.MagnitudeKind == PotionMagnitudeKind.Unknown);

        bool canDamageSelf = effects.Any(static effect => effect.Kind == PotionEffectKind.DealDamage && effect.CanAffectSelf);
        bool canDamageTeammate = effects.Any(static effect => effect.Kind == PotionEffectKind.DealDamage && effect.CanAffectTeammate);
        bool canDamageAllAllies = effects.Any(static effect => effect.Kind == PotionEffectKind.DealDamage && effect.CanAffectAllAllies);
        bool canDamageSingleEnemy = effects.Any(static effect => effect.Kind == PotionEffectKind.DealDamage && effect.CanAffectSingleEnemy);
        bool canDamageAllEnemies = effects.Any(static effect => effect.Kind == PotionEffectKind.DealDamage && effect.CanAffectAllEnemies);
        bool canHealSelf = effects.Any(static effect => effect.Kind == PotionEffectKind.Heal && effect.CanAffectSelf);
        bool canHealTeammate = effects.Any(static effect => effect.Kind == PotionEffectKind.Heal && effect.CanAffectTeammate);
        bool canHealAllAllies = effects.Any(static effect => effect.Kind == PotionEffectKind.Heal && effect.CanAffectAllAllies);
        bool grantsBlock = effects.Any(static effect => effect.Kind == PotionEffectKind.GainBlock);
        bool appliesBuffs = effects.Any(static effect => effect.Kind == PotionEffectKind.ApplyPower && effect.IsBuff);
        bool appliesDebuffs = effects.Any(static effect => effect.Kind == PotionEffectKind.ApplyPower && effect.IsDebuff);
        bool harmsSelf = canDamageSelf;
        bool harmsTeammate = canDamageTeammate;
        bool harmsAllAllies = canDamageAllAllies;
        bool defensive = canHealSelf || canHealTeammate || canHealAllAllies || grantsBlock || appliesBuffs;
        bool offensive = canDamageSingleEnemy || canDamageAllEnemies || appliesDebuffs;
        bool utility = effects.Any(static effect => effect.Kind is PotionEffectKind.GainEnergy or PotionEffectKind.DrawCards or PotionEffectKind.UpgradeCards or PotionEffectKind.AddCards or PotionEffectKind.ObtainPotion or PotionEffectKind.DiscardCards or PotionEffectKind.ManipulateDrawPile or PotionEffectKind.GainGold or PotionEffectKind.Utility);

        return new PotionMetadata
        {
            PotionId = potion.Id.Entry,
            DisplayName = GetSafeDisplayName(potion),
            SourceTypeName = potion.GetType().FullName ?? potion.GetType().Name,
            SourceFilePath = sourcePath,
            Rarity = potion.Rarity.ToString(),
            Usage = potion.Usage.ToString(),
            DeclaredTargetType = potion.TargetType.ToString(),
            DynamicVars = dynamicVars,
            TargetKinds = targetKinds,
            Effects = effects,
            EffectMagnitudesStaticallyKnown = effects.Count > 0 && effects.All(static effect => effect.MagnitudeKind == PotionMagnitudeKind.Static),
            RequiresRuntimeResolution = requiresRuntime,
            HasPartialUnknowns = partialUnknowns,
            CanDamageSelf = canDamageSelf,
            CanDamageTeammate = canDamageTeammate,
            CanDamageAllAllies = canDamageAllAllies,
            CanDamageSingleEnemy = canDamageSingleEnemy,
            CanDamageAllEnemies = canDamageAllEnemies,
            CanHealSelf = canHealSelf,
            CanHealTeammate = canHealTeammate,
            CanHealAllAllies = canHealAllAllies,
            GrantsBlockOrArmor = grantsBlock,
            AppliesBuffs = appliesBuffs,
            AppliesDebuffs = appliesDebuffs,
            HarmsSelf = harmsSelf,
            HarmsTeammate = harmsTeammate,
            HarmsAllAllies = harmsAllAllies,
            MixedBenefitAndHarm = offensive && defensive || offensive && (harmsSelf || harmsTeammate || harmsAllAllies),
            Offensive = offensive,
            Defensive = defensive,
            Utility = utility,
            Notes = BuildNotes(sourceText, partialUnknowns, requiresRuntime, targetKinds)
        };
    }

    private static string GetSafeDisplayName(PotionModel potion)
    {
        try
        {
            string? formatted = potion.Title?.GetFormattedText();
            if (!string.IsNullOrWhiteSpace(formatted))
            {
                return formatted;
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"[AITeammate][PotionMetadata] Failed to format display name for potion={potion.Id.Entry}: {ex.Message}");
        }

        return potion.Id.Entry;
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

    private static string ResolvePotionSourceFile(string sourceRoot, string typeName)
    {
        if (string.IsNullOrWhiteSpace(sourceRoot))
        {
            return string.Empty;
        }

        string direct = Path.Combine(sourceRoot, "src", "Core", "Models", "Potions", $"{typeName}.cs");
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

    private static List<PotionMetadataTargetKind> InferTargetKinds(PotionModel potion, string sourceText)
    {
        HashSet<PotionMetadataTargetKind> targetKinds = [MapTargetKind(potion.TargetType)];

        AddTargetKindIfPresent(sourceText, "TargetType.Self", PotionMetadataTargetKind.Self, targetKinds);
        AddTargetKindIfPresent(sourceText, "TargetType.AnyEnemy", PotionMetadataTargetKind.SingleEnemy, targetKinds);
        AddTargetKindIfPresent(sourceText, "TargetType.AllEnemies", PotionMetadataTargetKind.AllEnemies, targetKinds);
        AddTargetKindIfPresent(sourceText, "TargetType.AnyAlly", PotionMetadataTargetKind.SingleAlly, targetKinds);
        AddTargetKindIfPresent(sourceText, "TargetType.AnyPlayer", PotionMetadataTargetKind.SingleAlly, targetKinds);
        AddTargetKindIfPresent(sourceText, "TargetType.AllAllies", PotionMetadataTargetKind.AllAllies, targetKinds);

        if (sourceText.Contains("Creatures.Where((Creature c) => !c.IsPet)", StringComparison.Ordinal))
        {
            targetKinds.Add(PotionMetadataTargetKind.AllCreaturesExceptPets);
        }

        if (sourceText.Contains("CurrentRoomType", StringComparison.Ordinal) || sourceText.Contains("InCombat", StringComparison.Ordinal))
        {
            targetKinds.Add(PotionMetadataTargetKind.Mixed);
        }

        return targetKinds.Count > 0 ? targetKinds.OrderBy(static kind => kind).ToList() : [PotionMetadataTargetKind.Unknown];
    }

    private static void AddTargetKindIfPresent(string sourceText, string token, PotionMetadataTargetKind targetKind, ISet<PotionMetadataTargetKind> targetKinds)
    {
        if (sourceText.Contains(token, StringComparison.Ordinal))
        {
            targetKinds.Add(targetKind);
        }
    }

    private static List<PotionEffectDescriptor> ExtractEffects(
        PotionModel potion,
        string sourceText,
        IReadOnlyDictionary<string, int> dynamicVars,
        IReadOnlyList<PotionMetadataTargetKind> targetKinds)
    {
        List<PotionEffectDescriptor> effects = [];
        AddDamageEffects(effects, sourceText, dynamicVars, targetKinds);
        AddHealEffects(effects, sourceText, dynamicVars, targetKinds);
        AddBlockEffects(effects, sourceText, dynamicVars, targetKinds);
        AddMaxHpEffects(effects, sourceText, dynamicVars, targetKinds);
        AddApplyPowerEffects(effects, sourceText, dynamicVars, targetKinds);
        AddSimpleEffectMatches(effects, sourceText, dynamicVars, targetKinds);

        if (effects.Count == 0)
        {
            effects.Add(new PotionEffectDescriptor
            {
                Kind = PotionEffectKind.Unknown,
                TargetKind = targetKinds.FirstOrDefault(),
                MagnitudeKind = PotionMagnitudeKind.Unknown,
                Notes = $"No normalized effect pattern matched for potion {potion.Id.Entry}."
            });
        }

        return effects;
    }

    private static void AddDamageEffects(List<PotionEffectDescriptor> effects, string sourceText, IReadOnlyDictionary<string, int> dynamicVars, IReadOnlyList<PotionMetadataTargetKind> targetKinds)
    {
        foreach (Match match in DamageRegex.Matches(sourceText))
        {
            string args = match.Groups["args"].Value;
            PotionMetadataTargetKind targetKind = InferCommandTargetKind(args, targetKinds);
            bool affectsAllCreaturesExceptPets = args.Contains("Creatures.Where((Creature c) => !c.IsPet)", StringComparison.Ordinal);
            effects.Add(new PotionEffectDescriptor
            {
                Kind = PotionEffectKind.DealDamage,
                TargetKind = affectsAllCreaturesExceptPets ? PotionMetadataTargetKind.AllCreaturesExceptPets : targetKind,
                MagnitudeKind = InferMagnitudeKind(args),
                Magnitude = TryResolveMagnitude(dynamicVars, args, "Damage"),
                CanAffectSelf = affectsAllCreaturesExceptPets || targetKind is PotionMetadataTargetKind.Self or PotionMetadataTargetKind.AllCreatures,
                CanAffectTeammate = affectsAllCreaturesExceptPets || targetKind is PotionMetadataTargetKind.SingleAlly or PotionMetadataTargetKind.AllAllies or PotionMetadataTargetKind.AllCreatures,
                CanAffectAllAllies = affectsAllCreaturesExceptPets || targetKind is PotionMetadataTargetKind.AllAllies or PotionMetadataTargetKind.AllCreatures,
                CanAffectSingleEnemy = targetKind == PotionMetadataTargetKind.SingleEnemy,
                CanAffectAllEnemies = affectsAllCreaturesExceptPets || targetKind is PotionMetadataTargetKind.AllEnemies or PotionMetadataTargetKind.AllCreatures,
                CanHarmAllies = affectsAllCreaturesExceptPets || targetKind is PotionMetadataTargetKind.Self or PotionMetadataTargetKind.SingleAlly or PotionMetadataTargetKind.AllAllies or PotionMetadataTargetKind.AllCreatures,
                Notes = args
            });
        }
    }

    private static void AddHealEffects(List<PotionEffectDescriptor> effects, string sourceText, IReadOnlyDictionary<string, int> dynamicVars, IReadOnlyList<PotionMetadataTargetKind> targetKinds)
    {
        foreach (Match match in HealRegex.Matches(sourceText))
        {
            string args = match.Groups["args"].Value;
            PotionMetadataTargetKind targetKind = InferCommandTargetKind(args, targetKinds);
            effects.Add(new PotionEffectDescriptor
            {
                Kind = PotionEffectKind.Heal,
                TargetKind = targetKind,
                MagnitudeKind = InferMagnitudeKind(args),
                Magnitude = TryResolveMagnitude(dynamicVars, args, "Heal", "Healing"),
                CanAffectSelf = targetKind is PotionMetadataTargetKind.Self or PotionMetadataTargetKind.AllAllies or PotionMetadataTargetKind.AllCreatures or PotionMetadataTargetKind.Mixed,
                CanAffectTeammate = targetKind is PotionMetadataTargetKind.SingleAlly or PotionMetadataTargetKind.AllAllies or PotionMetadataTargetKind.AllCreatures,
                CanAffectAllAllies = targetKind is PotionMetadataTargetKind.AllAllies or PotionMetadataTargetKind.AllCreatures,
                Notes = args
            });
        }
    }

    private static void AddBlockEffects(List<PotionEffectDescriptor> effects, string sourceText, IReadOnlyDictionary<string, int> dynamicVars, IReadOnlyList<PotionMetadataTargetKind> targetKinds)
    {
        foreach (Match match in BlockRegex.Matches(sourceText))
        {
            string args = match.Groups["args"].Value;
            PotionMetadataTargetKind targetKind = InferCommandTargetKind(args, targetKinds);
            effects.Add(new PotionEffectDescriptor
            {
                Kind = PotionEffectKind.GainBlock,
                TargetKind = targetKind,
                MagnitudeKind = InferMagnitudeKind(args),
                Magnitude = TryResolveMagnitude(dynamicVars, args, "Block", "Armor"),
                CanAffectSelf = targetKind is PotionMetadataTargetKind.Self or PotionMetadataTargetKind.AllAllies or PotionMetadataTargetKind.AllCreatures or PotionMetadataTargetKind.Mixed,
                CanAffectTeammate = targetKind is PotionMetadataTargetKind.SingleAlly or PotionMetadataTargetKind.AllAllies or PotionMetadataTargetKind.AllCreatures,
                CanAffectAllAllies = targetKind is PotionMetadataTargetKind.AllAllies or PotionMetadataTargetKind.AllCreatures,
                IsBuff = true,
                Notes = args
            });
        }
    }

    private static void AddMaxHpEffects(List<PotionEffectDescriptor> effects, string sourceText, IReadOnlyDictionary<string, int> dynamicVars, IReadOnlyList<PotionMetadataTargetKind> targetKinds)
    {
        foreach (Match match in MaxHpRegex.Matches(sourceText))
        {
            string args = match.Groups["args"].Value;
            PotionMetadataTargetKind targetKind = InferCommandTargetKind(args, targetKinds);
            effects.Add(new PotionEffectDescriptor
            {
                Kind = PotionEffectKind.GainMaxHp,
                TargetKind = targetKind,
                MagnitudeKind = InferMagnitudeKind(args),
                Magnitude = TryResolveMagnitude(dynamicVars, args, "Health"),
                CanAffectSelf = targetKind is PotionMetadataTargetKind.Self or PotionMetadataTargetKind.AllAllies or PotionMetadataTargetKind.AllCreatures or PotionMetadataTargetKind.Mixed,
                CanAffectTeammate = targetKind is PotionMetadataTargetKind.SingleAlly or PotionMetadataTargetKind.AllAllies or PotionMetadataTargetKind.AllCreatures,
                CanAffectAllAllies = targetKind is PotionMetadataTargetKind.AllAllies or PotionMetadataTargetKind.AllCreatures,
                IsBuff = true,
                Notes = args
            });
        }
    }

    private static void AddApplyPowerEffects(List<PotionEffectDescriptor> effects, string sourceText, IReadOnlyDictionary<string, int> dynamicVars, IReadOnlyList<PotionMetadataTargetKind> targetKinds)
    {
        foreach (Match match in ApplyPowerRegex.Matches(sourceText))
        {
            string args = match.Groups["args"].Value;
            string powerId = match.Groups["power"].Value;
            PotionMetadataTargetKind targetKind = InferCommandTargetKind(args, targetKinds);
            bool isBuff = !IsLikelyDebuff(powerId, args);
            effects.Add(new PotionEffectDescriptor
            {
                Kind = PotionEffectKind.ApplyPower,
                TargetKind = targetKind,
                MagnitudeKind = InferMagnitudeKind(args),
                Magnitude = TryResolveMagnitude(dynamicVars, args, powerId, "Amount", "Value"),
                AppliedPowerId = powerId,
                CanAffectSelf = targetKind is PotionMetadataTargetKind.Self or PotionMetadataTargetKind.AllAllies or PotionMetadataTargetKind.AllCreatures,
                CanAffectTeammate = targetKind is PotionMetadataTargetKind.SingleAlly or PotionMetadataTargetKind.AllAllies or PotionMetadataTargetKind.AllCreatures,
                CanAffectAllAllies = targetKind is PotionMetadataTargetKind.AllAllies or PotionMetadataTargetKind.AllCreatures,
                CanAffectSingleEnemy = targetKind == PotionMetadataTargetKind.SingleEnemy,
                CanAffectAllEnemies = targetKind is PotionMetadataTargetKind.AllEnemies or PotionMetadataTargetKind.AllCreatures,
                IsBuff = isBuff,
                IsDebuff = !isBuff,
                Notes = args
            });
        }
    }

    private static void AddSimpleEffectMatches(List<PotionEffectDescriptor> effects, string sourceText, IReadOnlyDictionary<string, int> dynamicVars, IReadOnlyList<PotionMetadataTargetKind> targetKinds)
    {
        Match energyMatch = GainEnergyRegex.Match(sourceText);
        if (energyMatch.Success)
        {
            string args = energyMatch.Groups["args"].Value;
            PotionMetadataTargetKind targetKind = InferCommandTargetKind(args, targetKinds);
            effects.Add(new PotionEffectDescriptor
            {
                Kind = PotionEffectKind.GainEnergy,
                TargetKind = targetKind,
                MagnitudeKind = InferMagnitudeKind(args),
                Magnitude = TryResolveMagnitude(dynamicVars, args, "Energy"),
                CanAffectSelf = targetKind is PotionMetadataTargetKind.Self or PotionMetadataTargetKind.AllAllies or PotionMetadataTargetKind.AllCreatures or PotionMetadataTargetKind.Mixed,
                CanAffectTeammate = targetKind is PotionMetadataTargetKind.SingleAlly or PotionMetadataTargetKind.AllAllies or PotionMetadataTargetKind.AllCreatures,
                CanAffectAllAllies = targetKind is PotionMetadataTargetKind.AllAllies or PotionMetadataTargetKind.AllCreatures,
                IsBuff = true,
                Notes = args
            });
        }

        if (GainGoldRegex.IsMatch(sourceText))
        {
            effects.Add(new PotionEffectDescriptor
            {
                Kind = PotionEffectKind.GainGold,
                TargetKind = PotionMetadataTargetKind.Self,
                MagnitudeKind = InferMagnitudeKind(sourceText),
                Magnitude = TryResolveMagnitude(dynamicVars, sourceText, "Gold"),
                CanAffectSelf = true,
                Notes = "PlayerCmd.GainGold"
            });
        }

        Match drawMatch = DrawRegex.Match(sourceText);
        if (drawMatch.Success)
        {
            string args = drawMatch.Groups["args"].Value;
            PotionMetadataTargetKind targetKind = InferCommandTargetKind(args, targetKinds);
            effects.Add(new PotionEffectDescriptor
            {
                Kind = PotionEffectKind.DrawCards,
                TargetKind = targetKind,
                MagnitudeKind = InferMagnitudeKind(args),
                Magnitude = TryResolveMagnitude(dynamicVars, args, "Cards", "Draw"),
                CanAffectSelf = targetKind is PotionMetadataTargetKind.Self or PotionMetadataTargetKind.AllAllies or PotionMetadataTargetKind.AllCreatures or PotionMetadataTargetKind.Mixed,
                CanAffectTeammate = targetKind is PotionMetadataTargetKind.SingleAlly or PotionMetadataTargetKind.AllAllies or PotionMetadataTargetKind.AllCreatures,
                CanAffectAllAllies = targetKind is PotionMetadataTargetKind.AllAllies or PotionMetadataTargetKind.AllCreatures,
                IsBuff = true,
                Notes = args
            });
        }

        if (sourceText.Contains("Upgrade", StringComparison.Ordinal) && sourceText.Contains("CardCmd", StringComparison.Ordinal))
        {
            effects.Add(new PotionEffectDescriptor
            {
                Kind = PotionEffectKind.UpgradeCards,
                TargetKind = PotionMetadataTargetKind.Self,
                MagnitudeKind = sourceText.Contains("Random", StringComparison.Ordinal) ? PotionMagnitudeKind.Randomized : PotionMagnitudeKind.ChoiceDependent,
                CanAffectSelf = true,
                IsBuff = true,
                Notes = "Card upgrade effect"
            });
        }

        if (sourceText.Contains("PotionCmd.TryToProcure", StringComparison.Ordinal))
        {
            effects.Add(new PotionEffectDescriptor
            {
                Kind = PotionEffectKind.ObtainPotion,
                TargetKind = PotionMetadataTargetKind.Self,
                MagnitudeKind = sourceText.Contains("Random", StringComparison.Ordinal) ? PotionMagnitudeKind.Randomized : PotionMagnitudeKind.RuntimeComputed,
                CanAffectSelf = true,
                Notes = "Potion generation effect"
            });
        }

        if (sourceText.Contains("DiscardAndDraw", StringComparison.Ordinal))
        {
            effects.Add(new PotionEffectDescriptor
            {
                Kind = PotionEffectKind.DiscardCards,
                TargetKind = PotionMetadataTargetKind.Self,
                MagnitudeKind = PotionMagnitudeKind.ChoiceDependent,
                CanAffectSelf = true,
                Notes = "Discard and draw effect"
            });
        }

        if (sourceText.Contains("AutoPlayFromDrawPile", StringComparison.Ordinal) || sourceText.Contains("CreateRandom", StringComparison.Ordinal))
        {
            effects.Add(new PotionEffectDescriptor
            {
                Kind = PotionEffectKind.ManipulateDrawPile,
                TargetKind = PotionMetadataTargetKind.Self,
                MagnitudeKind = PotionMagnitudeKind.Randomized,
                CanAffectSelf = true,
                Notes = "Draw pile manipulation / random card effect"
            });
        }
    }

    private static PotionMetadataTargetKind InferCommandTargetKind(string args, IReadOnlyList<PotionMetadataTargetKind> declaredTargets)
    {
        if (args.Contains("Creatures.Where((Creature c) => !c.IsPet)", StringComparison.Ordinal))
        {
            return PotionMetadataTargetKind.AllCreaturesExceptPets;
        }

        if (args.Contains("choiceContext.Target", StringComparison.Ordinal) || args.Contains("target", StringComparison.Ordinal))
        {
            if (declaredTargets.Contains(PotionMetadataTargetKind.SingleEnemy))
            {
                return PotionMetadataTargetKind.SingleEnemy;
            }

            if (declaredTargets.Contains(PotionMetadataTargetKind.SingleAlly))
            {
                return PotionMetadataTargetKind.SingleAlly;
            }
        }

        if (args.Contains("base.Owner", StringComparison.Ordinal) || args.Contains("player", StringComparison.Ordinal))
        {
            return PotionMetadataTargetKind.Self;
        }

        if (declaredTargets.Contains(PotionMetadataTargetKind.AllEnemies))
        {
            return PotionMetadataTargetKind.AllEnemies;
        }

        if (declaredTargets.Contains(PotionMetadataTargetKind.AllAllies))
        {
            return PotionMetadataTargetKind.AllAllies;
        }

        return declaredTargets.FirstOrDefault();
    }

    private static PotionMagnitudeKind InferMagnitudeKind(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return PotionMagnitudeKind.Unknown;
        }

        if (text.Contains("Random", StringComparison.Ordinal))
        {
            return PotionMagnitudeKind.Randomized;
        }

        if (text.Contains("Choose", StringComparison.Ordinal) || text.Contains("Select", StringComparison.Ordinal))
        {
            return PotionMagnitudeKind.ChoiceDependent;
        }

        if (text.Contains("MaxHealth", StringComparison.Ordinal) ||
            text.Contains("CurrentHealth", StringComparison.Ordinal) ||
            text.Contains(".Block", StringComparison.Ordinal) ||
            text.Contains(".Value", StringComparison.Ordinal) ||
            text.Contains("InCombat", StringComparison.Ordinal) ||
            text.Contains("Count()", StringComparison.Ordinal))
        {
            return PotionMagnitudeKind.RuntimeComputed;
        }

        return PotionMagnitudeKind.Static;
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

    private static bool IsLikelyDebuff(string powerId, string args)
    {
        return powerId.Contains("Weak", StringComparison.OrdinalIgnoreCase) ||
               powerId.Contains("Vulnerable", StringComparison.OrdinalIgnoreCase) ||
               powerId.Contains("Poison", StringComparison.OrdinalIgnoreCase) ||
               powerId.Contains("Shackle", StringComparison.OrdinalIgnoreCase) ||
               powerId.Contains("Bind", StringComparison.OrdinalIgnoreCase) ||
               args.Contains("choiceContext.Target", StringComparison.Ordinal);
    }

    private static PotionMetadataTargetKind MapTargetKind(TargetType targetType)
    {
        return targetType switch
        {
            TargetType.None or TargetType.TargetedNoCreature => PotionMetadataTargetKind.None,
            TargetType.Self => PotionMetadataTargetKind.Self,
            TargetType.AnyEnemy or TargetType.RandomEnemy => PotionMetadataTargetKind.SingleEnemy,
            TargetType.AllEnemies => PotionMetadataTargetKind.AllEnemies,
            TargetType.AnyAlly or TargetType.AnyPlayer or TargetType.Osty => PotionMetadataTargetKind.SingleAlly,
            TargetType.AllAllies => PotionMetadataTargetKind.AllAllies,
            _ => PotionMetadataTargetKind.Special
        };
    }

    private static string BuildNotes(string sourceText, bool partialUnknowns, bool requiresRuntime, IReadOnlyList<PotionMetadataTargetKind> targetKinds)
    {
        List<string> notes = [];
        if (string.IsNullOrWhiteSpace(sourceText))
        {
            notes.Add("Source file unavailable; metadata derived from PotionModel and fallbacks only.");
        }

        if (requiresRuntime)
        {
            notes.Add("Contains runtime-conditioned, randomized, or choice-dependent behavior.");
        }

        if (partialUnknowns)
        {
            notes.Add("Some effect fields could not be normalized statically.");
        }

        if (targetKinds.Contains(PotionMetadataTargetKind.AllCreaturesExceptPets))
        {
            notes.Add("Includes all-creatures-except-pets branch, which can hit self and teammate.");
        }

        return string.Join(" ", notes);
    }
}

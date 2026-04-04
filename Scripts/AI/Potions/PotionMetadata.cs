using System;
using System.Collections.Generic;

namespace AITeammate.Scripts;

internal enum PotionMetadataTargetKind
{
    Unknown,
    None,
    Self,
    SingleEnemy,
    AllEnemies,
    SingleAlly,
    AllAllies,
    AllCreatures,
    AllCreaturesExceptPets,
    Mixed,
    Special
}

internal enum PotionMagnitudeKind
{
    Static,
    RuntimeComputed,
    ChoiceDependent,
    Randomized,
    Unknown
}

internal enum PotionEffectKind
{
    Unknown,
    DealDamage,
    Heal,
    GainBlock,
    GainMaxHp,
    ApplyPower,
    GainEnergy,
    GainGold,
    DrawCards,
    UpgradeCards,
    AddCards,
    ObtainPotion,
    DiscardCards,
    ManipulateDrawPile,
    Utility
}

internal sealed class PotionEffectDescriptor
{
    public required PotionEffectKind Kind { get; init; }

    public required PotionMetadataTargetKind TargetKind { get; init; }

    public PotionMagnitudeKind MagnitudeKind { get; init; } = PotionMagnitudeKind.Unknown;

    public int? Magnitude { get; init; }

    public int RepeatCount { get; init; } = 1;

    public string? AppliedPowerId { get; init; }

    public bool CanAffectSelf { get; init; }

    public bool CanAffectTeammate { get; init; }

    public bool CanAffectAllAllies { get; init; }

    public bool CanAffectSingleEnemy { get; init; }

    public bool CanAffectAllEnemies { get; init; }

    public bool IsBuff { get; init; }

    public bool IsDebuff { get; init; }

    public bool CanHarmAllies { get; init; }

    public string Notes { get; init; } = string.Empty;

    public string Describe()
    {
        string magnitudeText = Magnitude.HasValue ? Magnitude.Value.ToString() : "?";
        string powerText = string.IsNullOrWhiteSpace(AppliedPowerId) ? string.Empty : $":{AppliedPowerId}";
        string repeatText = RepeatCount > 1 ? $"x{RepeatCount}" : string.Empty;
        return $"{Kind}:{TargetKind}:{magnitudeText}:{MagnitudeKind}{powerText}{repeatText}";
    }
}

internal sealed class PotionMetadata
{
    public required string PotionId { get; init; }

    public required string DisplayName { get; init; }

    public required string SourceTypeName { get; init; }

    public string SourceFilePath { get; init; } = string.Empty;

    public string Rarity { get; init; } = string.Empty;

    public string Usage { get; init; } = string.Empty;

    public string DeclaredTargetType { get; init; } = string.Empty;

    public IReadOnlyDictionary<string, int> DynamicVars { get; init; } = new Dictionary<string, int>(StringComparer.Ordinal);

    public IReadOnlyList<PotionMetadataTargetKind> TargetKinds { get; init; } = [];

    public IReadOnlyList<PotionEffectDescriptor> Effects { get; init; } = [];

    public bool EffectMagnitudesStaticallyKnown { get; init; }

    public bool RequiresRuntimeResolution { get; init; }

    public bool HasPartialUnknowns { get; init; }

    public bool CanDamageSelf { get; init; }

    public bool CanDamageTeammate { get; init; }

    public bool CanDamageAllAllies { get; init; }

    public bool CanDamageSingleEnemy { get; init; }

    public bool CanDamageAllEnemies { get; init; }

    public bool CanHealSelf { get; init; }

    public bool CanHealTeammate { get; init; }

    public bool CanHealAllAllies { get; init; }

    public bool GrantsBlockOrArmor { get; init; }

    public bool AppliesBuffs { get; init; }

    public bool AppliesDebuffs { get; init; }

    public bool HarmsSelf { get; init; }

    public bool HarmsTeammate { get; init; }

    public bool HarmsAllAllies { get; init; }

    public bool MixedBenefitAndHarm { get; init; }

    public bool Offensive { get; init; }

    public bool Defensive { get; init; }

    public bool Utility { get; init; }

    public string Notes { get; init; } = string.Empty;
}

internal sealed class PotionMetadataBuildReport
{
    public string SourceRoot { get; init; } = string.Empty;

    public int TotalPotions { get; init; }

    public int FullyStaticPotions { get; init; }

    public int RuntimeResolvedPotions { get; init; }

    public int PartialPotions { get; init; }

    public IReadOnlyList<string> Findings { get; init; } = [];
}

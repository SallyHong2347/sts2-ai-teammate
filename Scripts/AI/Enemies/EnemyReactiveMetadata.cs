using System;
using System.Collections.Generic;

namespace AITeammate.Scripts;

internal enum EnemyReactiveTriggerKind
{
    Unknown,
    BeforeDamageReceived,
    AfterDamageReceived,
    AfterAttack,
    BeforeCardPlayed,
    AfterCardPlayed,
    AfterCardDrawn,
    AfterCardEnteredCombat,
    BeforeCombatStart,
    AfterApplied,
    AfterRemoved,
    AfterDeath,
    BeforeTurnEnd,
    AfterTurnEnd,
    BeforeSideTurnStart,
    AfterSideTurnStart,
    ShouldPlay,
    TryModifyEnergyCostInCombat,
    ModifyDamageMultiplicative,
    ModifyDamageAdditive,
    Special
}

internal enum EnemyReactiveOutcomeKind
{
    Unknown,
    RetaliateDamage,
    ApplyPower,
    ApplyDebuff,
    GainBlock,
    GainStrength,
    GainDexterity,
    ModifyIncomingDamage,
    ModifyCardCost,
    RestrictCardPlay,
    AddStatusCard,
    AddCurseCard,
    AfflictCards,
    ApplyKeyword,
    ClearAffliction,
    RemoveKeyword,
    Stun,
    Utility,
    SpecialPunishment
}

internal enum EnemyReactiveTargetScope
{
    Unknown,
    Attacker,
    ActingPlayer,
    PlayedCard,
    AffectedPlayerCards,
    MatchingCardsOfAffectedPlayer,
    OpposingSide,
    Owner,
    Allies,
    Mixed,
    Special
}

internal enum EnemyReactiveMagnitudeKind
{
    Static,
    RuntimeComputed,
    Conditional,
    ChoiceDependent,
    Randomized,
    Unknown
}

internal enum EnemyReactiveBlockability
{
    NotApplicable,
    Blockable,
    Unblockable,
    Unknown
}

internal sealed class EnemyReactiveEffectDescriptor
{
    public required EnemyReactiveTriggerKind TriggerKind { get; init; }

    public required EnemyReactiveOutcomeKind OutcomeKind { get; init; }

    public EnemyReactiveTargetScope TargetScope { get; init; } = EnemyReactiveTargetScope.Unknown;

    public EnemyReactiveMagnitudeKind MagnitudeKind { get; init; } = EnemyReactiveMagnitudeKind.Unknown;

    public EnemyReactiveBlockability Blockability { get; init; } = EnemyReactiveBlockability.NotApplicable;

    public int? Magnitude { get; init; }

    public string? AppliedPowerId { get; init; }

    public string? AfflictionId { get; init; }

    public string? AddedCardId { get; init; }

    public string? AppliedKeyword { get; init; }

    public bool ImmediateHpPunishment { get; init; }

    public bool CardFlowPunishment { get; init; }

    public bool DelayedPunishment { get; init; }

    public bool GlobalPunishment { get; init; }

    public bool RequiresRuntimeResolution { get; init; }

    public string Notes { get; init; } = string.Empty;

    public string Describe()
    {
        string magnitudeText = Magnitude.HasValue ? Magnitude.Value.ToString() : "?";
        string suffix = string.Empty;
        if (!string.IsNullOrWhiteSpace(AppliedPowerId))
        {
            suffix = $":{AppliedPowerId}";
        }
        else if (!string.IsNullOrWhiteSpace(AfflictionId))
        {
            suffix = $":{AfflictionId}";
        }
        else if (!string.IsNullOrWhiteSpace(AddedCardId))
        {
            suffix = $":{AddedCardId}";
        }
        else if (!string.IsNullOrWhiteSpace(AppliedKeyword))
        {
            suffix = $":{AppliedKeyword}";
        }

        return $"{TriggerKind}:{OutcomeKind}:{TargetScope}:{magnitudeText}:{MagnitudeKind}:{Blockability}{suffix}";
    }
}

internal sealed class EnemyReactiveMetadata
{
    public required string PowerId { get; init; }

    public required string DisplayName { get; init; }

    public required string SourceTypeName { get; init; }

    public string SourceFilePath { get; init; } = string.Empty;

    public string PowerType { get; init; } = string.Empty;

    public IReadOnlyDictionary<string, int> DynamicVars { get; init; } = new Dictionary<string, int>(StringComparer.Ordinal);

    public IReadOnlyList<EnemyReactiveEffectDescriptor> Effects { get; init; } = [];

    public bool HasReactivePunishment { get; init; }

    public bool RetaliatesDamage { get; init; }

    public bool CardFlowPunishment { get; init; }

    public bool AppliesDebuffs { get; init; }

    public bool AddsStatusCards { get; init; }

    public bool AddsCurseCards { get; init; }

    public bool AfflictsCards { get; init; }

    public bool ChangesStrength { get; init; }

    public bool ChangesDexterity { get; init; }

    public bool GrantsBlock { get; init; }

    public bool ModifiesIncomingDamage { get; init; }

    public bool RestrictsPlay { get; init; }

    public bool ModifiesCardCosts { get; init; }

    public bool IncludesBlockableRetaliation { get; init; }

    public bool IncludesUnblockableRetaliation { get; init; }

    public bool RequiresRuntimeResolution { get; init; }

    public bool HasPartialUnknowns { get; init; }

    public string Notes { get; init; } = string.Empty;
}

internal sealed class EnemyReactiveMetadataBuildReport
{
    public string SourceRoot { get; init; } = string.Empty;

    public int TotalPowerTypesScanned { get; init; }

    public int ReactivePowerCount { get; init; }

    public int FullyStaticPowers { get; init; }

    public int RuntimeResolvedPowers { get; init; }

    public int PartialPowers { get; init; }

    public IReadOnlyList<string> Findings { get; init; } = [];
}

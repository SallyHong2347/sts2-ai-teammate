using System.Collections.Generic;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Entities.Powers;

namespace AITeammate.Scripts;

internal sealed class DeterministicCombatContext
{
    public required Player Actor { get; init; }

    public required IReadOnlyList<AiLegalActionOption> LegalActions { get; init; }

    public required Dictionary<string, ResolvedCardView> HandCardsByInstanceId { get; init; }

    public required Dictionary<string, DeterministicEnemyState> EnemiesById { get; init; }

    public required Dictionary<string, DeterministicAllyState> AlliesById { get; init; }

    public required Dictionary<string, int> ActorPowerAmounts { get; init; }

    public required HashSet<string> ActorRelicIds { get; init; }

    public required AiCharacterCombatConfig CombatConfig { get; init; }

    public required string RoomTypeName { get; init; }

    public bool IsEliteCombat { get; init; }

    public bool IsBossCombat { get; init; }

    public bool IsEliteOrBossCombat => IsEliteCombat || IsBossCombat;

    public bool HasBlockRetention =>
        ActorRelicIds.Contains("CALIPERS") ||
        ActorRelicIds.Contains("CALIPER") ||
        ActorPowerAmounts.ContainsKey("BARRICADE");

    public int CurrentHp => Actor.Creature.CurrentHp;

    public int CurrentBlock => Actor.Creature.Block;

    public int Energy => Actor.PlayerCombatState?.Energy ?? 0;

    public int IncomingDamage { get; init; }
}

internal sealed class DeterministicAllyState
{
    public required string Id { get; init; }

    public required Player Player { get; init; }

    public required Creature Creature { get; init; }

    public bool IsActor { get; init; }

    public int CurrentHp => Creature.CurrentHp;

    public int Block => Creature.Block;

    public int IncomingDamage { get; init; }
}

internal sealed class DeterministicEnemyState
{
    public required string Id { get; init; }

    public required Creature Creature { get; init; }

    public required IReadOnlyList<DeterministicEnemyReactiveState> ReactiveStates { get; init; }

    public int CurrentHp => Creature.CurrentHp;

    public int Block => Creature.Block;

    public int IncomingDamage { get; init; }

    public bool IsAttacking => IncomingDamage > 0;

    public bool HasReactiveEffects => ReactiveStates.Count > 0;
}

internal sealed class DeterministicEnemyReactiveState
{
    public required string EnemyId { get; init; }

    public required PowerModel Power { get; init; }

    public required EnemyReactiveMetadata Metadata { get; init; }

    public required IReadOnlyList<DeterministicEnemyReactiveEffectEntry> Effects { get; init; }

    public required string PowerId { get; init; }

    public required string PowerTypeName { get; init; }

    public int CurrentAmount { get; init; }

    public int CurrentDisplayAmount { get; init; }

    public bool IsVisible { get; init; }

    public bool RequiresRuntimeResolution => Metadata.RequiresRuntimeResolution;
}

internal sealed class DeterministicEnemyReactiveEffectEntry
{
    public required string EnemyId { get; init; }

    public required string PowerId { get; init; }

    public required EnemyReactiveEffectDescriptor Descriptor { get; init; }

    public EnemyReactiveTriggerKind TriggerKind => Descriptor.TriggerKind;

    public EnemyReactiveOutcomeKind OutcomeKind => Descriptor.OutcomeKind;

    public EnemyReactiveTargetScope TargetScope => Descriptor.TargetScope;

    public EnemyReactiveBlockability Blockability => Descriptor.Blockability;

    public EnemyReactiveMagnitudeKind StaticMagnitudeKind => Descriptor.MagnitudeKind;

    public int? StaticMagnitude => Descriptor.Magnitude;

    public string? AppliedPowerId => Descriptor.AppliedPowerId;

    public string? AfflictionId => Descriptor.AfflictionId;

    public string? AddedCardId => Descriptor.AddedCardId;

    public string? AppliedKeyword => Descriptor.AppliedKeyword;

    public int CurrentPowerAmount { get; init; }

    public int CurrentDisplayAmount { get; init; }

    public bool RequiresRuntimeResolution => Descriptor.RequiresRuntimeResolution;

    public bool ImmediateHpPunishment => Descriptor.ImmediateHpPunishment;

    public bool CardFlowPunishment => Descriptor.CardFlowPunishment;

    public bool DelayedPunishment => Descriptor.DelayedPunishment;

    public bool GlobalPunishment => Descriptor.GlobalPunishment;
}

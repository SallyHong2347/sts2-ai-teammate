using System.Collections.Generic;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;

namespace AITeammate.Scripts;

internal sealed class DeterministicCombatContext
{
    public required Player Actor { get; init; }

    public required IReadOnlyList<AiLegalActionOption> LegalActions { get; init; }

    public required Dictionary<string, ResolvedCardView> HandCardsByInstanceId { get; init; }

    public required Dictionary<string, DeterministicEnemyState> EnemiesById { get; init; }

    public required Dictionary<string, int> ActorPowerAmounts { get; init; }

    public required HashSet<string> ActorRelicIds { get; init; }

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

internal sealed class DeterministicEnemyState
{
    public required string Id { get; init; }

    public required Creature Creature { get; init; }

    public int CurrentHp => Creature.CurrentHp;

    public int Block => Creature.Block;

    public int IncomingDamage { get; init; }

    public bool IsAttacking => IncomingDamage > 0;
}

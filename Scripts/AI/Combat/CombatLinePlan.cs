using System.Collections.Generic;

namespace AITeammate.Scripts;

internal sealed class CombatLinePlan
{
    public required IReadOnlyList<string> ActionIds { get; init; }

    public required int Score { get; init; }

    public required int EstimatedDamageDealt { get; init; }

    public required int EstimatedDamageTaken { get; init; }

    public int EstimatedReactiveDamageTaken { get; init; }

    public int EstimatedReactiveDamageBlocked { get; init; }

    public required int EstimatedBlockAfterEnemyTurn { get; init; }

    public int PotionSetupScore { get; init; }

    public int PotionFollowUpDamageBonus { get; init; }

    public int PotionFollowUpBlockBonus { get; init; }

    public IReadOnlyList<CombatLineCandidateSummary> CandidateSummaries { get; init; } = [];

    public string FirstActionId => ActionIds[0];
}

internal sealed class CombatLineCandidateSummary
{
    public required IReadOnlyList<string> ActionIds { get; init; }

    public required int TerminalScore { get; init; }

    public required int EstimatedDamageTaken { get; init; }

    public int EstimatedReactiveDamageTaken { get; init; }

    public int EstimatedReactiveDamageBlocked { get; init; }

    public required int EstimatedBlockAfterEnemyTurn { get; init; }

    public int PotionSetupScore { get; init; }

    public int PotionFollowUpDamageBonus { get; init; }

    public int PotionFollowUpBlockBonus { get; init; }

    public string FirstActionId => ActionIds[0];
}

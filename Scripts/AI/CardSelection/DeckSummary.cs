namespace AITeammate.Scripts;

internal sealed class DeckSummary
{
    public int CardCount { get; init; }

    public int UpgradedCardCount { get; init; }

    public int AttackCount { get; init; }

    public int SkillCount { get; init; }

    public int PowerCount { get; init; }

    public int FrontloadDamageSources { get; init; }

    public int BlockSources { get; init; }

    public int DrawSources { get; init; }

    public int EnergySources { get; init; }

    public int VulnerableSources { get; init; }

    public int WeakSources { get; init; }

    public int ScalingSources { get; init; }

    public int RetainCards { get; init; }

    public int ExhaustCards { get; init; }

    public int ZeroCostCards { get; init; }

    public int HighCostCards { get; init; }

    public double AverageCost { get; init; }

    public double AverageDamage { get; init; }

    public double AverageBlock { get; init; }
}

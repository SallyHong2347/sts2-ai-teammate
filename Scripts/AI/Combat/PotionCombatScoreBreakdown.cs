namespace AITeammate.Scripts;

internal sealed class PotionCombatScoreBreakdown
{
    public string PotionId { get; init; } = string.Empty;

    public string MetadataSource { get; init; } = string.Empty;

    public int BaseScore { get; set; }

    public int ConsumptionCostPenalty { get; set; }

    public int EnemyDamageValue { get; set; }

    public int AoeEnemyValue { get; set; }

    public int LethalValue { get; set; }

    public int SelfDamagePenalty { get; set; }

    public int TeammateDamagePenalty { get; set; }

    public int AllyDamagePenalty { get; set; }

    public int SelfDebuffPenalty { get; set; }

    public int AllyDebuffPenalty { get; set; }

    public int HealValue { get; set; }

    public int BlockValue { get; set; }

    public int BuffValue { get; set; }

    public int UtilityValue { get; set; }

    public int OverkillPenalty { get; set; }

    public int UncertaintyPenalty { get; set; }

    public int GraveDangerAdjustment { get; set; }

    public int AttackingTargetBonus { get; set; }

    public int FollowUpBonus { get; set; }

    public int FinalScore =>
        BaseScore
        - ConsumptionCostPenalty
        + EnemyDamageValue
        + AoeEnemyValue
        + LethalValue
        - SelfDamagePenalty
        - TeammateDamagePenalty
        - AllyDamagePenalty
        - SelfDebuffPenalty
        - AllyDebuffPenalty
        + HealValue
        + BlockValue
        + BuffValue
        + UtilityValue
        - OverkillPenalty
        - UncertaintyPenalty
        + GraveDangerAdjustment
        + AttackingTargetBonus
        + FollowUpBonus;
}

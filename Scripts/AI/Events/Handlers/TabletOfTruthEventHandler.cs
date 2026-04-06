using System;
using System.Text.RegularExpressions;

namespace AITeammate.Scripts;

/// <summary>
/// Tablet of Truth event handler.
///
/// Event structure:
///   INITIAL page: "Decipher" (lose 3 max HP, upgrade 1 card, enter decipher loop) | "Smash" (heal)
///   DECIPHER_N pages: "Continue Deciphering" (lose escalating max HP, upgrade 1 card) | "Give Up" (leave)
///
/// Max HP cost per decipher step: 3, 6, 12, 24, 24+ (doubles each time).
/// This handler caps deciphering at 2 times to avoid draining all max HP.
/// The first step reports a reduced cost (1 instead of 3) so the planner
/// treats the initial decipher as worthwhile — 3 max HP for an upgrade is
/// an efficient trade that the generic penalty-per-point would otherwise veto.
/// </summary>
internal sealed class TabletOfTruthEventHandler : EventSpecialHandlerBase
{
    private const int MaxDecipherCount = 2;

    private static readonly int[] ReportedMaxHpCostPerStep = [1, 6, 12, 24, 24];

    public override string HandlerName => nameof(TabletOfTruthEventHandler);

    protected override string EventTypeName => "TabletOfTruth";

    public override EventOptionDescriptor Normalize(EventVisitState snapshot, EventOptionDescriptor option)
    {
        bool isInitialPage = option.TextKey.Contains(".INITIAL.", StringComparison.Ordinal);

        if (isInitialPage)
        {
            return NormalizeInitialPage(snapshot, option);
        }

        return NormalizeDecipherPage(snapshot, option);
    }

    private EventOptionDescriptor NormalizeInitialPage(EventVisitState snapshot, EventOptionDescriptor option)
    {
        if (option.TextKey.Contains("DECIPHER", StringComparison.Ordinal))
        {
            int maxHpCost = ReportedMaxHpCostPerStep[0];
            return WithKnownOutcome(option, HandlerName, "special:TabletOfTruth", true,
                EventSupportLevel.SpecialHighConfidence, EventPlannerTrustLevel.High, true,
                [EventOptionKind.LoseMaxHp, EventOptionKind.UpgradeCard],
                new EventOutcomeSummary
                {
                    MaxHpDelta = -maxHpCost,
                    UpgradeCount = 1,
                    Notes = [$"decipher step 1: lose {maxHpCost} max HP, upgrade 1 card"]
                });
        }

        if (option.TextKey.Contains("SMASH", StringComparison.Ordinal))
        {
            int estimatedHeal = Math.Min(12, snapshot.MaxHp - snapshot.CurrentHp);
            return WithKnownOutcome(option, HandlerName, "special:TabletOfTruth", true,
                EventSupportLevel.SpecialHighConfidence, EventPlannerTrustLevel.High, true,
                [EventOptionKind.Heal],
                new EventOutcomeSummary
                {
                    HpDelta = Math.Max(estimatedHeal, 0),
                    LeaveLike = true,
                    Notes = ["smash tablet for healing"]
                });
        }

        return option;
    }

    private EventOptionDescriptor NormalizeDecipherPage(EventVisitState snapshot, EventOptionDescriptor option)
    {
        if (option.TextKey.Contains("GIVE_UP", StringComparison.Ordinal))
        {
            return WithKnownOutcome(option, HandlerName, "special:TabletOfTruth", true,
                EventSupportLevel.SpecialHighConfidence, EventPlannerTrustLevel.High, true,
                [EventOptionKind.Leave],
                new EventOutcomeSummary
                {
                    LeaveLike = true,
                    Notes = ["give up deciphering and leave"]
                });
        }

        if (option.TextKey.Contains("DECIPHER", StringComparison.Ordinal))
        {
            int decipherStep = ParseDecipherStep(snapshot);

            if (decipherStep >= MaxDecipherCount)
            {
                return WithKnownOutcome(option, HandlerName, "special:TabletOfTruth", true,
                    EventSupportLevel.SpecialHighConfidence, EventPlannerTrustLevel.High, true,
                    [EventOptionKind.LoseMaxHp, EventOptionKind.UpgradeCard],
                    new EventOutcomeSummary
                    {
                        MaxHpDelta = -snapshot.MaxHp,
                        UpgradeCount = 1,
                        HasUnknownEffects = false,
                        Notes = [$"decipher step {decipherStep + 1}: hard-capped, max HP cost too high"]
                    });
            }

            int maxHpCost = decipherStep < ReportedMaxHpCostPerStep.Length
                ? ReportedMaxHpCostPerStep[decipherStep]
                : ReportedMaxHpCostPerStep[^1];

            return WithKnownOutcome(option, HandlerName, "special:TabletOfTruth", true,
                EventSupportLevel.SpecialHighConfidence, EventPlannerTrustLevel.High, true,
                [EventOptionKind.LoseMaxHp, EventOptionKind.UpgradeCard],
                new EventOutcomeSummary
                {
                    MaxHpDelta = -maxHpCost,
                    UpgradeCount = 1,
                    Notes = [$"decipher step {decipherStep + 1}: lose {maxHpCost} max HP, upgrade 1 card"]
                });
        }

        return option;
    }

    private static int ParseDecipherStep(EventVisitState snapshot)
    {
        foreach (EventOptionDescriptor opt in snapshot.Options)
        {
            Match match = Regex.Match(opt.TextKey, @"DECIPHER_(\d+)\.options\.DECIPHER\b");
            if (match.Success && int.TryParse(match.Groups[1].Value, out int step))
            {
                return step;
            }
        }

        return 0;
    }
}

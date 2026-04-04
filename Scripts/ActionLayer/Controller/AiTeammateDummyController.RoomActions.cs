using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;

namespace AITeammate.Scripts;

internal sealed partial class AiTeammateDummyController
{
    private static readonly CardEvaluationContextFactory RestSiteCardContextFactory = new();
    private static readonly EventValuationHelpers RestSiteValuationHelpers = new();
    private static readonly MethodInfo? EventChooseOptionForEventMethod =
        AccessTools.Method(typeof(EventSynchronizer), "ChooseOptionForEvent");
    private static readonly MethodInfo? EventVoteForSharedOptionMethod =
        AccessTools.Method(typeof(EventSynchronizer), "PlayerVotedForSharedOptionIndex");
    private static readonly MethodInfo? RestSiteChooseOptionMethod =
        AccessTools.Method(typeof(RestSiteSynchronizer), "ChooseOption");
    private static readonly FieldInfo? EventPageIndexField =
        AccessTools.Field(typeof(EventSynchronizer), "_pageIndex");

    private IReadOnlyList<AiTeammateAvailableAction> DiscoverEventActions(Player player)
    {
        EventSynchronizer synchronizer = RunManager.Instance.EventSynchronizer;
        if (synchronizer.IsShared && synchronizer.GetPlayerVote(player).HasValue)
        {
            return [];
        }

        EventModel eventForPlayer = synchronizer.GetEventForPlayer(player);
        IReadOnlyList<EventOption> options = eventForPlayer.CurrentOptions;
        string eventFingerprint = BuildEventActionFingerprint(synchronizer, eventForPlayer);
        EventPlanningInspection inspection = InspectCurrentEventPlan(player, synchronizer, eventForPlayer, eventFingerprint);
        EventExecutionSelection selection = ResolveEventExecutionSelection(
            player,
            synchronizer,
            eventForPlayer,
            inspection,
            eventFingerprint,
            phase: "discover");

        if (selection.OptionIndex < 0 || selection.SelectedOption == null)
        {
            return [];
        }

        return
        [
            new AiTeammateAvailableAction(
                new AiLegalActionOption
                {
                    ActionId = BuildEventOptionActionId(eventFingerprint, selection.OptionIndex),
                    ActionType = AiTeammateActionKind.ChooseEventOption.ToString(),
                    Description = $"Choose event option {selection.SelectedOption.TextKey}",
                    Label = $"Event option {selection.SelectedOption.TextKey}",
                    Summary = $"Choose event option {selection.SelectedOption.TextKey}."
                },
                async () =>
                {
                    EventModel liveEvent = synchronizer.GetEventForPlayer(player);
                    EventExecutionSelection liveSelection = ResolveEventExecutionSelection(
                        player,
                        synchronizer,
                        liveEvent,
                        inspection,
                        eventFingerprint,
                        phase: "execute");
                    if (liveSelection.OptionIndex < 0 || liveSelection.SelectedOption == null)
                    {
                        return AiActionExecutionResult.Completed;
                    }

                    if (string.Equals(liveSelection.SelectionMode, "planner", System.StringComparison.Ordinal))
                    {
                        Log.Info($"[AITeammate][Event] Executing planner-selected event option player={PlayerId} optionIndex={liveSelection.OptionIndex} textKey={liveSelection.SelectedOption.TextKey} title=\"{DescribeOptionTitle(liveSelection.SelectedOption)}\"");
                    }
                    else
                    {
                        Log.Info($"[AITeammate][Event] Executing fallback event option player={PlayerId} optionIndex={liveSelection.OptionIndex} textKey={liveSelection.SelectedOption.TextKey} title=\"{DescribeOptionTitle(liveSelection.SelectedOption)}\" reason={liveSelection.Reason}");
                    }

                    await ChooseEventOptionAsync(synchronizer, player, liveSelection.OptionIndex);
                    return AiActionExecutionResult.Completed;
                },
                $"{PlayerId}:event:{eventFingerprint}:{selection.OptionIndex}")
        ];
    }

    private IReadOnlyList<AiTeammateAvailableAction> DiscoverRestSiteActions(Player player)
    {
        RestSiteSynchronizer synchronizer = RunManager.Instance.RestSiteSynchronizer;
        IReadOnlyList<RestSiteOption> options = synchronizer.GetOptionsForPlayer(player);
        if (options.Count == 0)
        {
            return [];
        }

        CardEvaluationContext context = RestSiteCardContextFactory.Create(
            player,
            CardChoiceSource.Event,
            skipAllowed: true,
            debugSource: "rest_site");
        AiEventTuning tuning = AiCharacterCombatConfigLoader.LoadForPlayer(player).Events;
        List<RestSiteOptionEvaluation> evaluations = options
            .Select((option, index) => EvaluateRestSiteOption(player, option, index, context, tuning))
            .ToList();

        string evaluationFingerprint = BuildRestSiteEvaluationFingerprint(player, evaluations);
        if (!string.Equals(_lastRestSiteEvaluationFingerprint, evaluationFingerprint, System.StringComparison.Ordinal))
        {
            _lastRestSiteEvaluationFingerprint = evaluationFingerprint;
            foreach (RestSiteOptionEvaluation evaluation in evaluations.OrderByDescending(static entry => entry.Score))
            {
                Log.Info($"[AITeammate][RestSite] player={player.NetId} option={evaluation.OptionId} index={evaluation.OptionIndex} enabled={evaluation.IsEnabled} supported={evaluation.IsSupported} score={evaluation.Score:F1} reasons=[{string.Join("; ", evaluation.Reasons)}]");
            }
        }

        List<AiTeammateAvailableAction> rankedActions = evaluations
            .Where(static evaluation => evaluation.IsEnabled && evaluation.IsSupported)
            .OrderByDescending(static evaluation => evaluation.Score)
            .ThenBy(evaluation => evaluation.OptionId, System.StringComparer.Ordinal)
            .Select(evaluation => new AiTeammateAvailableAction(
                new AiLegalActionOption
                {
                    ActionId = BuildRestSiteOptionActionId(evaluation.OptionId, evaluation.OptionIndex),
                    ActionType = AiTeammateActionKind.ChooseRestSiteOption.ToString(),
                    Description = $"Choose rest site option {evaluation.OptionId}",
                    Label = $"Rest site option {evaluation.OptionId}",
                    Summary = $"Choose rest site option {evaluation.OptionId}."
                },
                async () =>
                {
                    await ChooseRestSiteOptionAsync(synchronizer, player, evaluation.OptionIndex);
                    return AiActionExecutionResult.Completed;
                }))
            .ToList();

        return rankedActions;
    }

    private static RestSiteOptionEvaluation EvaluateRestSiteOption(
        Player player,
        RestSiteOption option,
        int optionIndex,
        CardEvaluationContext context,
        AiEventTuning tuning)
    {
        List<string> reasons = [];
        if (!option.IsEnabled)
        {
            reasons.Add("disabled by runtime option state");
            return new RestSiteOptionEvaluation
            {
                OptionId = option.OptionId,
                OptionIndex = optionIndex,
                Score = double.NegativeInfinity,
                IsEnabled = false,
                IsSupported = false,
                Reasons = reasons
            };
        }

        double score = option.OptionId switch
        {
            "HEAL" => EvaluateHealRestSiteOption(player, reasons, tuning),
            "SMITH" => EvaluateSmithRestSiteOption(player, reasons, tuning),
            "DIG" => EvaluateDigRestSiteOption(context, reasons),
            "LIFT" => EvaluateLiftRestSiteOption(player, context, reasons),
            "COOK" => EvaluateCookRestSiteOption(player, context, reasons, tuning),
            "HATCH" => EvaluateHatchRestSiteOption(reasons),
            "CLONE" => EvaluateCloneRestSiteOption(player, reasons),
            "MEND" => EvaluateMendRestSiteOption(player, reasons, tuning),
            _ => MarkUnsupported(reasons, "unknown rest-site option")
        };

        return new RestSiteOptionEvaluation
        {
            OptionId = option.OptionId,
            OptionIndex = optionIndex,
            Score = score,
            IsEnabled = true,
            IsSupported = !double.IsNegativeInfinity(score),
            Reasons = reasons
        };
    }

    private static double EvaluateHealRestSiteOption(Player player, List<string> reasons, AiEventTuning tuning)
    {
        int missingHp = System.Math.Max(0, player.Creature.MaxHp - player.Creature.CurrentHp);
        int healAmount = (int)System.Math.Ceiling(HealRestSiteOption.GetHealAmount(player));
        int effectiveHeal = System.Math.Min(missingHp, healAmount);
        reasons.Add($"healAmount={healAmount}");
        reasons.Add($"effectiveHeal={effectiveHeal}");
        return EvaluateRestSiteHealValue(player.Creature.CurrentHp, player.Creature.MaxHp, effectiveHeal, tuning, reasons, "heal");
    }

    private static double EvaluateMendRestSiteOption(Player player, List<string> reasons, AiEventTuning tuning)
    {
        MendTargetEvaluation? bestTarget = SelectBestMendTarget(player, tuning);
        if (bestTarget == null)
        {
            return MarkUnsupported(reasons, "no mend target with meaningful heal value");
        }

        reasons.Add($"mendTarget={bestTarget.Target.NetId}");
        reasons.AddRange(bestTarget.Reasons.Select(static reason => $"target:{reason}"));
        return bestTarget.Score;
    }

    private static double EvaluateSmithRestSiteOption(Player player, List<string> reasons, AiEventTuning tuning)
    {
        List<UpgradeCandidateEvaluation> ranked = RestSiteValuationHelpers
            .RankUpgradeCandidates(player.Deck.Cards, tuning)
            .ToList();
        if (ranked.Count == 0)
        {
            return MarkUnsupported(reasons, "no upgradable cards");
        }

        UpgradeCandidateEvaluation best = ranked[0];
        double score = best.Score * tuning.OutcomeWeights.UpgradeRewardMultiplier;
        score += 8d;
        double hpRatio = player.Creature.MaxHp > 0
            ? (double)player.Creature.CurrentHp / player.Creature.MaxHp
            : 1d;
        double healthyConfidenceBonus = System.Math.Clamp((hpRatio - 0.30d) / 0.35d, 0d, 1d) * 6d;
        score += healthyConfidenceBonus;
        reasons.Add($"bestUpgrade={best.CardId}");
        reasons.Add($"bestUpgradeScore={best.Score:F1}");
        reasons.Add("smith preference bias +8.0");
        if (healthyConfidenceBonus > 0d)
        {
            reasons.Add($"healthyHpSmithBonus={healthyConfidenceBonus:F1}");
        }
        if (best.Reasons.Count > 0)
        {
            reasons.AddRange(best.Reasons.Select(static reason => $"upgrade:{reason}"));
        }

        return score;
    }

    private static double EvaluateDigRestSiteOption(CardEvaluationContext context, List<string> reasons)
    {
        double score = 24d;
        if (context.RelicIds.Count >= 8)
        {
            score += 4d;
            reasons.Add("late relic snowball bonus +4.0");
        }

        reasons.Add($"digBaseline={score:F1}");
        return score;
    }

    private static double EvaluateLiftRestSiteOption(Player player, CardEvaluationContext context, List<string> reasons)
    {
        int attackCards = context.DeckSummary.AttackCount;
        double score = 12d + System.Math.Min(attackCards, 8);
        reasons.Add($"attackDeckBias={System.Math.Min(attackCards, 8):F1}");

        MegaCrit.Sts2.Core.Models.Relics.Girya? girya = player.GetRelic<MegaCrit.Sts2.Core.Models.Relics.Girya>();
        if (girya != null)
        {
            int liftsLeft = System.Math.Max(0, 3 - girya.TimesLifted);
            score += liftsLeft;
            reasons.Add($"liftsLeft={liftsLeft}");
        }

        reasons.Add($"liftScore={score:F1}");
        return score;
    }

    private static double EvaluateCookRestSiteOption(Player player, CardEvaluationContext context, List<string> reasons, AiEventTuning tuning)
    {
        List<EventRemovalCandidate> removalCandidates = player.Deck.Cards.Count == context.DeckCards.Count
            ? BuildTopRemovalCandidates(player, context.DeckCards)
            : [];
        if (removalCandidates.Count < 2)
        {
            return MarkUnsupported(reasons, "not enough removable cards");
        }

        double removalScore = removalCandidates
            .Take(2)
            .Sum(static candidate => candidate.BurdenScore) * tuning.OutcomeWeights.RemovalRewardMultiplier;
        double maxHpScore = 9d * tuning.OutcomeWeights.MaxHpGainValuePerPoint;
        double total = removalScore + maxHpScore + 4d;
        reasons.Add($"cookRemoveA={removalCandidates[0].CardId}:{removalCandidates[0].BurdenScore:F1}");
        reasons.Add($"cookRemoveB={removalCandidates[1].CardId}:{removalCandidates[1].BurdenScore:F1}");
        reasons.Add($"cookRemovalScore={removalScore:F1}");
        reasons.Add($"cookMaxHpScore={maxHpScore:F1}");
        reasons.Add("cook synergy bonus +4.0");
        return total;
    }

    private static double EvaluateHatchRestSiteOption(List<string> reasons)
    {
        reasons.Add("hatch fixed relic reward baseline +20.0");
        return 20d;
    }

    private static double EvaluateCloneRestSiteOption(Player player, List<string> reasons)
    {
        int cloneTargets = player.Deck.Cards.Count(static card => card.Enchantment?.Id.Entry?.Contains("CLONE", System.StringComparison.OrdinalIgnoreCase) == true);
        if (cloneTargets <= 0)
        {
            return MarkUnsupported(reasons, "no clone-enchanted cards to duplicate");
        }

        double score = cloneTargets * 12d;
        reasons.Add($"cloneTargets={cloneTargets}");
        reasons.Add($"cloneScore={score:F1}");
        return score;
    }

    private static List<EventRemovalCandidate> BuildTopRemovalCandidates(Player player, IReadOnlyList<ResolvedCardView> deckCards)
    {
        return RestSiteValuationHelpers
            .RankRemovalCandidates(player, deckCards)
            .Take(2)
            .ToList();
    }

    private static double MarkUnsupported(List<string> reasons, string reason)
    {
        reasons.Add(reason);
        return double.NegativeInfinity;
    }

    internal static MendTargetEvaluation? SelectBestMendTarget(Player owner, AiEventTuning tuning)
    {
        List<MendTargetEvaluation> ranked = owner.RunState.Players
            .Where(candidate => candidate != owner && candidate.Creature.IsAlive)
            .Select(candidate => EvaluateMendTarget(owner, candidate, tuning))
            .Where(static evaluation => evaluation.Score > double.NegativeInfinity)
            .OrderByDescending(static evaluation => evaluation.Score)
            .ThenBy(static evaluation => evaluation.Target.NetId)
            .ToList();
        return ranked.FirstOrDefault();
    }

    private static MendTargetEvaluation EvaluateMendTarget(Player owner, Player target, AiEventTuning tuning)
    {
        List<string> reasons = [];
        int missingHp = System.Math.Max(0, target.Creature.MaxHp - target.Creature.CurrentHp);
        int healAmount = (int)System.Math.Ceiling(MendRestSiteOption.GetHealAmount(target));
        int effectiveHeal = System.Math.Min(missingHp, healAmount);
        reasons.Add($"healAmount={healAmount}");
        reasons.Add($"effectiveHeal={effectiveHeal}");

        double score = EvaluateRestSiteHealValue(target.Creature.CurrentHp, target.Creature.MaxHp, effectiveHeal, tuning, reasons, "mend");
        if (score > double.NegativeInfinity)
        {
            double allySupportBonus = 4d;
            score += allySupportBonus;
            reasons.Add($"allySupportBonus={allySupportBonus:F1}");
        }

        return new MendTargetEvaluation
        {
            Target = target,
            Score = score,
            Reasons = reasons
        };
    }

    private static double EvaluateRestSiteHealValue(int currentHp, int maxHp, int effectiveHeal, AiEventTuning tuning, List<string> reasons, string source)
    {
        double hpRatio = maxHp > 0 ? (double)currentHp / maxHp : 1d;
        double baseHealScore = effectiveHeal * tuning.OutcomeWeights.HealValuePerPoint;
        double urgencyMultiplier = hpRatio switch
        {
            <= 0.34d => 1.55d,
            <= 0.40d => 0.45d,
            <= 0.55d => 0.30d,
            <= 0.70d => 0.18d,
            _ => 0.08d
        };
        double score = baseHealScore * urgencyMultiplier;
        reasons.Add($"{source}BaseScore={baseHealScore:F1}");
        reasons.Add($"{source}UrgencyMultiplier={urgencyMultiplier:F2}");

        if (hpRatio <= 0.34d)
        {
            score += 18d;
            reasons.Add($"{source}CriticalSafetyBonus=18.0");
        }
        else if (effectiveHeal <= 0)
        {
            score -= 12d;
            reasons.Add($"{source}WastePenalty=-12.0");
        }

        reasons.Add($"{source}Score={score:F1}");
        return score;
    }

    private static string BuildRestSiteEvaluationFingerprint(Player player, IReadOnlyList<RestSiteOptionEvaluation> evaluations)
    {
        string coord = player.RunState.CurrentMapCoord.HasValue
            ? $"{player.RunState.CurrentMapCoord.Value.col},{player.RunState.CurrentMapCoord.Value.row}"
            : "none";
        return $"{player.RunState.CurrentRoomCount}|{coord}|{string.Join("|", evaluations.Select(static evaluation => $"{evaluation.OptionIndex}:{evaluation.OptionId}:{evaluation.IsEnabled}:{evaluation.IsSupported}:{evaluation.Score:F1}"))}";
    }

    private static string BuildEventActionFingerprint(EventSynchronizer synchronizer, EventModel eventForPlayer)
    {
        uint pageIndex = EventPageIndexField?.GetValue(synchronizer) is uint currentPageIndex
            ? currentPageIndex
            : 0u;
        string optionFingerprint = string.Join(
            ",",
            eventForPlayer.CurrentOptions.Select(static option => $"{option.TextKey}:{option.IsLocked}:{option.IsProceed}"));
        return $"{eventForPlayer.Id}|finished={eventForPlayer.IsFinished}|page={pageIndex}|options={optionFingerprint}";
    }

    private static async Task ChooseEventOptionAsync(EventSynchronizer synchronizer, Player player, int optionIndex)
    {
        using IDisposable selectorScope = PushDeterministicCardSelector();

        if (synchronizer.IsShared)
        {
            uint pageIndex = EventPageIndexField?.GetValue(synchronizer) is uint currentPageIndex
                ? currentPageIndex
                : 0u;
            EventVoteForSharedOptionMethod?.Invoke(synchronizer, new object[] { player, (uint)optionIndex, pageIndex });
            await Task.CompletedTask;
            return;
        }

        EventChooseOptionForEventMethod?.Invoke(synchronizer, new object[] { player, optionIndex });
        await Task.CompletedTask;
    }

    private static async Task ChooseRestSiteOptionAsync(RestSiteSynchronizer synchronizer, Player player, int optionIndex)
    {
        if (RestSiteChooseOptionMethod?.Invoke(synchronizer, new object[] { player, optionIndex }) is Task<bool> task)
        {
            await task;
        }
    }

    private static string BuildEventOptionActionId(string eventFingerprint, int optionIndex)
    {
        return $"event_option_{optionIndex}_{SanitizeActionToken(eventFingerprint)}";
    }

    private static string BuildRestSiteOptionActionId(string optionId, int optionIndex)
    {
        return $"rest_site_option_{optionIndex}_{SanitizeActionToken(optionId)}";
    }

    private sealed class RestSiteOptionEvaluation
    {
        public required string OptionId { get; init; }

        public required int OptionIndex { get; init; }

        public required double Score { get; init; }

        public required bool IsEnabled { get; init; }

        public required bool IsSupported { get; init; }

        public IReadOnlyList<string> Reasons { get; init; } = [];
    }

    internal sealed class MendTargetEvaluation
    {
        public required Player Target { get; init; }

        public required double Score { get; init; }

        public IReadOnlyList<string> Reasons { get; init; } = [];
    }
}

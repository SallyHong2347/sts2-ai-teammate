using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Logging;

namespace AITeammate.Scripts;

internal sealed class DeterministicCombatDecisionBackend : IAiDecisionBackend
{
    private readonly DeterministicCombatContextBuilder _contextBuilder = new();
    private readonly CombatActionScorer _scorer = new();
    private readonly CombatTurnLinePlanner _linePlanner;
    private readonly IAiDecisionBackend _fallbackBackend;

    public DeterministicCombatDecisionBackend(IAiDecisionBackend fallbackBackend)
    {
        _fallbackBackend = fallbackBackend;
        _linePlanner = new CombatTurnLinePlanner(_scorer);
    }

    public Task<AiDecisionResult> DecideAsync(AiDecisionRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        DeterministicCombatContext? context = _contextBuilder.Build(request.ActorId, request.LegalActions);
        if (context == null)
        {
            return _fallbackBackend.DecideAsync(request, ct);
        }

        CombatLinePlan? bestPlan = _linePlanner.BuildBestPlan(context);

        List<CombatActionScore> scoredActions = request.LegalActions
            .Select(action => _scorer.Score(context, action))
            .OrderByDescending(static score => score.TotalScore)
            .ThenBy(static score => score.ActionId, StringComparer.Ordinal)
            .ToList();

        foreach (CombatActionScore score in scoredActions)
        {
            Log.Info($"[AITeammate] Combat score actor={request.ActorId} actionId={score.ActionId} category={score.Category} total={score.TotalScore}");
        }

        CombatActionScore chosen = bestPlan != null
            ? scoredActions.First(score => string.Equals(score.ActionId, bestPlan.FirstActionId, StringComparison.Ordinal))
            : scoredActions.First();
        string reason = bestPlan != null
            ? $"Deterministic combat line chose {chosen.Category} with score {chosen.TotalScore}. line=[{string.Join(", ", bestPlan.ActionIds)}] lineScore={bestPlan.Score} estDamage={bestPlan.EstimatedDamageDealt} estTaken={bestPlan.EstimatedDamageTaken} estRetainedBlock={bestPlan.EstimatedBlockAfterEnemyTurn}."
            : $"Deterministic combat score chose {chosen.Category} with score {chosen.TotalScore}.";
        if (bestPlan != null)
        {
            Log.Info($"[AITeammate] Combat line actor={request.ActorId} actions=[{string.Join(", ", bestPlan.ActionIds)}] score={bestPlan.Score} estDamage={bestPlan.EstimatedDamageDealt} estTaken={bestPlan.EstimatedDamageTaken} estRetainedBlock={bestPlan.EstimatedBlockAfterEnemyTurn}");
        }

        return Task.FromResult(new AiDecisionResult
        {
            ChosenActionId = chosen.ActionId,
            RankedActionIds = scoredActions.Select(static score => score.ActionId).ToList(),
            Reason = reason
        });
    }
}

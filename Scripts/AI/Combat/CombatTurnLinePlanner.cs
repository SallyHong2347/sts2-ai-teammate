using System;
using System.Collections.Generic;
using System.Linq;

namespace AITeammate.Scripts;

internal sealed class CombatTurnLinePlanner
{
    private const int MaxLineLength = 3;
    private const int BeamWidth = 6;

    private readonly CombatActionScorer _scorer;

    public CombatTurnLinePlanner(CombatActionScorer scorer)
    {
        _scorer = scorer;
    }

    public CombatLinePlan? BuildBestPlan(DeterministicCombatContext context)
    {
        List<PlannableAction> actions = context.LegalActions
            .Select(action => BuildPlannableAction(context, action))
            .Where(static action => !action.IsEndTurn)
            .ToList();
        if (actions.Count == 0)
        {
            return null;
        }

        List<LineNode> frontier = actions
            .OrderByDescending(static action => action.ImmediateScore.TotalScore)
            .ThenBy(static action => action.Action.ActionId, StringComparer.Ordinal)
            .Take(BeamWidth)
            .Select(action => CreateInitialNode(context, action))
            .ToList();
        List<LineNode> bestNodes = frontier.ToList();

        for (int depth = 1; depth < MaxLineLength; depth++)
        {
            List<LineNode> nextFrontier = [];
            foreach (LineNode node in frontier)
            {
                if (node.StopExpanding)
                {
                    continue;
                }

                foreach (PlannableAction candidate in actions)
                {
                    if (!node.CanApply(candidate))
                    {
                        continue;
                    }

                    nextFrontier.Add(node.Apply(context, candidate));
                }
            }

            if (nextFrontier.Count == 0)
            {
                break;
            }

            frontier = nextFrontier
                .OrderByDescending(node => node.ComputeTerminalScore(context, actions))
                .ThenBy(node => string.Join("|", node.ActionIds), StringComparer.Ordinal)
                .Take(BeamWidth)
                .ToList();
            bestNodes.AddRange(frontier);
        }

        LineNode best = bestNodes
            .OrderByDescending(node => node.ComputeTerminalScore(context, actions))
            .ThenBy(node => string.Join("|", node.ActionIds), StringComparer.Ordinal)
            .First();

        return new CombatLinePlan
        {
            ActionIds = best.ActionIds.ToList(),
            Score = best.ComputeTerminalScore(context, actions),
            EstimatedDamageDealt = best.TotalDamageDealt,
            EstimatedDamageTaken = best.EstimatedDamageTaken(context),
            EstimatedBlockAfterEnemyTurn = best.EstimatedBlockAfterEnemyTurn(context)
        };
    }

    private PlannableAction BuildPlannableAction(DeterministicCombatContext context, AiLegalActionOption action)
    {
        CombatActionScore immediateScore = _scorer.Score(context, action);
        ResolvedCardView? card = ResolveCard(context, action);
        int damage = card.GetEstimatedDamage();
        int block = card.GetEstimatedBlock();
        int cardsDrawn = card.GetCardsDrawn();
        int energyGain = Math.Max(card.GetEnergyGain(), 0);
        int vulnerable = card.GetEnemyVulnerableAmount();
        int weak = card.GetEnemyWeakAmount();
        int selfStrength = card.GetSelfStrengthAmount();
        int selfTemporaryStrength = card.GetSelfTemporaryStrengthAmount();
        int selfDexterity = card.GetSelfDexterityAmount();
        int selfTemporaryDexterity = card.GetSelfTemporaryDexterityAmount();
        bool isHighVariance = cardsDrawn > 0;
        bool isOffensivePotion = IsOffensivePotion(action);
        bool appliesVulnerable = vulnerable > 0;

        if (isOffensivePotion && vulnerable <= 0)
        {
            vulnerable = 1;
            appliesVulnerable = true;
        }

        return new PlannableAction
        {
            Action = action,
            ImmediateScore = immediateScore,
            EnergyCost = action.EnergyCost ?? 0,
            Damage = damage,
            DamageHits = Math.Max(GetDamageHits(card), 1),
            Block = block,
            CardsDrawn = cardsDrawn,
            EnergyGain = energyGain,
            Vulnerable = vulnerable,
            Weak = weak,
            SelfStrength = Math.Max(0, selfStrength - selfTemporaryStrength),
            SelfTemporaryStrength = selfTemporaryStrength,
            SelfDexterity = Math.Max(0, selfDexterity - selfTemporaryDexterity),
            SelfTemporaryDexterity = selfTemporaryDexterity,
            IsHighVariance = isHighVariance,
            IsEndTurn = string.Equals(action.ActionType, AiTeammateActionKind.EndTurn.ToString(), StringComparison.Ordinal),
            IsOffensivePotion = isOffensivePotion,
            AppliesVulnerable = appliesVulnerable,
            IsSetup = immediateScore.Category is CombatActionCategory.PowerSetup or CombatActionCategory.Utility,
            ConsumptionKey = BuildConsumptionKey(action)
        };
    }

    private static LineNode CreateInitialNode(DeterministicCombatContext context, PlannableAction action)
    {
        LineNode node = new(context.Energy);
        return node.Apply(context, action);
    }

    private static ResolvedCardView? ResolveCard(DeterministicCombatContext context, AiLegalActionOption action)
    {
        if (!string.IsNullOrEmpty(action.CardInstanceId) &&
            context.HandCardsByInstanceId.TryGetValue(action.CardInstanceId, out ResolvedCardView? card))
        {
            return card;
        }

        return null;
    }

    private static int GetDamageHits(ResolvedCardView? card)
    {
        if (card == null)
        {
            return 1;
        }

        return card.Effects
            .Where(static effect => effect.Kind == EffectKind.DealDamage)
            .Sum(static effect => Math.Max(effect.RepeatCount, 1));
    }

    private static string BuildConsumptionKey(AiLegalActionOption action)
    {
        if (!string.IsNullOrEmpty(action.CardInstanceId))
        {
            return $"card:{action.CardInstanceId}";
        }

        if (string.Equals(action.ActionType, AiTeammateActionKind.UsePotion.ToString(), StringComparison.Ordinal))
        {
            return $"potion:{action.ActionId}";
        }

        return $"action:{action.ActionId}";
    }

    private static bool IsOffensivePotion(AiLegalActionOption action)
    {
        string potionId = action.CardId ?? string.Empty;
        return potionId.Contains("BINDING", StringComparison.OrdinalIgnoreCase)
               || potionId.Contains("VULNERABLE", StringComparison.OrdinalIgnoreCase)
               || potionId.Contains("WEAK", StringComparison.OrdinalIgnoreCase)
               || potionId.Contains("POISON", StringComparison.OrdinalIgnoreCase)
               || potionId.Contains("FIRE", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class PlannableAction
    {
        public required AiLegalActionOption Action { get; init; }

        public required CombatActionScore ImmediateScore { get; init; }

        public required string ConsumptionKey { get; init; }

        public int EnergyCost { get; init; }

        public int Damage { get; init; }

        public int DamageHits { get; init; }

        public int Block { get; init; }

        public int CardsDrawn { get; init; }

        public int EnergyGain { get; init; }

        public int Vulnerable { get; init; }

        public int Weak { get; init; }

        public int SelfStrength { get; init; }

        public int SelfTemporaryStrength { get; init; }

        public int SelfDexterity { get; init; }

        public int SelfTemporaryDexterity { get; init; }

        public bool IsHighVariance { get; init; }

        public bool IsEndTurn { get; init; }

        public bool IsOffensivePotion { get; init; }

        public bool AppliesVulnerable { get; init; }

        public bool IsSetup { get; init; }
    }

    private sealed class LineNode
    {
        private readonly HashSet<string> _consumedKeys = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _damageByTargetId = new(StringComparer.Ordinal);
        private readonly HashSet<string> _deadEnemyIds = new(StringComparer.Ordinal);
        private readonly HashSet<string> _vulnerableTargets = new(StringComparer.Ordinal);
        private readonly HashSet<string> _weakenedTargets = new(StringComparer.Ordinal);

        public LineNode(int energyRemaining)
        {
            EnergyRemaining = energyRemaining;
        }

        private LineNode(LineNode other)
        {
            EnergyRemaining = other.EnergyRemaining;
            ActionIds = other.ActionIds.ToList();
            _consumedKeys = new HashSet<string>(other._consumedKeys, StringComparer.Ordinal);
            _damageByTargetId = new Dictionary<string, int>(other._damageByTargetId, StringComparer.Ordinal);
            _deadEnemyIds = new HashSet<string>(other._deadEnemyIds, StringComparer.Ordinal);
            _vulnerableTargets = new HashSet<string>(other._vulnerableTargets, StringComparer.Ordinal);
            _weakenedTargets = new HashSet<string>(other._weakenedTargets, StringComparer.Ordinal);
            BaseScore = other.BaseScore;
            TotalDamageDealt = other.TotalDamageDealt;
            TotalBlockGained = other.TotalBlockGained;
            SetupScore = other.SetupScore;
            DamagePreventedByKills = other.DamagePreventedByKills;
            DamagePreventedByWeak = other.DamagePreventedByWeak;
            StrengthGained = other.StrengthGained;
            TemporaryStrengthGained = other.TemporaryStrengthGained;
            DexterityGained = other.DexterityGained;
            TemporaryDexterityGained = other.TemporaryDexterityGained;
            CardsDrawn = other.CardsDrawn;
            EnergyGenerated = other.EnergyGenerated;
            StopExpanding = other.StopExpanding;
        }

        public int EnergyRemaining { get; private set; }

        public List<string> ActionIds { get; } = [];

        public int BaseScore { get; private set; }

        public int TotalDamageDealt { get; private set; }

        public int TotalBlockGained { get; private set; }

        public int SetupScore { get; private set; }

        public int DamagePreventedByKills { get; private set; }

        public int DamagePreventedByWeak { get; private set; }

        public int StrengthGained { get; private set; }

        public int TemporaryStrengthGained { get; private set; }

        public int DexterityGained { get; private set; }

        public int TemporaryDexterityGained { get; private set; }

        public int CardsDrawn { get; private set; }

        public int EnergyGenerated { get; private set; }

        public bool StopExpanding { get; private set; }

        public bool CanApply(PlannableAction action)
        {
            if (_consumedKeys.Contains(action.ConsumptionKey))
            {
                return false;
            }

            if (action.EnergyCost > EnergyRemaining)
            {
                return false;
            }

            return true;
        }

        public LineNode Apply(DeterministicCombatContext context, PlannableAction action)
        {
            AiCombatStatusWeights status = context.CombatConfig.Combat.StatusWeights;
            AiCombatResourceWeights resource = context.CombatConfig.Combat.ResourceWeights;
            LineNode next = new(this)
            {
                EnergyRemaining = Math.Max(0, EnergyRemaining - action.EnergyCost + action.EnergyGain)
            };
            next.ActionIds.Add(action.Action.ActionId);
            next._consumedKeys.Add(action.ConsumptionKey);
            next.BaseScore += action.ImmediateScore.TotalScore;
            next.EnergyGenerated += action.EnergyGain;
            next.CardsDrawn += action.CardsDrawn;

            int effectiveBlock = action.Block + next.DexterityGained + next.TemporaryDexterityGained;
            if (effectiveBlock > 0)
            {
                next.TotalBlockGained += effectiveBlock;
            }

            if (!string.IsNullOrEmpty(action.Action.TargetId))
            {
                if (action.AppliesVulnerable)
                {
                    next._vulnerableTargets.Add(action.Action.TargetId);
                }

                if (action.Weak > 0 && context.EnemiesById.TryGetValue(action.Action.TargetId, out DeterministicEnemyState? weakenedEnemy))
                {
                    next._weakenedTargets.Add(action.Action.TargetId);
                    next.DamagePreventedByWeak += Math.Max(1, weakenedEnemy.IncomingDamage / 4);
                }
            }

            if (action.Damage > 0 && !string.IsNullOrEmpty(action.Action.TargetId))
            {
                int dealtDamage = action.Damage + (next.StrengthGained + next.TemporaryStrengthGained) * action.DamageHits;
                if (next._vulnerableTargets.Contains(action.Action.TargetId))
                {
                    dealtDamage += (int)Math.Ceiling(dealtDamage * 0.5m);
                }

                next.TotalDamageDealt += dealtDamage;
                next._damageByTargetId[action.Action.TargetId] = next._damageByTargetId.GetValueOrDefault(action.Action.TargetId) + dealtDamage;

                if (!next._deadEnemyIds.Contains(action.Action.TargetId) &&
                    context.EnemiesById.TryGetValue(action.Action.TargetId, out DeterministicEnemyState? enemy))
                {
                    int effectiveEnemyHp = enemy.CurrentHp + enemy.Block;
                    if (next._damageByTargetId[action.Action.TargetId] >= effectiveEnemyHp)
                    {
                        next._deadEnemyIds.Add(action.Action.TargetId);
                        next.DamagePreventedByKills += enemy.IncomingDamage;
                    }
                }
            }

            next.StrengthGained += action.SelfStrength;
            next.TemporaryStrengthGained += action.SelfTemporaryStrength;
            next.DexterityGained += action.SelfDexterity;
            next.TemporaryDexterityGained += action.SelfTemporaryDexterity;

            if (action.IsSetup)
            {
                next.SetupScore += resource.SetupActionBonus;
            }

            if (action.SelfStrength > 0 || action.SelfTemporaryStrength > 0)
            {
                next.SetupScore += CountAffordableUnconsumedActions(next, context, requireDamage: true) * (action.SelfStrength * status.SetupPersistentStrengthValue + action.SelfTemporaryStrength * status.SetupTemporaryStrengthValue);
            }

            if (action.SelfDexterity > 0 || action.SelfTemporaryDexterity > 0)
            {
                next.SetupScore += CountAffordableUnconsumedActions(next, context, requireBlock: true) * (action.SelfDexterity * status.SetupPersistentDexterityValue + action.SelfTemporaryDexterity * status.SetupTemporaryDexterityValue);
            }

            if (action.CardsDrawn > 0)
            {
                int futurePlayableActions = CountAffordableUnconsumedActions(next, context);
                if (next.EnergyRemaining > 0 && futurePlayableActions > 0)
                {
                    next.SetupScore += action.CardsDrawn * resource.SetupDrawValueWhenPlayable;
                }
                else
                {
                    next.SetupScore -= action.CardsDrawn * resource.SetupDrawPenaltyWhenNotPlayable;
                }
            }

            if (action.EnergyGain > 0)
            {
                next.SetupScore += action.EnergyGain * resource.SetupEnergyGainValue;
            }

            next.StopExpanding = action.IsHighVariance || next.ActionIds.Count >= MaxLineLength;
            return next;
        }

        private int CountAffordableUnconsumedActions(LineNode node, DeterministicCombatContext context, bool requireDamage = false, bool requireBlock = false)
        {
            return context.LegalActions.Count(action =>
            {
                if (string.IsNullOrEmpty(action.ActionId) ||
                    node._consumedKeys.Contains(BuildConsumptionKey(action)) ||
                    string.Equals(action.ActionType, AiTeammateActionKind.EndTurn.ToString(), StringComparison.Ordinal) ||
                    (action.EnergyCost ?? 0) > node.EnergyRemaining)
                {
                    return false;
                }

                ResolvedCardView? card = ResolveCard(context, action);
                if (requireDamage)
                {
                    return card?.HasEffect(EffectKind.DealDamage) == true;
                }

                if (requireBlock)
                {
                    return card?.HasEffect(EffectKind.GainBlock) == true;
                }

                return true;
            });
        }

        public int EstimatedDamageTaken(DeterministicCombatContext context)
        {
            int incomingDamage = Math.Max(0, context.IncomingDamage - DamagePreventedByKills - DamagePreventedByWeak);
            int availableBlock = context.CurrentBlock + TotalBlockGained;
            return Math.Max(0, incomingDamage - availableBlock);
        }

        public int EstimatedBlockAfterEnemyTurn(DeterministicCombatContext context)
        {
            int incomingDamage = Math.Max(0, context.IncomingDamage - DamagePreventedByKills - DamagePreventedByWeak);
            int leftoverBlock = Math.Max(0, context.CurrentBlock + TotalBlockGained - incomingDamage);
            return context.HasBlockRetention ? leftoverBlock : 0;
        }

        public int ComputeTerminalScore(DeterministicCombatContext context, IReadOnlyList<PlannableAction> actions)
        {
            AiCharacterCombatTuning tuning = context.CombatConfig.Combat;
            AiCombatCoreWeights core = tuning.CoreWeights;
            AiCombatStatusWeights status = tuning.StatusWeights;
            AiCombatResourceWeights resource = tuning.ResourceWeights;
            AiCombatRiskProfile risk = tuning.RiskProfile;
            int incomingDamage = Math.Max(0, context.IncomingDamage - DamagePreventedByKills - DamagePreventedByWeak);
            int availableBlock = context.CurrentBlock + TotalBlockGained;
            int damageTaken = Math.Max(0, incomingDamage - availableBlock);
            int preventedByBlock = Math.Min(incomingDamage, availableBlock);
            int leftoverBlock = EstimatedBlockAfterEnemyTurn(context);
            int remainingAffordableActions = actions.Count(action =>
                !_consumedKeys.Contains(action.ConsumptionKey) &&
                !action.IsEndTurn &&
                action.EnergyCost <= EnergyRemaining);

            int score = BaseScore;
            score += risk.ApplySurvivalWeight(preventedByBlock * risk.PreventedDamageValuePerPoint);
            score -= risk.ApplySurvivalWeight(damageTaken * risk.DamageTakenPenaltyPerPoint);
            score += DamagePreventedByKills * risk.KillPreventionValuePerPoint;
            score += DamagePreventedByWeak * risk.WeakPreventionValuePerPoint;
            score += risk.ApplyAttackWeight(TotalDamageDealt * core.LineDamageValuePerPoint);
            score += SetupScore;
            score += risk.ApplyDefenseWeight(leftoverBlock * core.LeftoverBlockValuePerPoint);
            score += _deadEnemyIds.Count * risk.DeadEnemyReward;
            score += StrengthGained * status.LinePersistentStrengthValue;
            score += TemporaryStrengthGained * status.LineTemporaryStrengthValue;
            score += DexterityGained * status.LinePersistentDexterityValue;
            score += TemporaryDexterityGained * (incomingDamage > 0 ? status.LineTemporaryDexterityThreatenedValue : status.LineTemporaryDexteritySafeValue);
            score += EnergyGenerated * resource.LineEnergyGeneratedValue;
            score += CardsDrawn * (remainingAffordableActions > 0 ? resource.LineCardsDrawnValueWhenUsable : -resource.LineCardsDrawnPenaltyWhenNotUsable);
            score -= EnergyRemaining * resource.RemainingEnergyPenalty;
            score -= remainingAffordableActions * resource.RemainingAffordableActionsPenalty;

            if (damageTaken == 0 && preventedByBlock > 0)
            {
                score += risk.PerfectDefenseBonus;
            }

            if (damageTaken > 0 && TotalBlockGained == 0 && DamagePreventedByWeak == 0)
            {
                score -= risk.ExposedDamageWithoutDefensePenalty;
            }

            return score;
        }
    }
}

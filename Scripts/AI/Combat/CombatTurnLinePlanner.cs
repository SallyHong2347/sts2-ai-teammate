using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Logging;

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

        List<CombatLineCandidateSummary> candidateSummaries = bestNodes
            .Select(node => new CombatLineCandidateSummary
            {
                ActionIds = node.ActionIds.ToList(),
                TerminalScore = node.ComputeTerminalScore(context, actions),
                EstimatedDamageTaken = node.EstimatedDamageTaken(context),
                EstimatedReactiveDamageTaken = node.EstimatedReactiveDamageTaken,
                EstimatedReactiveDamageBlocked = node.EstimatedReactiveDamageBlocked,
                EstimatedBlockAfterEnemyTurn = node.EstimatedBlockAfterEnemyTurn(context),
                PotionSetupScore = node.PotionSetupScore,
                PotionFollowUpDamageBonus = node.PotionFollowUpDamageBonus,
                PotionFollowUpBlockBonus = node.PotionFollowUpBlockBonus
            })
            .Distinct(CombatLineCandidateSummaryComparer.Instance)
            .OrderByDescending(static summary => summary.TerminalScore)
            .ThenBy(summary => string.Join("|", summary.ActionIds), StringComparer.Ordinal)
            .Take(5)
            .ToList();

        List<CombatLineCandidateSummary> reactiveCandidateSummaries = bestNodes
            .Where(node => node.EstimatedReactiveDamageTaken > 0 || node.EstimatedReactiveDamageBlocked > 0)
            .Select(node => new CombatLineCandidateSummary
            {
                ActionIds = node.ActionIds.ToList(),
                TerminalScore = node.ComputeTerminalScore(context, actions),
                EstimatedDamageTaken = node.EstimatedDamageTaken(context),
                EstimatedReactiveDamageTaken = node.EstimatedReactiveDamageTaken,
                EstimatedReactiveDamageBlocked = node.EstimatedReactiveDamageBlocked,
                EstimatedBlockAfterEnemyTurn = node.EstimatedBlockAfterEnemyTurn(context),
                PotionSetupScore = node.PotionSetupScore,
                PotionFollowUpDamageBonus = node.PotionFollowUpDamageBonus,
                PotionFollowUpBlockBonus = node.PotionFollowUpBlockBonus
            })
            .Distinct(CombatLineCandidateSummaryComparer.Instance)
            .OrderByDescending(static summary => summary.TerminalScore)
            .ThenBy(summary => string.Join("|", summary.ActionIds), StringComparer.Ordinal)
            .Take(3)
            .ToList();

        foreach (CombatLineCandidateSummary summary in candidateSummaries)
        {
            Log.Info(
                $"[AITeammate] Combat line candidate actor={context.Actor.NetId} line=[{string.Join(", ", summary.ActionIds)}] firstAction={summary.FirstActionId} terminalScore={summary.TerminalScore} estTaken={summary.EstimatedDamageTaken} reactiveEstTaken={summary.EstimatedReactiveDamageTaken} reactiveEstBlocked={summary.EstimatedReactiveDamageBlocked} estRetainedBlock={summary.EstimatedBlockAfterEnemyTurn}");
            if (summary.PotionSetupScore > 0 || summary.PotionFollowUpDamageBonus > 0 || summary.PotionFollowUpBlockBonus > 0)
            {
                Log.Info(
                    $"[AITeammate] Combat potion line actor={context.Actor.NetId} line=[{string.Join(", ", summary.ActionIds)}] firstAction={summary.FirstActionId} potionSetupScore={summary.PotionSetupScore} potionFollowUpDamage={summary.PotionFollowUpDamageBonus} potionFollowUpBlock={summary.PotionFollowUpBlockBonus}");
            }
        }

        foreach (CombatLineCandidateSummary summary in reactiveCandidateSummaries)
        {
            Log.Info(
                $"[AITeammate] Combat reactive line actor={context.Actor.NetId} line=[{string.Join(", ", summary.ActionIds)}] firstAction={summary.FirstActionId} terminalScore={summary.TerminalScore} estTaken={summary.EstimatedDamageTaken} reactiveEstTaken={summary.EstimatedReactiveDamageTaken} reactiveEstBlocked={summary.EstimatedReactiveDamageBlocked} estRetainedBlock={summary.EstimatedBlockAfterEnemyTurn}");
        }

        return new CombatLinePlan
        {
            ActionIds = best.ActionIds.ToList(),
            Score = best.ComputeTerminalScore(context, actions),
            EstimatedDamageDealt = best.TotalDamageDealt,
            EstimatedDamageTaken = best.EstimatedDamageTaken(context),
            EstimatedReactiveDamageTaken = best.EstimatedReactiveDamageTaken,
            EstimatedReactiveDamageBlocked = best.EstimatedReactiveDamageBlocked,
            EstimatedBlockAfterEnemyTurn = best.EstimatedBlockAfterEnemyTurn(context),
            PotionSetupScore = best.PotionSetupScore,
            PotionFollowUpDamageBonus = best.PotionFollowUpDamageBonus,
            PotionFollowUpBlockBonus = best.PotionFollowUpBlockBonus,
            CandidateSummaries = candidateSummaries
        };
    }

    private PlannableAction BuildPlannableAction(DeterministicCombatContext context, AiLegalActionOption action)
    {
        CombatActionScore immediateScore = _scorer.Score(context, action);
        ResolvedCardView? card = ResolveCard(context, action);
        PotionPlanningContribution potionContribution = BuildPotionPlanningContribution(context, action);
        int damage = card?.GetEstimatedDamage() ?? 0;
        int block = Math.Max(card?.GetEstimatedBlock() ?? 0, potionContribution.Block);
        int cardsDrawn = Math.Max(card?.GetCardsDrawn() ?? 0, potionContribution.CardsDrawn);
        int energyGain = Math.Max(Math.Max(card?.GetEnergyGain() ?? 0, 0), potionContribution.EnergyGain);
        int vulnerable = Math.Max(card?.GetEnemyVulnerableAmount() ?? 0, potionContribution.Vulnerable);
        int weak = Math.Max(card?.GetEnemyWeakAmount() ?? 0, potionContribution.Weak);
        int selfStrength = Math.Max(card?.GetSelfStrengthAmount() ?? 0, potionContribution.SelfStrength + potionContribution.SelfTemporaryStrength);
        int selfTemporaryStrength = Math.Max(card?.GetSelfTemporaryStrengthAmount() ?? 0, potionContribution.SelfTemporaryStrength);
        int selfDexterity = Math.Max(card?.GetSelfDexterityAmount() ?? 0, potionContribution.SelfDexterity + potionContribution.SelfTemporaryDexterity);
        int selfTemporaryDexterity = Math.Max(card?.GetSelfTemporaryDexterityAmount() ?? 0, potionContribution.SelfTemporaryDexterity);
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
            IsPotion = string.Equals(action.ActionType, AiTeammateActionKind.UsePotion.ToString(), StringComparison.Ordinal),
            IsOffensivePotion = isOffensivePotion,
            AppliesVulnerable = appliesVulnerable,
            IsSetup = immediateScore.Category is CombatActionCategory.PowerSetup or CombatActionCategory.Utility || potionContribution.HasSetupContribution,
            PlanningNotes = potionContribution.Notes,
            ConsumptionKey = BuildConsumptionKey(action)
        };
    }

        private static LineNode CreateInitialNode(DeterministicCombatContext context, PlannableAction action)
        {
            LineNode node = new(context.Energy, context.CurrentBlock);
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
            // Target variants for the same hand card instance must collapse to one consumable resource.
            return $"card:{action.CardInstanceId}";
        }

        if (string.Equals(action.ActionType, AiTeammateActionKind.UsePotion.ToString(), StringComparison.Ordinal))
        {
            if (action.Metadata != null &&
                action.Metadata.TryGetValue("potion_slot_index", out string? potionSlotIndex) &&
                !string.IsNullOrWhiteSpace(potionSlotIndex))
            {
                return $"potion_slot:{potionSlotIndex}";
            }

            return $"potion:{action.ActionId}";
        }

        return $"action:{action.ActionId}";
    }

    private static bool IsOffensivePotion(AiLegalActionOption action)
    {
        return !string.IsNullOrWhiteSpace(action.CardId) &&
               PotionMetadataRepository.Shared.TryGet(action.CardId, out PotionMetadata? metadata) &&
               metadata?.Offensive == true &&
               !metadata.HarmsSelf &&
               !metadata.HarmsTeammate &&
               !metadata.HarmsAllAllies;
    }

    private static PotionPlanningContribution BuildPotionPlanningContribution(DeterministicCombatContext context, AiLegalActionOption action)
    {
        if (!string.Equals(action.ActionType, AiTeammateActionKind.UsePotion.ToString(), StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(action.CardId) ||
            !PotionMetadataRepository.Shared.TryGet(action.CardId, out PotionMetadata? metadata) ||
            metadata == null)
        {
            return PotionPlanningContribution.None;
        }

        PotionPlanningContribution contribution = PotionPlanningContribution.None;
        List<string> notes = [];
        foreach (PotionEffectDescriptor effect in metadata.Effects)
        {
            int magnitude = ResolvePotionPlanningMagnitude(effect);
            if (magnitude <= 0)
            {
                continue;
            }

            switch (effect.Kind)
            {
                case PotionEffectKind.GainBlock:
                    if (PotionEffectAppliesToActor(context, action, effect))
                    {
                        contribution.Block += magnitude;
                        notes.Add($"block={magnitude}@{DescribePotionRecipient(context, action, effect)}");
                    }
                    break;
                case PotionEffectKind.GainEnergy:
                    if (PotionEffectAppliesToActor(context, action, effect))
                    {
                        contribution.EnergyGain += magnitude;
                        notes.Add($"energy={magnitude}@{DescribePotionRecipient(context, action, effect)}");
                    }
                    break;
                case PotionEffectKind.DrawCards:
                    if (PotionEffectAppliesToActor(context, action, effect))
                    {
                        contribution.CardsDrawn += magnitude;
                        notes.Add($"draw={magnitude}@{DescribePotionRecipient(context, action, effect)}");
                    }
                    break;
                case PotionEffectKind.ApplyPower:
                    if (effect.IsBuff && PotionEffectAppliesToActor(context, action, effect))
                    {
                        if (TryApplyPotionSelfBuff(effect.AppliedPowerId, magnitude, ref contribution, out string? buffNote) &&
                            !string.IsNullOrWhiteSpace(buffNote))
                        {
                            notes.Add($"{buffNote}@{DescribePotionRecipient(context, action, effect)}");
                        }
                    }
                    else if (effect.IsDebuff && PotionEffectAppliesToChosenEnemy(context, action, effect))
                    {
                        if (IsVulnerablePower(effect.AppliedPowerId))
                        {
                            contribution.Vulnerable = Math.Max(contribution.Vulnerable, magnitude);
                            notes.Add($"targetVulnerable={magnitude}");
                        }
                        else if (IsWeakPower(effect.AppliedPowerId))
                        {
                            contribution.Weak = Math.Max(contribution.Weak, magnitude);
                            notes.Add($"targetWeak={magnitude}");
                        }
                    }
                    break;
            }
        }

        if (notes.Count > 0)
        {
            contribution.Notes = string.Join(", ", notes);
        }

        return contribution;
    }

    private static int ResolvePotionPlanningMagnitude(PotionEffectDescriptor effect)
    {
        int magnitude = Math.Max(0, effect.Magnitude ?? 0);
        return effect.MagnitudeKind switch
        {
            PotionMagnitudeKind.Static => magnitude,
            PotionMagnitudeKind.RuntimeComputed => magnitude,
            PotionMagnitudeKind.ChoiceDependent => Math.Max(0, (int)Math.Round(magnitude * 0.75d, MidpointRounding.AwayFromZero)),
            PotionMagnitudeKind.Randomized => Math.Max(0, (int)Math.Round(magnitude * 0.60d, MidpointRounding.AwayFromZero)),
            _ => 0
        };
    }

    private static bool PotionEffectAppliesToActor(DeterministicCombatContext context, AiLegalActionOption action, PotionEffectDescriptor effect)
    {
        if (effect.CanAffectSelf)
        {
            return true;
        }

        string actorTargetId = $"player_{context.Actor.NetId}";
        return effect.TargetKind == PotionMetadataTargetKind.SingleAlly &&
               string.Equals(action.TargetId, actorTargetId, StringComparison.Ordinal);
    }

    private static string DescribePotionRecipient(DeterministicCombatContext context, AiLegalActionOption action, PotionEffectDescriptor effect)
    {
        if (PotionEffectAppliesToActor(context, action, effect))
        {
            return "actor";
        }

        if (effect.CanAffectAllAllies)
        {
            return "all_allies";
        }

        if (effect.CanAffectTeammate && !string.IsNullOrWhiteSpace(action.TargetId))
        {
            return $"target:{action.TargetId}";
        }

        if (effect.CanAffectSingleEnemy && !string.IsNullOrWhiteSpace(action.TargetId))
        {
            return $"enemy:{action.TargetId}";
        }

        return "none";
    }

    private static bool PotionEffectAppliesToChosenEnemy(DeterministicCombatContext context, AiLegalActionOption action, PotionEffectDescriptor effect)
    {
        return !string.IsNullOrEmpty(action.TargetId) &&
               context.EnemiesById.ContainsKey(action.TargetId) &&
               effect.CanAffectSingleEnemy &&
               effect.TargetKind == PotionMetadataTargetKind.SingleEnemy;
    }

    private static bool TryApplyPotionSelfBuff(string? powerId, int magnitude, ref PotionPlanningContribution contribution, out string? note)
    {
        note = null;
        if (string.IsNullOrWhiteSpace(powerId) || magnitude <= 0)
        {
            return false;
        }

        if (powerId.Contains("FlexPotion", StringComparison.OrdinalIgnoreCase))
        {
            contribution.SelfTemporaryStrength += magnitude;
            note = $"tempStrength={magnitude}";
            return true;
        }

        if (powerId.Contains("SpeedPotion", StringComparison.OrdinalIgnoreCase))
        {
            contribution.SelfTemporaryDexterity += magnitude;
            note = $"tempDexterity={magnitude}";
            return true;
        }

        if (powerId.Contains("Strength", StringComparison.OrdinalIgnoreCase))
        {
            contribution.SelfStrength += magnitude;
            note = $"strength={magnitude}";
            return true;
        }

        if (powerId.Contains("Dexterity", StringComparison.OrdinalIgnoreCase))
        {
            contribution.SelfDexterity += magnitude;
            note = $"dexterity={magnitude}";
            return true;
        }

        return false;
    }

    private static bool IsVulnerablePower(string? powerId)
    {
        return !string.IsNullOrWhiteSpace(powerId) &&
               powerId.Contains("Vulnerable", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWeakPower(string? powerId)
    {
        return !string.IsNullOrWhiteSpace(powerId) &&
               powerId.Contains("Weak", StringComparison.OrdinalIgnoreCase);
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

        public bool IsPotion { get; init; }

        public bool IsOffensivePotion { get; init; }

        public bool AppliesVulnerable { get; init; }

        public bool IsSetup { get; init; }

        public string PlanningNotes { get; init; } = string.Empty;
    }

    private struct PotionPlanningContribution
    {
        public static PotionPlanningContribution None => new();

        public int Block { get; set; }

        public int CardsDrawn { get; set; }

        public int EnergyGain { get; set; }

        public int Vulnerable { get; set; }

        public int Weak { get; set; }

        public int SelfStrength { get; set; }

        public int SelfTemporaryStrength { get; set; }

        public int SelfDexterity { get; set; }

        public int SelfTemporaryDexterity { get; set; }

        public string Notes { get; set; }

        public bool HasSetupContribution =>
            CardsDrawn > 0 ||
            EnergyGain > 0 ||
            SelfStrength > 0 ||
            SelfTemporaryStrength > 0 ||
            SelfDexterity > 0 ||
            SelfTemporaryDexterity > 0 ||
            Vulnerable > 0 ||
            Weak > 0;
    }

    private sealed class LineNode
    {
        private readonly HashSet<string> _consumedKeys = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _damageByTargetId = new(StringComparer.Ordinal);
        private readonly HashSet<string> _deadEnemyIds = new(StringComparer.Ordinal);
        private readonly HashSet<string> _vulnerableTargets = new(StringComparer.Ordinal);
        private readonly HashSet<string> _weakenedTargets = new(StringComparer.Ordinal);

        public LineNode(int energyRemaining, int blockAvailable)
        {
            EnergyRemaining = energyRemaining;
            BlockAvailable = blockAvailable;
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
            PotionStrengthGained = other.PotionStrengthGained;
            PotionTemporaryStrengthGained = other.PotionTemporaryStrengthGained;
            PotionDexterityGained = other.PotionDexterityGained;
            PotionTemporaryDexterityGained = other.PotionTemporaryDexterityGained;
            PotionSetupScore = other.PotionSetupScore;
            PotionFollowUpDamageBonus = other.PotionFollowUpDamageBonus;
            PotionFollowUpBlockBonus = other.PotionFollowUpBlockBonus;
            CardsDrawn = other.CardsDrawn;
            EnergyGenerated = other.EnergyGenerated;
            BlockAvailable = other.BlockAvailable;
            ReactiveDamageTaken = other.ReactiveDamageTaken;
            ReactiveDamageBlocked = other.ReactiveDamageBlocked;
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

        public int PotionStrengthGained { get; private set; }

        public int PotionTemporaryStrengthGained { get; private set; }

        public int PotionDexterityGained { get; private set; }

        public int PotionTemporaryDexterityGained { get; private set; }

        public int PotionSetupScore { get; private set; }

        public int PotionFollowUpDamageBonus { get; private set; }

        public int PotionFollowUpBlockBonus { get; private set; }

        public int CardsDrawn { get; private set; }

        public int EnergyGenerated { get; private set; }

        public int BlockAvailable { get; private set; }

        public int ReactiveDamageTaken { get; private set; }

        public int ReactiveDamageBlocked { get; private set; }

        public bool StopExpanding { get; private set; }

        public int EstimatedReactiveDamageTaken => ReactiveDamageTaken;

        public int EstimatedReactiveDamageBlocked => ReactiveDamageBlocked;

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
            int blockBeforeAction = BlockAvailable;
            ReactiveCombatPenaltyEvaluation reactivePenalty = ReactiveCombatPenaltyEvaluator.Evaluate(
                context,
                action.Action,
                ResolveCard(context, action.Action),
                action.Damage,
                action.DamageHits,
                blockBeforeAction);
            LineNode next = new(this)
            {
                EnergyRemaining = Math.Max(0, EnergyRemaining - action.EnergyCost + action.EnergyGain)
            };
            next.ActionIds.Add(action.Action.ActionId);
            next._consumedKeys.Add(action.ConsumptionKey);
            next.BaseScore += action.ImmediateScore.TotalScore;
            next.EnergyGenerated += action.EnergyGain;
            next.CardsDrawn += action.CardsDrawn;
            next.ReactiveDamageTaken += reactivePenalty.ReactiveDamageTaken;
            next.ReactiveDamageBlocked += reactivePenalty.ReactiveDamageBlocked;
            next.BlockAvailable = Math.Max(0, next.BlockAvailable - reactivePenalty.ReactiveDamageBlocked);

            int potionDexterityForAction = next.PotionDexterityGained + next.PotionTemporaryDexterityGained;
            int potionStrengthForAction = next.PotionStrengthGained + next.PotionTemporaryStrengthGained;
            int effectiveBlock = action.Block + next.DexterityGained + next.TemporaryDexterityGained;
            int potionBlockBonus = action.Block > 0 ? potionDexterityForAction : 0;
            if (effectiveBlock > 0)
            {
                next.TotalBlockGained += effectiveBlock;
                next.BlockAvailable += effectiveBlock;
                next.PotionFollowUpBlockBonus += potionBlockBonus;
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
                int potionDamageBonus = potionStrengthForAction * action.DamageHits;
                if (next._vulnerableTargets.Contains(action.Action.TargetId))
                {
                    dealtDamage += (int)Math.Ceiling(dealtDamage * 0.5m);
                    if (potionDamageBonus > 0)
                    {
                        potionDamageBonus += (int)Math.Ceiling(potionDamageBonus * 0.5m);
                    }
                }

                next.TotalDamageDealt += dealtDamage;
                next.PotionFollowUpDamageBonus += potionDamageBonus;
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
            if (action.IsPotion)
            {
                next.PotionStrengthGained += action.SelfStrength;
                next.PotionTemporaryStrengthGained += action.SelfTemporaryStrength;
                next.PotionDexterityGained += action.SelfDexterity;
                next.PotionTemporaryDexterityGained += action.SelfTemporaryDexterity;
            }

            int setupAdded = 0;
            if (action.IsSetup)
            {
                setupAdded += resource.SetupActionBonus;
            }

            if (action.SelfStrength > 0 || action.SelfTemporaryStrength > 0)
            {
                setupAdded += CountAffordableUnconsumedActions(next, context, requireDamage: true) * (action.SelfStrength * status.SetupPersistentStrengthValue + action.SelfTemporaryStrength * status.SetupTemporaryStrengthValue);
            }

            if (action.SelfDexterity > 0 || action.SelfTemporaryDexterity > 0)
            {
                setupAdded += CountAffordableUnconsumedActions(next, context, requireBlock: true) * (action.SelfDexterity * status.SetupPersistentDexterityValue + action.SelfTemporaryDexterity * status.SetupTemporaryDexterityValue);
            }

            if (action.CardsDrawn > 0)
            {
                int futurePlayableActions = CountAffordableUnconsumedActions(next, context);
                if (next.EnergyRemaining > 0 && futurePlayableActions > 0)
                {
                    setupAdded += action.CardsDrawn * resource.SetupDrawValueWhenPlayable;
                }
                else
                {
                    setupAdded -= action.CardsDrawn * resource.SetupDrawPenaltyWhenNotPlayable;
                }
            }

            if (action.EnergyGain > 0)
            {
                setupAdded += action.EnergyGain * resource.SetupEnergyGainValue;
            }

            next.SetupScore += setupAdded;
            if (action.IsPotion)
            {
                next.PotionSetupScore += setupAdded;
            }

            if (reactivePenalty.ReactiveDamageTaken > 0 || reactivePenalty.ReactiveDamageBlocked > 0)
            {
                Log.Debug(
                    $"[AITeammate][ReactiveLine] actor={context.Actor.NetId} line=[{string.Join(", ", next.ActionIds)}] actionId={action.Action.ActionId} target={action.Action.TargetId ?? "none"} reactiveStepTaken={reactivePenalty.ReactiveDamageTaken} reactiveStepBlocked={reactivePenalty.ReactiveDamageBlocked} blockBefore={blockBeforeAction} blockAfterReactive={Math.Max(0, blockBeforeAction - reactivePenalty.ReactiveDamageBlocked)} blockAfterAction={next.BlockAvailable} lineReactiveTaken={next.ReactiveDamageTaken} lineReactiveBlocked={next.ReactiveDamageBlocked}");
            }

            if (action.IsPotion && (!string.IsNullOrWhiteSpace(action.PlanningNotes) || setupAdded != 0))
            {
                Log.Debug(
                    $"[AITeammate][PotionLine] actor={context.Actor.NetId} line=[{string.Join(", ", next.ActionIds)}] actionId={action.Action.ActionId} notes={action.PlanningNotes} potionSetupAdded={setupAdded} linePotionSetup={next.PotionSetupScore} energyAfter={next.EnergyRemaining} blockAfter={next.BlockAvailable}");
            }

            if (potionBlockBonus > 0 || (action.Damage > 0 && potionStrengthForAction > 0))
            {
                Log.Debug(
                    $"[AITeammate][PotionFollowUp] actor={context.Actor.NetId} line=[{string.Join(", ", next.ActionIds)}] actionId={action.Action.ActionId} stepPotionStrength={potionStrengthForAction} stepPotionDexterity={potionDexterityForAction} linePotionFollowUpDamage={next.PotionFollowUpDamageBonus} linePotionFollowUpBlock={next.PotionFollowUpBlockBonus}");
            }

            next.StopExpanding = action.IsEndTurn || action.IsHighVariance || next.ActionIds.Count >= MaxLineLength;
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
            return ReactiveDamageTaken + Math.Max(0, incomingDamage - BlockAvailable);
        }

        public int EstimatedBlockAfterEnemyTurn(DeterministicCombatContext context)
        {
            int incomingDamage = Math.Max(0, context.IncomingDamage - DamagePreventedByKills - DamagePreventedByWeak);
            int leftoverBlock = Math.Max(0, BlockAvailable - incomingDamage);
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
            int damageTaken = ReactiveDamageTaken + Math.Max(0, incomingDamage - BlockAvailable);
            int preventedByBlock = ReactiveDamageBlocked + Math.Min(incomingDamage, BlockAvailable);
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

    private sealed class CombatLineCandidateSummaryComparer : IEqualityComparer<CombatLineCandidateSummary>
    {
        public static CombatLineCandidateSummaryComparer Instance { get; } = new();

        public bool Equals(CombatLineCandidateSummary? x, CombatLineCandidateSummary? y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }

            if (x == null || y == null)
            {
                return false;
            }

            if (x.TerminalScore != y.TerminalScore ||
                x.EstimatedDamageTaken != y.EstimatedDamageTaken ||
                x.EstimatedReactiveDamageTaken != y.EstimatedReactiveDamageTaken ||
                x.EstimatedReactiveDamageBlocked != y.EstimatedReactiveDamageBlocked ||
                x.EstimatedBlockAfterEnemyTurn != y.EstimatedBlockAfterEnemyTurn ||
                x.PotionSetupScore != y.PotionSetupScore ||
                x.PotionFollowUpDamageBonus != y.PotionFollowUpDamageBonus ||
                x.PotionFollowUpBlockBonus != y.PotionFollowUpBlockBonus ||
                x.ActionIds.Count != y.ActionIds.Count)
            {
                return false;
            }

            for (int index = 0; index < x.ActionIds.Count; index++)
            {
                if (!string.Equals(x.ActionIds[index], y.ActionIds[index], StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        public int GetHashCode(CombatLineCandidateSummary obj)
        {
            HashCode hash = new();
            hash.Add(obj.TerminalScore);
            hash.Add(obj.EstimatedDamageTaken);
            hash.Add(obj.EstimatedReactiveDamageTaken);
            hash.Add(obj.EstimatedReactiveDamageBlocked);
            hash.Add(obj.EstimatedBlockAfterEnemyTurn);
            hash.Add(obj.PotionSetupScore);
            hash.Add(obj.PotionFollowUpDamageBonus);
            hash.Add(obj.PotionFollowUpBlockBonus);
            foreach (string actionId in obj.ActionIds)
            {
                hash.Add(actionId, StringComparer.Ordinal);
            }

            return hash.ToHashCode();
        }
    }
}

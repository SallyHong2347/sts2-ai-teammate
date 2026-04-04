using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace AITeammate.Scripts;

internal sealed partial class AiTeammateDummyController
{
    private static readonly FieldInfo? PotionBeforeUseField =
        AccessTools.Field(typeof(PotionModel), "BeforeUse");
    private static readonly FieldInfo? PotionIsQueuedField =
        AccessTools.Field(typeof(PotionModel), "<IsQueued>k__BackingField");

    private IReadOnlyList<AiTeammateAvailableAction> DiscoverCombatActions(Player player)
    {
        List<AiTeammateAvailableAction> actions = [];
        Log.Debug($"[AITeammate] DiscoverCombatActions player={player.NetId} roomCount={player.RunState.CurrentRoomCount} currentRoom={player.RunState.CurrentRoom?.GetType().Name ?? "null"} inProgress={CombatManager.Instance.IsInProgress} playPhase={CombatManager.Instance.IsPlayPhase}");

        foreach (CardModel card in PileType.Hand.GetPile(player).Cards)
        {
            string cardInstanceId = GetCardInstanceId(card);
            UnplayableReason reason;
            MegaCrit.Sts2.Core.Models.AbstractModel? preventer;
            if (!card.CanPlay(out reason, out preventer))
            {
                Log.Debug($"[AITeammate][Card] Skipped combat card player={player.NetId} card={card.Id.Entry} instance={cardInstanceId} reason=unplayable targetType={card.TargetType} unplayableReason={reason} preventer={preventer?.GetType().Name ?? "none"}");
                continue;
            }

            List<Creature?> targets = GetResolvedCardTargets(card, player);
            string targetSummary = string.Join(", ", targets.Select(static target => target?.ToString() ?? "none"));
            Log.Debug($"[AITeammate][Card] Expanding combat card player={player.NetId} card={card.Id.Entry} instance={cardInstanceId} targetType={card.TargetType} expands={ShouldExpandCardTargets(card.TargetType)} validTargets=[{targetSummary}]");

            List<string> emittedActionIds = [];
            foreach (Creature? target in targets)
            {
                emittedActionIds.Add(AddPlayCardAction(actions, card, target));
            }

            if (emittedActionIds.Count == 0)
            {
                Log.Debug($"[AITeammate][Card] Skipped combat card player={player.NetId} card={card.Id.Entry} instance={cardInstanceId} reason=no_valid_target targetType={card.TargetType}.");
                continue;
            }

            Log.Debug($"[AITeammate][Card] Discovered combat card actions player={player.NetId} card={card.Id.Entry} instance={cardInstanceId} count={emittedActionIds.Count} actionIds=[{string.Join(", ", emittedActionIds)}]");
        }

        List<PotionModel> potions = player.Potions.Where(static potion => !potion.IsQueued).ToList();
        for (int potionIndex = 0; potionIndex < potions.Count; potionIndex++)
        {
            PotionModel potion = potions[potionIndex];
            List<Creature?> targets = GetResolvedPotionTargets(potion, player);
            string targetSummary = string.Join(", ", targets.Select(static target => target?.ToString() ?? "none"));
            Log.Debug($"[AITeammate][Potion] Expanding combat potion player={player.NetId} potion={potion.Id.Entry} slot={potionIndex} targetType={potion.TargetType} expands={ShouldExpandPotionTargets(potion.TargetType)} validTargets=[{targetSummary}]");
            if (targets.Count == 0)
            {
                Log.Debug($"[AITeammate][Potion] Skipped combat potion player={player.NetId} potion={potion.Id.Entry} slot={potionIndex} reason=no_valid_target targetType={potion.TargetType}");
                continue;
            }

            List<string> emittedActionIds = [];
            foreach (Creature? target in targets)
            {
                emittedActionIds.Add(AddUsePotionAction(actions, player, potion, target, potionIndex));
            }

            Log.Debug($"[AITeammate][Potion] Discovered combat potion actions player={player.NetId} potion={potion.Id.Entry} slot={potionIndex} count={emittedActionIds.Count} actionIds=[{string.Join(", ", emittedActionIds)}]");
        }

        actions.Add(new AiTeammateAvailableAction(
            new AiLegalActionOption
            {
                ActionId = BuildEndTurnActionId(player),
                ActionType = AiTeammateActionKind.EndTurn.ToString(),
                Description = "End turn",
                Label = "End turn",
                Summary = "Finish the actor's current turn."
            },
            () =>
            {
                int roundNumber = player.Creature.CombatState?.RoundNumber ?? 0;
                EndPlayerTurnAction endTurnAction = new(player, roundNumber);
                RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(endTurnAction);
                return Task.FromResult(new AiActionExecutionResult
                {
                    GameAction = endTurnAction,
                    WaitForQueueSettle = true
                });
            }));

        return actions;
    }

    private static List<Creature?> GetResolvedCardTargets(CardModel card, Player player)
    {
        if (ShouldExpandCardTargets(card.TargetType))
        {
            return GetOrderedTargets(card.TargetType, player)
                .Where(target => target != null && IsPlayableTarget(card, target, player))
                .ToList();
        }

        Creature? target = GetFixedCardTarget(card, player);
        if (target == null)
        {
            return card.TargetType == TargetType.Osty
                ? []
                : [null];
        }

        return IsPlayableTarget(card, target, player)
            ? [target]
            : [];
    }

    private static bool ShouldExpandCardTargets(TargetType targetType)
    {
        return targetType is TargetType.AnyEnemy or TargetType.AnyAlly or TargetType.AnyPlayer;
    }

    private static List<Creature?> GetResolvedPotionTargets(PotionModel potion, Player player)
    {
        if (ShouldExpandPotionTargets(potion.TargetType))
        {
            return GetOrderedTargets(potion.TargetType, player)
                .Where(static target => target != null)
                .ToList();
        }

        Creature? target = GetFixedPotionTarget(potion, player);
        if (target == null)
        {
            return potion.TargetType.IsSingleTarget()
                ? []
                : [null];
        }

        return [target];
    }

    private static bool ShouldExpandPotionTargets(TargetType targetType)
    {
        return targetType is TargetType.AnyEnemy or TargetType.AnyAlly or TargetType.AnyPlayer;
    }

    private static IEnumerable<Creature?> GetOrderedTargets(TargetType targetType, Player player)
    {
        CombatState? combatState = player.Creature.CombatState;
        if (combatState == null)
        {
            return new Creature?[] { null };
        }

        return targetType switch
        {
            TargetType.AnyEnemy => combatState.HittableEnemies.OrderBy(static creature => creature.CombatId ?? uint.MaxValue).Cast<Creature?>(),
            TargetType.AnyAlly => combatState.PlayerCreatures.Where(static creature => creature.IsAlive).OrderBy(static creature => creature.Player?.NetId ?? 0UL).Cast<Creature?>(),
            TargetType.AnyPlayer => combatState.PlayerCreatures.Where(static creature => creature.IsAlive).OrderBy(static creature => creature.Player?.NetId ?? 0UL).Cast<Creature?>(),
            TargetType.Self => new Creature?[] { player.Creature },
            TargetType.Osty => player.Osty != null ? new Creature?[] { player.Osty } : [],
            _ => new Creature?[] { null },
        };
    }

    private static Creature? GetFixedCardTarget(CardModel card, Player player)
    {
        return card.TargetType switch
        {
            TargetType.Self => player.Creature,
            TargetType.Osty => player.Osty,
            _ => null,
        };
    }

    private static Creature? GetFixedPotionTarget(PotionModel potion, Player player)
    {
        return potion.TargetType switch
        {
            TargetType.Self => player.Creature,
            TargetType.Osty => player.Osty,
            _ => null,
        };
    }

    private static bool IsPlayableTarget(CardModel card, Creature target, Player player)
    {
        if (card.TargetType == TargetType.Self && ReferenceEquals(target, player.Creature))
        {
            return true;
        }

        return card.CanPlayTargeting(target);
    }

    private static string AddPlayCardAction(List<AiTeammateAvailableAction> actions, CardModel card, Creature? target)
    {
        Creature? executionTarget = card.TargetType == TargetType.Self ? null : target;
        string targetName = card.TargetType switch
        {
            TargetType.Self => "self",
            TargetType.Osty => "osty",
            _ => target?.ToString() ?? "none",
        };
        string actionId = BuildPlayCardActionId(card, executionTarget);
        Log.Debug($"[AITeammate][Card] Discovered combat card action card={card.Id.Entry} instance={GetCardInstanceId(card)} actionId={actionId} target={targetName} targetType={card.TargetType}");
        actions.Add(new AiTeammateAvailableAction(
            new AiLegalActionOption
            {
                ActionId = actionId,
                ActionType = AiTeammateActionKind.PlayCard.ToString(),
                Description = $"Play {card.Id.Entry} -> {targetName}",
                Label = $"Play {card.Id.Entry}",
                Summary = $"Play {card.Id.Entry} targeting {targetName}.",
                CardId = card.Id.Entry,
                CardInstanceId = GetCardInstanceId(card),
                TargetId = GetTargetId(executionTarget),
                TargetLabel = targetName,
                EnergyCost = card.EnergyCost.GetAmountToSpend()
            },
            () =>
            {
                TaskHelper.RunSafely(card.OnEnqueuePlayVfx(executionTarget));
                PlayCardAction playCardAction = new(card, executionTarget);
                RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(playCardAction);
                return Task.FromResult(new AiActionExecutionResult
                {
                    GameAction = playCardAction,
                    WaitForQueueSettle = true
                });
            },
            deduplicationKey: $"card:{GetCardInstanceId(card)}"));
        return actionId;
    }

    private static string AddUsePotionAction(List<AiTeammateAvailableAction> actions, Player player, PotionModel potion, Creature? target, int potionIndex)
    {
        string targetName = target?.ToString() ?? "none";
        string actionId = BuildUsePotionActionId(potion, target, potionIndex);
        Log.Debug($"[AITeammate][Potion] Discovered combat potion action player={player.NetId} actionId={actionId} potion={potion.Id.Entry} slot={potionIndex} target={targetName} targetType={potion.TargetType}");
        actions.Add(new AiTeammateAvailableAction(
            new AiLegalActionOption
            {
                ActionId = actionId,
                ActionType = AiTeammateActionKind.UsePotion.ToString(),
                Description = $"Use potion {potion.Id.Entry} -> {targetName}",
                Label = $"Use potion {potion.Id.Entry}",
                Summary = $"Use potion {potion.Id.Entry} targeting {targetName}.",
                CardId = potion.Id.Entry,
                TargetId = GetTargetId(target),
                TargetLabel = targetName,
                Metadata = new Dictionary<string, string>
                {
                    ["potion_slot_index"] = potionIndex.ToString(),
                    ["potion_target_kind"] = potion.TargetType.ToString()
                }
            },
            () =>
            {
                Log.Info($"[AITeammate][Potion] Executing combat potion player={player.NetId} actionId={actionId} potion={potion.Id.Entry} slot={potionIndex} target={targetName}");
                (PotionBeforeUseField?.GetValue(potion) as System.Action)?.Invoke();
                UsePotionAction usePotionAction = new(potion, target, CombatManager.Instance.IsInProgress);
                PotionIsQueuedField?.SetValue(potion, true);
                RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(usePotionAction);
                return Task.FromResult(new AiActionExecutionResult
                {
                    GameAction = usePotionAction,
                    WaitForQueueSettle = true
                });
            },
            deduplicationKey: $"potion_slot:{potionIndex}"));
        return actionId;
    }

    private static string BuildPlayCardActionId(CardModel card, Creature? target)
    {
        return $"play_card_{GetCardInstanceId(card)}_target_{GetTargetId(target)}";
    }

    private static string BuildUsePotionActionId(PotionModel potion, Creature? target, int potionIndex)
    {
        return $"use_potion_{SanitizeActionToken(potion.Id.Entry)}_{potionIndex}_target_{GetTargetId(target)}";
    }

    private static string BuildEndTurnActionId(Player player)
    {
        return $"end_turn_player_{player.NetId}";
    }

    private static string GetCardInstanceId(CardModel card)
    {
        return NetCombatCardDb.Instance.TryGetCardId(card, out uint cardId)
            ? $"combat_{cardId}"
            : SanitizeActionToken(card.Id.ToString());
    }

    private static string GetTargetId(Creature? target)
    {
        if (target == null)
        {
            return "none";
        }

        if (target.Player != null)
        {
            return $"player_{target.Player.NetId}";
        }

        return $"creature_{target.CombatId?.ToString() ?? SanitizeActionToken(target.ToString())}";
    }

    private static string SanitizeActionToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        return value.Replace(':', '_').Replace('/', '_').Replace(' ', '_');
    }
}

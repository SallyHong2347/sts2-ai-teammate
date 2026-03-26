using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Logging;

namespace AITeammate.Scripts;

internal enum AiTeammateActionKind
{
    PlayCard,
    UsePotion,
    EndTurn,
    ChooseMapNode,
    ChooseEventOption,
    ChooseRestSiteOption,
    ClaimReward,
}

internal sealed class AiTeammateAvailableAction
{
    public AiTeammateAvailableAction(
        AiTeammateActionKind kind,
        string label,
        Func<Task> executeAsync,
        string? deduplicationKey = null)
    {
        Kind = kind;
        Label = label;
        ExecuteAsync = executeAsync;
        DeduplicationKey = deduplicationKey;
    }

    public AiTeammateActionKind Kind { get; }

    public string Label { get; }

    public Func<Task> ExecuteAsync { get; }

    public string? DeduplicationKey { get; }
}

internal sealed class AiTeammateDummyController
{
    private static readonly TimeSpan IdleTickInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan ActionTickInterval = TimeSpan.FromMilliseconds(400);
    private static readonly MethodInfo? EventChooseOptionForEventMethod =
        AccessTools.Method(typeof(EventSynchronizer), "ChooseOptionForEvent");
    private static readonly MethodInfo? EventVoteForSharedOptionMethod =
        AccessTools.Method(typeof(EventSynchronizer), "PlayerVotedForSharedOptionIndex");
    private static readonly MethodInfo? RestSiteChooseOptionMethod =
        AccessTools.Method(typeof(RestSiteSynchronizer), "ChooseOption");
    private static readonly FieldInfo? EventPageIndexField =
        AccessTools.Field(typeof(EventSynchronizer), "_pageIndex");

    private DateTime _nextDecisionAtUtc = DateTime.MinValue;
    private bool _isExecutingAction;
    private string? _lastDeduplicationKey;

    public AiTeammateDummyController(int slotIndex, ulong playerId, CharacterModel character)
    {
        SlotIndex = slotIndex;
        PlayerId = playerId;
        Character = character;
    }

    public int SlotIndex { get; }

    public ulong PlayerId { get; }

    public CharacterModel Character { get; }

    public void Tick()
    {
        if (_isExecutingAction || DateTime.UtcNow < _nextDecisionAtUtc)
        {
            return;
        }

        IReadOnlyList<AiTeammateAvailableAction> actions = DiscoverAvailableActions();
        AiTeammateAvailableAction? action = ChooseDefaultAction(actions);
        if (action == null)
        {
            _nextDecisionAtUtc = DateTime.UtcNow + IdleTickInterval;
            return;
        }

        _isExecutingAction = true;
        _nextDecisionAtUtc = DateTime.UtcNow + ActionTickInterval;
        TaskHelper.RunSafely(ExecuteChosenActionAsync(action));
    }

    public IReadOnlyList<AiTeammateAvailableAction> DiscoverAvailableActions()
    {
        if (!TryGetControlledPlayer(out Player player, out RunState runState))
        {
            return Array.Empty<AiTeammateAvailableAction>();
        }

        if (!player.Creature.IsAlive)
        {
            return Array.Empty<AiTeammateAvailableAction>();
        }

        if (CombatManager.Instance.IsInProgress &&
            CombatManager.Instance.IsPlayPhase &&
            !CombatManager.Instance.PlayerActionsDisabled &&
            player.Creature.CombatState?.CurrentSide == player.Creature.Side)
        {
            return DiscoverCombatActions(player).ToList();
        }

        if (runState.CurrentRoom is EventRoom)
        {
            return DiscoverEventActions(player).ToList();
        }

        if (runState.CurrentRoom is RestSiteRoom)
        {
            return DiscoverRestSiteActions(player).ToList();
        }

        return Array.Empty<AiTeammateAvailableAction>();
    }

    public static bool IsAiPlayer(Player? player)
    {
        return player != null &&
               AiTeammateSessionRegistry.Current?.AiControllers.ContainsKey(player.NetId) == true;
    }

    public static bool TryGetControllerFor(ulong playerId, out AiTeammateDummyController controller)
    {
        if (AiTeammateSessionRegistry.Current is { } session &&
            session.AiControllers.TryGetValue(playerId, out AiTeammateDummyController? foundController))
        {
            controller = foundController;
            return true;
        }

        controller = null!;
        return false;
    }

    public static async Task ExecuteDeterministicRewardSetAsync(RewardsSet rewardsSet)
    {
        await rewardsSet.GenerateWithoutOffering();
        using IDisposable selectorScope = PushDeterministicCardSelector();
        foreach (Reward reward in rewardsSet.Rewards.ToList())
        {
            await ExecuteRewardAsync(reward);
        }
    }

    public static async Task<CardModel?> ChooseFirstCardFromChooseScreenAsync(
        PlayerChoiceContext context,
        IReadOnlyList<CardModel> cards)
    {
        await context.SignalPlayerChoiceBegun(PlayerChoiceOptions.None);
        CardModel? chosen = cards.FirstOrDefault();
        await context.SignalPlayerChoiceEnded();
        return chosen;
    }

    public static async Task<IEnumerable<CardModel>> ChooseDeterministicCardsAsync(
        PlayerChoiceContext? context,
        IEnumerable<CardModel> options,
        int minSelect,
        int maxSelect,
        PlayerChoiceOptions choiceOptions = PlayerChoiceOptions.None)
    {
        if (context != null)
        {
            await context.SignalPlayerChoiceBegun(choiceOptions);
        }

        List<CardModel> list = options.ToList();
        int desiredCount = ComputeSelectionCount(list.Count, minSelect, maxSelect);
        IEnumerable<CardModel> selected = list.Take(desiredCount).ToList();

        if (context != null)
        {
            await context.SignalPlayerChoiceEnded();
        }

        return selected;
    }

    public static RelicModel? ChooseFirstRelic(IReadOnlyList<RelicModel> relics)
    {
        return relics.FirstOrDefault();
    }

    public static IReadOnlyList<CardModel> ChooseFirstBundle(IReadOnlyList<IReadOnlyList<CardModel>> bundles)
    {
        return bundles.FirstOrDefault() ?? Array.Empty<CardModel>();
    }

    public static IDisposable PushDeterministicCardSelector()
    {
        var selector = new DeterministicCardSelector();
        return CardSelectCmd.Selector == null
            ? CardSelectCmd.UseSelector(selector)
            : CardSelectCmd.PushSelector(selector);
    }

    private static int ComputeSelectionCount(int optionCount, int minSelect, int maxSelect)
    {
        if (optionCount <= 0 || maxSelect <= 0)
        {
            return 0;
        }

        int desiredCount = minSelect > 0 ? minSelect : 1;
        desiredCount = Math.Min(desiredCount, optionCount);
        desiredCount = Math.Min(desiredCount, maxSelect);
        return Math.Max(desiredCount, 0);
    }

    private static async Task ExecuteRewardAsync(Reward reward)
    {
        switch (reward)
        {
            case PotionReward potionReward:
                if (await potionReward.OnSelectWrapper())
                {
                    return;
                }

                PotionModel? currentPotion = potionReward.Player.Potions.FirstOrDefault();
                if (currentPotion != null)
                {
                    await PotionCmd.Discard(currentPotion);
                    await potionReward.OnSelectWrapper();
                }

                return;
            default:
                await reward.OnSelectWrapper();
                return;
        }
    }

    private AiTeammateAvailableAction? ChooseDefaultAction(IReadOnlyList<AiTeammateAvailableAction> actions)
    {
        return actions.FirstOrDefault(action => action.DeduplicationKey == null || action.DeduplicationKey != _lastDeduplicationKey);
    }

    private async Task ExecuteChosenActionAsync(AiTeammateAvailableAction action)
    {
        try
        {
            await action.ExecuteAsync();
            if (!string.IsNullOrEmpty(action.DeduplicationKey))
            {
                _lastDeduplicationKey = action.DeduplicationKey;
            }
        }
        catch (Exception exception)
        {
            Log.Warn($"[AITeammate] Dummy controller {PlayerId} failed to execute {action.Kind}: {exception}");
        }
        finally
        {
            _isExecutingAction = false;
        }
    }

    private bool TryGetControlledPlayer(out Player player, out RunState runState)
    {
        runState = RunManager.Instance.DebugOnlyGetState()!;
        player = runState?.GetPlayer(PlayerId)!;
        return runState != null && player != null;
    }

    private IEnumerable<AiTeammateAvailableAction> DiscoverCombatActions(Player player)
    {
        foreach (CardModel card in PileType.Hand.GetPile(player).Cards)
        {
            UnplayableReason reason;
            AbstractModel? preventer;
            if (!card.CanPlay(out reason, out preventer))
            {
                continue;
            }

            foreach (Creature? target in GetOrderedTargets(card.TargetType, player))
            {
                if (target == null || card.CanPlayTargeting(target))
                {
                    string targetName = target?.ToString() ?? "none";
                    yield return new AiTeammateAvailableAction(
                        AiTeammateActionKind.PlayCard,
                        $"Play {card.Id.Entry} -> {targetName}",
                        () =>
                        {
                            card.TryManualPlay(target);
                            return Task.CompletedTask;
                        });
                    break;
                }
            }
        }

        foreach (PotionModel potion in player.Potions.Where(static potion => !potion.IsQueued))
        {
            Creature? target = GetOrderedTargets(potion.TargetType, player).FirstOrDefault();
            if (potion.TargetType.IsSingleTarget() && target == null)
            {
                continue;
            }

            string targetName = target?.ToString() ?? "none";
            yield return new AiTeammateAvailableAction(
                AiTeammateActionKind.UsePotion,
                $"Use potion {potion.Id.Entry} -> {targetName}",
                () =>
                {
                    potion.EnqueueManualUse(target);
                    return Task.CompletedTask;
                });
        }

        yield return new AiTeammateAvailableAction(
            AiTeammateActionKind.EndTurn,
            "End turn",
            () =>
            {
                int roundNumber = player.Creature.CombatState?.RoundNumber ?? 0;
                RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(new EndPlayerTurnAction(player, roundNumber));
                return Task.CompletedTask;
            });
    }

    private IEnumerable<AiTeammateAvailableAction> DiscoverEventActions(Player player)
    {
        EventSynchronizer synchronizer = RunManager.Instance.EventSynchronizer;
        if (synchronizer.IsShared && synchronizer.GetPlayerVote(player).HasValue)
        {
            yield break;
        }

        EventModel eventForPlayer = synchronizer.GetEventForPlayer(player);
        IReadOnlyList<EventOption> options = eventForPlayer.CurrentOptions;
        string eventFingerprint = BuildEventActionFingerprint(synchronizer, eventForPlayer);

        for (int optionIndex = 0; optionIndex < options.Count; optionIndex++)
        {
            EventOption option = options[optionIndex];
            if (option.IsLocked)
            {
                continue;
            }

            yield return new AiTeammateAvailableAction(
                AiTeammateActionKind.ChooseEventOption,
                $"Choose event option {option.TextKey}",
                async () => await ChooseEventOptionAsync(synchronizer, player, optionIndex),
                $"{PlayerId}:event:{eventFingerprint}:{optionIndex}");
            break;
        }
    }

    private IEnumerable<AiTeammateAvailableAction> DiscoverRestSiteActions(Player player)
    {
        RestSiteSynchronizer synchronizer = RunManager.Instance.RestSiteSynchronizer;
        IReadOnlyList<RestSiteOption> options = synchronizer.GetOptionsForPlayer(player);
        RestSiteOption? preferredOption = options.FirstOrDefault(static option => option.OptionId == "HEAL");
        if (preferredOption == null)
        {
            preferredOption = options.FirstOrDefault();
        }

        if (preferredOption == null)
        {
            yield break;
        }

        int optionIndex = options.ToList().IndexOf(preferredOption);
        yield return new AiTeammateAvailableAction(
            AiTeammateActionKind.ChooseRestSiteOption,
            $"Choose rest site option {preferredOption.OptionId}",
            async () => await ChooseRestSiteOptionAsync(synchronizer, player, optionIndex));
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
            _ => new Creature?[] { null },
        };
    }

    private static async Task ChooseEventOptionAsync(EventSynchronizer synchronizer, Player player, int optionIndex)
    {
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

    private sealed class DeterministicCardSelector : ICardSelector
    {
        public Task<IEnumerable<CardModel>> GetSelectedCards(IEnumerable<CardModel> options, int minSelect, int maxSelect)
        {
            List<CardModel> list = options.ToList();
            int selectionCount = ComputeSelectionCount(list.Count, minSelect, maxSelect);
            IEnumerable<CardModel> selected = list.Take(selectionCount).ToList();
            return Task.FromResult(selected);
        }

        public CardModel? GetSelectedCardReward(IReadOnlyList<CardCreationResult> options, IReadOnlyList<CardRewardAlternative> alternatives)
        {
            return options.FirstOrDefault()?.Card;
        }
    }
}

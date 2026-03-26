using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.TestSupport;

namespace AITeammate.Scripts;

internal sealed partial class AiTeammateDummyController
{
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

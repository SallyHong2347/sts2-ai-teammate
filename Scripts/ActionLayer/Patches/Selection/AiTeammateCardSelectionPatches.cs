using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;

namespace AITeammate.Scripts;

internal static class AiTeammateCardSelectionPatches
{
    [HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromChooseACardScreen))]
    private static class CardSelectChooseACardPatch
    {
        private static bool Prefix(
            PlayerChoiceContext context,
            IReadOnlyList<CardModel> cards,
            Player player,
            bool canSkip,
            ref Task<CardModel?> __result)
        {
            if (!AiTeammateDummyController.IsAiPlayer(player))
            {
                return true;
            }

            __result = AiTeammateDummyController.ChooseFirstCardFromChooseScreenAsync(context, cards, player, canSkip);
            return false;
        }
    }

    [HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromSimpleGridForRewards))]
    private static class CardSelectSimpleGridRewardsPatch
    {
        private static bool Prefix(
            PlayerChoiceContext context,
            List<CardCreationResult> cards,
            Player player,
            CardSelectorPrefs prefs,
            ref Task<IEnumerable<CardModel>> __result)
        {
            if (!AiTeammateDummyController.IsAiPlayer(player))
            {
                return true;
            }

            __result = AiTeammateDummyController.ChooseDeterministicCardsAsync(
                context,
                cards.Select(static card => card.Card),
                prefs.MinSelect,
                prefs.MaxSelect);
            return false;
        }
    }

    [HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromSimpleGrid))]
    private static class CardSelectSimpleGridPatch
    {
        private static bool Prefix(
            PlayerChoiceContext context,
            IReadOnlyList<CardModel> cardsIn,
            Player player,
            CardSelectorPrefs prefs,
            ref Task<IEnumerable<CardModel>> __result)
        {
            if (!AiTeammateDummyController.IsAiPlayer(player))
            {
                return true;
            }

            __result = AiTeammateDummyController.ChooseDeterministicCardsAsync(
                context,
                cardsIn,
                prefs.MinSelect,
                prefs.MaxSelect);
            return false;
        }
    }

    [HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromDeckForUpgrade))]
    private static class CardSelectDeckUpgradePatch
    {
        private static bool Prefix(Player player, CardSelectorPrefs prefs, ref Task<IEnumerable<CardModel>> __result)
        {
            if (!AiTeammateDummyController.IsAiPlayer(player))
            {
                return true;
            }

            IEnumerable<CardModel> options = PileType.Deck.GetPile(player).Cards.Where(static card => card.IsUpgradable);
            __result = AiTeammateDummyController.ChooseBestUpgradeCardsAsync(player, options, prefs.MinSelect, prefs.MaxSelect, "deck_upgrade");
            return false;
        }
    }

    [HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromDeckForTransformation))]
    private static class CardSelectDeckTransformPatch
    {
        private static bool Prefix(Player player, CardSelectorPrefs prefs, ref Task<IEnumerable<CardModel>> __result)
        {
            if (!AiTeammateDummyController.IsAiPlayer(player))
            {
                return true;
            }

            IEnumerable<CardModel> options = PileType.Deck.GetPile(player).Cards.Where(static card => card.Type != CardType.Quest && card.IsTransformable);
            __result = AiTeammateDummyController.ChooseDeterministicCardsAsync(null, options, prefs.MinSelect, prefs.MaxSelect);
            return false;
        }
    }

    [HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromDeckForEnchantment), new[] { typeof(IReadOnlyList<CardModel>), typeof(EnchantmentModel), typeof(int), typeof(CardSelectorPrefs) })]
    private static class CardSelectDeckEnchantmentPatch
    {
        private static bool Prefix(
            IReadOnlyList<CardModel> cards,
            EnchantmentModel enchantment,
            int amount,
            CardSelectorPrefs prefs,
            ref Task<IEnumerable<CardModel>> __result)
        {
            Player? player = cards.FirstOrDefault()?.Owner;
            if (!AiTeammateDummyController.IsAiPlayer(player))
            {
                return true;
            }

            IEnumerable<CardModel> options = cards.Where(enchantment.CanEnchant);
            __result = AiTeammateDummyController.ChooseDeterministicCardsAsync(null, options, prefs.MinSelect, prefs.MaxSelect);
            return false;
        }
    }

    [HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromDeckGeneric))]
    private static class CardSelectDeckGenericPatch
    {
        private static bool Prefix(
            Player player,
            CardSelectorPrefs prefs,
            Func<CardModel, bool>? filter,
            Func<CardModel, int>? sortingOrder,
            ref Task<IEnumerable<CardModel>> __result)
        {
            if (!AiTeammateDummyController.IsAiPlayer(player))
            {
                return true;
            }

            IEnumerable<CardModel> options = PileType.Deck.GetPile(player).Cards;
            if (filter != null)
            {
                options = options.Where(filter);
            }

            if (sortingOrder != null)
            {
                options = options.OrderBy(sortingOrder);
            }

            if (string.Equals(prefs.Prompt.LocEntryKey, CardSelectorPrefs.RemoveSelectionPrompt.LocEntryKey, StringComparison.Ordinal) &&
                AiTeammateDummyController.TryConsumePendingShopRemovalSelection(player, options, out IEnumerable<CardModel> selectedRemovalCards))
            {
                __result = Task.FromResult(selectedRemovalCards);
                return false;
            }

            __result = AiTeammateDummyController.ChooseDeterministicCardsAsync(
                null,
                options,
                prefs.MinSelect,
                prefs.MaxSelect);
            return false;
        }
    }

    [HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromHand))]
    private static class CardSelectHandPatch
    {
        private static bool Prefix(
            PlayerChoiceContext context,
            Player player,
            CardSelectorPrefs prefs,
            Func<CardModel, bool>? filter,
            ref Task<IEnumerable<CardModel>> __result)
        {
            if (!AiTeammateDummyController.IsAiPlayer(player))
            {
                return true;
            }

            IEnumerable<CardModel> options = PileType.Hand.GetPile(player).Cards;
            if (filter != null)
            {
                options = options.Where(filter);
            }

            __result = AiTeammateDummyController.ChooseDeterministicCardsAsync(
                context,
                options,
                prefs.MinSelect,
                prefs.MaxSelect,
                PlayerChoiceOptions.CancelPlayCardActions);
            return false;
        }
    }

    [HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromHandForUpgrade))]
    private static class CardSelectHandUpgradePatch
    {
        private static bool Prefix(
            PlayerChoiceContext context,
            Player player,
            AbstractModel source,
            ref Task<CardModel?> __result)
        {
            if (!AiTeammateDummyController.IsAiPlayer(player))
            {
                return true;
            }

            __result = ChooseHandUpgradeAsync(context, player);
            return false;
        }

        private static async Task<CardModel?> ChooseHandUpgradeAsync(PlayerChoiceContext context, Player player)
        {
            IEnumerable<CardModel> selected = await AiTeammateDummyController.ChooseBestUpgradeCardsAsync(
                player,
                PileType.Hand.GetPile(player).Cards.Where(static card => card.IsUpgradable),
                1,
                1,
                "hand_upgrade");
            return selected.FirstOrDefault();
        }
    }

    [HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromChooseABundleScreen))]
    private static class CardSelectBundlePatch
    {
        private static bool Prefix(
            Player player,
            IReadOnlyList<IReadOnlyList<CardModel>> bundles,
            ref Task<IEnumerable<CardModel>> __result)
        {
            if (!AiTeammateDummyController.IsAiPlayer(player))
            {
                return true;
            }

            __result = Task.FromResult<IEnumerable<CardModel>>(AiTeammateDummyController.ChooseFirstBundle(bundles));
            return false;
        }
    }
}

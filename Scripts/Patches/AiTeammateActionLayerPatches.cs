using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using Godot;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.PeerInput;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace AITeammate.Scripts;

internal static class AiTeammateActionLayerPatches
{
    private static bool _syncingAiEnemyTurnReady;
    private static readonly HashSet<ulong> _initializedPeerInputPlayers = new();
    private static readonly MethodInfo? GetOrCreatePeerInputStateMethod =
        AccessTools.Method(typeof(PeerInputSynchronizer), "GetOrCreateStateForPlayer");
    private static readonly Type? PeerInputStateType =
        AccessTools.Inner(typeof(PeerInputSynchronizer), "PeerInputState");
    private static readonly FieldInfo? PeerInputStateNetMousePositionField =
        PeerInputStateType != null ? AccessTools.Field(PeerInputStateType, "netMousePosition") : null;
    private static readonly FieldInfo? PeerInputStateControllerFocusPositionField =
        PeerInputStateType != null ? AccessTools.Field(PeerInputStateType, "controllerFocusPosition") : null;
    private static readonly FieldInfo? PeerInputStateIsMouseDownField =
        PeerInputStateType != null ? AccessTools.Field(PeerInputStateType, "isMouseDown") : null;
    private static readonly FieldInfo? PeerInputStateIsUsingControllerField =
        PeerInputStateType != null ? AccessTools.Field(PeerInputStateType, "isUsingController") : null;
    private static readonly FieldInfo? PeerInputStateNetScreenTypeField =
        PeerInputStateType != null ? AccessTools.Field(PeerInputStateType, "netScreenType") : null;
    private static readonly FieldInfo? StateChangedEventField =
        AccessTools.Field(typeof(PeerInputSynchronizer), "StateChanged");
    private static readonly FieldInfo? ScreenChangedEventField =
        AccessTools.Field(typeof(PeerInputSynchronizer), "ScreenChanged");
    private static readonly FieldInfo? TreasureRoomRelicCollectionField =
        AccessTools.Field(typeof(NTreasureRoom), "_relicCollection");
    private static readonly FieldInfo? RelicCollectionHoldersInUseField =
        AccessTools.Field(typeof(NTreasureRoomRelicCollection), "_holdersInUse");

    [HarmonyPatch(typeof(NRun), nameof(NRun._Process))]
    private static class NRunProcessPatch
    {
        private static void Postfix()
        {
            AiTeammateSessionState? session = AiTeammateSessionRegistry.Current;
            if (session == null)
            {
                return;
            }

            EnsureAiPeerInputStates(session);

            foreach (AiTeammateDummyController controller in session.AiControllers.Values)
            {
                controller.Tick();
            }
        }

        private static void EnsureAiPeerInputStates(AiTeammateSessionState session)
        {
            PeerInputSynchronizer synchronizer = RunManager.Instance.InputSynchronizer;
            bool isInSharedRelicPicking = IsSharedRelicPickingUiActive();
            NetScreenType desiredScreenType = isInSharedRelicPicking
                ? NetScreenType.SharedRelicPicking
                : NetScreenType.Room;

            int aiIndex = 0;
            foreach (AiTeammateSessionParticipant participant in session.Participants.Where(static participant => !participant.IsHost))
            {
                object? state = GetOrCreatePeerInputStateMethod?.Invoke(synchronizer, new object[] { participant.PlayerId });
                if (state == null)
                {
                    continue;
                }

                if (_initializedPeerInputPlayers.Add(participant.PlayerId))
                {
                    Log.Info($"[AITeammate] Created peer input state for AI player={participant.PlayerId}");
                }

                SyncAiPeerInputState(synchronizer, state, participant.PlayerId, aiIndex, desiredScreenType);
                aiIndex++;
            }
        }

        private static void SyncAiPeerInputState(
            PeerInputSynchronizer synchronizer,
            object state,
            ulong playerId,
            int aiIndex,
            NetScreenType desiredScreenType)
        {
            bool changed = false;
            changed |= SetFieldValue(PeerInputStateNetScreenTypeField, state, desiredScreenType);
            changed |= SetFieldValue(PeerInputStateIsMouseDownField, state, false);
            changed |= SetFieldValue(PeerInputStateIsUsingControllerField, state, false);

            Vector2 desiredPointingPosition = GetDesiredPointerPosition(playerId, aiIndex, desiredScreenType);
            changed |= SetFieldValue(PeerInputStateNetMousePositionField, state, desiredPointingPosition);
            changed |= SetFieldValue(PeerInputStateControllerFocusPositionField, state, desiredPointingPosition);

            if (!changed)
            {
                return;
            }

            RaiseStateChanged(synchronizer, playerId);
            if (desiredScreenType == NetScreenType.SharedRelicPicking)
            {
                Log.Info($"[AITeammate] Synced AI peer input for shared relic picking. player={playerId} slot={aiIndex}");
            }
        }

        private static bool SetFieldValue<T>(FieldInfo? field, object target, T value)
        {
            if (field == null)
            {
                return false;
            }

            object? currentValue = field.GetValue(target);
            if (Equals(currentValue, value))
            {
                return false;
            }

            field.SetValue(target, value);
            return true;
        }

        private static void RaiseStateChanged(PeerInputSynchronizer synchronizer, ulong playerId)
        {
            (StateChangedEventField?.GetValue(synchronizer) as Action<ulong>)?.Invoke(playerId);
            (ScreenChangedEventField?.GetValue(synchronizer) as Action<ulong, NetScreenType>)?.Invoke(playerId, synchronizer.GetScreenType(playerId));
        }

        private static bool IsSharedRelicPickingUiActive()
        {
            return NRun.Instance?.TreasureRoom is { DefaultFocusedControl: not null };
        }

        private static Vector2 GetDesiredPointerPosition(ulong playerId, int aiIndex, NetScreenType desiredScreenType)
        {
            if (desiredScreenType == NetScreenType.SharedRelicPicking &&
                TryGetNormalizedRelicHolderPosition(playerId, out Vector2 holderPosition))
            {
                return holderPosition;
            }

            return new Vector2(0.3f + aiIndex * 0.2f, 0.72f);
        }

        private static bool TryGetNormalizedRelicHolderPosition(ulong playerId, out Vector2 normalizedPosition)
        {
            normalizedPosition = default;

            NTreasureRoom? treasureRoom = NRun.Instance?.TreasureRoom;
            if (treasureRoom == null)
            {
                return false;
            }

            Player? player = RunManager.Instance.DebugOnlyGetState()?.GetPlayer(playerId);
            int? voteIndex = player == null
                ? null
                : RunManager.Instance.TreasureRoomRelicSynchronizer.GetPlayerVote(player);
            if (!voteIndex.HasValue)
            {
                return false;
            }

            if (TreasureRoomRelicCollectionField?.GetValue(treasureRoom) is not NTreasureRoomRelicCollection relicCollection ||
                RelicCollectionHoldersInUseField?.GetValue(relicCollection) is not List<NTreasureRoomRelicHolder> holders)
            {
                return false;
            }

            NTreasureRoomRelicHolder? holder = holders.FirstOrDefault(candidate => candidate.Index == voteIndex.Value);
            if (holder == null || !holder.IsInsideTree())
            {
                return false;
            }

            Vector2 viewportSize = holder.GetViewportRect().Size;
            if (viewportSize.X <= 0f || viewportSize.Y <= 0f)
            {
                return false;
            }

            Vector2 center = holder.GlobalPosition + holder.Size * 0.5f;
            normalizedPosition = new Vector2(
                Mathf.Clamp(center.X / viewportSize.X, 0.05f, 0.95f),
                Mathf.Clamp(center.Y / viewportSize.Y, 0.05f, 0.95f));
            return true;
        }
    }

    [HarmonyPatch(typeof(CombatManager), nameof(CombatManager.SetReadyToEndTurn))]
    private static class CombatManagerReadyToEndTurnPatch
    {
        private static void Postfix(Player player, bool canBackOut)
        {
            AiTeammateSessionState? session = AiTeammateSessionRegistry.Current;
            if (session == null || RunManager.Instance.NetService is not AiTeammateLoopbackHostGameService)
            {
                return;
            }

            Log.Info($"[AITeammate] ReadyToEndTurn player={player.NetId} host={session.HostPlayerId} canBackOut={canBackOut} allReady={CombatManager.Instance.AllPlayersReadyToEndTurn()}");
        }
    }

    [HarmonyPatch(typeof(CombatManager), nameof(CombatManager.SetReadyToBeginEnemyTurn))]
    private static class CombatManagerReadyToBeginEnemyTurnPatch
    {
        private static void Postfix(Player player)
        {
            AiTeammateSessionState? session = AiTeammateSessionRegistry.Current;
            if (session == null || RunManager.Instance.NetService is not AiTeammateLoopbackHostGameService)
            {
                return;
            }

            Log.Info($"[AITeammate] ReadyToBeginEnemyTurn player={player.NetId} host={session.HostPlayerId}");
            if (_syncingAiEnemyTurnReady || player.NetId != session.HostPlayerId)
            {
                return;
            }

            try
            {
                _syncingAiEnemyTurnReady = true;
                foreach (AiTeammateSessionParticipant participant in session.Participants.Where(static participant => !participant.IsHost))
                {
                    Player? aiPlayer = RunManager.Instance.DebugOnlyGetState()?.GetPlayer(participant.PlayerId);
                    if (aiPlayer == null)
                    {
                        continue;
                    }

                    if (CombatManager.Instance.IsPlayerReadyToEndTurn(aiPlayer))
                    {
                        Log.Info($"[AITeammate] Auto-marking AI ready to begin enemy turn. aiPlayer={participant.PlayerId}");
                        CombatManager.Instance.SetReadyToBeginEnemyTurn(aiPlayer);
                    }
                }
            }
            finally
            {
                _syncingAiEnemyTurnReady = false;
            }
        }
    }

    [HarmonyPatch(typeof(MapSelectionSynchronizer), nameof(MapSelectionSynchronizer.PlayerVotedForMapCoord))]
    private static class MapSelectionSynchronizerPatch
    {
        private static void Postfix(Player player, RunLocation source, MapVote? destination)
        {
            AiTeammateSessionState? session = AiTeammateSessionRegistry.Current;
            if (session == null || player.NetId != session.HostPlayerId)
            {
                return;
            }

            foreach (AiTeammateSessionParticipant participant in session.Participants.Where(static participant => !participant.IsHost))
            {
                Player? aiPlayer = RunManager.Instance.DebugOnlyGetState()?.GetPlayer(participant.PlayerId);
                if (aiPlayer == null)
                {
                    continue;
                }

                MapVote? existingVote = RunManager.Instance.MapSelectionSynchronizer.GetVote(aiPlayer);
                if (existingVote.Equals(destination))
                {
                    continue;
                }

                RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(
                    new VoteForMapCoordAction(aiPlayer, source, destination));
            }
        }
    }

    [HarmonyPatch(typeof(TreasureRoomRelicSynchronizer), nameof(TreasureRoomRelicSynchronizer.BeginRelicPicking))]
    private static class TreasureRoomRelicSynchronizerBeginRelicPickingPatch
    {
        private static void Postfix(TreasureRoomRelicSynchronizer __instance)
        {
            AiTeammateSessionState? session = AiTeammateSessionRegistry.Current;
            if (session == null || RunManager.Instance.NetService is not AiTeammateLoopbackHostGameService)
            {
                return;
            }

            IReadOnlyList<RelicModel>? currentRelics = __instance.CurrentRelics;
            if (currentRelics == null || currentRelics.Count == 0)
            {
                Log.Info("[AITeammate] Treasure relic picking started with no relic options.");
                return;
            }

            Log.Info($"[AITeammate] Treasure relic picking started. relicCount={currentRelics.Count}");
            int aiIndex = 0;
            foreach (AiTeammateSessionParticipant participant in session.Participants.Where(static participant => !participant.IsHost))
            {
                Player? aiPlayer = RunManager.Instance.DebugOnlyGetState()?.GetPlayer(participant.PlayerId);
                if (aiPlayer == null || __instance.GetPlayerVote(aiPlayer).HasValue)
                {
                    continue;
                }

                int chosenRelicIndex = aiIndex % currentRelics.Count;
                Log.Info($"[AITeammate] Auto-voting treasure relic for AI player={participant.PlayerId} relicIndex={chosenRelicIndex}");
                __instance.OnPicked(aiPlayer, chosenRelicIndex);
                aiIndex++;
            }
        }
    }

    [HarmonyPatch(typeof(RewardsSet), nameof(RewardsSet.Offer))]
    private static class RewardsSetOfferPatch
    {
        private static bool Prefix(RewardsSet __instance, ref Task __result)
        {
            if (!AiTeammateDummyController.IsAiPlayer(__instance.Player))
            {
                return true;
            }

            __result = AiTeammateDummyController.ExecuteDeterministicRewardSetAsync(__instance);
            return false;
        }
    }

    [HarmonyPatch(typeof(RewardsCmd), nameof(RewardsCmd.OfferForRoomEnd))]
    private static class RewardsCmdOfferForRoomEndPatch
    {
        private static void Postfix(Player player, AbstractRoom room, ref Task __result)
        {
            __result = OfferForAiTeammatesAfterHostAsync(__result, player, room);
        }

        private static async Task OfferForAiTeammatesAfterHostAsync(Task originalTask, Player player, AbstractRoom room)
        {
            await originalTask;

            AiTeammateSessionState? session = AiTeammateSessionRegistry.Current;
            if (session == null || player.NetId != session.HostPlayerId)
            {
                return;
            }

            if (room is TreasureRoom)
            {
                return;
            }

            foreach (AiTeammateSessionParticipant participant in session.Participants.Where(static participant => !participant.IsHost))
            {
                Player? aiPlayer = player.RunState.GetPlayer(participant.PlayerId);
                if (aiPlayer != null)
                {
                    await RewardsCmd.OfferForRoomEnd(aiPlayer, room);
                }
            }
        }
    }

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

            __result = AiTeammateDummyController.ChooseFirstCardFromChooseScreenAsync(context, cards);
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
            __result = AiTeammateDummyController.ChooseDeterministicCardsAsync(null, options, prefs.MinSelect, prefs.MaxSelect);
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
            IEnumerable<CardModel> selected = await AiTeammateDummyController.ChooseDeterministicCardsAsync(
                context,
                PileType.Hand.GetPile(player).Cards.Where(static card => card.IsUpgradable),
                1,
                1,
                PlayerChoiceOptions.CancelPlayCardActions);
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

    [HarmonyPatch(typeof(RelicSelectCmd), nameof(RelicSelectCmd.FromChooseARelicScreen))]
    private static class RelicSelectPatch
    {
        private static bool Prefix(Player player, IReadOnlyList<RelicModel> relics, ref Task<RelicModel?> __result)
        {
            if (!AiTeammateDummyController.IsAiPlayer(player))
            {
                return true;
            }

            __result = Task.FromResult(AiTeammateDummyController.ChooseFirstRelic(relics));
            return false;
        }
    }
}

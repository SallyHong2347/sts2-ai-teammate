using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace AITeammate.Scripts;

internal static class AiTeammateTestMapPatches
{
    private const int TargetHumanBelieveInYouCopies = 3;
    private static readonly Func<PotionModel>[] TestMapPotionFactories =
    [
        static () => ModelDb.Potion<VulnerablePotion>().ToMutable(),
        static () => ModelDb.Potion<BlockPotion>().ToMutable(),
        static () => ModelDb.Potion<FoulPotion>().ToMutable(),
        static () => ModelDb.Potion<FirePotion>().ToMutable(),
        static () => ModelDb.Potion<BloodPotion>().ToMutable(),
        static () => ModelDb.Potion<EnergyPotion>().ToMutable(),
        static () => ModelDb.Potion<DexterityPotion>().ToMutable(),
        static () => ModelDb.Potion<WeakPotion>().ToMutable()
    ];
    private static readonly Func<PotionModel>[] HumanTestMapPotionFactories =
    [
        static () => ModelDb.Potion<FoulPotion>().ToMutable(),
        static () => ModelDb.Potion<FoulPotion>().ToMutable(),
        static () => ModelDb.Potion<FoulPotion>().ToMutable(),
        static () => ModelDb.Potion<FoulPotion>().ToMutable(),
        static () => ModelDb.Potion<FoulPotion>().ToMutable(),
        static () => ModelDb.Potion<FoulPotion>().ToMutable(),
        static () => ModelDb.Potion<FoulPotion>().ToMutable(),
        static () => ModelDb.Potion<FoulPotion>().ToMutable()
    ];

    [HarmonyPatch(typeof(ActModel), nameof(ActModel.CreateMap))]
    private static class ActModelCreateMapPatch
    {
        private static void Postfix(RunState runState, ref ActMap __result)
        {
            if (!AiTeammateSessionRegistry.ShouldUseTestMap(runState))
            {
                return;
            }

            __result = new AiTeammateTestActMap(runState.CurrentActIndex);
            Log.Info($"[AITeammate] Replaced generated Act {runState.CurrentActIndex + 1} map with AI teammate test map.");
        }
    }

    [HarmonyPatch(typeof(RunManager), "RollRoomTypeFor")]
    private static class RunManagerRollRoomTypeForPatch
    {
        private static bool Prefix(MapPointType pointType, ref RoomType __result)
        {
            RunState? runState = RunManager.Instance.DebugOnlyGetState();
            if (pointType != MapPointType.Unknown || !AiTeammateSessionRegistry.ShouldUseTestMap(runState))
            {
                return true;
            }

            __result = RoomType.Event;
            Log.Info("[AITeammate] Forced test-map unknown room to resolve as an Event room.");
            return false;
        }
    }

    [HarmonyPatch(typeof(RunManager), "CreateRoom")]
    private static class RunManagerCreateRoomPatch
    {
        private static bool Prefix(RoomType roomType, MapPointType mapPointType, AbstractModel? model, ref AbstractRoom __result)
        {
            RunState? runState = RunManager.Instance.DebugOnlyGetState();
            if (!AiTeammateSessionRegistry.ShouldUseTestMap(runState))
            {
                return true;
            }

            if (roomType == RoomType.Event && mapPointType == MapPointType.Unknown)
            {
                EventModel? forcedEvent = GetForcedTestMapEvent(runState?.CurrentMapCoord);
                if (forcedEvent == null)
                {
                    return true;
                }

                __result = new EventRoom((model as EventModel) ?? forcedEvent);
                Log.Info($"[AITeammate] Forced test-map event branch to create {forcedEvent.GetType().Name} at coord={runState?.CurrentMapCoord?.col},{runState?.CurrentMapCoord?.row}.");
                return false;
            }

            EncounterModel? forcedEncounter = GetForcedTestMapEncounter(roomType, runState);
            if (forcedEncounter == null)
            {
                return true;
            }

            __result = new CombatRoom((model as EncounterModel) ?? forcedEncounter, runState);
            Log.Info($"[AITeammate] Forced test-map combat room to create {forcedEncounter.GetType().Name} at coord={runState?.CurrentMapCoord?.col},{runState?.CurrentMapCoord?.row} roomType={roomType}.");
            return false;
        }
    }

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.GenerateRooms))]
    private static class RunManagerGenerateRoomsPatch
    {
        private const int TestMapStartingGold = 999;

        private static void Postfix()
        {
            RunState? runState = RunManager.Instance.DebugOnlyGetState();
            if (!AiTeammateSessionRegistry.ShouldUseTestMap(runState) || runState == null || runState.CurrentActIndex != 0)
            {
                return;
            }

            foreach (var player in runState.Players)
            {
                bool isHumanPlayer = AiTeammateSessionRegistry.Current?.HostPlayerId == player.NetId;
                player.Gold = TestMapStartingGold;
                Log.Info($"[AITeammate] Seeded test-map starting gold player={player.NetId} gold={TestMapStartingGold}");
                SeedTestMapPotions(player, isHumanPlayer);
                RemoveOverclockFromDeck(player);

                if (isHumanPlayer)
                {
                    SeedHumanTestMapDeck(player);
                }
            }

            LogTestCardLibraryInitialization();
        }
    }

    private static EventModel? GetForcedTestMapEvent(MapCoord? coord)
    {
        if (AiTeammateTestActMap.IsTabletOfTruthCoord(coord))
        {
            return ModelDb.Event<TabletOfTruth>();
        }

        if (AiTeammateTestActMap.IsAromaOfChaosCoord(coord))
        {
            return ModelDb.Event<AromaOfChaos>();
        }

        if (AiTeammateTestActMap.IsDrowningBeaconCoord(coord))
        {
            return ModelDb.Event<DrowningBeacon>();
        }

        if (AiTeammateTestActMap.IsWellspringCoord(coord))
        {
            return ModelDb.Event<Wellspring>();
        }

        if (AiTeammateTestActMap.IsFakeMerchantCoord(coord))
        {
            return ModelDb.Event<FakeMerchant>();
        }

        return null;
    }

    private static EncounterModel? GetForcedTestMapEncounter(RoomType roomType, RunState? runState)
    {
        MapCoord? coord = runState?.CurrentMapCoord;

        if (roomType == RoomType.Boss && runState?.CurrentActIndex == 2)
        {
            return ModelDb.Encounter<QueenBoss>().ToMutable();
        }

        if (roomType == RoomType.Elite && AiTeammateTestActMap.IsFirstEliteCoord(coord))
        {
            return ModelDb.Encounter<EntomancerElite>().ToMutable();
        }

        if (roomType == RoomType.Monster && AiTeammateTestActMap.IsFirstMonsterCoord(coord))
        {
            return ModelDb.Encounter<ToadpolesWeak>().ToMutable();
        }

        if (roomType == RoomType.Monster &&
            (AiTeammateTestActMap.IsSecondMonsterCoord(coord) ||
             AiTeammateTestActMap.IsThirdMonsterCoord(coord) ||
             AiTeammateTestActMap.IsFourthMonsterCoord(coord)))
        {
            return ModelDb.Encounter<ToadpolesWeak>().ToMutable();
        }

        return null;
    }

    private static void SeedTestMapPotions(MegaCrit.Sts2.Core.Entities.Players.Player player, bool overwriteWithEnergyPotions)
    {
        Func<PotionModel>[] potionFactories = overwriteWithEnergyPotions
            ? HumanTestMapPotionFactories
            : TestMapPotionFactories;

        if (player.MaxPotionCount < potionFactories.Length)
        {
            player.AddToMaxPotionCount(potionFactories.Length - player.MaxPotionCount);
        }

        if (overwriteWithEnergyPotions)
        {
            foreach (PotionModel potion in player.PotionSlots.Where(static potion => potion != null).Cast<PotionModel>().ToList())
            {
                player.DiscardPotionInternal(potion, silent: true);
            }
        }

        int added = 0;
        for (int slotIndex = 0; slotIndex < potionFactories.Length; slotIndex++)
        {
            if (player.PotionSlots[slotIndex] != null)
            {
                continue;
            }

            var result = player.AddPotionInternal(potionFactories[slotIndex](), slotIndex, silent: true);
            if (result.success)
            {
                added++;
            }
        }

        string potionSummary = string.Join(", ", player.PotionSlots.Select(static potion => potion?.Id.Entry ?? "empty"));
        Log.Info($"[AITeammate] Seeded test-map potion belt player={player.NetId} humanOverride={overwriteWithEnergyPotions} added={added} slots={player.MaxPotionCount} potions=[{potionSummary}]");
    }

    private static void RemoveOverclockFromDeck(MegaCrit.Sts2.Core.Entities.Players.Player player)
    {
        List<CardModel> overclockCards = player.Deck.Cards.Where(static card => card is Overclock).ToList();
        foreach (CardModel overclock in overclockCards)
        {
            player.Deck.RemoveInternal(overclock, silent: true);
            player.RunState.RemoveCard(overclock);
        }

        string deckSummary = string.Join(", ", player.Deck.Cards.Select(static card => card.Id.Entry));
        Log.Info($"[AITeammate] Removed test-map Overclock cards player={player.NetId} removed={overclockCards.Count} deckCount={player.Deck.Cards.Count} deck=[{deckSummary}]");
    }

    private static void SeedHumanTestMapDeck(MegaCrit.Sts2.Core.Entities.Players.Player player)
    {
        int existingCopies = player.Deck.Cards.Count(static card => card is BelieveInYou);
        int addedCopies = 0;
        for (int copyIndex = existingCopies; copyIndex < TargetHumanBelieveInYouCopies; copyIndex++)
        {
            CardModel believeInYou = ModelDb.Card<BelieveInYou>().ToMutable();
            believeInYou.FloorAddedToDeck = 1;
            player.RunState.AddCard(believeInYou, player);
            player.Deck.AddInternal(believeInYou, -1, silent: true);
            believeInYou.AfterCreated();
            addedCopies++;
        }

        string deckSummary = string.Join(", ", player.Deck.Cards.Select(static card => card.Id.Entry));
        Log.Info($"[AITeammate] Seeded human test-map deck player={player.NetId} believeInYouCopies={player.Deck.Cards.Count(static card => card is BelieveInYou)} added={addedCopies} deckCount={player.Deck.Cards.Count} deck=[{deckSummary}]");
    }

    private static void LogTestCardLibraryInitialization()
    {
        try
        {
            CardCatalogRepository repository = CardCatalogRepository.Shared;
            string overclockId = ModelDb.Card<Overclock>().Id.Entry;
            string burnId = ModelDb.Card<Burn>().Id.Entry;

            bool hasOverclockEntry = repository.TryGet(overclockId, out CardCatalogEntry? overclockEntry) && overclockEntry != null;
            bool hasBurnEntry = repository.TryGet(burnId, out CardCatalogEntry? burnEntry) && burnEntry != null;
            bool hasOverclockStatus = repository.TryGetStatus(overclockId, out CardCatalogBuildStatus overclockStatus);
            bool hasBurnStatus = repository.TryGetStatus(burnId, out CardCatalogBuildStatus burnStatus);

            Log.Info(
                $"[AITeammate] Test card library initialization completed repositoryEntries={repository.Count} " +
                $"overclockStatus={(hasOverclockStatus ? overclockStatus : CardCatalogBuildStatus.Failed)} " +
                $"overclockEntry={(hasOverclockEntry ? "present" : "missing")} " +
                $"burnStatus={(hasBurnStatus ? burnStatus : CardCatalogBuildStatus.Failed)} " +
                $"burnEntry={(hasBurnEntry ? "present" : "missing")}.");
        }
        catch (Exception exception)
        {
            Log.Warn($"[AITeammate] Test card library initialization probe failed: {exception.GetType().Name}: {exception.Message}");
            throw;
        }
    }
}

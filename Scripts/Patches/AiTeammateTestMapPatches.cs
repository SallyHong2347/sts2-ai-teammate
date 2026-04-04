using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace AITeammate.Scripts;

internal static class AiTeammateTestMapPatches
{
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
                player.Gold = TestMapStartingGold;
                Log.Info($"[AITeammate] Seeded test-map starting gold player={player.NetId} gold={TestMapStartingGold}");
                SeedTestMapPotions(player);
            }
        }
    }

    [HarmonyPatch(typeof(CombatState), nameof(CombatState.CreateCreature))]
    private static class CombatStateCreateCreaturePatch
    {
        private static void Postfix(CombatState __instance, CombatSide side, Creature __result)
        {
            RunState? runState = RunManager.Instance.DebugOnlyGetState();
            if (side != CombatSide.Enemy || __result == null || !AiTeammateSessionRegistry.ShouldUseTestMap(runState))
            {
                return;
            }

            __result.SetCurrentHpInternal(1);
            Log.Info($"[AITeammate] Clamped test-map enemy current HP creature={__result.LogName} combatId={__result.CombatId} hp=1/{__result.MaxHp}.");
        }
    }

    private static EventModel? GetForcedTestMapEvent(MapCoord? coord)
    {
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

        return null;
    }

    private static void SeedTestMapPotions(MegaCrit.Sts2.Core.Entities.Players.Player player)
    {
        if (player.MaxPotionCount < TestMapPotionFactories.Length)
        {
            player.AddToMaxPotionCount(TestMapPotionFactories.Length - player.MaxPotionCount);
        }

        int added = 0;
        for (int slotIndex = 0; slotIndex < TestMapPotionFactories.Length; slotIndex++)
        {
            if (player.PotionSlots[slotIndex] != null)
            {
                continue;
            }

            var result = player.AddPotionInternal(TestMapPotionFactories[slotIndex](), slotIndex, silent: true);
            if (result.success)
            {
                added++;
            }
        }

        string potionSummary = string.Join(", ", player.PotionSlots.Select(static potion => potion?.Id.Entry ?? "empty"));
        Log.Info($"[AITeammate] Seeded test-map potion belt player={player.NetId} added={added} slots={player.MaxPotionCount} potions=[{potionSummary}]");
    }
}

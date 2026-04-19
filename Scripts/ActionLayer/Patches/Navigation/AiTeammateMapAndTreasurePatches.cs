using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace AITeammate.Scripts;

internal static class AiTeammateMapAndTreasurePatches
{
    [HarmonyPatch(typeof(MapSelectionSynchronizer), nameof(MapSelectionSynchronizer.PlayerVotedForMapCoord))]
    private static class MapSelectionSynchronizerPatch
    {
        // 4.16: MapSelectionSynchronizer.PlayerVotedForMapCoord source parameter moved from RunLocation to MapLocation.
        private static void Postfix(Player player, MapLocation source, MapVote? destination)
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
                if (aiPlayer == null)
                {
                    continue;
                }

                // 4.16: GetPlayerVote now returns a PlayerVote object (with voteReceived/index) instead of int?.
                var playerVote = __instance.GetPlayerVote(aiPlayer);
                if (playerVote.voteReceived)
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
}

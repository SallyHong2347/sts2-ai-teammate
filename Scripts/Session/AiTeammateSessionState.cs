using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Runs;

namespace AITeammate.Scripts;

internal readonly record struct AiTeammateSessionParticipant(
    int SlotIndex,
    ulong PlayerId,
    CharacterModel Character,
    bool IsHost,
    string DisplayName);

internal sealed class AiTeammateSessionState
{
    private const ulong AiNetIdOffset = 10_000UL;
    private const int HostSlotIndex = 0;
    private const int FirstAiSlotIndex = 1;
    private const int LastAiSlotIndex = 3;

    public AiTeammateSessionState(
        ulong hostPlayerId,
        IReadOnlyList<AiTeammateSessionParticipant> participants,
        IReadOnlyDictionary<ulong, AiTeammateDummyController> aiControllers,
        bool useTestMap)
    {
        HostPlayerId = hostPlayerId;
        Participants = participants;
        AiControllers = aiControllers;
        UseTestMap = useTestMap;
    }

    public ulong HostPlayerId { get; }

    public IReadOnlyList<AiTeammateSessionParticipant> Participants { get; }

    public IReadOnlyDictionary<ulong, AiTeammateDummyController> AiControllers { get; }

    public bool UseTestMap { get; }

    public bool HasHost => Participants.Any((participant) => participant.IsHost);

    public int AiCount => Participants.Count((participant) => !participant.IsHost);

    public static AiTeammateSessionState? CreateFromSelections(IReadOnlyDictionary<int, string?> selections, bool useTestMap)
    {
        TryResolveHostPlayerId(out ulong hostPlayerId);

        if (!selections.TryGetValue(HostSlotIndex, out string? hostCharacterId) ||
            string.IsNullOrWhiteSpace(hostCharacterId) ||
            !AiTeammatePlaceholderCharacters.TryGetById(hostCharacterId, out AiTeammatePlaceholderCharacter hostCharacterOption))
        {
            return null;
        }

        List<AiTeammateSessionParticipant> participants =
        [
            new AiTeammateSessionParticipant(
                SlotIndex: HostSlotIndex,
                PlayerId: hostPlayerId,
                Character: hostCharacterOption.ResolveModel(),
                IsHost: true,
                DisplayName: "Host Player")
        ];

        Dictionary<ulong, AiTeammateDummyController> aiControllers = new();
        for (int slotIndex = FirstAiSlotIndex; slotIndex <= LastAiSlotIndex; slotIndex++)
        {
            if (!selections.TryGetValue(slotIndex, out string? aiCharacterId) ||
                string.IsNullOrWhiteSpace(aiCharacterId) ||
                !AiTeammatePlaceholderCharacters.TryGetById(aiCharacterId, out AiTeammatePlaceholderCharacter aiCharacterOption))
            {
                continue;
            }

            ulong playerId = hostPlayerId + AiNetIdOffset + (ulong)slotIndex;
            CharacterModel character = aiCharacterOption.ResolveModel();
            participants.Add(new AiTeammateSessionParticipant(
                SlotIndex: slotIndex,
                PlayerId: playerId,
                Character: character,
                IsHost: false,
                DisplayName: $"AI Player {slotIndex}"));
            aiControllers[playerId] = new AiTeammateDummyController(slotIndex, playerId, character);
        }

        return new AiTeammateSessionState(hostPlayerId, participants, aiControllers, useTestMap);
    }

    private static bool TryResolveHostPlayerId(out ulong hostPlayerId)
    {
        hostPlayerId = PlatformUtil.GetLocalPlayerId(PlatformUtil.PrimaryPlatform);
        if (hostPlayerId != 0UL)
        {
            return true;
        }

        hostPlayerId = 1UL;
        return false;
    }
}

internal static class AiTeammateSessionRegistry
{
    public static AiTeammateSessionState? Current { get; private set; }

    public static void SetCurrent(AiTeammateSessionState? session)
    {
        Current = session;
    }

    public static bool TryGetDisplayName(ulong playerId, out string displayName)
    {
        foreach (AiTeammateSessionParticipant participant in Current?.Participants ?? Array.Empty<AiTeammateSessionParticipant>())
        {
            if (participant.PlayerId == playerId)
            {
                displayName = participant.DisplayName;
                return true;
            }
        }

        displayName = string.Empty;
        return false;
    }

    public static bool TryGetParticipant(ulong playerId, out AiTeammateSessionParticipant participant)
    {
        foreach (AiTeammateSessionParticipant currentParticipant in Current?.Participants ?? Array.Empty<AiTeammateSessionParticipant>())
        {
            if (currentParticipant.PlayerId == playerId)
            {
                participant = currentParticipant;
                return true;
            }
        }

        participant = default;
        return false;
    }

    public static bool ShouldUseTestMap(RunState? runState)
    {
        return Current?.UseTestMap == true &&
               runState != null;
    }
}

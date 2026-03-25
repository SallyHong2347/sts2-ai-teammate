using MegaCrit.Sts2.Core.Models;

namespace AITeammate.Scripts;

internal sealed class AiTeammateDummyController
{
    public AiTeammateDummyController(int slotIndex, ulong playerId, CharacterModel character)
    {
        SlotIndex = slotIndex;
        PlayerId = playerId;
        Character = character;
    }

    public int SlotIndex { get; }

    public ulong PlayerId { get; }

    public CharacterModel Character { get; }
}

using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;

namespace AITeammate.Scripts;

internal readonly record struct AiTeammatePlaceholderCharacter(string Id, string DisplayName, string TexturePath)
{
    public CharacterModel ResolveModel()
    {
        return Id switch
        {
            "ironclad" => ModelDb.Character<Ironclad>(),
            "silent" => ModelDb.Character<Silent>(),
            "defect" => ModelDb.Character<Defect>(),
            "regent" => ModelDb.Character<Regent>(),
            "necrobinder" => ModelDb.Character<Necrobinder>(),
            _ => throw new InvalidOperationException($"Unknown AI teammate character id: {Id}")
        };
    }
}

internal static class AiTeammatePlaceholderCharacters
{
    private static readonly Dictionary<string, AiTeammatePlaceholderCharacter> CharactersById = new(StringComparer.Ordinal);
    public static readonly AiTeammatePlaceholderCharacter[] All =
    {
        new("ironclad", "Ironclad", "res://images/packed/character_select/char_select_ironclad.png"),
        new("silent", "Silent", "res://images/packed/character_select/char_select_silent.png"),
        new("defect", "Defect", "res://images/packed/character_select/char_select_defect.png"),
        new("regent", "Regent", "res://images/packed/character_select/char_select_regent.png"),
        new("necrobinder", "Necrobinder", "res://images/packed/character_select/char_select_necrobinder.png")
    };

    private static readonly Dictionary<string, Texture2D?> LoadedTextures = new(StringComparer.Ordinal);

    static AiTeammatePlaceholderCharacters()
    {
        foreach (AiTeammatePlaceholderCharacter character in All)
        {
            CharactersById[character.Id] = character;
        }
    }

    public static bool TryGetById(string id, out AiTeammatePlaceholderCharacter character)
    {
        return CharactersById.TryGetValue(id, out character);
    }

    public static Texture2D? LoadTexture(string texturePath)
    {
        if (!LoadedTextures.TryGetValue(texturePath, out var texture))
        {
            texture = ResourceLoader.Load<Texture2D>(texturePath);
            LoadedTextures[texturePath] = texture;
        }

        return texture;
    }
}

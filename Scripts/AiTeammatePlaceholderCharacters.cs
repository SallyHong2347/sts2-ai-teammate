using System;
using System.Collections.Generic;
using Godot;

namespace AITeammate.Scripts;

internal readonly record struct AiTeammatePlaceholderCharacter(string Id, string DisplayName, string TexturePath);

internal static class AiTeammatePlaceholderCharacters
{
    public static readonly AiTeammatePlaceholderCharacter[] All =
    {
        new("ironclad", "Ironclad", "res://images/packed/character_select/char_select_ironclad.png"),
        new("silent", "Silent", "res://images/packed/character_select/char_select_silent.png"),
        new("defect", "Defect", "res://images/packed/character_select/char_select_defect.png"),
        new("regent", "Regent", "res://images/packed/character_select/char_select_regent.png"),
        new("necrobinder", "Necrobinder", "res://images/packed/character_select/char_select_necrobinder.png")
    };

    private static readonly Dictionary<string, Texture2D?> LoadedTextures = new(StringComparer.Ordinal);

    public static bool TryGetById(string id, out AiTeammatePlaceholderCharacter character)
    {
        foreach (var option in All)
        {
            if (string.Equals(option.Id, id, StringComparison.Ordinal))
            {
                character = option;
                return true;
            }
        }

        character = default;
        return false;
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

using System;
using System.Threading.Tasks;

namespace AITeammate.Scripts;

internal enum AiTeammateActionKind
{
    PlayCard,
    UsePotion,
    EndTurn,
    ChooseMapNode,
    ChooseEventOption,
    ChooseRestSiteOption,
    ClaimReward,
}

internal sealed class AiTeammateAvailableAction
{
    public AiTeammateAvailableAction(
        AiTeammateActionKind kind,
        string label,
        Func<Task> executeAsync,
        string? deduplicationKey = null)
    {
        Kind = kind;
        Label = label;
        ExecuteAsync = executeAsync;
        DeduplicationKey = deduplicationKey;
    }

    public AiTeammateActionKind Kind { get; }

    public string Label { get; }

    public Func<Task> ExecuteAsync { get; }

    public string? DeduplicationKey { get; }
}

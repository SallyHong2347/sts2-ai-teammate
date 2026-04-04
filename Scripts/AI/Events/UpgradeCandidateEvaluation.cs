using System.Collections.Generic;
using MegaCrit.Sts2.Core.Models;

namespace AITeammate.Scripts;

internal sealed class UpgradeCandidateEvaluation
{
    public required string CardId { get; init; }

    public required string Name { get; init; }

    public required double Score { get; init; }

    public required CardModel RuntimeCard { get; init; }

    public IReadOnlyList<string> Reasons { get; init; } = [];

    public string Describe()
    {
        return $"card={CardId} name={Name} score={Score:F1} reasons=[{string.Join("; ", Reasons)}]";
    }
}

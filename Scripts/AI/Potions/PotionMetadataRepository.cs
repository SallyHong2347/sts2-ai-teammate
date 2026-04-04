using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Models;

namespace AITeammate.Scripts;

internal sealed class PotionMetadataRepository
{
    private static readonly Lazy<PotionMetadataRepository> SharedInstance = new(static () => new PotionMetadataBuilder().Build());

    private readonly Dictionary<string, PotionMetadata> _entries = new(StringComparer.Ordinal);

    public static PotionMetadataRepository Shared => SharedInstance.Value;

    public PotionMetadataBuildReport BuildReport { get; private set; } = new();

    public IEnumerable<PotionMetadata> All => _entries.Values;

    public void Upsert(PotionMetadata metadata)
    {
        _entries[metadata.PotionId] = metadata;
    }

    public void SetReport(PotionMetadataBuildReport report)
    {
        BuildReport = report;
    }

    public bool TryGet(string potionId, out PotionMetadata? metadata)
    {
        return _entries.TryGetValue(potionId, out metadata);
    }

    public bool TryGet(PotionModel potion, out PotionMetadata? metadata)
    {
        metadata = null;
        string? id = potion?.Id.Entry;
        return !string.IsNullOrWhiteSpace(id) && _entries.TryGetValue(id, out metadata);
    }
}

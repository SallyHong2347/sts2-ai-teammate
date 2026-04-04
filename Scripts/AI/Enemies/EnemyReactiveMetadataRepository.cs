using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Models;

namespace AITeammate.Scripts;

internal sealed class EnemyReactiveMetadataRepository
{
    private static readonly Lazy<EnemyReactiveMetadataRepository> SharedInstance = new(static () => new EnemyReactiveMetadataBuilder().Build());

    private readonly Dictionary<string, EnemyReactiveMetadata> _entries = new(StringComparer.Ordinal);

    public static EnemyReactiveMetadataRepository Shared => SharedInstance.Value;

    public EnemyReactiveMetadataBuildReport BuildReport { get; private set; } = new();

    public IEnumerable<EnemyReactiveMetadata> All => _entries.Values;

    public void Upsert(EnemyReactiveMetadata metadata)
    {
        _entries[metadata.PowerId] = metadata;
    }

    public void SetReport(EnemyReactiveMetadataBuildReport report)
    {
        BuildReport = report;
    }

    public bool TryGet(string powerId, out EnemyReactiveMetadata? metadata)
    {
        return _entries.TryGetValue(powerId, out metadata);
    }

    public bool TryGet(PowerModel power, out EnemyReactiveMetadata? metadata)
    {
        metadata = null;
        string? id = power?.Id.Entry;
        return !string.IsNullOrWhiteSpace(id) && _entries.TryGetValue(id, out metadata);
    }
}

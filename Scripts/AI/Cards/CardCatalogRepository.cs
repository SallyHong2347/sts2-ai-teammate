using System;
using System.Collections.Generic;

namespace AITeammate.Scripts;

internal sealed class CardCatalogRepository
{
    private static readonly Lazy<CardCatalogRepository> SharedCatalog = new(() => new CardCatalogBuilder().Build());

    private readonly Dictionary<string, CardCatalogEntry> _entries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CardCatalogBuildStatus> _statuses = new(StringComparer.Ordinal);

    public static CardCatalogRepository Shared => SharedCatalog.Value;

    public int Count => _entries.Count;

    public bool TryGet(string cardId, out CardCatalogEntry? entry)
    {
        return _entries.TryGetValue(cardId, out entry);
    }

    public void Upsert(CardCatalogEntry entry)
    {
        _entries[entry.CardId] = entry;
        _statuses[entry.CardId] = entry.BuildStatus;
    }

    public void MarkFailed(string cardId)
    {
        _statuses[cardId] = CardCatalogBuildStatus.Failed;
    }

    public bool TryGetStatus(string cardId, out CardCatalogBuildStatus status)
    {
        return _statuses.TryGetValue(cardId, out status);
    }
}

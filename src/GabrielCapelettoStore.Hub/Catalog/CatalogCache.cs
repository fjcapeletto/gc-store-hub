using System;

namespace GabrielCapelettoStore.Hub.Catalog;

/// <summary>The last catalog that was fetched successfully, plus when. Lets the hub open
/// offline (or when the backend is down) showing the last-known shelves instead of an error.</summary>
public sealed class CachedCatalog
{
    public CatalogManifest Manifest { get; set; } = new();

    public DateTimeOffset SyncedAt { get; set; }
}

/// <summary>Persists and retrieves the last-good catalog for offline-first rendering.</summary>
public interface ICatalogCache
{
    CachedCatalog? Load();

    void Save(CatalogManifest manifest, DateTimeOffset syncedAt);
}

namespace GabrielCapelettoStore.Hub.Catalog;

/// <summary>
/// Persists and retrieves the catalog-observation state (freshness timestamps and
/// the banner-dismissed marker).
/// </summary>
public interface ICatalogStateStore
{
    /// <summary>Returns the stored state, or null on a first-ever run (no baseline yet).</summary>
    CatalogObservationState? Load();

    void Save(CatalogObservationState state);
}

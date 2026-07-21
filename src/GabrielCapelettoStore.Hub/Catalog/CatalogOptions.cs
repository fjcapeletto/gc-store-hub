namespace GabrielCapelettoStore.Hub.Catalog;

/// <summary>
/// Configuration for where the hub fetches its catalog from. Bound from the
/// "Catalog" section of appsettings.json, overridable via environment variables
/// (prefix GCSTORE_, e.g. GCSTORE_Catalog__Url) for prod / secret-manager use.
/// </summary>
public sealed class CatalogOptions
{
    public const string SectionName = "Catalog";

    /// <summary>Absolute URL of the catalog document. Placeholder in the committed config.</summary>
    public string? Url { get; set; }
}

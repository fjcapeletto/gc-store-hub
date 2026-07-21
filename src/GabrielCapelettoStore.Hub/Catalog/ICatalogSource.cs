using System.Threading;
using System.Threading.Tasks;

namespace GabrielCapelettoStore.Hub.Catalog;

/// <summary>
/// The seam between the hub UI and wherever the catalog comes from. Today this is
/// a static file over HTTP; swapping to the real backend means providing a
/// different implementation (or just a different URL) — nothing in the UI changes.
/// </summary>
public interface ICatalogSource
{
    Task<CatalogManifest> GetCatalogAsync(CancellationToken cancellationToken = default);
}

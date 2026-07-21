using System.Threading;
using System.Threading.Tasks;

namespace GabrielCapelettoStore.Hub.Catalog;

/// <summary>
/// A catalog source with baked-in sample data, used only by the XAML previewer
/// (via the parameterless design-time view-model constructor). Never wired at runtime.
/// </summary>
public sealed class DesignCatalogSource : ICatalogSource
{
    public static readonly CatalogManifest SampleCatalog = new()
    {
        CatalogVersion = 1,
        Apps =
        [
            new CatalogApp
            {
                Id = "com.gc.weather",
                Name = "Weather",
                Version = "1.0.0",
                Summary = "Local forecast at a glance.",
                IdentityMode = AppIdentityMode.DeviceOnly,
            },
            new CatalogApp
            {
                Id = "com.gc.notes",
                Name = "Notes",
                Version = "0.9.2",
                Summary = "Quick notes that sync to your account.",
                IdentityMode = AppIdentityMode.AccountRequired,
            },
        ],
    };

    public Task<CatalogManifest> GetCatalogAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(SampleCatalog);
}

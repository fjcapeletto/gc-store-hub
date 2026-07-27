using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GabrielCapelettoStore.Hub.Catalog;

/// <summary>
/// Dev-only catalog with enough apps to exercise the paged 3×3 shelf (dots / arrows / wheel).
/// Enabled via the <c>GCSTORE_MockCatalog=1</c> environment variable; never used in production.
/// </summary>
public sealed class MockCatalogSource : ICatalogSource
{
    private const int AppCount = 14;
    private static readonly string[] Glyphs = ["cloud", "cpu", "database", "photo", "music", "calendar", "notes"];

    public Task<CatalogManifest> GetCatalogAsync(CancellationToken cancellationToken = default)
    {
        var apps = new List<CatalogApp>(AppCount);
        for (var i = 1; i <= AppCount; i++)
        {
            apps.Add(new CatalogApp
            {
                Id = $"com.gc.mock{i:00}",
                Name = $"Mock App {i}",
                Version = "1.0.0",
                Summary = "Sample app for paging",
                Icon = Glyphs[i % Glyphs.Length],
                // Every 5th app is a developer-stage app, to exercise the DEV badge.
                Stage = i % 5 == 0 ? "developer" : "released",
            });
        }

        return Task.FromResult(new CatalogManifest
        {
            CatalogVersion = 99,
            Access = new CatalogAccess { State = StoreAccessState.Granted },
            Apps = apps,
        });
    }
}

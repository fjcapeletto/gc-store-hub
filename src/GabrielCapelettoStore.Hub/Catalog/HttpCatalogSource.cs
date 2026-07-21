using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace GabrielCapelettoStore.Hub.Catalog;

/// <summary>
/// Fetches the catalog over HTTP from a configured URL. This is the ONLY place
/// that knows the catalog arrives as JSON over HTTP — the rest of the app talks
/// to <see cref="ICatalogSource"/>. To point at the real backend, change the
/// configured URL (and later add auth headers); this class barely changes.
/// </summary>
public sealed class HttpCatalogSource : ICatalogSource
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower) },
    };

    private readonly HttpClient _http;
    private readonly Uri _catalogUri;

    public HttpCatalogSource(HttpClient http, CatalogOptions options)
    {
        _http = http;

        if (string.IsNullOrWhiteSpace(options.Url))
        {
            throw new InvalidOperationException(
                "Catalog:Url is not configured. Set it in appsettings.json or via GCSTORE_Catalog__Url.");
        }

        if (!Uri.TryCreate(options.Url, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException($"Catalog:Url is not a valid absolute URL: '{options.Url}'.");
        }

        _catalogUri = uri;
    }

    public async Task<CatalogManifest> GetCatalogAsync(CancellationToken cancellationToken = default)
    {
        var manifest = await _http
            .GetFromJsonAsync<CatalogManifest>(_catalogUri, JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        // An empty document deserializes to a manifest with no apps → empty shelves.
        return manifest ?? new CatalogManifest();
    }
}

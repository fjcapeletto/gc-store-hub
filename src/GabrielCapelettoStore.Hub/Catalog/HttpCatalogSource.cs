using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using GabrielCapelettoStore.Hub.Entitlement;

namespace GabrielCapelettoStore.Hub.Catalog;

/// <summary>
/// Fetches the catalog over HTTP from a configured URL. This is the ONLY place that knows the catalog
/// arrives as JSON over HTTP — the rest of the app talks to <see cref="ICatalogSource"/>.
///
/// The request carries the device identity (the same <c>{ claim, capabilities }</c> envelope as
/// authorize/delivery/web), so the response can vary per device: a developer-marked device sees the
/// released apps plus the unlocked "developer"-stage apps; everyone else sees released only. Identity
/// goes in the POST body (never a querystring), and a per-device response must not be publicly cached —
/// both reasons the catalog is a POST. See contract/developer-stage.md.
/// </summary>
public sealed class HttpCatalogSource : ICatalogSource
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower) },
    };

    private readonly HttpClient _http;
    private readonly DeviceIdentity _device;
    private readonly Uri _catalogUri;

    public HttpCatalogSource(HttpClient http, CatalogOptions options, DeviceIdentity device)
    {
        _http = http;
        _device = device;

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
        var body = new AuthorizeRequest
        {
            Claim = ClaimFactory.Claim(_device),
            Capabilities = ClaimFactory.Capabilities(),
        };

        using var response = await _http
            .PostAsJsonAsync(_catalogUri, body, JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var manifest = await response.Content
            .ReadFromJsonAsync<CatalogManifest>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        // An empty document deserializes to a manifest with no apps → empty shelves.
        return manifest ?? new CatalogManifest();
    }
}

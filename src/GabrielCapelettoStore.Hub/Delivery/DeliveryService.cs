using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GabrielCapelettoStore.Hub.Entitlement;

namespace GabrielCapelettoStore.Hub.Delivery;

/// <summary>Requests an app package from the server (POST /v1/delivery/{appId}).</summary>
public interface IDeliveryService
{
    bool IsConfigured { get; }
    Task<DeliveryOutcome> RequestAsync(string appId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Talks to `POST /v1/delivery/{appId}` with the same `{ claim, capabilities }` as authorize; the
/// server re-verifies entitlement. Normalizes: 200 → package pointer; 402/403 → denied+offer;
/// 404 → no package published; anything else / network → unreachable.
/// </summary>
public sealed class HttpDeliveryService : IDeliveryService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly DeviceIdentity _device;
    private readonly Uri? _baseUri;

    public HttpDeliveryService(HttpClient http, EntitlementOptions options, DeviceIdentity device)
    {
        _http = http;
        _device = device;
        if (!string.IsNullOrWhiteSpace(options.BaseUrl)
            && Uri.TryCreate(options.BaseUrl!.TrimEnd('/') + "/", UriKind.Absolute, out var uri))
        {
            _baseUri = uri;
        }
    }

    public bool IsConfigured => _baseUri is not null;

    public async Task<DeliveryOutcome> RequestAsync(string appId, CancellationToken cancellationToken = default)
    {
        if (_baseUri is null)
        {
            return DeliveryOutcome.Offline("No delivery endpoint configured.");
        }

        var body = new AuthorizeRequest { Claim = ClaimFactory.Claim(_device), Capabilities = ClaimFactory.Capabilities() };
        var uri = new Uri(_baseUri, $"v1/delivery/{Uri.EscapeDataString(appId)}");

        try
        {
            using var response = await _http.PostAsJsonAsync(uri, body, Json, cancellationToken).ConfigureAwait(false);
            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                {
                    var delivery = await response.Content.ReadFromJsonAsync<DeliveryResponse>(Json, cancellationToken)
                        .ConfigureAwait(false);
                    return delivery is not null
                        ? DeliveryOutcome.Ready(delivery)
                        : DeliveryOutcome.Offline("Empty delivery response.");
                }

                case HttpStatusCode.PaymentRequired:
                case HttpStatusCode.Forbidden:
                {
                    var denied = await ReadOrNull<AuthorizeDenied>(response, cancellationToken).ConfigureAwait(false);
                    return DeliveryOutcome.DeniedOut(denied?.Offer, denied?.Reason);
                }

                case HttpStatusCode.NotFound:
                    return DeliveryOutcome.None("No package published for this app yet.");

                default:
                    return DeliveryOutcome.Offline($"Delivery returned HTTP {(int)response.StatusCode}.");
            }
        }
        catch (Exception ex)
        {
            return DeliveryOutcome.Offline(ex.Message);
        }
    }

    private static async Task<T?> ReadOrNull<T>(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(Json, ct).ConfigureAwait(false);
        }
        catch
        {
            return default;
        }
    }
}

public sealed class NullDeliveryService : IDeliveryService
{
    public bool IsConfigured => false;
    public Task<DeliveryOutcome> RequestAsync(string appId, CancellationToken cancellationToken = default)
        => Task.FromResult(DeliveryOutcome.Offline("No delivery endpoint configured."));
}

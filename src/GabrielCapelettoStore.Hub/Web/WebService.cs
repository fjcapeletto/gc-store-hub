using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GabrielCapelettoStore.Hub.Entitlement;

namespace GabrielCapelettoStore.Hub.Web;

/// <summary>Polls a `type: web` app's link-list (POST /v1/delivery/{appId}), gated server-side.</summary>
public interface IWebService
{
    bool IsConfigured { get; }
    Task<WebPollOutcome> PollAsync(string appId, CancellationToken cancellationToken = default);
}

public sealed class HttpWebService : IWebService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly DeviceIdentity _device;
    private readonly Uri? _baseUri;

    public HttpWebService(HttpClient http, EntitlementOptions options, DeviceIdentity device)
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

    public async Task<WebPollOutcome> PollAsync(string appId, CancellationToken cancellationToken = default)
    {
        if (_baseUri is null)
        {
            return WebPollOutcome.Offline();
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
                    var poll = await response.Content.ReadFromJsonAsync<WebPollResponse>(Json, cancellationToken)
                        .ConfigureAwait(false);
                    return WebPollOutcome.Delivered(poll?.Items ?? []);
                }

                case HttpStatusCode.PaymentRequired:
                case HttpStatusCode.Forbidden:
                {
                    AuthorizeDenied? denied = null;
                    try { denied = await response.Content.ReadFromJsonAsync<AuthorizeDenied>(Json, cancellationToken).ConfigureAwait(false); }
                    catch { /* body may be absent */ }
                    return WebPollOutcome.RevokedOut(denied?.Offer, denied?.Reason);
                }

                default:
                    return WebPollOutcome.Offline();
            }
        }
        catch
        {
            return WebPollOutcome.Offline();
        }
    }
}

public sealed class NullWebService : IWebService
{
    public bool IsConfigured => false;
    public Task<WebPollOutcome> PollAsync(string appId, CancellationToken cancellationToken = default)
        => Task.FromResult(WebPollOutcome.Offline());
}

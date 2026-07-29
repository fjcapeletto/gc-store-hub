using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GabrielCapelettoStore.Hub.Entitlement;

namespace GabrielCapelettoStore.Hub.Api;

/// <summary>Mints a scoped API token for a `type: api` app via POST /v1/delivery/{appId} (the usual
/// { claim, capabilities } envelope — same gate as every other type). See contract/type-api.draft.md.</summary>
public interface IApiTokenService
{
    bool IsConfigured { get; }
    Task<ApiTokenOutcome> FetchAsync(string appId, CancellationToken cancellationToken = default);
}

public sealed class HttpApiTokenService : IApiTokenService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly DeviceIdentity _device;
    private readonly Uri? _baseUri;

    public HttpApiTokenService(HttpClient http, EntitlementOptions options, DeviceIdentity device)
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

    public async Task<ApiTokenOutcome> FetchAsync(string appId, CancellationToken cancellationToken = default)
    {
        if (_baseUri is null)
        {
            return ApiTokenOutcome.Offline();
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
                    var dto = await response.Content.ReadFromJsonAsync<ApiDeliveryResponse>(Json, cancellationToken)
                        .ConfigureAwait(false);
                    if (dto?.ApiBaseUrl is null || dto.Token is null)
                    {
                        return ApiTokenOutcome.Offline(); // malformed — treat as transient
                    }

                    return ApiTokenOutcome.Granted(new ApiToken
                    {
                        AppId = dto.AppId ?? appId,
                        ApiBaseUrl = dto.ApiBaseUrl,
                        Token = dto.Token,
                        ExpiresAt = ParseExpiry(dto.ExpiresAt),
                    });
                }

                case HttpStatusCode.PaymentRequired:
                case HttpStatusCode.Forbidden:
                case HttpStatusCode.NotFound: // 404 unknown-app (dev-stage / not for this device) — no offer
                {
                    AuthorizeDenied? denied = null;
                    try { denied = await response.Content.ReadFromJsonAsync<AuthorizeDenied>(Json, cancellationToken).ConfigureAwait(false); }
                    catch { /* body may be absent */ }
                    return ApiTokenOutcome.DeniedOut(denied?.Offer, denied?.Reason);
                }

                default:
                    return ApiTokenOutcome.Offline();
            }
        }
        catch
        {
            return ApiTokenOutcome.Offline();
        }
    }

    /// <summary>Unparseable/absent expiry ⇒ treat as already stale so the broker re-fetches promptly.</summary>
    private static DateTimeOffset ParseExpiry(string? s)
        => DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var t)
            ? t
            : DateTimeOffset.UtcNow;
}

public sealed class NullApiTokenService : IApiTokenService
{
    public bool IsConfigured => false;
    public Task<ApiTokenOutcome> FetchAsync(string appId, CancellationToken cancellationToken = default)
        => Task.FromResult(ApiTokenOutcome.Offline());
}

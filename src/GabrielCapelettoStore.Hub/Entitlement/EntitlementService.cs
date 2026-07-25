using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GabrielCapelettoStore.Hub.Entitlement;

/// <summary>Talks to the entitlement server: liveness, authorization, and access requests.</summary>
public interface IEntitlementService
{
    /// <summary>Whether a server endpoint is configured. If not, the hub runs catalog-only.</summary>
    bool IsConfigured { get; }

    /// <summary>This device's hub-generated id (surfaced so it can be provisioned server-side).</summary>
    string DeviceId { get; }

    /// <summary>Whether the device holds a license to present (baked or user-entered).</summary>
    bool HasLicense { get; }

    /// <summary>Whether an access request was already submitted (drives the "requested" UX).</summary>
    bool AccessRequested { get; }

    /// <summary>Authorize this device (POST /v1/authorize). Works with or without a license — a
    /// license-less pending device reaches the server to receive access-pending. It is also the
    /// heartbeat (the one pull to the one server).</summary>
    Task<AuthorizeOutcome> AuthorizeAsync(CancellationToken cancellationToken = default);

    /// <summary>Submit an access request (POST /v1/access-request) for a machine with no license.</summary>
    Task<AccessRequestOutcome> RequestAccessAsync(string name, string email, string? phone, CancellationToken cancellationToken = default);

    /// <summary>Persist a license the user typed on the licensing screen.</summary>
    void SetLicense(string license);
}

public sealed class HttpEntitlementService : IEntitlementService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly DeviceIdentity _device;
    private readonly Uri? _baseUri;

    public HttpEntitlementService(HttpClient http, EntitlementOptions options, DeviceIdentity device)
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
    public string DeviceId => _device.DeviceId;
    public bool HasLicense => _device.HasLicense;
    public bool AccessRequested => _device.AccessRequested;

    public void SetLicense(string license) => _device.SetLicense(license);

    public async Task<AuthorizeOutcome> AuthorizeAsync(CancellationToken cancellationToken = default)
    {
        if (_baseUri is null)
        {
            return AuthorizeOutcome.Offline("No entitlement endpoint configured.");
        }

        var request = new AuthorizeRequest
        {
            Claim = new DeviceClaim
            {
                DeviceId = _device.DeviceId,
                License = _device.License, // optional — omitted when null; a pending device still authorizes
                Fingerprint = _device.FingerprintClaim(),
            },
            Capabilities = new Capabilities
            {
                Schemes = ["fingerprint-wrap-v1"],
                CanSealTpm = false,
                HasTpmEk = false,
                TrustedKeyIds = TrustAnchors.KeyIds,
            },
        };

        try
        {
            using var response = await _http.PostAsJsonAsync(new Uri(_baseUri, "v1/authorize"), request, Json, cancellationToken)
                .ConfigureAwait(false);

            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                {
                    var jws = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    var lease = LeaseVerifier.Verify(jws);
                    return lease is not null
                        ? AuthorizeOutcome.Granted(lease)
                        : AuthorizeOutcome.Offline("Lease failed signature verification.");
                }

                case HttpStatusCode.PaymentRequired:
                case HttpStatusCode.Forbidden:
                {
                    var denied = await ReadJsonAsync<AuthorizeDenied>(response, cancellationToken).ConfigureAwait(false);
                    return AuthorizeOutcome.LockedOut(denied?.Offer, denied?.Reason);
                }

                case HttpStatusCode.Conflict:
                    return AuthorizeOutcome.NeedsUpdate("Server trusts no key this hub build carries.");

                default:
                    return AuthorizeOutcome.Offline($"Authorize returned HTTP {(int)response.StatusCode}.");
            }
        }
        catch (Exception ex)
        {
            return AuthorizeOutcome.Offline(ex.Message);
        }
    }

    public async Task<AccessRequestOutcome> RequestAccessAsync(string name, string email, string? phone, CancellationToken cancellationToken = default)
    {
        if (_baseUri is null)
        {
            return AccessRequestOutcome.Fail("No entitlement endpoint configured.");
        }

        var body = new AccessRequest
        {
            DeviceId = _device.DeviceId,
            Name = name.Trim(),
            Email = email.Trim(),
            Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim(),
            Fingerprint = _device.FingerprintClaim(),
        };

        try
        {
            using var response = await _http.PostAsJsonAsync(new Uri(_baseUri, "v1/access-request"), body, Json, cancellationToken)
                .ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                _device.MarkRequested(name, email);
                return AccessRequestOutcome.Ok();
            }

            return AccessRequestOutcome.Fail($"Server returned HTTP {(int)response.StatusCode}.");
        }
        catch (Exception ex)
        {
            return AccessRequestOutcome.Fail(ex.Message);
        }
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken ct)
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

/// <summary>No endpoint configured: used at design time and when the hub runs catalog-only.</summary>
public sealed class NullEntitlementService : IEntitlementService
{
    public bool IsConfigured => false;
    public string DeviceId => "";
    public bool HasLicense => false;
    public bool AccessRequested => false;
    public Task<AuthorizeOutcome> AuthorizeAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(AuthorizeOutcome.Offline("No entitlement endpoint configured."));
    public Task<AccessRequestOutcome> RequestAccessAsync(string name, string email, string? phone, CancellationToken cancellationToken = default)
        => Task.FromResult(AccessRequestOutcome.Fail("No entitlement endpoint configured."));
    public void SetLicense(string license) { }
}

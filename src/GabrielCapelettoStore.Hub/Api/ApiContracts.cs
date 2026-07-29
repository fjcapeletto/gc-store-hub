using System;
using GabrielCapelettoStore.Hub.Catalog;

namespace GabrielCapelettoStore.Hub.Api;

/// <summary>Wire shape of POST /v1/delivery/{appId} for a `type: api` app. See contract/type-api.draft.md.</summary>
public sealed record ApiDeliveryResponse
{
    public string? AppId { get; init; }
    public string? Type { get; init; }
    public string? ApiBaseUrl { get; init; }
    public string? Token { get; init; }
    public string? ExpiresAt { get; init; }
}

/// <summary>
/// A minted, short-lived scoped API token for an app to call its OWN backend. Opaque to the hub — the
/// hub never verifies it (the app's backend does, against gcstore's Ed25519 public key). The hub only
/// brokers it and keeps the device license to itself.
/// </summary>
public sealed record ApiToken
{
    public required string AppId { get; init; }
    public required string ApiBaseUrl { get; init; }
    public required string Token { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
}

public enum ApiTokenStatus
{
    Granted,      // 200 — a scoped token
    Denied,       // 402/403 revoked/ungranted (offer) or 404 unknown-app (dev-stage / not for this device)
    Unreachable,  // network / other
}

public sealed record ApiTokenOutcome
{
    public required ApiTokenStatus Status { get; init; }
    public ApiToken? Token { get; init; }
    public CatalogOffer? Offer { get; init; }
    public string? Reason { get; init; }

    public static ApiTokenOutcome Granted(ApiToken token) => new() { Status = ApiTokenStatus.Granted, Token = token };
    public static ApiTokenOutcome DeniedOut(CatalogOffer? offer, string? reason) =>
        new() { Status = ApiTokenStatus.Denied, Offer = offer, Reason = reason };
    public static ApiTokenOutcome Offline() => new() { Status = ApiTokenStatus.Unreachable };
}

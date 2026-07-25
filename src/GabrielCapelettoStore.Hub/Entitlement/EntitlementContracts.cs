using System.Collections.Generic;
using System.Text.Json.Serialization;
using GabrielCapelettoStore.Hub.Catalog;

namespace GabrielCapelettoStore.Hub.Entitlement;

// Wire shapes for POST /v1/authorize, per contract/device-authorization.md + .schema.json.
// camelCase over the wire (configured on the serializer).

/// <summary>The device claim: hub-generated id + optional license + advisory fingerprint + optional TPM EK.</summary>
public sealed record DeviceClaim
{
    public required string DeviceId { get; init; }

    /// <summary>Optional — a license-less device with a pending request still authorizes (→ access-pending).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? License { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, string>? Fingerprint { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Attestation? Attestation { get; init; }
}

/// <summary>Body of POST /v1/access-request — a machine with no license asking for store access.</summary>
public sealed record AccessRequest
{
    public required string DeviceId { get; init; }
    public required string Name { get; init; }
    public required string Email { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Phone { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, string>? Fingerprint { get; init; }
}

/// <summary>Result of submitting an access request.</summary>
public sealed record AccessRequestOutcome
{
    public required bool Submitted { get; init; }
    public string? Detail { get; init; }

    public static AccessRequestOutcome Ok() => new() { Submitted = true };
    public static AccessRequestOutcome Fail(string? detail) => new() { Submitted = false, Detail = detail };
}

public sealed record Attestation
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TpmEk { get; init; }
}

/// <summary>What this device's hub can do — lets the server pick a scheme/key and fail closed.</summary>
public sealed record Capabilities
{
    public required IReadOnlyList<string> Schemes { get; init; }
    public bool CanSealTpm { get; init; }
    public bool HasTpmEk { get; init; }
    public required IReadOnlyList<string> TrustedKeyIds { get; init; }
}

/// <summary>Body of POST /v1/authorize (also the renewal call).</summary>
public sealed record AuthorizeRequest
{
    public required DeviceClaim Claim { get; init; }
    public required Capabilities Capabilities { get; init; }
}

/// <summary>Decoded claims of the JWS lease (the wire form is a JWS compact string).</summary>
public sealed record LeasePayload
{
    [JsonPropertyName("sub")] public required string Sub { get; init; }
    [JsonPropertyName("iat")] public long Iat { get; init; }
    [JsonPropertyName("exp")] public long? Exp { get; init; }
    [JsonPropertyName("tier")] public string? Tier { get; init; }
    [JsonPropertyName("entitlements")] public IReadOnlyList<LeaseEntitlement> Entitlements { get; init; } = [];
}

public sealed record LeaseEntitlement
{
    [JsonPropertyName("appId")] public required string AppId { get; init; }
    [JsonPropertyName("state")] public required string State { get; init; }
    [JsonPropertyName("notAfter")] public long? NotAfter { get; init; }
}

/// <summary>403/402 body: no store access, carries the catalog offer + a reason.</summary>
public sealed record AuthorizeDenied
{
    public CatalogOffer? Offer { get; init; }
    public string? Reason { get; init; }
}

/// <summary>The outcome of an authorize call, normalized for the UI layer.</summary>
public enum AuthorizeStatus
{
    Granted,      // 200 + a valid, verified lease
    Locked,       // 402/403 — no access; render the offer
    MustUpdate,   // 409 — server retired every key this build trusts; hub must update
    Unreachable,  // network/TLS error or an unverifiable/garbled response
}

public sealed record AuthorizeOutcome
{
    public required AuthorizeStatus Status { get; init; }
    public LeasePayload? Lease { get; init; }          // Granted
    public CatalogOffer? Offer { get; init; }          // Locked
    public string? Reason { get; init; }               // Locked
    public string? Detail { get; init; }               // Unreachable/MustUpdate diagnostics

    public static AuthorizeOutcome Granted(LeasePayload lease) => new() { Status = AuthorizeStatus.Granted, Lease = lease };
    public static AuthorizeOutcome LockedOut(CatalogOffer? offer, string? reason) =>
        new() { Status = AuthorizeStatus.Locked, Offer = offer, Reason = reason };
    public static AuthorizeOutcome NeedsUpdate(string? detail) => new() { Status = AuthorizeStatus.MustUpdate, Detail = detail };
    public static AuthorizeOutcome Offline(string? detail) => new() { Status = AuthorizeStatus.Unreachable, Detail = detail };
}

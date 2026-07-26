using GabrielCapelettoStore.Hub.Catalog;

namespace GabrielCapelettoStore.Hub.Delivery;

// Wire shapes for POST /v1/delivery/{appId} (clear phase — no keyEnvelope). Matches
// contract/device-authorization.schema.json (deliveryResponse). camelCase over the wire.

public sealed record DeliveryResponse
{
    public required string AppId { get; init; }
    public required string Version { get; init; }
    public required PackageInfo Package { get; init; }
}

public sealed record PackageInfo
{
    /// <summary>Short-lived nginx-signed URL to the (clear-phase) zip. Download directly.</summary>
    public required string Url { get; init; }
    public string? ExpiresAt { get; init; }
    public long? SizeBytes { get; init; }
    public required PackageHash Hash { get; init; }
}

public sealed record PackageHash
{
    public required string Alg { get; init; }   // "sha-256"
    public required string Value { get; init; } // hex
}

/// <summary>The outcome of a delivery request, normalized for the install layer.</summary>
public enum DeliveryStatus
{
    Ready,        // 200 — a package to install
    Denied,       // 402/403 — ungranted/revoked/expired; carries an offer
    NoPackage,    // 404 — the app has no published package yet
    Unreachable,  // network/other error
}

public sealed record DeliveryOutcome
{
    public required DeliveryStatus Status { get; init; }
    public DeliveryResponse? Response { get; init; }  // Ready
    public CatalogOffer? Offer { get; init; }         // Denied
    public string? Reason { get; init; }              // Denied
    public string? Detail { get; init; }              // NoPackage / Unreachable

    public static DeliveryOutcome Ready(DeliveryResponse r) => new() { Status = DeliveryStatus.Ready, Response = r };
    public static DeliveryOutcome DeniedOut(CatalogOffer? offer, string? reason) =>
        new() { Status = DeliveryStatus.Denied, Offer = offer, Reason = reason };
    public static DeliveryOutcome None(string? detail) => new() { Status = DeliveryStatus.NoPackage, Detail = detail };
    public static DeliveryOutcome Offline(string? detail) => new() { Status = DeliveryStatus.Unreachable, Detail = detail };
}

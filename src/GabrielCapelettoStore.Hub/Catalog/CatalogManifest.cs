using System.Collections.Generic;

namespace GabrielCapelettoStore.Hub.Catalog;

/// <summary>
/// The store catalog as published by the backend. This is the wire contract the
/// hub fetches; it is deliberately transport-agnostic so the same shape works
/// whether it comes from a static file today or the real catalog API later.
/// </summary>
public sealed record CatalogManifest
{
    /// <summary>Monotonic version of the catalog payload; lets the hub detect changes.</summary>
    public int CatalogVersion { get; init; }

    public IReadOnlyList<CatalogApp> Apps { get; init; } = [];
}

/// <summary>A single publishable app entry on the store shelves.</summary>
public sealed record CatalogApp
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Version { get; init; }
    public string? Summary { get; init; }

    /// <summary>Which identity the app declares it needs. Anchors the three archetypes.</summary>
    public AppIdentityMode IdentityMode { get; init; } = AppIdentityMode.DeviceOnly;
}

/// <summary>
/// The identity an app declares. Serialized as kebab-case ("device-only", ...).
/// Coexistence of device-identity and optional user-accounts is by design.
/// </summary>
public enum AppIdentityMode
{
    DeviceOnly,
    AccountRequired,
    Hybrid,
}

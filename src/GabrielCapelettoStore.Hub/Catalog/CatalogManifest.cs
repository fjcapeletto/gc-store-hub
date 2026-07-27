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

    /// <summary>
    /// The device's store-access state. Drives whether apps render installable or
    /// locked-with-offer. Absent in the payload = granted (back-compatible default).
    /// </summary>
    public CatalogAccess Access { get; init; } = new();

    public IReadOnlyList<CatalogApp> Apps { get; init; } = [];
}

/// <summary>
/// The device's store-access, as decided server-side. The catalog is visible to every
/// device; this gates the <em>right to install</em>, not visibility. The client lock is
/// UX only — the delivery endpoint is the real gate. See <c>contract/</c>.
/// </summary>
public sealed record CatalogAccess
{
    public StoreAccessState State { get; init; } = StoreAccessState.Granted;

    /// <summary>Optional, only meaningful when locked; lets the client tailor the copy.</summary>
    public string? Reason { get; init; }

    /// <summary>The sales hook, present when locked. Surfaced in place of the install action.</summary>
    public CatalogOffer? Offer { get; init; }
}

/// <summary>Whether this device may install from the store. Serialized kebab-case.</summary>
public enum StoreAccessState
{
    Granted,
    Locked,
}

/// <summary>The call-to-action shown when an app (or the whole device) is locked.</summary>
public sealed record CatalogOffer
{
    public required string Headline { get; init; }

    /// <summary>Where to buy access. Opaque — never carries a license key/fingerprint/PII.</summary>
    public required string ActionUrl { get; init; }
}

/// <summary>A single publishable app entry on the store shelves.</summary>
public sealed record CatalogApp
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Version { get; init; }
    public string? Summary { get; init; }

    /// <summary>
    /// Publisher-supplied icon asset (https). Preferred over <see cref="Icon"/>; the hub fetches and
    /// caches it, keyed by the URL — a changed URL refreshes the mark (see contract/app-icons.md).
    /// </summary>
    public string? IconUrl { get; init; }

    /// <summary>Fallback glyph key for the chip's inner identity (e.g. "cloud", "music"); used only
    /// when <see cref="IconUrl"/> is absent or unfetchable.</summary>
    public string? Icon { get; init; }

    /// <summary>Which identity the app declares it needs. Anchors the three archetypes.</summary>
    public AppIdentityMode IdentityMode { get; init; } = AppIdentityMode.DeviceOnly;

    /// <summary>
    /// What kind of shelf item this is — decides how the hub delivers/runs it (see app-types.md).
    /// "app" (packaged, default), "web" (pushed links → external browser), "feed", "api".
    /// </summary>
    public string Type { get; init; } = "app";

    /// <summary>
    /// Lifecycle stage: "released" (default) or "developer". Developer apps appear only for
    /// developer-marked devices and arrive unlocked; the stage is a purely visual "DEV" badge —
    /// install/open/subscribe behave like any released app. See contract/developer-stage.md.
    /// </summary>
    public string Stage { get; init; } = "released";

    /// <summary>
    /// Optional per-app override of installability. Absent = inherit the device-wide
    /// <see cref="CatalogAccess.State"/> (granted → installable, otherwise → locked).
    /// </summary>
    public CatalogInstall? Install { get; init; }
}

/// <summary>Per-app installability override; wins over the device-wide access default.</summary>
public sealed record CatalogInstall
{
    public AppInstallState State { get; init; } = AppInstallState.Installable;

    /// <summary>App-specific offer, used when this one app is locked while others are not.</summary>
    public CatalogOffer? Offer { get; init; }
}

/// <summary>Per-app installability (distinct from the persisted install-state store). Kebab-case.</summary>
public enum AppInstallState
{
    Installable,
    Locked,
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

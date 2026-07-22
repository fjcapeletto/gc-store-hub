using System;
using System.Collections.Generic;

namespace GabrielCapelettoStore.Hub.Catalog;

/// <summary>
/// The hub's memory of what the catalog last showed. Drives the freshness window:
/// each app records when its currently-available version first appeared, so a NEW or
/// UPDATE marker can live for a fixed window and reset when a newer version arrives.
/// </summary>
public sealed class CatalogObservationState
{
    /// <summary>Per app id → last observed available version and when it first appeared.</summary>
    public Dictionary<string, AppObservation> Apps { get; set; } = new(StringComparer.Ordinal);

    /// <summary>When the user last dismissed the session banner; the greeting only counts items newer than this.</summary>
    public DateTimeOffset? BannerDismissedAt { get; set; }
}

public sealed class AppObservation
{
    public string AvailableVersion { get; set; } = "";

    /// <summary>When this available version was first seen. Reset whenever the version changes → restarts freshness.</summary>
    public DateTimeOffset NoveltySince { get; set; }
}

/// <summary>How a shelf item reads to the user right now.</summary>
public enum NoveltyStatus
{
    None,
    New,
    Updated,
}

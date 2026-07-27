using System.Collections.Generic;
using GabrielCapelettoStore.Hub.Catalog;

namespace GabrielCapelettoStore.Hub.Web;

/// <summary>A single pushed link (a "deal") in a `type: web` app. See contract/type-web.md.</summary>
public sealed record WebItem
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Url { get; init; }
    public string? PublishedAt { get; init; }

    /// <summary>
    /// OPTIONAL link-preview thumbnail (the target's og:image), resolved and re-hosted on our own
    /// domain by the server, content-hashed. Absent when no preview was available. The hub caches it
    /// (URL = version) and shows it in the individual toast. See contract/type-web.md.
    /// </summary>
    public string? ImageUrl { get; init; }

    /// <summary>
    /// OPTIONAL scheduled-release time (ISO-8601). Absent = deliver now. While this is in the future
    /// the hub hides the item entirely (no toast, not in the inbox) until it's due — a way for the
    /// server to queue posts ahead of time. Pending server ratification (see contract/type-web.md).
    /// </summary>
    public string? DeliverAt { get; init; }
}

/// <summary>Body of POST /v1/delivery/{appId} for a web app: the current link-list.</summary>
public sealed record WebPollResponse
{
    public string? AppId { get; init; }
    public string? Type { get; init; }
    public IReadOnlyList<WebItem> Items { get; init; } = [];
}

public enum WebPollStatus
{
    Ok,
    Revoked,      // 402/403 — device no longer granted; stop the stream
    Unreachable,  // 404 / network / other
}

public sealed record WebPollOutcome
{
    public required WebPollStatus Status { get; init; }
    public IReadOnlyList<WebItem> Items { get; init; } = [];
    public CatalogOffer? Offer { get; init; }
    public string? Reason { get; init; }

    public static WebPollOutcome Delivered(IReadOnlyList<WebItem> items) => new() { Status = WebPollStatus.Ok, Items = items };
    public static WebPollOutcome RevokedOut(CatalogOffer? offer, string? reason) =>
        new() { Status = WebPollStatus.Revoked, Offer = offer, Reason = reason };
    public static WebPollOutcome Offline() => new() { Status = WebPollStatus.Unreachable };
}

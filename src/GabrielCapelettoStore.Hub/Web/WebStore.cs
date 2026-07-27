using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace GabrielCapelettoStore.Hub.Web;

/// <summary>
/// How a web app surfaces new items to the user. Client-side, per app, chosen in the app's delivery
/// config (see contract/type-web.md). Default (value 0) = one toast per item, oldest→newest, spaced.
/// </summary>
public enum WebDeliveryMode
{
    /// <summary>Default: one toast per unseen item, oldest→newest, one every <c>EmitInterval</c>.</summary>
    IndividualOldestFirst = 0,

    /// <summary>One toast per unseen item, newest→oldest, one every <c>EmitInterval</c>.</summary>
    IndividualNewestFirst = 1,

    /// <summary>A single (image-less) toast listing all new titles as a clickable group.</summary>
    GroupedTitles = 2,

    /// <summary>No toasts at all — the user only sees deals by opening the app's inbox.</summary>
    Silent = 3,
}

/// <summary>Per web-app local state: subscription, delivery mode, seen ids, and the inbox.</summary>
public sealed class WebAppState
{
    public bool Subscribed { get; set; }

    /// <summary>The app's display name (from the catalog), so toasts can name the right app.</summary>
    public string? Name { get; set; }

    /// <summary>How new items are surfaced (toast policy). Absent in old state = 0 = the default.</summary>
    public WebDeliveryMode DeliveryMode { get; set; } = WebDeliveryMode.IndividualOldestFirst;

    /// <summary>Seconds between individual toasts (clamped 30–3600 by the manager). Default 2 min.</summary>
    public int EmitIntervalSeconds { get; set; } = 120;

    /// <summary>Set when the last poll returned 403 — this device was revoked from the stream.</summary>
    public bool Revoked { get; set; }
    public string? OfferHeadline { get; set; }
    public string? OfferUrl { get; set; }

    public List<string> SeenIds { get; set; } = [];

    /// <summary>Client-side sanitization: ids the user marked read (moved to the Read tab).</summary>
    public List<string> ReadIds { get; set; } = [];

    /// <summary>Client-side sanitization: ids the user deleted — suppressed even if the server resends.</summary>
    public List<string> DeletedIds { get; set; } = [];

    public List<WebInboxItem> Inbox { get; set; } = [];
}

public sealed class WebInboxItem
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
    public string? PublishedAt { get; set; }

    /// <summary>The link-preview thumbnail URL (same as the toast's), shown left of the inbox row.</summary>
    public string? ImageUrl { get; set; }
}

public sealed class WebState
{
    public Dictionary<string, WebAppState> Apps { get; set; } = new(StringComparer.Ordinal);
}

public interface IWebStore
{
    WebState Load();
    void Save(WebState state);
}

public sealed class FileWebStore : IWebStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly string _path;

    public FileWebStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GabrielCapelettoStore");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "web.json");
    }

    public WebState Load()
    {
        try
        {
            return File.Exists(_path)
                ? JsonSerializer.Deserialize<WebState>(File.ReadAllText(_path), Json) ?? new WebState()
                : new WebState();
        }
        catch
        {
            return new WebState();
        }
    }

    public void Save(WebState state)
    {
        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(state, Json));
        }
        catch
        {
            // best-effort
        }
    }
}

public sealed class NullWebStore : IWebStore
{
    private WebState _state = new();
    public WebState Load() => _state;
    public void Save(WebState state) => _state = state;
}

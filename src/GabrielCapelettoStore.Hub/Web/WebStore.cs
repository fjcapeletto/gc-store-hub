using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace GabrielCapelettoStore.Hub.Web;

/// <summary>Per web-app local state: subscription, which items were already seen, and the inbox.</summary>
public sealed class WebAppState
{
    public bool Subscribed { get; set; }
    public bool Muted { get; set; }

    /// <summary>Set when the last poll returned 403 — this device was revoked from the stream.</summary>
    public bool Revoked { get; set; }
    public string? OfferHeadline { get; set; }
    public string? OfferUrl { get; set; }

    public List<string> SeenIds { get; set; } = [];
    public List<WebInboxItem> Inbox { get; set; } = [];
}

public sealed class WebInboxItem
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
    public string? PublishedAt { get; set; }
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

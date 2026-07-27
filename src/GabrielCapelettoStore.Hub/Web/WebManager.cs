using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using GabrielCapelettoStore.Hub.Notifications;

namespace GabrielCapelettoStore.Hub.Web;

/// <summary>
/// The hub-side brain for `type: web` apps (GC Deals): subscribe/unsubscribe, poll the gated
/// link-list on a cadence, toast new links, keep an inbox, and open links in the external browser.
/// Publishing is server-side; this only consumes.
/// </summary>
public sealed class WebManager
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60);
    private const int InboxCap = 50;

    private readonly IWebService _web;
    private readonly IWebStore _store;
    private readonly IToastService _toast;
    private readonly WebState _state;
    private readonly DispatcherTimer _timer;

    /// <summary>Raised when subscription/inbox state changes (so the shelf/inbox can refresh).</summary>
    public event Action? Changed;

    public WebManager(IWebService web, IWebStore store, IToastService toast)
    {
        _web = web;
        _store = store;
        _toast = toast;
        _state = store.Load();
        _timer = new DispatcherTimer { Interval = PollInterval };
        _timer.Tick += async (_, _) => await PollAllAsync();
    }

    public void Start()
    {
        if (_web.IsConfigured)
        {
            _timer.Start();
            _ = PollAllAsync();
        }
    }

    public bool IsSubscribed(string appId) => _state.Apps.TryGetValue(appId, out var a) && a.Subscribed;
    public bool IsMuted(string appId) => _state.Apps.TryGetValue(appId, out var a) && a.Muted;

    /// <summary>Whether this device was revoked from the stream (last poll 403). Web's own gate.</summary>
    public bool IsRevoked(string appId) => _state.Apps.TryGetValue(appId, out var a) && a.Revoked;
    public string? RevokedHeadline(string appId) => _state.Apps.TryGetValue(appId, out var a) ? a.OfferHeadline : null;
    public string? RevokedUrl(string appId) => _state.Apps.TryGetValue(appId, out var a) ? a.OfferUrl : null;

    public IReadOnlyList<WebInboxItem> Inbox(string appId)
        => _state.Apps.TryGetValue(appId, out var a) ? a.Inbox : [];

    public void Subscribe(string appId)
    {
        var app = GetOrCreate(appId);
        app.Subscribed = true;
        _store.Save(_state);
        Changed?.Invoke();
        _ = PollAsync(appId);
    }

    public void Unsubscribe(string appId)
    {
        if (_state.Apps.TryGetValue(appId, out var a))
        {
            a.Subscribed = false;
            _store.Save(_state);
            Changed?.Invoke();
        }
    }

    public void ToggleMute(string appId)
    {
        var app = GetOrCreate(appId);
        app.Muted = !app.Muted;
        _store.Save(_state);
        Changed?.Invoke();
    }

    public void OpenLink(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // A dead link must not crash the hub.
        }
    }

    private WebAppState GetOrCreate(string appId)
    {
        if (!_state.Apps.TryGetValue(appId, out var app))
        {
            app = new WebAppState();
            _state.Apps[appId] = app;
        }

        return app;
    }

    private async Task PollAllAsync()
    {
        foreach (var appId in _state.Apps.Where(kv => kv.Value.Subscribed).Select(kv => kv.Key).ToList())
        {
            await PollAsync(appId);
        }
    }

    private async Task PollAsync(string appId)
    {
        var outcome = await _web.PollAsync(appId);

        if (outcome.Status == WebPollStatus.Revoked)
        {
            // This device was revoked from the stream: stop, surface the offer. Web's own gate —
            // independent of store-access (a device with no entitlement still gets the stream).
            var revoked = GetOrCreate(appId);
            revoked.Revoked = true;
            revoked.OfferHeadline = outcome.Offer?.Headline;
            revoked.OfferUrl = outcome.Offer?.ActionUrl;
            _store.Save(_state);
            Changed?.Invoke();
            return;
        }

        if (outcome.Status != WebPollStatus.Ok)
        {
            return; // unreachable / transient — leave state as-is.
        }

        var app = GetOrCreate(appId);
        if (app.Revoked)
        {
            app.Revoked = false;
            app.OfferHeadline = null;
            app.OfferUrl = null;
        }
        var newItems = outcome.Items.Where(i => !app.SeenIds.Contains(i.Id)).ToList();

        // The server's list is the fresh inbox (recent items), newest first assumed.
        app.Inbox = outcome.Items
            .Select(i => new WebInboxItem { Id = i.Id, Title = i.Title, Url = i.Url, PublishedAt = i.PublishedAt })
            .Take(InboxCap)
            .ToList();
        foreach (var i in newItems)
        {
            app.SeenIds.Add(i.Id);
        }

        _store.Save(_state);
        Changed?.Invoke();

        if (newItems.Count == 0 || app.Muted)
        {
            return;
        }

        if (newItems.Count == 1)
        {
            var item = newItems[0];
            _toast.Show(item.Title, "GC Deals", () => OpenLink(item.Url));
        }
        else
        {
            var newest = newItems[0];
            _toast.Show($"{newItems.Count} new deals", "Click to open the latest", () => OpenLink(newest.Url));
        }
    }
}

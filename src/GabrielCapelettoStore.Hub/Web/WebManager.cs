using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using GabrielCapelettoStore.Hub.Notifications;

namespace GabrielCapelettoStore.Hub.Web;

/// <summary>
/// The hub-side brain for `type: web` apps (GC Deals): subscribe/unsubscribe, poll the gated
/// link-list on a cadence, surface new links per the app's delivery mode, keep an inbox, and open
/// links in the external browser. Publishing is server-side; this only consumes.
///
/// Delivery is configurable per app (see <see cref="WebDeliveryMode"/>). Individual modes drip one
/// toast per item on a per-app cadence (30 s–1 h, default 2 min) so nothing is lost when toasts later
/// carry their own image; grouped is a single image-less toast; silent is inbox-only. Items with a
/// future <see cref="WebItem.DeliverAt"/> are hidden until due.
/// </summary>
public sealed class WebManager
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60);
    public const int MinEmitSeconds = 30;
    public const int MaxEmitSeconds = 3600;
    public const int DefaultEmitSeconds = 120;
    private const int InboxCap = 50;

    private readonly IWebService _web;
    private readonly IWebStore _store;
    private readonly IToastService _toast;
    private readonly WebState _state;
    private readonly DispatcherTimer _pollTimer;

    // Per-app one-by-one emission: each app drips its own queue at its own cadence.
    private readonly Dictionary<string, Emitter> _emitters = new(StringComparer.Ordinal);

    private readonly record struct PendingToast(string Id, string Title, string Url);

    private sealed class Emitter
    {
        public readonly List<PendingToast> Queue = [];
        public readonly HashSet<string> Keys = new(StringComparer.Ordinal);
        public DispatcherTimer? Timer;
    }

    /// <summary>Raised when subscription/inbox/mode state changes (so the shelf/inbox can refresh).</summary>
    public event Action? Changed;

    public WebManager(IWebService web, IWebStore store, IToastService toast)
    {
        _web = web;
        _store = store;
        _toast = toast;
        _state = store.Load();
        _pollTimer = new DispatcherTimer { Interval = PollInterval };
        _pollTimer.Tick += async (_, _) => await PollAllAsync();
    }

    public void Start()
    {
        if (_web.IsConfigured)
        {
            _pollTimer.Start();
            _ = PollAllAsync();
        }
    }

    public bool IsSubscribed(string appId) => _state.Apps.TryGetValue(appId, out var a) && a.Subscribed;

    /// <summary>Whether this device was revoked from the stream (last poll 403). Web's own gate.</summary>
    public bool IsRevoked(string appId) => _state.Apps.TryGetValue(appId, out var a) && a.Revoked;
    public string? RevokedHeadline(string appId) => _state.Apps.TryGetValue(appId, out var a) ? a.OfferHeadline : null;
    public string? RevokedUrl(string appId) => _state.Apps.TryGetValue(appId, out var a) ? a.OfferUrl : null;

    public WebDeliveryMode GetMode(string appId)
        => _state.Apps.TryGetValue(appId, out var a) ? a.DeliveryMode : WebDeliveryMode.IndividualOldestFirst;

    public void SetMode(string appId, WebDeliveryMode mode)
    {
        var app = GetOrCreate(appId);
        app.DeliveryMode = mode;
        _store.Save(_state);

        // Switching to silent drops anything still queued for this app — it should go quiet at once.
        if (mode == WebDeliveryMode.Silent)
        {
            DropPending(appId);
        }

        Changed?.Invoke();
    }

    /// <summary>The per-app cadence (seconds) between individual toasts, clamped to the allowed range.</summary>
    public int GetIntervalSeconds(string appId)
        => _state.Apps.TryGetValue(appId, out var a) ? Clamp(a.EmitIntervalSeconds) : DefaultEmitSeconds;

    public void SetIntervalSeconds(string appId, int seconds)
    {
        var app = GetOrCreate(appId);
        app.EmitIntervalSeconds = Clamp(seconds);
        _store.Save(_state);

        // Apply live to a drain already in progress.
        if (_emitters.TryGetValue(appId, out var em) && em.Timer is not null)
        {
            em.Timer.Interval = TimeSpan.FromSeconds(app.EmitIntervalSeconds);
        }

        Changed?.Invoke();
    }

    public IReadOnlyList<WebInboxItem> Inbox(string appId)
        => _state.Apps.TryGetValue(appId, out var a) ? a.Inbox : [];

    public void Subscribe(string appId, string appName)
    {
        var app = GetOrCreate(appId);
        app.Subscribed = true;
        if (!string.IsNullOrWhiteSpace(appName))
        {
            app.Name = appName;
        }
        _store.Save(_state);
        Changed?.Invoke();
        _ = PollAsync(appId);
    }

    /// <summary>Keep the stored display name current with the catalog (only for apps we already track).</summary>
    public void SetDisplayName(string appId, string appName)
    {
        if (!string.IsNullOrWhiteSpace(appName)
            && _state.Apps.TryGetValue(appId, out var app)
            && !string.Equals(app.Name, appName, StringComparison.Ordinal))
        {
            app.Name = appName;
            _store.Save(_state);
        }
    }

    private string DisplayName(string appId)
        => _state.Apps.TryGetValue(appId, out var a) && !string.IsNullOrWhiteSpace(a.Name) ? a.Name! : appId;

    public void Unsubscribe(string appId)
    {
        if (_state.Apps.TryGetValue(appId, out var a))
        {
            a.Subscribed = false;
            _store.Save(_state);
            DropPending(appId);
            Changed?.Invoke();
        }
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
            // Revoked from the stream: stop, surface the offer. Web's own gate — independent of
            // store-access (a device with no entitlement still gets the stream).
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

        var now = DateTimeOffset.UtcNow;

        // Only items whose scheduled release has arrived are visible at all (future = hidden).
        var present = outcome.Items.Where(i => IsDue(i, now)).ToList();
        var presentIds = new HashSet<string>(present.Select(i => i.Id), StringComparer.Ordinal);

        // Bound the seen-set to what's still on the server's list (recent N) so it can't grow forever.
        app.SeenIds = app.SeenIds.Where(presentIds.Contains).ToList();

        // The inbox is the present list, newest first, capped.
        app.Inbox = present
            .OrderByDescending(i => ParseTime(i.PublishedAt) ?? DateTimeOffset.MinValue)
            .Take(InboxCap)
            .Select(i => new WebInboxItem { Id = i.Id, Title = i.Title, Url = i.Url, PublishedAt = i.PublishedAt })
            .ToList();

        // Unseen = present, not already surfaced, and not already queued this session.
        var em = GetEmitter(appId);
        var unseen = present
            .Where(i => !app.SeenIds.Contains(i.Id) && !em.Keys.Contains(i.Id))
            .ToList();

        _store.Save(_state);
        Changed?.Invoke();

        if (unseen.Count == 0)
        {
            return;
        }

        switch (app.DeliveryMode)
        {
            case WebDeliveryMode.Silent:
                MarkSeen(app, unseen);
                _store.Save(_state);
                break;

            case WebDeliveryMode.GroupedTitles:
            {
                var ordered = OrderNewestFirst(unseen);
                var links = ordered.Select(i => new ToastLink(i.Title, () => OpenLink(i.Url))).ToList();
                _toast.ShowList($"{DisplayName(appId)} · {ordered.Count} new", links);
                MarkSeen(app, unseen);
                _store.Save(_state);
                break;
            }

            case WebDeliveryMode.IndividualNewestFirst:
                EnqueueAll(appId, OrderNewestFirst(unseen));
                break;

            case WebDeliveryMode.IndividualOldestFirst:
            default:
                EnqueueAll(appId, OrderOldestFirst(unseen));
                break;
        }
    }

    // --- Per-app one-by-one emission ---

    private Emitter GetEmitter(string appId)
    {
        if (!_emitters.TryGetValue(appId, out var em))
        {
            em = new Emitter();
            _emitters[appId] = em;
        }

        return em;
    }

    private void EnqueueAll(string appId, IEnumerable<WebItem> ordered)
    {
        var em = GetEmitter(appId);
        foreach (var i in ordered)
        {
            if (em.Keys.Add(i.Id))
            {
                em.Queue.Add(new PendingToast(i.Id, i.Title, i.Url));
            }
        }

        // Fire the first straight away; the rest follow one per cadence ("N between them").
        if (em.Queue.Count > 0 && em.Timer is null)
        {
            DrainOne(appId);
            if (em.Queue.Count > 0)
            {
                em.Timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(GetIntervalSeconds(appId)) };
                em.Timer.Tick += (_, _) => DrainOne(appId);
                em.Timer.Start();
            }
        }
    }

    private void DrainOne(string appId)
    {
        if (!_emitters.TryGetValue(appId, out var em))
        {
            return;
        }

        if (em.Queue.Count == 0)
        {
            StopTimer(em);
            return;
        }

        var p = em.Queue[0];
        em.Queue.RemoveAt(0);
        em.Keys.Remove(p.Id);

        var app = GetOrCreate(appId);
        if (!app.SeenIds.Contains(p.Id))
        {
            app.SeenIds.Add(p.Id); // persist as surfaced so a restart won't re-toast (inbox still has it).
        }
        _store.Save(_state);

        // Respect a mid-drain switch to silent / an unsubscribe: keep it seen, skip the toast.
        if (app.Subscribed && app.DeliveryMode != WebDeliveryMode.Silent)
        {
            _toast.Show(p.Title, DisplayName(appId), () => OpenLink(p.Url));
        }

        if (em.Queue.Count == 0)
        {
            StopTimer(em);
        }
    }

    private void DropPending(string appId)
    {
        if (_emitters.TryGetValue(appId, out var em))
        {
            em.Queue.Clear();
            em.Keys.Clear();
            StopTimer(em);
        }
    }

    private static void StopTimer(Emitter em)
    {
        em.Timer?.Stop();
        em.Timer = null;
    }

    private static void MarkSeen(WebAppState app, IEnumerable<WebItem> items)
    {
        foreach (var i in items)
        {
            if (!app.SeenIds.Contains(i.Id))
            {
                app.SeenIds.Add(i.Id);
            }
        }
    }

    private static List<WebItem> OrderOldestFirst(IEnumerable<WebItem> items)
        => items.OrderBy(i => ParseTime(i.PublishedAt) ?? DateTimeOffset.MaxValue).ToList();

    private static List<WebItem> OrderNewestFirst(IEnumerable<WebItem> items)
        => items.OrderByDescending(i => ParseTime(i.PublishedAt) ?? DateTimeOffset.MinValue).ToList();

    private static int Clamp(int seconds)
        => seconds <= 0 ? DefaultEmitSeconds : Math.Clamp(seconds, MinEmitSeconds, MaxEmitSeconds);

    /// <summary>Absent DeliverAt = deliver now; a future DeliverAt hides the item until due. Unparseable
    /// fails open (shown) — never hide content by accident.</summary>
    private static bool IsDue(WebItem i, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(i.DeliverAt))
        {
            return true;
        }

        var t = ParseTime(i.DeliverAt);
        return t is null || t.Value <= now;
    }

    private static DateTimeOffset? ParseTime(string? s)
        => DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t)
            ? t
            : null;
}

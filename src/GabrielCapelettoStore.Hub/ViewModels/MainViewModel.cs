using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GabrielCapelettoStore.Hub.Catalog;
using GabrielCapelettoStore.Hub.Identity;

namespace GabrielCapelettoStore.Hub.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private static readonly TimeSpan FreshnessWindow = TimeSpan.FromDays(14);

    private readonly ICatalogSource _catalogSource;
    private readonly ICatalogStateStore _observationStore;
    private readonly IInstallStateStore _installStore;
    private readonly ICatalogCache _cache;

    private CatalogManifest? _manifest;
    private CatalogObservationState _observation = new();
    private InstallState _install = new();
    private string _shelfSignature = "";

    public MainViewModel(
        ICatalogSource catalogSource,
        ICatalogStateStore observationStore,
        IInstallStateStore installStore,
        ICatalogCache cache,
        IDeviceFingerprintCollector fingerprintCollector,
        IIdentityBaselineStore baselineStore)
    {
        _catalogSource = catalogSource;
        _observationStore = observationStore;
        _installStore = installStore;
        _cache = cache;
        DeviceIdentity = new DeviceIdentityViewModel(fingerprintCollector, baselineStore);
    }

    /// <summary>Design-time constructor: seeds the previewer with mixed states so badges/buttons show.</summary>
    public MainViewModel() : this(
        new DesignCatalogSource(), new NullCatalogStateStore(), new NullInstallStateStore(),
        new NullCatalogCache(), new NullFingerprintCollector(), new NullIdentityBaselineStore())
    {
        var apps = DesignCatalogSource.SampleCatalog.Apps;
        Apps.Add(new ShelfItemViewModel(apps[0], installedVersion: null, updateAvailable: false, NoveltyStatus.New));
        Apps.Add(new ShelfItemViewModel(apps[1], installedVersion: "0.9.0", updateAvailable: true, NoveltyStatus.Updated));

        NewCount = 1;
        UpdatedCount = 1;
        AppCount = Apps.Count;
    }

    [ObservableProperty]
    public partial string StoreName { get; set; } = "Gabriel Capeletto Store";

    /// <summary>The running hub version (stamped at build time; shown in the header).</summary>
    public string HubVersion { get; } = ResolveVersion();

    private static string ResolveVersion()
    {
        var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        return v is null ? "dev" : $"v{v.Major}.{v.Minor}.{v.Build}";
    }

    /// <summary>The "This device" identity section shown in Settings.</summary>
    public DeviceIdentityViewModel DeviceIdentity { get; }

    /// <summary>Whether the Settings view is showing instead of the catalog.</summary>
    [ObservableProperty]
    public partial bool ShowSettings { get; set; }

    public ObservableCollection<ShelfItemViewModel> Apps { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCatalog))]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    public partial bool IsLoading { get; set; }

    /// <summary>True during a silent background refresh (shelves stay visible; the sync icon shows activity).</summary>
    [ObservableProperty]
    public partial bool IsRefreshing { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyPropertyChangedFor(nameof(ShowCatalog))]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCatalog))]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    [NotifyPropertyChangedFor(nameof(ShowOfflineNotice))]
    public partial int AppCount { get; set; }

    /// <summary>Whether the last fetch reached the store. False = showing the cached (last-known) catalog.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowOfflineNotice))]
    [NotifyPropertyChangedFor(nameof(ConnectivityLabel))]
    public partial bool IsOnline { get; set; } = true;

    public string ConnectivityLabel => IsOnline ? "Store online" : "Store server offline";

    [ObservableProperty]
    public partial string OfflineNotice { get; set; } = "";

    public bool ShowOfflineNotice => !IsOnline && AppCount > 0;

    /// <summary>Transient status by the sync button: "Checking…" while trying, "Store server offline" on failure.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSyncStatus))]
    public partial string SyncStatus { get; set; } = "";

    public bool HasSyncStatus => !string.IsNullOrEmpty(SyncStatus);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNews))]
    [NotifyPropertyChangedFor(nameof(NewsSummary))]
    public partial int NewCount { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNews))]
    [NotifyPropertyChangedFor(nameof(NewsSummary))]
    public partial int UpdatedCount { get; set; }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public bool ShowCatalog => !IsLoading && !HasError && AppCount > 0;

    public bool ShowEmptyState => !IsLoading && !HasError && AppCount == 0;

    public bool HasNews => NewCount + UpdatedCount > 0;

    public string NewsSummary => BuildNewsSummary();

    /// <summary>Initial / visible load (shows the loading state when there is nothing on screen yet).</summary>
    public Task LoadAsync() => FetchAsync(silent: false);

    /// <summary>Background refresh (focus, hourly timer, sync icon): keeps the shelves visible.</summary>
    public Task RefreshAsync() => FetchAsync(silent: true);

    private async Task FetchAsync(bool silent)
    {
        if (IsLoading || IsRefreshing)
        {
            return; // never overlap fetches
        }

        if (silent)
        {
            IsRefreshing = true;
        }
        else
        {
            IsLoading = true;
            ErrorMessage = null;
        }

        SyncStatus = "Checking…";
        var now = DateTimeOffset.UtcNow;

        try
        {
            var manifest = await _catalogSource.GetCatalogAsync();

            var previous = _observationStore.Load();
            var firstRun = previous is null;
            var observation = previous ?? new CatalogObservationState();

            // Refresh freshness timestamps: a new app or a changed available version restarts its window.
            var apps = new Dictionary<string, AppObservation>(StringComparer.Ordinal);
            foreach (var app in manifest.Apps)
            {
                var noveltySince =
                    observation.Apps.TryGetValue(app.Id, out var prior) &&
                    string.Equals(prior.AvailableVersion, app.Version, StringComparison.Ordinal)
                        ? prior.NoveltySince
                        : now;

                apps[app.Id] = new AppObservation { AvailableVersion = app.Version, NoveltySince = noveltySince };
            }
            observation.Apps = apps;

            // First-ever run: suppress the "since your last visit" greeting (badges still show).
            if (firstRun && observation.BannerDismissedAt is null)
            {
                observation.BannerDismissedAt = now;
            }

            _observationStore.Save(observation);

            _manifest = manifest;
            _observation = observation;
            _install = _installStore.Load();

            IsOnline = true;
            OfflineNotice = "";
            SyncStatus = "";
            Rebuild(now);

            // A successful fetch clears any prior error and refreshes the offline cache.
            ErrorMessage = null;
            _cache.Save(manifest, now);
        }
        catch (Exception ex)
        {
            IsOnline = false;
            SyncStatus = "Store server offline";

            var cached = _cache.Load();
            if (cached is not null)
            {
                // Offline / backend down, but we have last-known shelves → show them, not an error.
                _manifest = cached.Manifest;
                _observation = _observationStore.Load() ?? new CatalogObservationState();
                _install = _installStore.Load();
                ErrorMessage = null;
                Rebuild(now);
                OfflineNotice = $"Showing last known catalog · synced {RelativeTime(cached.SyncedAt, now)}";
            }
            else if (!silent)
            {
                // No cache at all (first-run offline) → the honest hard error.
                Apps.Clear();
                AppCount = 0;
                NewCount = 0;
                UpdatedCount = 0;
                ErrorMessage = $"Couldn't reach the store catalog.\n{ex.Message}";
            }
        }
        finally
        {
            if (silent)
            {
                IsRefreshing = false;
            }
            else
            {
                IsLoading = false;
            }
        }
    }

    /// <summary>Rebuilds the shelves from the current manifest + observation + install state.</summary>
    private void Rebuild(DateTimeOffset now)
    {
        if (_manifest is null)
        {
            return;
        }

        var dismissedAt = _observation.BannerDismissedAt ?? DateTimeOffset.MinValue;

        var access = _manifest.Access;

        var items = new List<ShelfItemViewModel>(_manifest.Apps.Count);
        var signature = new StringBuilder();
        signature.Append(IsOnline ? "on;" : "off;");
        signature.Append((int)access.State).Append(';');
        var newCount = 0;
        var updatedCount = 0;

        foreach (var app in _manifest.Apps)
        {
            _observation.Apps.TryGetValue(app.Id, out var obs);
            var noveltySince = obs?.NoveltySince ?? now;
            var fresh = now - noveltySince <= FreshnessWindow;

            var installedVersion = _install.Installed.TryGetValue(app.Id, out var iv) ? iv : null;
            var isInstalled = installedVersion is not null;
            var updateAvailable = isInstalled && IsNewer(app.Version, installedVersion!);

            NoveltyStatus status;
            if (!isInstalled)
            {
                status = fresh ? NoveltyStatus.New : NoveltyStatus.None;
            }
            else if (updateAvailable)
            {
                status = fresh ? NoveltyStatus.Updated : NoveltyStatus.None;
            }
            else
            {
                status = NoveltyStatus.None;
            }

            // The banner greets only what became new/updated since the last dismissal.
            if (noveltySince > dismissedAt)
            {
                if (status == NoveltyStatus.New)
                {
                    newCount++;
                }
                else if (status == NoveltyStatus.Updated)
                {
                    updatedCount++;
                }
            }

            var item = new ShelfItemViewModel(
                app, installedVersion, updateAvailable, status, IsOnline, InstallApp, access, OpenOffer);
            items.Add(item);
            signature.Append(app.Id).Append('|').Append((int)status).Append('|')
                     .Append(installedVersion ?? "-").Append('|').Append(app.Version).Append('|')
                     .Append(item.IsLocked ? 'L' : '_').Append(';');
        }

        AppCount = items.Count;
        NewCount = newCount;
        UpdatedCount = updatedCount;

        // Only touch the collection when the shelves actually changed. Rebuilding it on a
        // no-op refresh would destroy the item controls mid-interaction (e.g. eat a click).
        var newSignature = signature.ToString();
        if (newSignature == _shelfSignature && Apps.Count == items.Count)
        {
            return;
        }

        _shelfSignature = newSignature;
        Apps.Clear();
        foreach (var item in items)
        {
            Apps.Add(item);
        }
    }

    /// <summary>Opens the offer landing (buy store access) in the user's default browser.</summary>
    private void OpenOffer(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // A bad/unreachable offer URL must never crash the hub; the CTA is best-effort.
        }
    }

    /// <summary>Stub install: records the installed version only (no real installation yet), then re-renders.</summary>
    private void InstallApp(ShelfItemViewModel item)
    {
        _install.Installed[item.Id] = item.AvailableVersion;
        _installStore.Save(_install);
        Rebuild(DateTimeOffset.UtcNow);
    }

    [RelayCommand]
    private void DismissBanner()
    {
        _observation.BannerDismissedAt = DateTimeOffset.UtcNow;
        _observationStore.Save(_observation);
        Rebuild(DateTimeOffset.UtcNow);
    }

    [RelayCommand]
    private Task Sync() => RefreshAsync();

    [RelayCommand]
    private void OpenSettings()
    {
        DeviceIdentity.Load();
        ShowSettings = true;
    }

    [RelayCommand]
    private void CloseSettings() => ShowSettings = false;

    private static bool IsNewer(string candidate, string baseline)
    {
        if (Version.TryParse(candidate, out var c) && Version.TryParse(baseline, out var b))
        {
            return c > b;
        }

        // Fallback for non-numeric versions: any difference counts as an update.
        return !string.Equals(candidate, baseline, StringComparison.Ordinal);
    }

    private static string RelativeTime(DateTimeOffset then, DateTimeOffset now)
    {
        var elapsed = now - then;
        if (elapsed < TimeSpan.FromMinutes(1))
        {
            return "just now";
        }

        if (elapsed < TimeSpan.FromHours(1))
        {
            return $"{(int)elapsed.TotalMinutes}m ago";
        }

        if (elapsed < TimeSpan.FromDays(1))
        {
            return $"{(int)elapsed.TotalHours}h ago";
        }

        return $"{(int)elapsed.TotalDays}d ago";
    }

    private string BuildNewsSummary()
    {
        var parts = new List<string>(2);

        if (NewCount > 0)
        {
            parts.Add($"{NewCount} new app{(NewCount == 1 ? "" : "s")}");
        }

        if (UpdatedCount > 0)
        {
            parts.Add($"{UpdatedCount} update{(UpdatedCount == 1 ? "" : "s")}");
        }

        return string.Join(" · ", parts);
    }
}

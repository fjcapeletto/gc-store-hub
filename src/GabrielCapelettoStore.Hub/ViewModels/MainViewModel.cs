using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GabrielCapelettoStore.Hub.Catalog;
using GabrielCapelettoStore.Hub.Delivery;
using GabrielCapelettoStore.Hub.Entitlement;
using GabrielCapelettoStore.Hub.Identity;
using GabrielCapelettoStore.Hub.Update;

namespace GabrielCapelettoStore.Hub.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private static readonly TimeSpan FreshnessWindow = TimeSpan.FromDays(14);

    private readonly ICatalogSource _catalogSource;
    private readonly ICatalogStateStore _observationStore;
    private readonly IInstallStateStore _installStore;
    private readonly ICatalogCache _cache;
    private readonly IEntitlementService _entitlement;
    private readonly IUpdateService _updateService;
    private readonly IDeliveryService _delivery;
    private readonly IAppInstaller _installer;

    private CatalogManifest? _manifest;
    private CatalogObservationState _observation = new();
    private InstallState _install = new();
    private AuthorizeOutcome? _authorize;
    private string _shelfSignature = "";

    public MainViewModel(
        ICatalogSource catalogSource,
        ICatalogStateStore observationStore,
        IInstallStateStore installStore,
        ICatalogCache cache,
        IDeviceFingerprintCollector fingerprintCollector,
        IIdentityBaselineStore baselineStore,
        IEntitlementService entitlement,
        IUpdateService updateService,
        IDeliveryService delivery,
        IAppInstaller installer)
    {
        _catalogSource = catalogSource;
        _observationStore = observationStore;
        _installStore = installStore;
        _cache = cache;
        _entitlement = entitlement;
        _updateService = updateService;
        _delivery = delivery;
        _installer = installer;
        _updateService.StateChanged += OnUpdateStateChanged;
        DeviceIdentity = new DeviceIdentityViewModel(fingerprintCollector, baselineStore);
    }

    // Update state changes arrive off the UI thread → marshal, then refresh the header indicator.
    private void OnUpdateStateChanged() => Dispatcher.UIThread.Post(() =>
    {
        OnPropertyChanged(nameof(ShowUpdate));
        OnPropertyChanged(nameof(IsUpdateReady));
        OnPropertyChanged(nameof(UpdateStatusText));
    });

    /// <summary>Show the header update chip while an update is downloading or ready.</summary>
    public bool ShowUpdate => _updateService.State is UpdateState.Downloading or UpdateState.Ready;

    /// <summary>Downloaded and staged → the chip becomes a "Restart to update" button.</summary>
    public bool IsUpdateReady => _updateService.State == UpdateState.Ready;

    public string UpdateStatusText => _updateService.State switch
    {
        UpdateState.Downloading => $"Downloading update {_updateService.NewVersion}…",
        UpdateState.Ready => $"Update {_updateService.NewVersion} ready — restart",
        _ => "",
    };

    [RelayCommand]
    private void RestartUpdate() => _updateService.ApplyAndRestart();

    /// <summary>Design-time constructor: seeds the previewer with mixed states so badges/buttons show.</summary>
    public MainViewModel() : this(
        new DesignCatalogSource(), new NullCatalogStateStore(), new NullInstallStateStore(),
        new NullCatalogCache(), new NullFingerprintCollector(), new NullIdentityBaselineStore(),
        new NullEntitlementService(), new NullUpdateService(), new NullDeliveryService(), new NullAppInstaller())
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
    [NotifyPropertyChangedFor(nameof(ShowMainArea))]
    public partial bool ShowSettings { get; set; }

    /// <summary>Whether the Access / licensing view is showing instead of the catalog.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowMainArea))]
    public partial bool ShowAccess { get; set; }

    /// <summary>The catalog area shows only when neither Settings nor Access is open.</summary>
    public bool ShowMainArea => !ShowSettings && !ShowAccess;

    // --- Access / licensing (a-posteriori path; see contract/device-identity-provisioning.md) ---
    [ObservableProperty] public partial string AccessName { get; set; } = "";
    [ObservableProperty] public partial string AccessEmail { get; set; } = "";
    [ObservableProperty] public partial string AccessPhone { get; set; } = "";
    [ObservableProperty] public partial string LicenseKeyInput { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAccessMessage))]
    public partial string AccessMessage { get; set; } = "";

    public bool HasAccessMessage => !string.IsNullOrEmpty(AccessMessage);

    public string DeviceId => _entitlement.DeviceId;
    public bool HasLicense => _entitlement.HasLicense;
    public bool AccessRequested => _entitlement.AccessRequested;

    /// <summary>Awaiting approval — from the server (403 access-pending) or a just-submitted request.</summary>
    public bool IsAccessPending =>
        (_authorize is { Status: AuthorizeStatus.Locked, Reason: "access-pending" })
        || _entitlement.AccessRequested;

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
        _install = _installStore.Load();

        try
        {
            // --- Load the shelf (catalog). Best effort: fall back to the last-known cache. ---
            Exception? catalogError = null;
            var catalogFromCache = false;
            var cacheAge = default(DateTimeOffset);

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
                _cache.Save(manifest, now);
            }
            catch (Exception ex)
            {
                catalogError = ex;
                var cached = _cache.Load();
                if (cached is not null)
                {
                    _manifest = cached.Manifest;
                    _observation = _observationStore.Load() ?? new CatalogObservationState();
                    catalogFromCache = true;
                    cacheAge = cached.SyncedAt;
                }
            }

            // --- Heartbeat + authorization: ONE pull to the ONE server. With a license, the
            //     authorize call is the pull (its app-level reply = alive AND carries access).
            //     Without a license, the access flow is local, so a plain /healthz ping is the beat. ---
            bool online;
            if (_entitlement.IsConfigured)
            {
                // One pull to the one server — liveness AND access, license or not. A license-less
                // pending device reaches the server to receive access-pending.
                _authorize = await _entitlement.AuthorizeAsync();
                online = _authorize.Status != AuthorizeStatus.Unreachable;
            }
            else
            {
                online = catalogError is null;
            }
            IsOnline = online;
            OnPropertyChanged(nameof(IsAccessPending));

            if (_manifest is not null)
            {
                ErrorMessage = null;
                Rebuild(now);
                OfflineNotice =
                    !online ? "Can't reach the store server — access is limited until it's back."
                    : catalogFromCache ? $"Showing last known catalog · synced {RelativeTime(cacheAge, now)}"
                    : "";
                SyncStatus = online ? "" : "Store server offline";
            }
            else
            {
                Apps.Clear();
                AppCount = 0;
                NewCount = 0;
                UpdatedCount = 0;

                if (online)
                {
                    // The store server is up but there's no catalog yet (no endpoint / transient).
                    // Don't dead-end to "Store unavailable": show the empty state, and the access /
                    // licensing flow (the key menu) stays fully usable.
                    ErrorMessage = null;
                    OfflineNotice = "";
                    SyncStatus = "";
                }
                else if (!silent)
                {
                    // Truly nothing reachable and no cache → the honest hard error.
                    ErrorMessage = $"Couldn't reach the store.\n{catalogError?.Message}";
                    SyncStatus = "Store server offline";
                }
                else
                {
                    SyncStatus = "Store server offline";
                }
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

        var access = EffectiveAccess();

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

            var installedVersion = _install.Apps.TryGetValue(app.Id, out var iv) ? iv.Version : null;
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
                app, installedVersion, updateAvailable, status, IsOnline, OnInstall, access, OpenOffer,
                EntitlementOverride(app.Id), OnOpen, OnUpdate, OnUninstall);
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

    /// <summary>The "Unlock" CTA on a locked app opens the in-hub access / licensing panel.</summary>
    private void OpenOffer(string url) => OpenAccess();

    /// <summary>
    /// The device's effective store access. Catalog-only mode (no entitlement endpoint) trusts the
    /// catalog's own access field (mock). Otherwise the live <c>/v1/authorize</c> outcome decides:
    /// granted, locked-with-offer, or locked (can't verify — needs update / unreachable).
    /// </summary>
    private CatalogAccess EffectiveAccess()
    {
        if (!_entitlement.IsConfigured)
        {
            return _manifest?.Access ?? new CatalogAccess();
        }

        if (_authorize is null)
        {
            return new CatalogAccess { State = StoreAccessState.Locked };
        }

        // Server-driven: the authorize reason (unrecognized-device / no-store-access /
        // access-pending) decides how the locked shelf reads. The CTA opens the in-hub
        // access panel (OpenOffer → OpenAccess); synthesize an offer when the server sends none.
        return _authorize.Status switch
        {
            AuthorizeStatus.Granted => new CatalogAccess { State = StoreAccessState.Granted },
            AuthorizeStatus.Locked => new CatalogAccess
            {
                State = StoreAccessState.Locked,
                Reason = _authorize.Reason,
                Offer = _authorize.Offer ?? new CatalogOffer
                {
                    Headline = _authorize.Reason == "access-pending"
                        ? "Access requested — awaiting approval"
                        : "Unlock the Gabriel Capeletto Store",
                    ActionUrl = "",
                },
            },
            // MustUpdate / Unreachable: we cannot prove entitlement → lock, no offer to sell.
            _ => new CatalogAccess { State = StoreAccessState.Locked },
        };
    }

    /// <summary>
    /// Per-app installability derived from the verified lease. Only meaningful under a granted
    /// device; a locked device is already handled device-wide by <see cref="EffectiveAccess"/>.
    /// </summary>
    private CatalogInstall? EntitlementOverride(string appId)
    {
        if (_authorize?.Status != AuthorizeStatus.Granted || _authorize.Lease is null)
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        const long skewSeconds = 300; // ±5 min clock tolerance (contract §5)

        foreach (var e in _authorize.Lease.Entitlements)
        {
            if (!string.Equals(e.AppId, appId, StringComparison.Ordinal))
            {
                continue;
            }

            var granted = string.Equals(e.State, "granted", StringComparison.Ordinal)
                && (e.NotAfter is null || now <= e.NotAfter.Value + skewSeconds);
            return new CatalogInstall { State = granted ? AppInstallState.Installable : AppInstallState.Locked };
        }

        // Granted device but no entitlement for this app → not installable.
        return new CatalogInstall { State = AppInstallState.Locked };
    }

    // Tile actions (see contract/delivery-lifecycle). Install/Update pull the package from the
    // server and install it locally; Open gates on the lease then launches; Uninstall removes it.
    private void OnInstall(ShelfItemViewModel item) => _ = InstallOrUpdateAsync(item);
    private void OnUpdate(ShelfItemViewModel item) => _ = InstallOrUpdateAsync(item);
    private void OnOpen(ShelfItemViewModel item) => LaunchApp(item);
    private void OnUninstall(ShelfItemViewModel item) => UninstallApp(item);

    private async Task InstallOrUpdateAsync(ShelfItemViewModel item)
    {
        if (!_delivery.IsConfigured)
        {
            return;
        }

        SyncStatus = $"Installing {item.Name}…";
        try
        {
            var outcome = await _delivery.RequestAsync(item.Id);
            switch (outcome.Status)
            {
                case DeliveryStatus.Ready:
                    var installed = await _installer.InstallAsync(outcome.Response!);
                    _install.Apps[item.Id] = new InstalledEntry
                    {
                        Version = installed.Version,
                        EntryExe = installed.EntryExe,
                        InstallDir = installed.InstallDir,
                    };
                    _installStore.Save(_install);
                    SyncStatus = $"{item.Name} installed";
                    Rebuild(DateTimeOffset.UtcNow);
                    break;

                case DeliveryStatus.Denied:
                    SyncStatus = $"{item.Name}: access denied";
                    break;
                case DeliveryStatus.NoPackage:
                    SyncStatus = $"{item.Name}: no package published yet";
                    break;
                default:
                    SyncStatus = "Store server offline";
                    break;
            }
        }
        catch (Exception ex)
        {
            SyncStatus = $"Install failed: {ex.Message}";
        }
    }

    private void LaunchApp(ShelfItemViewModel item)
    {
        if (!_install.Apps.TryGetValue(item.Id, out var entry))
        {
            return;
        }

        if (!MayLaunch(item.Id))
        {
            SyncStatus = $"{item.Name}: access expired or revoked";
            return;
        }

        try
        {
            _installer.Launch(new InstalledApp
            {
                AppId = item.Id,
                Version = entry.Version,
                EntryExe = entry.EntryExe,
                InstallDir = entry.InstallDir,
            });
        }
        catch (Exception ex)
        {
            SyncStatus = $"Couldn't open {item.Name}: {ex.Message}";
        }
    }

    private void UninstallApp(ShelfItemViewModel item)
    {
        try
        {
            _installer.Uninstall(item.Id);
        }
        catch
        {
            // Best effort — even if the folder is partly locked, drop the record so the UI recovers.
        }

        _install.Apps.Remove(item.Id);
        _installStore.Save(_install);
        Rebuild(DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// The launch gate: the app's entitlement in the current lease must be granted and within
    /// validity (±5 min skew). No verified lease + a configured server ⇒ refuse (fail-closed;
    /// offline lease-grace is a follow-up). Catalog-only mode (no server) ⇒ allow.
    /// </summary>
    private bool MayLaunch(string appId)
    {
        var lease = _authorize?.Lease;
        if (lease is null)
        {
            return !_entitlement.IsConfigured;
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        const long skewSeconds = 300;

        foreach (var e in lease.Entitlements)
        {
            if (!string.Equals(e.AppId, appId, StringComparison.Ordinal))
            {
                continue;
            }

            return string.Equals(e.State, "granted", StringComparison.Ordinal)
                && (e.NotAfter is null || now <= e.NotAfter.Value + skewSeconds);
        }

        return false; // granted device, but no entitlement for this app
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

    [RelayCommand]
    private void OpenAccess()
    {
        AccessMessage = "";
        ShowSettings = false;
        ShowAccess = true;
    }

    [RelayCommand]
    private void CloseAccess() => ShowAccess = false;

    [RelayCommand]
    private async Task RequestAccess()
    {
        if (string.IsNullOrWhiteSpace(AccessName) || string.IsNullOrWhiteSpace(AccessEmail))
        {
            AccessMessage = "Enter your full name and email.";
            return;
        }

        AccessMessage = "Sending request…";
        var result = await _entitlement.RequestAccessAsync(AccessName, AccessEmail, AccessPhone);
        if (result.Submitted)
        {
            AccessMessage = "Request sent. You'll get a license key by email once approved.";
            NotifyAccessStateChanged();
            await RefreshAsync();
        }
        else
        {
            AccessMessage = $"Couldn't send the request. {result.Detail}";
        }
    }

    [RelayCommand]
    private async Task ActivateLicense()
    {
        if (string.IsNullOrWhiteSpace(LicenseKeyInput))
        {
            AccessMessage = "Paste the license key from your email.";
            return;
        }

        _entitlement.SetLicense(LicenseKeyInput);
        AccessMessage = "Activating…";
        NotifyAccessStateChanged();
        await RefreshAsync();
        AccessMessage = HasLicense ? "License saved. Syncing your access…" : "Couldn't save the license.";
    }

    private void NotifyAccessStateChanged()
    {
        OnPropertyChanged(nameof(HasLicense));
        OnPropertyChanged(nameof(AccessRequested));
        OnPropertyChanged(nameof(IsAccessPending));
        OnPropertyChanged(nameof(DeviceId));
    }

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

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

    private CatalogManifest? _manifest;
    private CatalogObservationState _observation = new();
    private InstallState _install = new();
    private string _shelfSignature = "";

    public MainViewModel(
        ICatalogSource catalogSource,
        ICatalogStateStore observationStore,
        IInstallStateStore installStore,
        IDeviceFingerprintCollector fingerprintCollector,
        IIdentityBaselineStore baselineStore)
    {
        _catalogSource = catalogSource;
        _observationStore = observationStore;
        _installStore = installStore;
        DeviceIdentity = new DeviceIdentityViewModel(fingerprintCollector, baselineStore);
    }

    /// <summary>Design-time constructor: seeds the previewer with mixed states so badges/buttons show.</summary>
    public MainViewModel() : this(
        new DesignCatalogSource(), new NullCatalogStateStore(), new NullInstallStateStore(),
        new NullFingerprintCollector(), new NullIdentityBaselineStore())
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
    public partial int AppCount { get; set; }

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

        try
        {
            var manifest = await _catalogSource.GetCatalogAsync();
            var now = DateTimeOffset.UtcNow;

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

            Rebuild(now);

            // A successful fetch clears any prior error — including a silent refresh
            // that recovers after the store was briefly unreachable.
            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            if (!silent)
            {
                Apps.Clear();
                AppCount = 0;
                NewCount = 0;
                UpdatedCount = 0;
                ErrorMessage = $"Couldn't reach the store catalog.\n{ex.Message}";
            }
            // Silent failure: keep whatever is already on the shelves.
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

        var items = new List<ShelfItemViewModel>(_manifest.Apps.Count);
        var signature = new StringBuilder();
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

            items.Add(new ShelfItemViewModel(app, installedVersion, updateAvailable, status, InstallApp));
            signature.Append(app.Id).Append('|').Append((int)status).Append('|')
                     .Append(installedVersion ?? "-").Append('|').Append(app.Version).Append(';');
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

using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GabrielCapelettoStore.Hub.Catalog;

namespace GabrielCapelettoStore.Hub.ViewModels;

/// <summary>
/// A single shelf item: the catalog app plus how it reads to the user right now
/// (installed state, available version, novelty). Immutable per render — the whole
/// list is rebuilt whenever state changes.
/// </summary>
public sealed partial class ShelfItemViewModel : ObservableObject
{
    private readonly Action<ShelfItemViewModel>? _installAction;
    private readonly Action<string>? _openOfferAction;
    private readonly bool _isOnline;

    public ShelfItemViewModel(
        CatalogApp app,
        string? installedVersion,
        bool updateAvailable,
        NoveltyStatus status,
        bool isOnline = true,
        Action<ShelfItemViewModel>? installAction = null,
        CatalogAccess? access = null,
        Action<string>? openOfferAction = null)
    {
        App = app;
        InstalledVersion = installedVersion;
        UpdateAvailable = updateAvailable;
        Status = status;
        _isOnline = isOnline;
        _installAction = installAction;
        _openOfferAction = openOfferAction;

        // Resolution rule (see contract/): an app is installable when its own install
        // override says so, otherwise it inherits the device-wide access state.
        access ??= new CatalogAccess();
        IsLocked = app.Install is not null
            ? app.Install.State == AppInstallState.Locked
            : access.State == StoreAccessState.Locked;
        Offer = app.Install?.Offer ?? access.Offer;
    }

    public CatalogApp App { get; }
    public string Id => App.Id;
    public string Name => App.Name;

    /// <summary>Placeholder inner identity until apps ship real icons — the app's first letter.</summary>
    public string Initial => string.IsNullOrWhiteSpace(Name) ? "?" : Name.Trim()[..1].ToUpperInvariant();
    public string? Summary => App.Summary;
    public AppIdentityMode IdentityMode => App.IdentityMode;

    public string AvailableVersion => App.Version;
    public string? InstalledVersion { get; }
    public bool IsInstalled => InstalledVersion is not null;
    public bool UpdateAvailable { get; }

    public NoveltyStatus Status { get; }
    public bool IsNew => Status == NoveltyStatus.New;
    public bool IsUpdated => Status == NoveltyStatus.Updated;

    /// <summary>
    /// Single version line under the summary:
    ///  - not installed → the version available to install
    ///  - update available → the transition installed → available
    ///  - up to date → the installed version
    /// </summary>
    public string VersionSubline =>
        !IsInstalled ? $"v{AvailableVersion} · available"
        : UpdateAvailable ? $"v{InstalledVersion} → v{AvailableVersion}"
        : $"v{AvailableVersion} · installed";

    public string InstallButtonText =>
        !IsInstalled ? "Install"
        : UpdateAvailable ? "Update"
        : "Installed";

    /// <summary>
    /// Store-access state for this app, resolved from the per-app override or the
    /// device-wide access. Locked = visible but not installable; show the offer.
    /// </summary>
    public bool IsLocked { get; }

    /// <summary>The sales hook to surface when locked (per-app offer, else device offer).</summary>
    public CatalogOffer? Offer { get; }

    public bool ShowInstallButton => !IsLocked;
    public string OfferHeadline => Offer?.Headline ?? "Buy store access";

    /// <summary>Dims the app identity (chip + labels) when locked; the lock/CTA stay bright.</summary>
    public double IdentityOpacity => IsLocked ? 0.4 : 1.0;

    /// <summary>Enabled only when installable, there is an action to take, AND the store is
    /// reachable (installing/updating downloads from the backend — not possible offline).</summary>
    public bool CanInstall => !IsLocked && (!IsInstalled || UpdateAvailable) && _isOnline;

    [RelayCommand]
    private void Install() => _installAction?.Invoke(this);

    [RelayCommand]
    private void OpenOffer()
    {
        if (!string.IsNullOrWhiteSpace(Offer?.ActionUrl))
        {
            _openOfferAction?.Invoke(Offer.ActionUrl);
        }
    }
}

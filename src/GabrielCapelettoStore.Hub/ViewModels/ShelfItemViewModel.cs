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
    private readonly Action<ShelfItemViewModel>? _openAction;
    private readonly Action<ShelfItemViewModel>? _updateAction;
    private readonly Action<ShelfItemViewModel>? _uninstallAction;
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
        Action<string>? openOfferAction = null,
        CatalogInstall? installOverride = null,
        Action<ShelfItemViewModel>? openAction = null,
        Action<ShelfItemViewModel>? updateAction = null,
        Action<ShelfItemViewModel>? uninstallAction = null)
    {
        App = app;
        InstalledVersion = installedVersion;
        UpdateAvailable = updateAvailable;
        Status = status;
        _isOnline = isOnline;
        _installAction = installAction;
        _openAction = openAction;
        _updateAction = updateAction;
        _uninstallAction = uninstallAction;
        _openOfferAction = openOfferAction;

        // Resolution rule (see contract/): an app is installable when its install state says so
        // (an entitlement-derived override wins over the catalog's own hint), otherwise it
        // inherits the device-wide access state.
        access ??= new CatalogAccess();
        var install = installOverride ?? app.Install;
        IsLocked = install is not null
            ? install.State == AppInstallState.Locked
            : access.State == StoreAccessState.Locked;
        Offer = install?.Offer ?? access.Offer;
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

    /// <summary>Primary action for this tile: Install (not installed) / Update (newer) / Open (up to date).</summary>
    public string PrimaryActionText =>
        !IsInstalled ? "Install"
        : UpdateAvailable ? "Update"
        : "Open";

    /// <summary>
    /// Store-access state for this app, resolved from the per-app override or the
    /// device-wide access. Locked = visible but not installable; show the offer.
    /// </summary>
    public bool IsLocked { get; }

    /// <summary>The sales hook to surface when locked (per-app offer, else device offer).</summary>
    public CatalogOffer? Offer { get; }

    public bool ShowInstallButton => !IsLocked;
    public bool ShowUninstall => IsInstalled && !IsLocked;
    public string OfferHeadline => Offer?.Headline ?? "Buy store access";

    /// <summary>Dims the app identity (chip + labels) when locked; the lock/CTA stay bright.</summary>
    public double IdentityOpacity => IsLocked ? 0.4 : 1.0;

    /// <summary>Install and Update download from the server → need to be online. Open (already
    /// installed, up to date) does not — its gate is the lease, checked on click.</summary>
    private bool NeedsOnline => !IsInstalled || UpdateAvailable;

    public bool CanPrimary => !IsLocked && (!NeedsOnline || _isOnline);

    [RelayCommand]
    private void PrimaryAction()
    {
        if (!IsInstalled)
        {
            _installAction?.Invoke(this);
        }
        else if (UpdateAvailable)
        {
            _updateAction?.Invoke(this);
        }
        else
        {
            _openAction?.Invoke(this);
        }
    }

    [RelayCommand]
    private void Uninstall() => _uninstallAction?.Invoke(this);

    [RelayCommand]
    private void OpenOffer()
    {
        if (!string.IsNullOrWhiteSpace(Offer?.ActionUrl))
        {
            _openOfferAction?.Invoke(Offer.ActionUrl);
        }
    }
}

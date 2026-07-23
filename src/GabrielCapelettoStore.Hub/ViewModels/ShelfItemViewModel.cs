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
    private readonly bool _isOnline;

    public ShelfItemViewModel(
        CatalogApp app,
        string? installedVersion,
        bool updateAvailable,
        NoveltyStatus status,
        bool isOnline = true,
        Action<ShelfItemViewModel>? installAction = null)
    {
        App = app;
        InstalledVersion = installedVersion;
        UpdateAvailable = updateAvailable;
        Status = status;
        _isOnline = isOnline;
        _installAction = installAction;
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

    /// <summary>Enabled only when there is an action to take AND the store is reachable
    /// (installing/updating downloads from the backend — not possible offline).</summary>
    public bool CanInstall => (!IsInstalled || UpdateAvailable) && _isOnline;

    [RelayCommand]
    private void Install() => _installAction?.Invoke(this);
}

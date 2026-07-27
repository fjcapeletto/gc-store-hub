using System;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GabrielCapelettoStore.Hub.Catalog;

namespace GabrielCapelettoStore.Hub.ViewModels;

/// <summary>Web-app tile state + actions (for a `type: web` item like GC Deals).</summary>
public sealed record WebTile(
    bool Subscribed,
    bool Revoked,
    string? OfferHeadline,
    string? OfferUrl,
    Action Subscribe,
    Action Unsubscribe,
    Action OpenInbox,
    Action OpenConfig);

/// <summary>
/// A single shelf item. Two shapes: a packaged <c>app</c> (Install/Open/Update/Uninstall, gated by
/// the lease) or a <c>web</c> item (Subscribe/Open-inbox/Unsubscribe, NOT gated by store access —
/// its gate is the delivery poll: 403 = revoked). Immutable per render.
/// </summary>
public sealed partial class ShelfItemViewModel : ObservableObject
{
    private readonly Action<ShelfItemViewModel>? _installAction;
    private readonly Action<ShelfItemViewModel>? _openAction;
    private readonly Action<ShelfItemViewModel>? _updateAction;
    private readonly Action<ShelfItemViewModel>? _uninstallAction;
    private readonly Action<string>? _openOfferAction;
    private readonly WebTile? _web;
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
        Action<ShelfItemViewModel>? uninstallAction = null,
        WebTile? web = null,
        Bitmap? icon = null)
    {
        App = app;
        Icon = icon;
        InstalledVersion = installedVersion;
        UpdateAvailable = updateAvailable;
        Status = status;
        _isOnline = isOnline;
        _installAction = installAction;
        _openAction = openAction;
        _updateAction = updateAction;
        _uninstallAction = uninstallAction;
        _openOfferAction = openOfferAction;
        _web = web;

        if (_web is not null)
        {
            // Web items are NOT gated by store access — the gate is the delivery poll (403 = revoked).
            IsLocked = _web.Revoked;
            Offer = _web.Revoked
                ? new CatalogOffer
                {
                    Headline = _web.OfferHeadline ?? "You've been removed from this stream",
                    ActionUrl = _web.OfferUrl ?? "",
                }
                : null;
        }
        else
        {
            access ??= new CatalogAccess();
            var install = installOverride ?? app.Install;
            IsLocked = install is not null
                ? install.State == AppInstallState.Locked
                : access.State == StoreAccessState.Locked;
            Offer = install?.Offer ?? access.Offer;
        }
    }

    public CatalogApp App { get; }
    public string Id => App.Id;
    public string Name => App.Name;
    public bool IsWeb => _web is not null;

    /// <summary>The publisher's resolved icon (from iconUrl), or null → fall back to the glyph.</summary>
    public Bitmap? Icon { get; }
    public bool HasIcon => Icon is not null;

    public string? Summary => App.Summary;
    public AppIdentityMode IdentityMode => App.IdentityMode;

    public string AvailableVersion => App.Version;
    public string? InstalledVersion { get; }
    public bool IsInstalled => InstalledVersion is not null;
    public bool UpdateAvailable { get; }

    public NoveltyStatus Status { get; }
    public bool IsNew => Status == NoveltyStatus.New;
    public bool IsUpdated => Status == NoveltyStatus.Updated;

    /// <summary>Version/status line under the name.</summary>
    public string VersionSubline =>
        IsWeb ? (_web!.Subscribed ? "subscribed" : "offers & deals")
        : !IsInstalled ? $"v{AvailableVersion} · available"
        : UpdateAvailable ? $"v{InstalledVersion} → v{AvailableVersion}"
        : $"v{AvailableVersion} · installed";

    /// <summary>Primary action: web → Subscribe/Open(inbox); app → Install/Update/Open.</summary>
    public string PrimaryActionText =>
        IsWeb ? (_web!.Subscribed ? "Open" : "Subscribe")
        : !IsInstalled ? "Install"
        : UpdateAvailable ? "Update"
        : "Open";

    public bool IsLocked { get; }
    public CatalogOffer? Offer { get; }

    public bool ShowInstallButton => !IsLocked;
    public string OfferHeadline => Offer?.Headline ?? "Buy store access";
    public double IdentityOpacity => IsLocked ? 0.4 : 1.0;

    /// <summary>Secondary action: web → Unsubscribe (when subscribed); app → Uninstall (when installed).</summary>
    public bool ShowSecondary => !IsLocked && (IsWeb ? _web!.Subscribed : IsInstalled);
    public string SecondaryActionText => IsWeb ? "Unsubscribe" : "Uninstall";

    /// <summary>Delivery-config gear — only for a subscribed web app (chooses how new deals notify).</summary>
    public bool ShowConfig => IsWeb && !IsLocked && _web!.Subscribed;

    private bool NeedsOnline => !IsWeb && (!IsInstalled || UpdateAvailable);
    public bool CanPrimary => !IsLocked && (!NeedsOnline || _isOnline);

    [RelayCommand]
    private void PrimaryAction()
    {
        if (IsWeb)
        {
            if (_web!.Subscribed)
            {
                _web.OpenInbox();
            }
            else
            {
                _web.Subscribe();
            }

            return;
        }

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
    private void SecondaryAction()
    {
        if (IsWeb)
        {
            _web!.Unsubscribe();
        }
        else
        {
            _uninstallAction?.Invoke(this);
        }
    }

    [RelayCommand]
    private void OpenConfig() => _web?.OpenConfig();

    [RelayCommand]
    private void OpenOffer()
    {
        if (!string.IsNullOrWhiteSpace(Offer?.ActionUrl))
        {
            _openOfferAction?.Invoke(Offer.ActionUrl);
        }
    }
}

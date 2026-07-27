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
    Action OpenInbox);

/// <summary>
/// A single shelf item. Two shapes: a packaged <c>app</c> (Install/Open/Update, gated by the lease) or
/// a <c>web</c> item (Subscribe/Open-inbox, NOT gated by store access — its gate is the delivery poll:
/// 403 = revoked). The tile face is just the clickable icon + title; everything else (unsubscribe,
/// uninstall, delivery, status) lives behind a per-tile Settings gear. Immutable per render.
/// </summary>
public sealed partial class ShelfItemViewModel : ObservableObject
{
    private readonly Action<ShelfItemViewModel>? _installAction;
    private readonly Action<ShelfItemViewModel>? _openAction;
    private readonly Action<ShelfItemViewModel>? _updateAction;
    private readonly Action<ShelfItemViewModel>? _openSettingsAction;
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
        Action<ShelfItemViewModel>? openSettingsAction = null,
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
        _openSettingsAction = openSettingsAction;
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

    public AppIdentityMode IdentityMode => App.IdentityMode;

    public string AvailableVersion => App.Version;
    public string? InstalledVersion { get; }
    public bool IsInstalled => InstalledVersion is not null;
    public bool UpdateAvailable { get; }

    public NoveltyStatus Status { get; }
    public bool IsNew => Status == NoveltyStatus.New;
    public bool IsUpdated => Status == NoveltyStatus.Updated;

    /// <summary>Developer-stage app (only present for developer-marked devices); shows a "DEV" badge.</summary>
    public bool IsDeveloper => string.Equals(App.Stage, "developer", StringComparison.OrdinalIgnoreCase);

    // DEV takes the top-right badge slot; suppress NEW/UPD there so they don't overlap.
    public bool ShowNewBadge => IsNew && !IsDeveloper;
    public bool ShowUpdatedBadge => IsUpdated && !IsDeveloper;

    /// <summary>Primary verb: web → Subscribe/Open(inbox); app → Install/Update/Open.</summary>
    public string PrimaryActionText =>
        IsWeb ? (_web!.Subscribed ? "Open" : "Subscribe")
        : !IsInstalled ? "Install"
        : UpdateAvailable ? "Update"
        : "Open";

    public bool IsLocked { get; }
    public CatalogOffer? Offer { get; }

    /// <summary>The verb shown when the mouse is over the icon (the icon is the primary affordance).</summary>
    public string IconHoverText => IsLocked ? "Unlock" : PrimaryActionText;

    /// <summary>The icon is clickable whenever there's an action: unlock (locked) or the primary action.</summary>
    public bool IconEnabled => IsLocked || CanPrimary;

    public string OfferHeadline => Offer?.Headline ?? "Buy store access";
    public double IdentityOpacity => IsLocked ? 0.4 : 1.0;

    /// <summary>The per-tile Settings gear is always available — it holds status plus whatever actions
    /// the state offers (web: subscribe/unsubscribe/delivery; app: uninstall once installed).</summary>
    public bool ShowSettingsGear => true;

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

    /// <summary>Clicking the icon = unlock when locked, otherwise the state's primary action.</summary>
    [RelayCommand]
    private void IconAction()
    {
        if (IsLocked)
        {
            OpenOffer();
        }
        else
        {
            PrimaryAction();
        }
    }

    [RelayCommand]
    private void OpenSettings() => _openSettingsAction?.Invoke(this);

    [RelayCommand]
    private void OpenOffer()
    {
        if (!string.IsNullOrWhiteSpace(Offer?.ActionUrl))
        {
            _openOfferAction?.Invoke(Offer.ActionUrl);
        }
    }
}

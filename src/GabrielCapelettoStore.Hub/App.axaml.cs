using System;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using GabrielCapelettoStore.Hub.Catalog;
using GabrielCapelettoStore.Hub.Entitlement;
using GabrielCapelettoStore.Hub.Identity;
using GabrielCapelettoStore.Hub.Update;
using GabrielCapelettoStore.Hub.ViewModels;
using GabrielCapelettoStore.Hub.Views;
using Microsoft.Extensions.Configuration;

namespace GabrielCapelettoStore.Hub;

public partial class App : Application
{
    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private MainWindow? _mainWindow;
    private TrayIcon? _trayIcon;
    private IUpdateService _updateService = new NullUpdateService();

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _desktop = desktop;

            // The hub lives in the tray: closing the window hides it, so the app must not
            // shut down just because no window is open. Quit is explicit (tray menu).
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var configuration = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
                .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: false)
                .AddEnvironmentVariables(prefix: "GCSTORE_")
                .Build();

            var catalogOptions = configuration
                .GetSection(CatalogOptions.SectionName)
                .Get<CatalogOptions>() ?? new CatalogOptions();

            var entitlementOptions = new EntitlementOptions
            {
                BaseUrl = configuration.GetSection(EntitlementOptions.EntitlementSection)["BaseUrl"],
                License = configuration.GetSection(EntitlementOptions.DeviceSection)["License"],
            };

            var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            ICatalogSource catalogSource = new HttpCatalogSource(httpClient, catalogOptions);
            ICatalogStateStore observationStore = new FileCatalogStateStore();
            IInstallStateStore installStore = new FileInstallStateStore();
            ICatalogCache catalogCache = new FileCatalogCache();

            IDeviceFingerprintCollector fingerprintCollector;
            if (OperatingSystem.IsWindows())
            {
                fingerprintCollector = new WindowsFingerprintCollector();
            }
            else
            {
                fingerprintCollector = new NullFingerprintCollector();
            }

            // Device identity: the hub-generated id + the license it presents (dev/config license
            // seeds the store fallback so the current test flow keeps working).
            var deviceIdentity = new DeviceIdentity(new FileDeviceStore(), fingerprintCollector, entitlementOptions.License);
            IEntitlementService entitlement = new HttpEntitlementService(httpClient, entitlementOptions, deviceIdentity);

            IIdentityBaselineStore baselineStore = new FileIdentityBaselineStore();

            _updateService = new UpdateService(
                configuration["Update:GithubRepo"], configuration["Update:FeedUrl"]);

            var viewModel = new MainViewModel(
                catalogSource, observationStore, installStore, catalogCache, fingerprintCollector, baselineStore,
                entitlement, _updateService);

            _mainWindow = new MainWindow { DataContext = viewModel };

            SetupTrayIcon();

            // Start in the tray; only pop the window open on the first run after an install,
            // so the user sees the hub come up once and finds it in the tray thereafter.
            if (Program.IsFirstRun)
            {
                ShowMainWindow();
            }

            // Kick off the first catalog fetch; LoadAsync never throws (errors become UI state).
            _ = viewModel.LoadAsync();

            // Self-update check — surfaces progress in the header (no-op unless a source is
            // configured AND this is a real Velopack install).
            _ = _updateService.CheckAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void SetupTrayIcon()
    {
        var open = new NativeMenuItem("Open");
        open.Click += (_, _) => ShowMainWindow();

        var quit = new NativeMenuItem("Quit");
        quit.Click += (_, _) => QuitApp();

        var menu = new NativeMenu();
        menu.Items.Add(open);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(quit);

        _trayIcon = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://GabrielCapelettoStore.Hub/Assets/gcstore.ico"))),
            ToolTipText = "Gabriel Capeletto Store",
            Menu = menu,
            IsVisible = true,
        };
        _trayIcon.Clicked += (_, _) => ShowMainWindow();

        // Register on the application so Avalonia owns its lifetime and disposes it on shutdown.
        TrayIcon.SetIcons(this, new TrayIcons { _trayIcon });
    }

    private void ShowMainWindow()
    {
        if (_mainWindow is null)
        {
            return;
        }

        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    private void QuitApp()
    {
        if (_mainWindow is not null)
        {
            _mainWindow.AllowClose = true;
        }

        if (_trayIcon is not null)
        {
            _trayIcon.IsVisible = false;
        }

        // If an update was downloaded but not applied, swap it in after we exit.
        _updateService.ApplyOnExit();

        _desktop?.Shutdown();
    }
}

using System;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using GabrielCapelettoStore.Hub.Api;
using GabrielCapelettoStore.Hub.Catalog;
using GabrielCapelettoStore.Hub.Delivery;
using GabrielCapelettoStore.Hub.Entitlement;
using GabrielCapelettoStore.Hub.Icons;
using GabrielCapelettoStore.Hub.Identity;
using GabrielCapelettoStore.Hub.Notifications;
using GabrielCapelettoStore.Hub.Startup;
using GabrielCapelettoStore.Hub.Update;
using GabrielCapelettoStore.Hub.Web;
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

            // Dev-only: GCSTORE_MockCatalog=1 swaps in a many-app catalog (and skips entitlement) to
            // exercise the paged shelf. Never set in production.
            var useMockCatalog = configuration["MockCatalog"] is "1" or "true";

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

            // The catalog request carries the device identity so the response can vary per device
            // (developer-marked devices also see unlocked "developer"-stage apps). See developer-stage.md.
            ICatalogSource catalogSource = useMockCatalog
                ? new MockCatalogSource()
                : new HttpCatalogSource(httpClient, catalogOptions, deviceIdentity);

            IEntitlementService entitlement = useMockCatalog
                ? new NullEntitlementService()
                : new HttpEntitlementService(httpClient, entitlementOptions, deviceIdentity);
            IDeliveryService delivery = new HttpDeliveryService(httpClient, entitlementOptions, deviceIdentity);
            IAppInstaller installer = new AppInstaller(httpClient);
            IWebService webService = new HttpWebService(httpClient, entitlementOptions, deviceIdentity);
            IAppIconCache iconCache = new FileAppIconCache(httpClient);
            var webManager = new WebManager(webService, new FileWebStore(), new ToastService(), iconCache);

            IIdentityBaselineStore baselineStore = new FileIdentityBaselineStore();

            _updateService = new UpdateService(
                configuration["Update:GithubRepo"], configuration["Update:FeedUrl"]);

            // Launch-at-startup (mandatory): only for a real installed (Velopack) Windows hub. Re-applied
            // every launch, so an already-installed client picks it up the first time the updated hub runs.
            ResolveStartupService().EnsureRegistered();

            // type:api token broker (shared for embedded + standalone api apps). Dev-only tester
            // (GCSTORE_ApiTester=1) exercises the tokenization loop without the real app UX.
            IApiTokenService apiTokenService = useMockCatalog
                ? new NullApiTokenService()
                : new HttpApiTokenService(httpClient, entitlementOptions, deviceIdentity);
            var apiBroker = new ApiTokenBroker(apiTokenService);
            var apiDescriptors = new ApiDescriptorClient(apiBroker);
            var apiTester = configuration["ApiTester"] is "1" or "true"
                ? new ApiTesterViewModel(apiBroker)
                : null;

            var viewModel = new MainViewModel(
                catalogSource, observationStore, installStore, catalogCache, fingerprintCollector, baselineStore,
                entitlement, _updateService, delivery, installer, webManager, iconCache, apiTester, apiDescriptors);

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

            // Start the web-app poller (GC Deals etc.) — notifies even while in the tray.
            webManager.Start();

            // Self-update check — surfaces progress in the header (no-op unless a source is
            // configured AND this is a real Velopack install).
            _ = _updateService.CheckAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }

    // Auto-start is managed only for a real installed Windows hub. Velopack installs to
    // %LocalAppData%\<AppId>\current\<exe> with Update.exe in the parent; the `current` dir is stable
    // across updates, so ProcessPath there is a durable Run-key target. Source/dev runs (bin\Debug)
    // don't match → no-op service, registry untouched.
    private static IStartupService ResolveStartupService()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new NullStartupService();
        }

        try
        {
            var exe = Environment.ProcessPath;
            var dir = string.IsNullOrEmpty(exe) ? null : System.IO.Path.GetDirectoryName(exe);
            if (dir is not null &&
                string.Equals(System.IO.Path.GetFileName(dir), "current", StringComparison.OrdinalIgnoreCase))
            {
                var root = System.IO.Path.GetDirectoryName(dir);
                if (root is not null && System.IO.File.Exists(System.IO.Path.Combine(root, "Update.exe")))
                {
                    return new WindowsStartupService(exe!);
                }
            }
        }
        catch
        {
            // Anything unexpected → don't manage auto-start.
        }

        return new NullStartupService();
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

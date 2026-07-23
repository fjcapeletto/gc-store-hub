using System;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using GabrielCapelettoStore.Hub.Catalog;
using GabrielCapelettoStore.Hub.Identity;
using GabrielCapelettoStore.Hub.ViewModels;
using GabrielCapelettoStore.Hub.Views;
using Microsoft.Extensions.Configuration;
using Velopack;
using Velopack.Sources;

namespace GabrielCapelettoStore.Hub;

public partial class App : Application
{
    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private MainWindow? _mainWindow;
    private TrayIcon? _trayIcon;

    private UpdateManager? _updateManager;
    private UpdateInfo? _pendingUpdate;

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

            IIdentityBaselineStore baselineStore = new FileIdentityBaselineStore();

            var viewModel = new MainViewModel(
                catalogSource, observationStore, installStore, catalogCache, fingerprintCollector, baselineStore);

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

            // Best-effort self-update check (no-op unless an update source is configured
            // AND this is a real Velopack install).
            _ = CheckForUpdatesAsync(configuration["Update:GithubRepo"], configuration["Update:FeedUrl"]);
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

        // If an update was downloaded, swap it in after we exit — next launch is the new version.
        if (_updateManager is not null && _pendingUpdate is not null)
        {
            try
            {
                _updateManager.WaitExitThenApplyUpdates(_pendingUpdate, silent: true, restart: false);
            }
            catch
            {
                // Applying an update must never block quitting.
            }
        }

        _desktop?.Shutdown();
    }

    private async Task CheckForUpdatesAsync(string? githubRepo, string? feedUrl)
    {
        // Prefer the GitHub Releases feed (the hub's binaries are public — no infra needed);
        // fall back to a plain static feed URL if one is configured instead.
        IUpdateSource? source = null;
        if (!string.IsNullOrWhiteSpace(githubRepo))
        {
            source = new GithubSource(githubRepo, accessToken: null, prerelease: false);
        }
        else if (!string.IsNullOrWhiteSpace(feedUrl))
        {
            source = new SimpleWebSource(feedUrl);
        }

        if (source is null)
        {
            return;
        }

        try
        {
            var manager = new UpdateManager(source);
            if (!manager.IsInstalled)
            {
                return; // running from source / not a Velopack install — nothing to update.
            }

            var info = await manager.CheckForUpdatesAsync().ConfigureAwait(false);
            if (info is null)
            {
                return;
            }

            await manager.DownloadUpdatesAsync(info).ConfigureAwait(false);

            // Staged, not applied: it swaps in on the next quit/relaunch, never mid-use.
            _updateManager = manager;
            _pendingUpdate = info;
        }
        catch
        {
            // Update checks are best-effort; a bad/unreachable feed must not affect the hub.
        }
    }
}

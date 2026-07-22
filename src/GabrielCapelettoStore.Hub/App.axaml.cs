using System;
using System.Net.Http;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using GabrielCapelettoStore.Hub.Catalog;
using GabrielCapelettoStore.Hub.Identity;
using GabrielCapelettoStore.Hub.ViewModels;
using GabrielCapelettoStore.Hub.Views;
using Microsoft.Extensions.Configuration;

namespace GabrielCapelettoStore.Hub;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
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

            IDeviceFingerprintCollector fingerprintCollector;
            if (OperatingSystem.IsWindows())
            {
                fingerprintCollector = new WindowsFingerprintCollector();
            }
            else
            {
                fingerprintCollector = new NullFingerprintCollector();
            }

            var viewModel = new MainViewModel(catalogSource, observationStore, installStore, fingerprintCollector);
            desktop.MainWindow = new MainWindow { DataContext = viewModel };

            // Kick off the first catalog fetch; LoadAsync never throws (errors become UI state).
            _ = viewModel.LoadAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }
}

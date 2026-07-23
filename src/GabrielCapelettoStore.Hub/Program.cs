using Avalonia;
using System;
using System.Threading;
using Velopack;

namespace GabrielCapelettoStore.Hub;

sealed class Program
{
    /// <summary>True only on the first launch right after an install/update (set by Velopack).</summary>
    public static bool IsFirstRun { get; private set; }

    private static Mutex? _instanceMutex;

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // Velopack must run first: it handles the install / update / uninstall hooks and
        // exits early for those. WithFirstRun fires only right after a (re)install.
        VelopackApp.Build()
            .OnFirstRun(_ => IsFirstRun = true)
            .Run();

        // Single instance: if the hub is already running, just exit — its tray icon is there.
        _instanceMutex = new Mutex(initiallyOwned: true, @"Local\GabrielCapelettoStoreHub", out var createdNew);
        if (!createdNew)
        {
            return;
        }

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            _instanceMutex.ReleaseMutex();
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}

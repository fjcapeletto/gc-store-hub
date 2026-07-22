using System;
using Avalonia.Controls;
using Avalonia.Threading;
using GabrielCapelettoStore.Hub.ViewModels;

namespace GabrielCapelettoStore.Hub.Views;

public partial class MainWindow : Window
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(1);

    // Ignore focus-triggered refreshes that come right after another one — including
    // the very click that activates the window (which must not rebuild the shelves).
    private static readonly TimeSpan ActivateDebounce = TimeSpan.FromSeconds(30);

    private DispatcherTimer? _refreshTimer;
    private DateTime _lastActivateRefreshUtc = DateTime.MinValue;

    public MainWindow()
    {
        InitializeComponent();

        Opened += OnOpened;
        Activated += OnActivated;
        Closed += OnClosed;
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private void OnOpened(object? sender, EventArgs e)
    {
        // Auto-refresh on a gentle hourly cadence while the window is open.
        _refreshTimer = new DispatcherTimer { Interval = RefreshInterval };
        _refreshTimer.Tick += (_, _) => Refresh();
        _refreshTimer.Start();
    }

    // Re-check when the user brings the window back into focus, but debounced so a
    // click that merely activates the window does not trigger a rebuild mid-click.
    private void OnActivated(object? sender, EventArgs e)
    {
        var nowUtc = DateTime.UtcNow;
        if (nowUtc - _lastActivateRefreshUtc < ActivateDebounce)
        {
            return;
        }

        _lastActivateRefreshUtc = nowUtc;
        Refresh();
    }

    private void OnClosed(object? sender, EventArgs e) => _refreshTimer?.Stop();

    private void Refresh()
    {
        if (ViewModel is { } vm)
        {
            _ = vm.RefreshAsync();
        }
    }
}

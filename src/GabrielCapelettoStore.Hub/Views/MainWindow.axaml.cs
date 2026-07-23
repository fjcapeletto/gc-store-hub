using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using GabrielCapelettoStore.Hub.ViewModels;

namespace GabrielCapelettoStore.Hub.Views;

public partial class MainWindow : Window
{
    // Gentle cadence while reachable; a faster retry while offline so recovery feels automatic.
    private static readonly TimeSpan OnlineInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan OfflineRetryInterval = TimeSpan.FromSeconds(60);

    // Ignore focus-triggered refreshes that come right after another one — including
    // the very click that activates the window (which must not rebuild the shelves).
    private static readonly TimeSpan ActivateDebounce = TimeSpan.FromSeconds(30);

    private DispatcherTimer? _refreshTimer;
    private DateTime _lastActivateRefreshUtc = DateTime.MinValue;

    /// <summary>When false, closing the window hides it to the tray instead of exiting.
    /// The tray's Quit sets this true so the app can actually shut down.</summary>
    public bool AllowClose { get; set; }

    public MainWindow()
    {
        InitializeComponent();

        Opened += OnOpened;
        Activated += OnActivated;
        Closed += OnClosed;
    }

    // Close (the window X) hides to the tray; the app keeps running there until Quit.
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!AllowClose)
        {
            e.Cancel = true;
            Hide();
        }

        base.OnClosing(e);
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private void OnOpened(object? sender, EventArgs e)
    {
        _refreshTimer = new DispatcherTimer { Interval = OnlineInterval };
        _refreshTimer.Tick += (_, _) => Refresh();
        _refreshTimer.Start();

        if (ViewModel is { } vm)
        {
            vm.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    // Adaptive recovery: retry faster while offline, back off once reachable again.
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsOnline) && _refreshTimer is not null && ViewModel is { } vm)
        {
            _refreshTimer.Interval = vm.IsOnline ? OnlineInterval : OfflineRetryInterval;
        }
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

    private void OnClosed(object? sender, EventArgs e)
    {
        _refreshTimer?.Stop();
        if (ViewModel is { } vm)
        {
            vm.PropertyChanged -= OnViewModelPropertyChanged;
        }
    }

    private void Refresh()
    {
        if (ViewModel is { } vm)
        {
            _ = vm.RefreshAsync();
        }
    }

    private void OnCopyDiagnostics(object? sender, RoutedEventArgs e)
    {
        var text = ViewModel?.DeviceIdentity.DiagnosticsText;
        if (!string.IsNullOrEmpty(text) && Clipboard is not null)
        {
            _ = Clipboard.SetTextAsync(text);
        }
    }
}

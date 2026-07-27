using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using GabrielCapelettoStore.Hub.ViewModels;

namespace GabrielCapelettoStore.Hub.Views;

public partial class MainWindow : Window
{
    // Heartbeat cadence follows visibility: beat fast while someone is looking at the LED,
    // slow while parked in the tray (nobody sees it — spares the one server across many hubs).
    private static readonly TimeSpan VisibleInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan HiddenInterval = TimeSpan.FromHours(1);

    // Coalesce the show+activate burst into a single refresh; well under the 30s cadence.
    private static readonly TimeSpan CoalesceWindow = TimeSpan.FromSeconds(3);

    private readonly DispatcherTimer _refreshTimer;
    private DateTime _lastRefreshUtc = DateTime.MinValue;

    /// <summary>When false, closing the window hides it to the tray instead of exiting.
    /// The tray's Quit sets this true so the app can actually shut down.</summary>
    public bool AllowClose { get; set; }

    public MainWindow()
    {
        InitializeComponent();

        // The window starts hidden (tray); the visibility handler speeds it up when shown.
        _refreshTimer = new DispatcherTimer { Interval = HiddenInterval };
        _refreshTimer.Tick += (_, _) => Refresh();
        _refreshTimer.Start();
    }

    // Close (the window X) hides to the tray; the app keeps running there until Quit.
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!AllowClose)
        {
            e.Cancel = true;
            Hide();
        }
        else
        {
            _refreshTimer.Stop();
        }

        base.OnClosing(e);
    }

    // Visibility drives the heartbeat: fast + an immediate check when shown, slow when hidden.
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty)
        {
            var visible = change.GetNewValue<bool>();
            _refreshTimer.Interval = visible ? VisibleInterval : HiddenInterval;
            if (visible)
            {
                Refresh();
            }
        }
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private void Refresh()
    {
        var nowUtc = DateTime.UtcNow;
        if (nowUtc - _lastRefreshUtc < CoalesceWindow)
        {
            return;
        }

        _lastRefreshUtc = nowUtc;
        if (ViewModel is { } vm)
        {
            _ = vm.RefreshAsync();
        }
    }

    // Wheel / horizontal scroll over the shelf turns the page (phone-style paging).
    private void ShelfPager_PointerWheelChanged(object? sender, Avalonia.Input.PointerWheelEventArgs e)
    {
        if (ViewModel is not { } vm)
        {
            return;
        }

        var delta = e.Delta.Y + e.Delta.X;
        if (delta == 0)
        {
            return;
        }

        vm.TurnPage(forward: delta < 0); // wheel down / right → next page
        e.Handled = true;
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

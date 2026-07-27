using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace GabrielCapelettoStore.Hub.Notifications;

/// <summary>Raises a small pop-up near the tray (a hub-drawn toast). Click → the callback.</summary>
public interface IToastService
{
    void Show(string title, string body, Action onClick);
}

/// <summary>
/// A code-drawn Avalonia toast: a borderless, non-activating window in the bottom-right corner that
/// auto-dismisses. Avoids the WinRT/AUMID/COM plumbing native toasts need in an unpackaged app;
/// native Action-Center toasts can be a hardening follow-up.
/// </summary>
public sealed class ToastService : IToastService
{
    private const int Width = 340;
    private const int Height = 96;
    private const int Margin = 16;
    private static readonly TimeSpan Linger = TimeSpan.FromSeconds(7);

    public void Show(string title, string body, Action onClick)
    {
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                ShowCore(title, body, onClick);
            }
            catch
            {
                // A toast must never crash the hub.
            }
        });
    }

    private static void ShowCore(string title, string body, Action onClick)
    {
        var window = new Window
        {
            // Avalonia 12 dropped SystemDecorations — extend the client area over the chrome for a
            // borderless toast.
            ExtendClientAreaToDecorationsHint = true,
            ExtendClientAreaTitleBarHeightHint = -1,
            Topmost = true,
            ShowInTaskbar = false,
            ShowActivated = false,
            CanResize = false,
            Width = Width,
            Height = Height,
            Background = Brushes.Transparent,
        };

        var title2 = new TextBlock
        {
            Text = title,
            FontWeight = FontWeight.SemiBold,
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(0xEA, 0xF1, 0xEC)),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1,
        };
        var body2 = new TextBlock
        {
            Text = body,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0x9D, 0xB3, 0xA6)),
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 2,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 2, 0, 0),
        };

        window.Content = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x10, 0x1A, 0x15)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x47, 0x3A)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 12),
            BoxShadow = BoxShadows.Parse("0 6 18 0 #90000000"),
            Child = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Children = { title2, body2 },
            },
        };

        var timer = new DispatcherTimer { Interval = Linger };
        timer.Tick += (_, _) => { timer.Stop(); Close(window); };

        window.PointerPressed += (_, _) =>
        {
            timer.Stop();
            Close(window);
            try { onClick(); } catch { /* best effort */ }
        };

        window.Opened += (_, _) =>
        {
            var screen = window.Screens.Primary ?? (window.Screens.ScreenCount > 0 ? window.Screens.All[0] : null);
            if (screen is not null)
            {
                var scale = window.RenderScaling;
                var wa = screen.WorkingArea;
                var w = (int)(Width * scale);
                var h = (int)(Height * scale);
                var m = (int)(Margin * scale);
                window.Position = new PixelPoint(wa.X + wa.Width - w - m, wa.Y + wa.Height - h - m);
            }

            timer.Start();
        };

        window.Show();
    }

    private static void Close(Window window)
    {
        try { window.Close(); } catch { /* already closed */ }
    }
}

public sealed class NullToastService : IToastService
{
    public void Show(string title, string body, Action onClick) { }
}

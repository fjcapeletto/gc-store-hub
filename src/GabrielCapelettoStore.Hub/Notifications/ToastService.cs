using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace GabrielCapelettoStore.Hub.Notifications;

/// <summary>One clickable line in a grouped toast: a title and what to do when it's clicked.</summary>
public sealed record ToastLink(string Title, Action OnClick);

/// <summary>Raises a small pop-up near the tray (a hub-drawn toast). Click → the callback.</summary>
public interface IToastService
{
    /// <summary>A single toast: an optional thumbnail + title + body; clicking anywhere runs onClick.</summary>
    void Show(string title, string body, Action onClick, Bitmap? image = null);

    /// <summary>A grouped toast: a header over a list of individually clickable titles (no image).</summary>
    void ShowList(string header, IReadOnlyList<ToastLink> links);
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
    private const int MaxListRows = 5;
    private static readonly TimeSpan Linger = TimeSpan.FromSeconds(7);
    private static readonly TimeSpan ListLinger = TimeSpan.FromSeconds(10);

    private static readonly IBrush PanelBg = new SolidColorBrush(Color.FromRgb(0x10, 0x1A, 0x15));
    private static readonly IBrush PanelBorder = new SolidColorBrush(Color.FromRgb(0x33, 0x47, 0x3A));
    private static readonly IBrush TitleFg = new SolidColorBrush(Color.FromRgb(0xEA, 0xF1, 0xEC));
    private static readonly IBrush BodyFg = new SolidColorBrush(Color.FromRgb(0x9D, 0xB3, 0xA6));

    public void Show(string title, string body, Action onClick, Bitmap? image = null)
        => Post(() => ShowCore(title, body, onClick, image));

    public void ShowList(string header, IReadOnlyList<ToastLink> links) => Post(() => ShowListCore(header, links));

    private static void Post(Action action) => Dispatcher.UIThread.Post(() =>
    {
        try { action(); } catch { /* a toast must never crash the hub */ }
    });

    private static void ShowCore(string title, string body, Action onClick, Bitmap? image)
    {
        var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        textStack.Children.Add(new TextBlock
        {
            Text = title, FontWeight = FontWeight.SemiBold, FontSize = 13, Foreground = TitleFg,
            TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1,
        });
        textStack.Children.Add(new TextBlock
        {
            Text = body, FontSize = 12, Foreground = BodyFg, TextWrapping = TextWrapping.Wrap,
            MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0),
        });

        Control content;
        if (image is not null)
        {
            // Thumbnail (the link's og:image, re-hosted by our server) on the left, text on the right.
            var thumb = new Border
            {
                Width = 56, Height = 56, CornerRadius = new CornerRadius(8), ClipToBounds = true,
                Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center,
                Child = new Image { Source = image, Stretch = Stretch.UniformToFill },
            };
            Grid.SetColumn(thumb, 0);
            Grid.SetColumn(textStack, 1);
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            grid.Children.Add(thumb);
            grid.Children.Add(textStack);
            content = grid;
        }
        else
        {
            content = textStack;
        }

        var window = CreateShell(Height, content);
        var timer = StartAutoClose(window, Linger);
        window.PointerPressed += (_, _) => { timer.Stop(); Close(window); try { onClick(); } catch { /* best effort */ } };
        window.Show();
    }

    private static void ShowListCore(string header, IReadOnlyList<ToastLink> links)
    {
        if (links.Count == 0)
        {
            return;
        }

        var shown = Math.Min(links.Count, MaxListRows);
        var height = 24 + shown * 28 + (links.Count > shown ? 20 : 0) + 24;

        // Row click needs the window/timer, which are created after the content — capture forward.
        Window window = null!;
        DispatcherTimer timer = null!;

        var stack = new StackPanel { Spacing = 2 };
        stack.Children.Add(new TextBlock
        {
            Text = header, FontWeight = FontWeight.SemiBold, FontSize = 13, Foreground = TitleFg,
            Margin = new Thickness(0, 0, 0, 4),
        });

        for (var i = 0; i < shown; i++)
        {
            var link = links[i];
            var row = new Button
            {
                Content = new TextBlock
                {
                    Text = link.Title, FontSize = 12, Foreground = BodyFg,
                    TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1,
                },
                Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                Padding = new Thickness(0, 2), HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            row.Click += (_, _) => { timer.Stop(); Close(window); try { link.OnClick(); } catch { /* best effort */ } };
            stack.Children.Add(row);
        }

        if (links.Count > shown)
        {
            stack.Children.Add(new TextBlock
            {
                Text = $"+{links.Count - shown} more — open GC Deals", FontSize = 11, Foreground = BodyFg,
                Margin = new Thickness(0, 2, 0, 0),
            });
        }

        window = CreateShell(height, stack);
        timer = StartAutoClose(window, ListLinger);
        window.PointerPressed += (_, _) => { timer.Stop(); Close(window); }; // background click = dismiss only
        window.Show();
    }

    /// <summary>A borderless, non-activating, bottom-right toast window wrapping the given content.</summary>
    private static Window CreateShell(double height, Control content)
    {
        var window = new Window
        {
            // Avalonia 12 dropped SystemDecorations — extend the client area over the chrome instead.
            // Blank the Title so the default "Window" caption doesn't bleed through over the content.
            Title = "",
            ExtendClientAreaToDecorationsHint = true,
            ExtendClientAreaTitleBarHeightHint = -1,
            Topmost = true,
            ShowInTaskbar = false,
            ShowActivated = false,
            CanResize = false,
            Width = Width,
            Height = height,
            Background = Brushes.Transparent,
            Content = new Border
            {
                Background = PanelBg,
                BorderBrush = PanelBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 12),
                BoxShadow = BoxShadows.Parse("0 6 18 0 #90000000"),
                Child = content,
            },
        };

        window.Opened += (_, _) =>
        {
            var screen = window.Screens.Primary ?? (window.Screens.ScreenCount > 0 ? window.Screens.All[0] : null);
            if (screen is not null)
            {
                var scale = window.RenderScaling;
                var wa = screen.WorkingArea;
                var w = (int)(Width * scale);
                var h = (int)(height * scale);
                var m = (int)(Margin * scale);
                window.Position = new PixelPoint(wa.X + wa.Width - w - m, wa.Y + wa.Height - h - m);
            }
        };

        return window;
    }

    private static DispatcherTimer StartAutoClose(Window window, TimeSpan linger)
    {
        var timer = new DispatcherTimer { Interval = linger };
        timer.Tick += (_, _) => { timer.Stop(); Close(window); };
        window.Opened += (_, _) => timer.Start();
        return timer;
    }

    private static void Close(Window window)
    {
        try { window.Close(); } catch { /* already closed */ }
    }
}

public sealed class NullToastService : IToastService
{
    public void Show(string title, string body, Action onClick, Bitmap? image = null) { }
    public void ShowList(string header, IReadOnlyList<ToastLink> links) { }
}

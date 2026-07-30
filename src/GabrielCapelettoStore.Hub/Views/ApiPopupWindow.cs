using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using TheArtOfDev.HtmlRenderer.Avalonia;

namespace GabrielCapelettoStore.Hub.Views;

/// <summary>
/// Shows a `pop-up` field's full content in its own top-level window, rendering it as HTML with the
/// Avalonia HtmlRenderer (real CSS/div/list support, no script). White "document" background because
/// scraped page HTML assumes a light page. Free-floating, resizable, with the OS close button.
/// </summary>
public static class ApiPopupWindow
{
    public static void Show(string text)
    {
        text ??= "";

        // TEMP DIAGNOSTIC: dump exactly what arrives so we can tell data-vs-control apart.
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "gcstore-popup-debug");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "last.html"),
                $"len={text.Length}\n----\n{text}");
        }
        catch { /* diagnostic only */ }

        var html = new HtmlLabel
        {
            Text = Document(text),
            AutoSize = true,
            MaxWidth = 560,
        };

        var window = new Window
        {
            Title = "Details",
            Width = 620,
            Height = 680,
            MinWidth = 360,
            MinHeight = 240,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = Brushes.White,
            ShowInTaskbar = false,
            Content = new ScrollViewer
            {
                Padding = new Thickness(4),
                Content = html,
            },
        };

        window.Show();
    }

    // HtmlRenderer paints blank on a bare fragment; give it a real document with a body + base styling.
    private static string Document(string body)
    {
        var trimmed = body.TrimStart();
        if (trimmed.StartsWith("<html", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("<!doctype", StringComparison.OrdinalIgnoreCase))
        {
            return body;
        }

        return "<html><head><style>" +
               "body{font-family:'Segoe UI',Arial,sans-serif;font-size:14px;color:#1a1a1a;line-height:1.5;margin:16px;}" +
               "h1,h2,h3{margin:0.6em 0 0.3em;}a{color:#127a3d;}ul,ol{margin:0.4em 0 0.4em 1.2em;}" +
               "</style></head><body>" + body + "</body></html>";
    }
}

using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace GabrielCapelettoStore.Hub.Converters;

/// <summary>
/// Maps an app id/name to a stable "lit component" colour, so each chip's inner identity
/// glows in its own hue. Placeholder until apps ship their own icons.
/// </summary>
public sealed class AppAccentConverter : IValueConverter
{
    private static readonly Color[] Palette =
    [
        Color.Parse("#5AB0FF"), // blue
        Color.Parse("#F2B44C"), // amber
        Color.Parse("#3FD0B8"), // teal
        Color.Parse("#A98BFF"), // purple
        Color.Parse("#F286B4"), // pink
        Color.Parse("#FF9166"), // coral
        Color.Parse("#7BD154"), // green
        Color.Parse("#E4BD84"), // copper
    ];

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value as string ?? "";
        var hash = 0;
        foreach (var c in key)
        {
            hash = (hash * 31 + c) & 0x7FFFFFFF;
        }

        return new SolidColorBrush(Palette[hash % Palette.Length]);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace GabrielCapelettoStore.Hub.Converters;

/// <summary>
/// Maps an app's icon key to a vector glyph (drawn in a 24x24 box) for the chip's inner
/// identity. Placeholder set until apps ship their own icons; unknown keys get a chip glyph.
/// </summary>
public sealed class AppGlyphConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => (value as string ?? "").ToLowerInvariant() switch
        {
            "cloud" or "weather" => Cloud(),
            "cpu" or "sensor" => Cpu(),
            "database" or "backup" => Cylinder(),
            "photo" or "photos" => Photo(),
            "music" => Music(),
            "calendar" => Calendar(),
            "notes" => Notes(),
            _ => Cpu(),
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static Geometry Ellipse(double cx, double cy, double rx, double ry)
        => new EllipseGeometry(new Rect(cx - rx, cy - ry, rx * 2, ry * 2));

    private static Geometry Rect(double x, double y, double w, double h, double r = 0)
        => new RectangleGeometry(new Rect(x, y, w, h), r, r);

    private static Geometry Path(string data) => Geometry.Parse(data);

    private static Geometry Group(FillRule rule, params Geometry[] parts)
    {
        var g = new GeometryGroup { FillRule = rule };
        foreach (var p in parts)
        {
            g.Children.Add(p);
        }

        return g;
    }

    private static Geometry Cloud() => Group(FillRule.NonZero,
        Ellipse(8, 14, 4.5, 4.5), Ellipse(13.5, 11, 6, 6), Ellipse(18, 14.5, 4, 4), Rect(5, 14, 14, 5));

    private static Geometry Cpu() => Group(FillRule.NonZero,
        Rect(6.5, 6.5, 11, 11, 2),
        Rect(9, 3, 1.6, 3.5), Rect(13.4, 3, 1.6, 3.5),
        Rect(9, 17.5, 1.6, 3.5), Rect(13.4, 17.5, 1.6, 3.5),
        Rect(3, 9, 3.5, 1.6), Rect(3, 13.4, 3.5, 1.6),
        Rect(17.5, 9, 3.5, 1.6), Rect(17.5, 13.4, 3.5, 1.6));

    private static Geometry Cylinder() => Group(FillRule.NonZero,
        Rect(6.5, 6, 11, 12), Ellipse(12, 6, 5.5, 2.3), Ellipse(12, 18, 5.5, 2.3));

    private static Geometry Photo() => Group(FillRule.EvenOdd,
        Rect(3.5, 5.5, 17, 13, 2),
        Ellipse(8, 10, 1.8, 1.8),
        Path("M5,17 L10,11.5 L13,14.5 L16.5,10 L19,17 Z"));

    private static Geometry Music() => Group(FillRule.NonZero,
        Ellipse(8, 17, 2.7, 2.2), Ellipse(16.5, 15.5, 2.7, 2.2),
        Rect(10.3, 6.5, 1.4, 10.5), Rect(18.4, 5, 1.4, 10.5),
        Path("M10.3,6.5 L18.4,4.8 L18.4,7.6 L10.3,9.3 Z"));

    private static Geometry Calendar() => Group(FillRule.EvenOdd,
        Rect(4, 5.5, 16, 14.5, 2),
        Rect(4, 5.5, 16, 3.5),
        Rect(8, 3, 1.6, 4, 0.6), Rect(14, 3, 1.6, 4, 0.6));

    private static Geometry Notes() => Path("M6,3 H13.5 L18,7.5 V21 H6 Z M13.5,3 V7.5 H18");
}

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace GabrielCapelettoStore.Hub.Controls;

/// <summary>
/// Draws the copper wiring for the chip grid like a single-line (unifilar) diagram:
///  - 3 horizontal traces between left/right neighbours (pin to pin);
///  - to each DIAGONAL neighbour, a staircase that leaves the horizontal pins, drops in
///    strictly 45-degree PARALLEL diagonals (constant spacing), turns vertical down the
///    clear column boundary (never over the name/button), then 45-degrees into the pins of
///    the app diagonally below — Weather↘Calendar, and symmetrically Notes↙Music;
///  - a NODE (thick dot) where the ↘ and ↙ traces cross.
/// A faint "current" of light travels the copper (animated dash) so the board reads as live.
/// Sized to match the items grid; recomputes on resize.
/// </summary>
public sealed class ChipTraceOverlay : Control
{
    public static readonly StyledProperty<int> ItemCountProperty =
        AvaloniaProperty.Register<ChipTraceOverlay, int>(nameof(ItemCount));

    public int ItemCount
    {
        get => GetValue(ItemCountProperty);
        set => SetValue(ItemCountProperty, value);
    }

    private const double CellW = 150;
    private const double CellH = 196;
    private const double ChipCenterY = 62;  // chip centre from the tile top
    private const double PinReach = 45;     // chip centre → outer pin tip
    private const double PinGap = 9;        // spacing between the 3 pins
    private const double Lead = 20;         // 45-degree diagonal lead-out length
    private const double Drop = 136;        // vertical segment length (keeps the corners 45°)

    private readonly DispatcherTimer _timer;
    private double _phase;                  // animated dash offset → the flowing current

    public ChipTraceOverlay()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        _timer.Tick += (_, _) =>
        {
            _phase -= 1.4;                  // negative → current flows pin-to-pin outward
            InvalidateVisual();
        };
    }

    protected override void OnAttachedToVisualTree(global::Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(global::Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _timer.Stop();
    }

    static ChipTraceOverlay()
    {
        AffectsRender<ChipTraceOverlay>(ItemCountProperty);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty)
        {
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        var count = ItemCount;
        var w = Bounds.Width;
        if (count <= 0 || w < CellW)
        {
            return;
        }

        var cols = Math.Max(1, (int)(w / CellW));
        var copper = new Pen(new SolidColorBrush(Color.FromRgb(0xC9, 0x9C, 0x59), 0.9), 2)
        {
            LineJoin = PenLineJoin.Round,
            LineCap = PenLineCap.Round,
        };
        // Bright travelling pulse: short "on" segments spaced far apart, offset animated per frame.
        var spark = new Pen(new SolidColorBrush(Color.FromRgb(0x6C, 0xF2, 0xC0), 0.9), 2)
        {
            LineJoin = PenLineJoin.Round,
            LineCap = PenLineCap.Round,
            DashStyle = new DashStyle(new double[] { 1.4, 22 }, _phase),
        };
        var node = new SolidColorBrush(Color.FromRgb(0xD8, 0xAE, 0x6E), 0.95);

        // Collect every trace as a geometry, then stroke copper first and the spark on top.
        var traces = new List<Geometry>();

        for (var i = 0; i < count; i++)
        {
            var col = i % cols;
            var cx = col * CellW + CellW / 2;
            var cy = i / cols * CellH + ChipCenterY;

            // 3 horizontal traces → right neighbour
            if (col < cols - 1 && i + 1 < count)
            {
                var ncx = cx + CellW;
                for (var k = -1; k <= 1; k++)
                {
                    var y = cy + k * PinGap;
                    traces.Add(Line(new Point(cx + PinReach, y), new Point(ncx - PinReach, y)));
                }
            }

            var hasDownRight = col < cols - 1 && i + cols + 1 < count;

            // Lightning ↘ to the down-right neighbour (Weather → Calendar) — 45° parallel.
            if (hasDownRight)
            {
                var bcy = cy + CellH;
                for (var k = -1; k <= 1; k++)
                {
                    var vx = cx + (PinReach + Lead) + PinGap * k; // parallel vertical, spaced with the pins
                    traces.Add(Stair(
                        new Point(cx + PinReach, cy + k * PinGap),
                        new Point(vx, cy + Lead + 2 * PinGap * k),
                        new Point(vx, cy + Lead + Drop + 2 * PinGap * k),
                        new Point(cx + CellW - PinReach, bcy + k * PinGap)));
                }
            }

            // Lightning ↙ to the down-left neighbour (symmetric — Notes → Music) — 45° parallel.
            if (col > 0 && i + cols - 1 < count)
            {
                var bcy = cy + CellH;
                for (var k = -1; k <= 1; k++)
                {
                    var vx = cx - (PinReach + Lead) - PinGap * k;
                    traces.Add(Stair(
                        new Point(cx - PinReach, cy + k * PinGap),
                        new Point(vx, cy + Lead + 2 * PinGap * k),
                        new Point(vx, cy + Lead + Drop + 2 * PinGap * k),
                        new Point(cx - CellW + PinReach, bcy + k * PinGap)));
                }
            }
        }

        foreach (var geo in traces)
        {
            context.DrawGeometry(null, copper, geo);
        }

        foreach (var geo in traces)
        {
            context.DrawGeometry(null, spark, geo);
        }

        // Nodes where ↘ and ↙ traces cross (unifilar junctions) — drawn last, over the copper.
        for (var i = 0; i < count; i++)
        {
            var col = i % cols;
            var cy = i / cols * CellH + ChipCenterY;
            if (col < cols - 1 && i + cols + 1 < count)
            {
                context.DrawEllipse(node, null, new Point(col * CellW + CellW, cy + CellH - 30), 4.5, 4.5);
            }
        }
    }

    private static StreamGeometry Line(Point a, Point b)
    {
        var geo = new StreamGeometry();
        using var ctx = geo.Open();
        ctx.BeginFigure(a, false);
        ctx.LineTo(b);
        ctx.EndFigure(false);
        return geo;
    }

    private static StreamGeometry Stair(Point a, Point b, Point c, Point d)
    {
        var geo = new StreamGeometry();
        using var ctx = geo.Open();
        ctx.BeginFigure(a, false);
        ctx.LineTo(b);
        ctx.LineTo(c);
        ctx.LineTo(d);
        ctx.EndFigure(false);
        return geo;
    }
}

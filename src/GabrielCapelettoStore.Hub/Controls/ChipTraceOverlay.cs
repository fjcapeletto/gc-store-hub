using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace GabrielCapelettoStore.Hub.Controls;

/// <summary>
/// Draws the copper wiring for the chip grid like a single-line (unifilar) diagram:
///  - 3 horizontal traces between left/right neighbours (pin to pin);
///  - to each DIAGONAL neighbour, a staircase that leaves the horizontal pins, drops in
///    strictly 45-degree PARALLEL diagonals (constant spacing), turns vertical down the
///    clear column boundary (never over the name/button), then 45-degrees into the pins of
///    the app diagonally below — Weather↘Calendar, and symmetrically Notes↙Music;
///  - a NODE (thick dot) where the ↘ and ↙ traces cross.
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
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(0xB0, 0x89, 0x4F), 0.85), 2)
        {
            LineJoin = PenLineJoin.Round,
            LineCap = PenLineCap.Round,
        };
        var node = new SolidColorBrush(Color.FromRgb(0xD8, 0xAE, 0x6E), 0.95);

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
                    context.DrawLine(pen, new Point(cx + PinReach, y), new Point(ncx - PinReach, y));
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
                    context.DrawGeometry(null, pen, Stair(
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
                    context.DrawGeometry(null, pen, Stair(
                        new Point(cx - PinReach, cy + k * PinGap),
                        new Point(vx, cy + Lead + 2 * PinGap * k),
                        new Point(vx, cy + Lead + Drop + 2 * PinGap * k),
                        new Point(cx - CellW + PinReach, bcy + k * PinGap)));
                }
            }

            // Node where ↘ and ↙ cross (unifilar junction) — centre of the 2x2 block.
            if (hasDownRight)
            {
                context.DrawEllipse(node, null, new Point(col * CellW + CellW, cy + CellH - 30), 4.5, 4.5);
            }
        }
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

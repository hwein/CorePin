using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace IconGen;

/// Rasterises one group of the source drawing into a straight-alpha BGRA bitmap.
internal static class FrameRenderer
{
    private const double Dpi = 96;

    public static BitmapSource Render(SvgGroup group, int sizePx)
    {
        double factor = sizePx / IconSvg.ReferenceBox;

        var visual = new DrawingVisual();
        using (DrawingContext context = visual.RenderOpen())
        {
            foreach (SvgRect rect in group.Rects) Draw(context, rect, factor);
        }

        var rendered = new RenderTargetBitmap(sizePx, sizePx, Dpi, Dpi, PixelFormats.Pbgra32);
        rendered.Render(visual);

        // WPF renders premultiplied; both .ico and .png carry straight alpha.
        var straight = new FormatConvertedBitmap(rendered, PixelFormats.Bgra32, null, 0);
        straight.Freeze();
        return straight;
    }

    private static void Draw(DrawingContext context, SvgRect rect, double factor)
    {
        Brush? fill = rect.Fill is Color fillColour ? new SolidColorBrush(fillColour) : null;
        // A WPF pen straddles the geometry just like an SVG stroke, so the source values carry over.
        Pen? pen = rect.Stroke is Color strokeColour
            ? new Pen(new SolidColorBrush(strokeColour), rect.StrokeWidth * factor)
            : null;

        var bounds = new Rect(
            rect.X * factor, rect.Y * factor, rect.Width * factor, rect.Height * factor);
        double radius = rect.Rx * factor;
        context.DrawRoundedRectangle(fill, pen, bounds, radius, radius);
    }
}

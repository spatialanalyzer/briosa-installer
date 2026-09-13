using System.Windows;
using System.Windows.Media;

namespace Briosa.Installer.App;

/// <summary>Resolution-independent workspace decoration, drawn by WPF at the display DPI.</summary>
internal static class LayeredPlanes
{
    public static DrawingBrush Create(bool dark, Color graphite, Color silver, Color cyan)
    {
        const double width = 932, height = 800;
        var bounds = new Rect(0, 0, width, height);
        var drawing = new DrawingGroup { ClipGeometry = new RectangleGeometry(bounds) };
        var cross = FrontEdge(400);
        var accentStart = FrontEdge(756);
        var risingEdge = new Point(width, 332);
        var accentEnd = new Point(width, 568);

        // Flat, controlled fills avoid baked-in grain, compression noise and gradient banding.
        drawing.Children.Add(new GeometryDrawing(Fill(dark ? graphite : Colors.White), null, new RectangleGeometry(bounds)));
        Plane(dark ? Tone(graphite, 6) : silver, cross, risingEdge, new(width, height), new(0, height));
        Plane(dark ? Tone(graphite, 1) : Tone(silver, -3), accentStart, accentEnd, new(width, 720));
        Plane(dark ? Tone(graphite, -3) : Tone(silver, 7), new(0, 492), new(width, 720), new(width, height), new(0, height));

        Edge(cross, risingEdge, new SolidColorBrush(dark ? Color.FromArgb(38, 255, 255, 255) : Color.FromArgb(22, 0, 56, 117)));
        Edge(new(0, 492), new(width, 720), new SolidColorBrush(Color.FromArgb(dark ? (byte)28 : (byte)210, 255, 255, 255)));
        // Only the short cyan reflection fades; the plane surfaces are solid colors.
        Edge(accentStart, accentEnd, new LinearGradientBrush(
            Color.FromArgb(0, cyan.R, cyan.G, cyan.B),
            Color.FromArgb(dark ? (byte)165 : (byte)55, cyan.R, cyan.G, cyan.B),
            new Point(0, 1), new Point(1, 0)));

        var brush = new DrawingBrush(drawing)
        {
            Viewbox = bounds,
            ViewboxUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.UniformToFill,
            AlignmentX = AlignmentX.Right,
            AlignmentY = AlignmentY.Bottom
        };
        brush.Freeze();
        return brush;

        Point FrontEdge(double x) => new(x, 492 + (720 - 492) * x / width);

        void Plane(Color color, params Point[] points)
        {
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(points[0], isFilled: true, isClosed: true);
                context.PolyLineTo(points.Skip(1).ToArray(), isStroked: true, isSmoothJoin: false);
            }
            drawing.Children.Add(new GeometryDrawing(Fill(color), null, geometry));
        }

        void Edge(Point start, Point end, Brush stroke) =>
            drawing.Children.Add(new GeometryDrawing(null, new Pen(stroke, 1), new LineGeometry(start, end)));
    }

    private static SolidColorBrush Fill(Color color) => new(color);
    private static Color Tone(Color color, int offset) => Color.FromRgb(
        (byte)Math.Clamp(color.R + offset, 0, 255),
        (byte)Math.Clamp(color.G + offset, 0, 255),
        (byte)Math.Clamp(color.B + offset, 0, 255));
}

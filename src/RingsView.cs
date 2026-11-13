using System.Windows;
using System.Windows.Media;
using UsageViewer.Core;

namespace UsageViewer;

internal sealed class RingsView : FrameworkElement
{
    private static readonly Pen Track = MakePen("#414141");
    private static readonly Pen Green = MakePen("#37D493");
    private static readonly Pen Red = MakePen("#FF606C");
    private static readonly Pen White = MakePen("#FFFFFF");
    private static readonly Pen Gray = MakePen("#858585");
    private Limits limits = Limits.Empty;
    private DateTimeOffset now;

    public void Update(Limits next, DateTimeOffset time)
    {
        limits = next;
        now = time;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        double scale = Math.Min(ActualWidth, ActualHeight) / 48;
        drawing.PushTransform(new TranslateTransform(ActualWidth / 2, ActualHeight / 2));
        drawing.PushTransform(new ScaleTransform(scale, scale));
        DrawRing(drawing, 20, 3.2, Ring.Arcs(limits.Weekly, now), Track.Brush);
        DrawRing(drawing, 13, 3.2, Ring.Arcs(limits.Short, now), Track.Brush);
        drawing.Pop();
        drawing.Pop();
    }

    internal static void DrawRing(DrawingContext drawing, double radius, double thickness,
        IEnumerable<Arc> arcs, Brush track)
    {
        drawing.DrawEllipse(null, new Pen(track, thickness), new Point(), radius, radius);
        foreach (var arc in arcs)
        {
            if (arc.End <= arc.Start) continue;
            var pen = new Pen(PenFor(arc.Kind).Brush, thickness);
            if (arc.Start == 0 && arc.End == 1)
            {
                drawing.DrawEllipse(null, pen, new Point(), radius, radius);
                continue;
            }
            var geometry = new StreamGeometry();
            using (var path = geometry.Open())
            {
                path.BeginFigure(At(arc.Start), false, false);
                path.ArcTo(At(arc.End), new Size(radius, radius), 0,
                    arc.End - arc.Start > .5, SweepDirection.Clockwise, true, false);
            }
            drawing.DrawGeometry(null, pen, geometry);
        }

        Point At(double fraction)
        {
            double angle = fraction * Math.Tau;
            return new(Math.Sin(angle) * radius, -Math.Cos(angle) * radius);
        }
    }

    internal static Pen PenFor(SegmentKind ink) => ink switch
    {
        SegmentKind.Consumed => White,
        SegmentKind.Margin => Green,
        SegmentKind.Overlap => Red,
        _ => Gray
    };

    private static Pen MakePen(string color)
    {
        var pen = new Pen(new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)), 3.2);
        pen.Freeze();
        return pen;
    }
}

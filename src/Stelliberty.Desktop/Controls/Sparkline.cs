using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Stelliberty.Desktop.Controls;

public sealed class Sparkline : Control
{
    public static readonly StyledProperty<IReadOnlyList<double>?> ValuesProperty =
        AvaloniaProperty.Register<Sparkline, IReadOnlyList<double>?>(nameof(Values));

    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<Sparkline, IBrush?>(nameof(Stroke));

    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<Sparkline, IBrush?>(nameof(Fill));

    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<Sparkline, double>(nameof(StrokeThickness), 2d);

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<Sparkline, double>(nameof(Maximum));

    public static readonly StyledProperty<bool> ShowEndPointProperty =
        AvaloniaProperty.Register<Sparkline, bool>(nameof(ShowEndPoint));

    static Sparkline()
    {
        AffectsRender<Sparkline>(ValuesProperty, StrokeProperty, FillProperty, StrokeThicknessProperty, MaximumProperty, ShowEndPointProperty);
    }

    public IReadOnlyList<double>? Values
    {
        get => GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public double Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public bool ShowEndPoint
    {
        get => GetValue(ShowEndPointProperty);
        set => SetValue(ShowEndPointProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var values = Values;
        var width = Bounds.Width;
        var height = Bounds.Height;
        if (values is null || values.Count < 2 || width <= 0 || height <= 0)
        {
            return;
        }

        var max = 0d;
        for (var i = 0; i < values.Count; i++)
        {
            if (values[i] > max)
            {
                max = values[i];
            }
        }

        if (Maximum > 0)
        {
            max = Maximum;
        }

        var pad = StrokeThickness;
        var bottom = height - pad;
        var usable = bottom - pad;
        if (usable <= 0)
        {
            usable = height;
            bottom = height;
        }

        var scale = max > 0 ? usable / max : 0;

        var dotRadius = Math.Max(3.5, StrokeThickness * 1.9);
        var rightPad = ShowEndPoint ? dotRadius + 2 : 0;
        var stepX = (width - rightPad) / (values.Count - 1);

        var points = new Point[values.Count];
        for (var i = 0; i < values.Count; i++)
        {
            points[i] = new Point(stepX * i, bottom - values[i] * scale);
        }

        if (Fill is { } fill)
        {
            var area = new StreamGeometry();
            using (var ctx = area.Open())
            {
                ctx.BeginFigure(new Point(0, bottom), true);
                ctx.LineTo(points[0]);
                AppendSmoothCurve(ctx, points, height);
                ctx.LineTo(new Point(points[^1].X, bottom));
                ctx.EndFigure(true);
            }
            context.DrawGeometry(fill, null, area);
        }

        if (Stroke is { } stroke)
        {
            var line = new StreamGeometry();
            using (var ctx = line.Open())
            {
                ctx.BeginFigure(points[0], false);
                AppendSmoothCurve(ctx, points, height);
                ctx.EndFigure(false);
            }
            context.DrawGeometry(null, new Pen(stroke, StrokeThickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), line);

            if (ShowEndPoint)
            {

                context.DrawEllipse(stroke, new Pen(Brushes.White, 2), points[^1], dotRadius, dotRadius);
            }
        }
    }

    private static void AppendSmoothCurve(StreamGeometryContext ctx, IReadOnlyList<Point> points, double height)
    {
        for (var i = 0; i < points.Count - 1; i++)
        {
            var p0 = points[i == 0 ? 0 : i - 1];
            var p1 = points[i];
            var p2 = points[i + 1];
            var p3 = points[i + 2 < points.Count ? i + 2 : points.Count - 1];

            const double tension = 1d / 6d;
            var c1 = new Point(p1.X + (p2.X - p0.X) * tension, Math.Clamp(p1.Y + (p2.Y - p0.Y) * tension, 0, height));
            var c2 = new Point(p2.X - (p3.X - p1.X) * tension, Math.Clamp(p2.Y - (p3.Y - p1.Y) * tension, 0, height));
            ctx.CubicBezierTo(c1, c2, p2);
        }
    }
}

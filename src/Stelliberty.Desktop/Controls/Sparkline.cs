using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace Stelliberty.Desktop.Controls;

// 曲线按进度逐帧滚动或形变，末段在可视右边界处精确截断。
public sealed class Sparkline : Control
{
    private const double CurveTension = 1d / 6d;
    private const double AxisShrinkThreshold = 0.55d;
    private const double AxisFloor = 64 * 1024d;
    private const int AnimationFramesPerSecond = 30;
    // 渲染中断（隐藏到托盘、切页）后序列跳多格，形变追赶代替瞬移；滚动多格会变成飞掠。
    private static readonly TimeSpan MorphDuration = TimeSpan.FromMilliseconds(320);
    private static readonly double[] AxisMantissas = [1d, 1.25d, 1.6d, 2d, 2.5d, 3.2d, 4d, 5d, 6.3d, 8d];

    public static readonly StyledProperty<IReadOnlyList<double>?> ValuesProperty =
        AvaloniaProperty.Register<Sparkline, IReadOnlyList<double>?>(nameof(Values));

    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<Sparkline, IBrush?>(nameof(Stroke));

    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<Sparkline, IBrush?>(nameof(Fill));

    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<Sparkline, double>(nameof(StrokeThickness), 2d);

    // 标称推送间隔，只作播放时长的基准与上下限；实际时长按实测到达间隔自适应。
    public static readonly StyledProperty<TimeSpan> SampleIntervalProperty =
        AvaloniaProperty.Register<Sparkline, TimeSpan>(nameof(SampleInterval), TimeSpan.FromSeconds(1));

    private readonly DispatcherTimer _animationTimer = new()
    {
        Interval = TimeSpan.FromSeconds(1d / AnimationFramesPerSecond),
    };
    private IReadOnlyList<double>? _previousValues;
    private IReadOnlyList<double>? _drawnValues;
    private double[]? _morphFrom;
    private long _seriesRevision;
    private long _drawnRevision = -1;
    private double _axisMax;
    private double _progress;
    private long _animationStartedAt;
    private TimeSpan _measuredInterval;
    private long _lastSeriesAt;
    private double _lastWidth = double.NaN;
    private double _lastHeight = double.NaN;
    private double _lastBottom = double.NaN;
    private bool _isVisualDirty = true;
    private IBrush? _anchoredSource;
    private IBrush? _anchoredFill;
    private double _anchoredTop = double.NaN;
    private double _anchoredBottom = double.NaN;

    static Sparkline()
    {
        AffectsRender<Sparkline>(ValuesProperty, StrokeProperty, FillProperty, StrokeThicknessProperty, SampleIntervalProperty);
    }

    public Sparkline()
    {
        IsHitTestVisible = false;
        _animationTimer.Tick += OnAnimationTick;
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

    public TimeSpan SampleInterval
    {
        get => GetValue(SampleIntervalProperty);
        set => SetValue(SampleIntervalProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValuesProperty)
        {
            _seriesRevision++;
            return;
        }

        if (change.Property == StrokeProperty
            || change.Property == FillProperty
            || change.Property == StrokeThicknessProperty)
        {
            _isVisualDirty = true;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _animationTimer.Stop();
        // 定格到终态：滚动靠 progress 对齐末格，形变直接落到目标。
        _progress = 1;
        _morphFrom = null;
        base.OnDetachedFromVisualTree(e);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var currentValues = Values;
        var width = Bounds.Width;
        var height = Bounds.Height;
        var pad = StrokeThickness;
        if (currentValues is null || currentValues.Count < 2 || width <= 0 || height <= pad * 2)
        {
            return;
        }

        var bottom = height - pad;
        var layoutChanged = Math.Abs(_lastWidth - width) > 0.01d
            || Math.Abs(_lastHeight - height) > 0.01d
            || Math.Abs(_lastBottom - bottom) > 0.01d;
        var seriesChanged = _drawnRevision != _seriesRevision;
        if (layoutChanged || _isVisualDirty || _drawnValues is null || _previousValues is null)
        {
            SetStaticSeries(currentValues, width, height, bottom);
        }
        else if (seriesChanged)
        {
            // 形变未播完时继续形变，切回滚动会二次跳变。
            if (_morphFrom is null && CanScrollTransition(_previousValues, currentValues))
            {
                SetScrollSeries(currentValues, width, height, bottom);
            }
            else
            {
                SetMorphSeries(currentValues, width, height, bottom);
            }
        }

        var values = _drawnValues;
        if (values is null)
        {
            return;
        }

        var stepX = width / (currentValues.Count - 1);
        var isScrolling = values.Count == currentValues.Count + 1;
        var offsetX = isScrolling ? -stepX * _progress : 0;
        if (_morphFrom is { } morphFrom)
        {
            values = LerpSeries(morphFrom, values, EaseOut(_progress));
        }

        var points = BuildPoints(values, stepX, offsetX, bottom, (bottom - pad) / _axisMax);
        var visibleEnd = ResolveVisibleEnd(points, width, height);

        using (context.PushClip(new Rect(0, 0, width, height)))
        {
            DrawFill(context, points, bottom, height, visibleEnd);
            DrawLine(context, points, height, visibleEnd);
        }
    }

    private void SetStaticSeries(IReadOnlyList<double> values, double width, double height, double bottom)
    {
        StopAnimation();
        // 两个字段只读不改写，共享同一份快照。
        var snapshot = CopyValues(values);
        _drawnValues = snapshot;
        _previousValues = snapshot;
        _drawnRevision = _seriesRevision;
        UpdateAxis(_drawnValues);
        _lastWidth = width;
        _lastHeight = height;
        _lastBottom = bottom;
        _isVisualDirty = false;
    }

    private void SetScrollSeries(IReadOnlyList<double> currentValues, double width, double height, double bottom)
    {
        _drawnValues = AppendNewValue(_previousValues!, currentValues[^1]);
        _previousValues = CopyValues(currentValues);
        _drawnRevision = _seriesRevision;
        UpdateAxis(_drawnValues);
        _lastWidth = width;
        _lastHeight = height;
        _lastBottom = bottom;
        _isVisualDirty = false;
        StartAnimation();
    }

    private void SetMorphSeries(IReadOnlyList<double> currentValues, double width, double height, double bottom)
    {
        if (ResolveMorphOrigin(currentValues.Count) is not { } origin)
        {
            SetStaticSeries(currentValues, width, height, bottom);
            return;
        }

        var snapshot = CopyValues(currentValues);
        _drawnValues = snapshot;
        _previousValues = snapshot;
        _morphFrom = origin;
        _drawnRevision = _seriesRevision;
        // 轴要同时容纳起点与目标，否则形变途中峰值被裁。
        UpdateAxis(snapshot, Peak(origin));
        _lastWidth = width;
        _lastHeight = height;
        _lastBottom = bottom;
        _isVisualDirty = false;
        // 中断期间的到达间隔不可用于测速，清空基准。
        _measuredInterval = TimeSpan.Zero;
        _lastSeriesAt = 0;
        _progress = 0;
        _animationStartedAt = Environment.TickCount64;
        _animationTimer.Start();
    }

    private double[]? ResolveMorphOrigin(int count)
    {
        if (_drawnValues is not { } drawn)
        {
            return null;
        }

        if (_morphFrom is { Length: > 0 } from && from.Length == count && drawn.Count == count)
        {
            return LerpSeries(from, drawn, EaseOut(_progress));
        }

        if (drawn.Count == count + 1)
        {
            // 滚动中的半格偏移用相邻采样混合近似。
            var origin = new double[count];
            for (var i = 0; i < count; i++)
            {
                origin[i] = drawn[i] + (drawn[i + 1] - drawn[i]) * _progress;
            }

            return origin;
        }

        return drawn.Count == count ? CopyValues(drawn) : null;
    }

    private void StartAnimation()
    {
        var now = Environment.TickCount64;
        MeasureInterval(now);
        _progress = 0;
        _animationStartedAt = now;
        _animationTimer.Start();
    }

    // 播放时长取实测到达间隔的保守估计：被下一笔打断会丢弃残余进度并跳过对应位移，宁可提前播完留下不可见的短停顿。
    private void MeasureInterval(long now)
    {
        if (_lastSeriesAt > 0 && SampleInterval > TimeSpan.Zero)
        {
            // 扣 2% 保证先播完；余量再大停顿就会可见。
            var target = TimeSpan.FromMilliseconds((now - _lastSeriesAt) * 0.98);
            var lower = SampleInterval * 0.5;
            var upper = SampleInterval * 1.25;
            target = target < lower ? lower : target > upper ? upper : target;
            // 变短立即跟上以免被打断，变长缓慢收敛以免一次慢帧长期顶高时长。
            _measuredInterval = _measuredInterval <= TimeSpan.Zero || target < _measuredInterval
                ? target
                : _measuredInterval * 0.9 + target * 0.1;
        }

        _lastSeriesAt = now;
    }

    private void StopAnimation()
    {
        _animationTimer.Stop();
        _progress = 0;
        _morphFrom = null;
    }

    private void OnAnimationTick(object? sender, EventArgs e)
    {
        var isMorphing = _morphFrom is not null;
        var duration = isMorphing
            ? MorphDuration
            : _measuredInterval > TimeSpan.Zero ? _measuredInterval : SampleInterval;
        if (duration <= TimeSpan.Zero)
        {
            _progress = 1;
            _animationTimer.Stop();
        }
        else
        {
            var elapsed = TimeSpan.FromMilliseconds(Environment.TickCount64 - _animationStartedAt);
            _progress = Math.Clamp(elapsed / duration, 0, 1);
            if (_progress >= 1)
            {
                _animationTimer.Stop();
            }
        }

        // 收尾必须清起点，否则后续数据回不到横向滚动。
        if (isMorphing && _progress >= 1)
        {
            _morphFrom = null;
        }

        InvalidateVisual();
    }

    private void UpdateAxis(IReadOnlyList<double> values, double extraPeak = 0)
    {
        var peak = Math.Max(Peak(values), extraPeak);

        // 首帧全零时 peak 与 _axisMax 同为 0，换档条件都不成立，必须兜住除零。
        if (_axisMax <= 0 || peak > _axisMax || peak < _axisMax * AxisShrinkThreshold)
        {
            _axisMax = ResolveAxisStep(Math.Max(peak, AxisFloor));
        }
    }

    private static double Peak(IReadOnlyList<double> values)
    {
        var peak = 0d;
        for (var i = 0; i < values.Count; i++)
        {
            if (values[i] > peak)
            {
                peak = values[i];
            }
        }

        return peak;
    }

    private void DrawFill(DrawingContext context, IReadOnlyList<Point> points, double bottom, double height, VisibleEnd visibleEnd)
    {
        if (ResolveFill(height - bottom, bottom) is not { } fill)
        {
            return;
        }

        var area = new StreamGeometry();
        using (var geometry = area.Open())
        {
            geometry.BeginFigure(new Point(points[0].X, bottom), true);
            geometry.LineTo(points[0]);
            AppendSmoothCurve(geometry, points, height, visibleEnd);
            geometry.LineTo(new Point(visibleEnd.Point.X, bottom));
            geometry.EndFigure(true);
        }

        context.DrawGeometry(fill, null, area);
    }

    private void DrawLine(DrawingContext context, IReadOnlyList<Point> points, double height, VisibleEnd visibleEnd)
    {
        if (Stroke is not { } stroke)
        {
            return;
        }

        var line = new StreamGeometry();
        using (var geometry = line.Open())
        {
            geometry.BeginFigure(points[0], false);
            AppendSmoothCurve(geometry, points, height, visibleEnd);
            geometry.EndFigure(false);
        }

        context.DrawGeometry(
            null,
            new Pen(stroke, StrokeThickness, lineCap: PenLineCap.Flat, lineJoin: PenLineJoin.Round),
            line);
    }

    // 路径按可视右边界精确截断，不能依赖裁剪矩形处理最后一段的抗锯齿边缘。
    private static VisibleEnd ResolveVisibleEnd(IReadOnlyList<Point> points, double endX, double height)
    {
        for (var i = 0; i < points.Count - 1; i++)
        {
            var p1 = points[i];
            var p2 = points[i + 1];
            if (p1.X <= endX && p2.X >= endX)
            {
                var p0 = points[i == 0 ? 0 : i - 1];
                var p3 = points[i + 2 < points.Count ? i + 2 : points.Count - 1];
                GetCubicControls(p0, p1, p2, p3, height, out var c1, out var c2);
                var u = SolveCurveParameter(p1.X, c1.X, c2.X, p2.X, endX);
                SplitCubicAt(p1, c1, c2, p2, u, out var cutC1, out var cutC2, out var cutPoint);
                return new VisibleEnd(i, u, cutC1, cutC2, cutPoint);
            }
        }

        var last = points[^1];
        return new VisibleEnd(points.Count - 2, 1, last, last, last);
    }

    // 渐变用绘图区绝对坐标锚定，避免峰值变化时按几何边界框重新映射。
    private IBrush? ResolveFill(double top, double bottom)
    {
        if (Fill is not LinearGradientBrush gradient)
        {
            return Fill;
        }

        if (_anchoredFill is not null
            && ReferenceEquals(_anchoredSource, gradient)
            && Math.Abs(_anchoredTop - top) < 0.01d
            && Math.Abs(_anchoredBottom - bottom) < 0.01d)
        {
            return _anchoredFill;
        }

        var anchored = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, top, RelativeUnit.Absolute),
            EndPoint = new RelativePoint(0, bottom, RelativeUnit.Absolute),
            SpreadMethod = gradient.SpreadMethod,
            Opacity = gradient.Opacity,
        };
        foreach (var stop in gradient.GradientStops)
        {
            anchored.GradientStops.Add(new GradientStop(stop.Color, stop.Offset));
        }

        _anchoredSource = gradient;
        _anchoredTop = top;
        _anchoredBottom = bottom;
        _anchoredFill = anchored;
        return anchored;
    }

    private static bool CanScrollTransition(IReadOnlyList<double> previous, IReadOnlyList<double> current)
    {
        if (previous.Count != current.Count)
        {
            return false;
        }

        for (var i = 0; i < current.Count - 1; i++)
        {
            if (previous[i + 1] != current[i])
            {
                return false;
            }
        }

        return true;
    }

    private static double[] AppendNewValue(IReadOnlyList<double> previous, double newValue)
    {
        var values = new double[previous.Count + 1];
        for (var i = 0; i < previous.Count; i++)
        {
            values[i] = previous[i];
        }

        values[^1] = newValue;
        return values;
    }

    private static double[] CopyValues(IReadOnlyList<double> values)
    {
        var copy = new double[values.Count];
        for (var i = 0; i < values.Count; i++)
        {
            copy[i] = values[i];
        }

        return copy;
    }

    private static double[] LerpSeries(IReadOnlyList<double> from, IReadOnlyList<double> to, double t)
    {
        var values = new double[to.Count];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = from[i] + (to[i] - from[i]) * t;
        }

        return values;
    }

    private static double EaseOut(double t)
    {
        var rest = 1d - t;
        return 1d - rest * rest * rest;
    }

    private static Point[] BuildPoints(IReadOnlyList<double> values, double stepX, double offsetX, double bottom, double scale)
    {
        var points = new Point[values.Count];
        for (var i = 0; i < values.Count; i++)
        {
            points[i] = new Point(stepX * i + offsetX, bottom - values[i] * scale);
        }

        return points;
    }

    private static double ResolveAxisStep(double value)
    {
        var basis = Math.Pow(10, Math.Floor(Math.Log10(value)));
        foreach (var mantissa in AxisMantissas)
        {
            var candidate = mantissa * basis;
            if (candidate >= value)
            {
                return candidate;
            }
        }

        return basis * 10d;
    }

    private static void AppendSmoothCurve(StreamGeometryContext context, IReadOnlyList<Point> points, double height, VisibleEnd visibleEnd)
    {
        for (var i = 0; i <= visibleEnd.SegmentIndex; i++)
        {
            if (i == visibleEnd.SegmentIndex && visibleEnd.Parameter < 1)
            {
                context.CubicBezierTo(visibleEnd.Control1, visibleEnd.Control2, visibleEnd.Point);
                return;
            }

            var p0 = points[i == 0 ? 0 : i - 1];
            var p1 = points[i];
            var p2 = points[i + 1];
            var p3 = points[i + 2 < points.Count ? i + 2 : points.Count - 1];
            GetCubicControls(p0, p1, p2, p3, height, out var c1, out var c2);
            context.CubicBezierTo(c1, c2, p2);
        }
    }

    private static void GetCubicControls(Point p0, Point p1, Point p2, Point p3, double height, out Point c1, out Point c2)
    {
        c1 = new Point(
            p1.X + (p2.X - p0.X) * CurveTension,
            Math.Clamp(p1.Y + (p2.Y - p0.Y) * CurveTension, 0, height));
        c2 = new Point(
            p2.X - (p3.X - p1.X) * CurveTension,
            Math.Clamp(p2.Y - (p3.Y - p1.Y) * CurveTension, 0, height));
    }

    private static void SplitCubicAt(Point p0, Point p1, Point p2, Point p3, double u, out Point c1, out Point c2, out Point endpoint)
    {
        var q0 = Lerp(p0, p1, u);
        var q1 = Lerp(p1, p2, u);
        var q2 = Lerp(p2, p3, u);
        var r0 = Lerp(q0, q1, u);
        var r1 = Lerp(q1, q2, u);
        endpoint = Lerp(r0, r1, u);
        c1 = q0;
        c2 = r0;
    }

    private static Point Lerp(Point start, Point end, double u)
        => new(start.X + (end.X - start.X) * u, start.Y + (end.Y - start.Y) * u);

    private static double SolveCurveParameter(double x0, double x1, double x2, double x3, double targetX)
    {
        var low = 0d;
        var high = 1d;
        for (var i = 0; i < 20; i++)
        {
            var middle = (low + high) * 0.5;
            if (CubicAt(x0, x1, x2, x3, middle) < targetX)
            {
                low = middle;
            }
            else
            {
                high = middle;
            }
        }

        return (low + high) * 0.5;
    }

    private static double CubicAt(double a, double b, double c, double d, double u)
    {
        var v = 1d - u;
        return v * v * v * a + 3 * v * v * u * b + 3 * v * u * u * c + u * u * u * d;
    }

    private readonly record struct VisibleEnd(int SegmentIndex, double Parameter, Point Control1, Point Control2, Point Point);
}

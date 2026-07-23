using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace Stelliberty.Desktop.Controls;

public sealed class PageLoadingIndicator : Control
{
    private static readonly TimeSpan OneWayDuration = TimeSpan.FromMilliseconds(1200);

    public static readonly StyledProperty<IBrush?> AccentBrushProperty =
        AvaloniaProperty.Register<PageLoadingIndicator, IBrush?>(nameof(AccentBrush));

    public static readonly StyledProperty<IBrush?> TrackBrushProperty =
        AvaloniaProperty.Register<PageLoadingIndicator, IBrush?>(nameof(TrackBrush));

    public static readonly StyledProperty<IBrush?> SurfaceBrushProperty =
        AvaloniaProperty.Register<PageLoadingIndicator, IBrush?>(nameof(SurfaceBrush));

    private readonly DispatcherTimer _timer;
    private long _startedAt;
    private bool _awaitingFirstFrame;
    private bool _isAttached;
    private bool _isRunning;

    static PageLoadingIndicator()
    {
        AffectsRender<PageLoadingIndicator>(
            AccentBrushProperty,
            TrackBrushProperty,
            SurfaceBrushProperty);
    }

    public PageLoadingIndicator()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += OnTick;
        MinWidth = 240;
        MinHeight = 64;
        IsHitTestVisible = false;
    }

    public IBrush? AccentBrush
    {
        get => GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }

    public IBrush? TrackBrush
    {
        get => GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public IBrush? SurfaceBrush
    {
        get => GetValue(SurfaceBrushProperty);
        set => SetValue(SurfaceBrushProperty, value);
    }

    public void Start()
    {
        _awaitingFirstFrame = true;
        _startedAt = 0;
        _isRunning = true;
        if (_isAttached && !_timer.IsEnabled)
        {
            _timer.Start();
        }

        InvalidateVisual();
    }

    public void Stop()
    {
        _isRunning = false;
        _awaitingFirstFrame = false;
        _timer.Stop();
        _startedAt = 0;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isAttached = true;
        if (_isRunning)
        {
            _timer.Start();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _timer.Stop();
        _isAttached = false;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (!_isAttached || !_isRunning)
        {
            return;
        }

        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var bounds = Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        // 首帧绘制后再计时，确保加载条从起点开始。
        if (_isRunning && _awaitingFirstFrame)
        {
            _awaitingFirstFrame = false;
            _startedAt = Stopwatch.GetTimestamp();
        }

        DrawSoftBar(context, bounds, ResolveAccent(), ResolveTrack(), ResolveSurface());
    }

    private IBrush ResolveAccent()
        => AccentBrush
           ?? TryGetBrush("AppAccentBrush")
           ?? new SolidColorBrush(Color.Parse("#60A5FA"));

    private IBrush ResolveTrack()
        => TrackBrush
           ?? TryGetBrush("AppOverlayBrush")
           ?? new SolidColorBrush(Color.FromArgb(56, 255, 255, 255));

    private IBrush ResolveSurface()
        => SurfaceBrush
           ?? TryGetBrush("AppOverlaySubtleBrush")
           ?? new SolidColorBrush(Color.FromArgb(36, 255, 255, 255));

    private IBrush? TryGetBrush(string key)
        => TryGetResource(key, ActualThemeVariant, out var value) ? value as IBrush : null;

    private double ResolveTravel()
    {
        if (_startedAt == 0 || _awaitingFirstFrame)
        {
            return 0;
        }

        var elapsed = Stopwatch.GetElapsedTime(_startedAt).TotalSeconds;
        var oneWaySeconds = OneWayDuration.TotalSeconds;
        var cycle = elapsed / oneWaySeconds;
        // 三角波把单向时间映射为 0→1→0。
        var segment = cycle % 2d;
        return segment <= 1d ? segment : 2d - segment;
    }

    private void DrawSoftBar(DrawingContext context, Rect bounds, IBrush accent, IBrush track, IBrush surface)
    {
        var barWidth = Math.Min(280, Math.Max(180, bounds.Width * 0.42));
        var barHeight = 6d;
        var x = (bounds.Width - barWidth) * 0.5;
        var y = bounds.Height * 0.5 - barHeight * 0.5;
        var trackRect = new Rect(x, y, barWidth, barHeight);
        using (context.PushOpacity(70d / byte.MaxValue))
        {
            context.FillRectangle(surface, trackRect, 3);
        }

        using (context.PushOpacity(55d / byte.MaxValue))
        {
            context.FillRectangle(track, trackRect, 3);
        }

        var travel = ResolveTravel();
        var thumbWidth = barWidth * 0.34;
        var thumbX = x + (barWidth - thumbWidth) * travel;
        context.FillRectangle(accent, new Rect(thumbX, y, thumbWidth, barHeight), 3);
    }

}

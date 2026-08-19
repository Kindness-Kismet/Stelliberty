using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;

namespace Stelliberty.Desktop.Controls;

public sealed class FpsCounter : Control
{
    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<FpsCounter, IBrush?>(nameof(Foreground));

    private readonly Stopwatch _stopwatch = new();
    private int _frames;
    private int _runGeneration;
    private bool _running;
    private string _text = "-- FPS";

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public FontFamily FontFamily { get; init; } = FontFamily.Default;

    public double FontSize { get; init; } = 12;

    public FontWeight FontWeight { get; init; } = FontWeight.Normal;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        SetRunning(IsVisible);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        SetRunning(false);
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != IsVisibleProperty || VisualRoot is null)
        {
            return;
        }

        SetRunning(IsVisible);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var text = BuildText();
        return new Size(text.Width, text.Height);
    }

    public override void Render(DrawingContext context)
    {
        var text = BuildText();
        var top = (Bounds.Height - text.Height) / 2;
        context.DrawText(text, new Point(0, top > 0 ? top : 0));
    }

    // 每次合成器回调都重新注册，以对齐 vsync。
    private void RequestNextFrame(int generation)
    {
        if (!_running || generation != _runGeneration)
        {
            return;
        }

        var compositor = ElementComposition.GetElementVisual(this)?.Compositor;
        if (compositor is null)
        {
            // 合成视觉还没就绪；下一帧重试。
            Dispatcher.UIThread.Post(() => RequestNextFrame(generation), DispatcherPriority.Background);
            return;
        }

        compositor.RequestCompositionUpdate(() => OnComposed(generation));
    }

    private void OnComposed(int generation)
    {
        if (!_running || generation != _runGeneration)
        {
            return;
        }

        _frames++;
        var elapsed = _stopwatch.ElapsedMilliseconds;
        if (elapsed >= 1000)
        {
            var next = $"{(int)(_frames * 1000.0 / elapsed)} FPS";
            _frames = 0;
            _stopwatch.Restart();
            if (next != _text)
            {
                _text = next;
                InvalidateMeasure();
                InvalidateVisual();
            }
        }

        RequestNextFrame(generation);
    }

    private void SetRunning(bool isRunning)
    {
        if (_running == isRunning)
        {
            return;
        }

        _running = isRunning;
        _runGeneration++;
        if (!isRunning)
        {
            _stopwatch.Reset();
            return;
        }

        _frames = 0;
        _stopwatch.Restart();
        RequestNextFrame(_runGeneration);
    }

    private FormattedText BuildText() => new(
        _text,
        CultureInfo.InvariantCulture,
        FlowDirection.LeftToRight,
        new Typeface(FontFamily, FontStyle.Normal, FontWeight),
        FontSize,
        Foreground ?? Brushes.Gray);
}

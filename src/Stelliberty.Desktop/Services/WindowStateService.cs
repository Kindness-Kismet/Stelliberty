using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Settings;

namespace Stelliberty.Desktop.Services;

internal sealed class WindowStateService : IDisposable
{
    private readonly DispatcherTimer _saveWindowStateTimer;
    private readonly IAppSettingsStore? _settingsStore;
    private readonly AppSettings? _settings;
    private MainWindow? _window;
    private Size? _lastNormalSize;

    public WindowStateService(IAppSettingsStore? settingsStore = null, AppSettings? settings = null)
    {
        _settingsStore = settingsStore;
        _settings = settings;
        _saveWindowStateTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _saveWindowStateTimer.Tick += OnSaveWindowStateTimerTick;
    }

    public void Attach(MainWindow window)
    {
        if (_window is not null)
        {
            _window.PropertyChanged -= OnWindowPropertyChanged;
        }

        _window = window;
        _window.PropertyChanged += OnWindowPropertyChanged;
        RestoreWindowState();
    }

    public void SaveNow()
    {
        if (_window is null || _settingsStore is null || _settings is null)
        {
            return;
        }

        var size = _window.WindowState == WindowState.Normal
            ? CurrentWindowSize(preferBounds: true)
            : _lastNormalSize ?? CurrentWindowSize(preferBounds: false);
        if (size is null || size.Value.Width <= 0 || size.Value.Height <= 0)
        {
            return;
        }

        var isMaximized = _window.WindowState == WindowState.Maximized;
        _settings.WindowWidth = size.Value.Width;
        _settings.WindowHeight = size.Value.Height;
        _settings.IsWindowMaximized = isMaximized;
        _settingsStore.Save(_settings);
        AppLogger.Debug($"Saved window state: {size.Value.Width}x{size.Value.Height} maximized={isMaximized}");
    }

    public void Dispose()
    {
        _saveWindowStateTimer.Stop();
        _saveWindowStateTimer.Tick -= OnSaveWindowStateTimerTick;
        if (_window is not null)
        {
            _window.PropertyChanged -= OnWindowPropertyChanged;
            _window = null;
        }
    }

    private void RestoreWindowState()
    {
        if (_window is null)
        {
            return;
        }

        var width = _settings?.WindowWidth;
        var height = _settings?.WindowHeight;
        var isMaximized = _settings?.IsWindowMaximized == true;
        if (width is > 0 && height is > 0)
        {
            _window.Width = width.Value;
            _window.Height = height.Value;
            _lastNormalSize = new Size(width.Value, height.Value);
            AppLogger.Debug($"Restored window size: {width.Value}x{height.Value}");
        }

        FitToWorkingArea();
        if (isMaximized)
        {
            _window.WindowState = WindowState.Maximized;
            AppLogger.Debug("Restored maximized window state");
        }
    }

    // 构造时已能读到工作区；先收进可用区域，再按工作区居中，避免整屏居中把标题栏顶出屏幕。
    private void FitToWorkingArea()
    {
        if (_window is null)
        {
            return;
        }

        var screens = _window.Screens;
        var screen = screens?.ScreenFromWindow(_window) ?? screens?.Primary;
        if (screen is null || screen.WorkingArea.Width <= 0 || screen.WorkingArea.Height <= 0)
        {
            return;
        }

        var scale = _window.RenderScaling <= 0 ? 1 : _window.RenderScaling;
        var maxWidth = screen.WorkingArea.Width / scale;
        var maxHeight = screen.WorkingArea.Height / scale;
        var requestedWidth = FirstPositive(_window.Width, _window.Bounds.Width) ?? maxWidth;
        var requestedHeight = FirstPositive(_window.Height, _window.Bounds.Height) ?? maxHeight;
        var (width, height) = WindowPlacement.FitSize(
            requestedWidth,
            requestedHeight,
            _window.MinWidth,
            _window.MinHeight,
            maxWidth,
            maxHeight);

        _window.Width = width;
        _window.Height = height;
        _lastNormalSize = new Size(width, height);

        if (_window.WindowState != WindowState.Normal)
        {
            return;
        }

        var (x, y) = WindowPlacement.CenterInPixels(
            width,
            height,
            screen.WorkingArea.X,
            screen.WorkingArea.Y,
            screen.WorkingArea.Width,
            screen.WorkingArea.Height,
            scale);
        _window.WindowStartupLocation = WindowStartupLocation.Manual;
        _window.Position = new PixelPoint(x, y);
        AppLogger.Debug($"Fitted window to working area: {width}x{height} at {x},{y}");
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == Window.WindowStateProperty)
        {
            TrackNormalWindowState();
            ScheduleWindowStateSave();
            return;
        }

        if (args.Property == Window.BoundsProperty)
        {
            TrackNormalWindowState();
            ScheduleWindowStateSave();
        }
    }

    private void OnSaveWindowStateTimerTick(object? sender, EventArgs args)
    {
        _saveWindowStateTimer.Stop();
        SaveNow();
    }

    private void TrackNormalWindowState()
    {
        if (_window is null)
        {
            return;
        }

        if (_window.WindowState != WindowState.Normal || _window.Bounds.Width <= 0 || _window.Bounds.Height <= 0 || IsMaximizedLikeBounds())
        {
            return;
        }

        _lastNormalSize = CurrentWindowSize(preferBounds: true);
    }

    private bool IsMaximizedLikeBounds()
    {
        if (_window is null)
        {
            return false;
        }

        var screen = _window.Screens.ScreenFromWindow(_window);
        if (screen is null)
        {
            return false;
        }

        var scale = _window.RenderScaling <= 0 ? 1 : _window.RenderScaling;
        var screenLeft = screen.Bounds.X / scale;
        var screenTop = screen.Bounds.Y / scale;
        var workingWidth = screen.WorkingArea.Width / scale;
        var workingHeight = screen.WorkingArea.Height / scale;
        return _window.Position.X < screenLeft - 1
            || _window.Position.Y < screenTop - 1
            || _window.Bounds.Width >= workingWidth - 16 && _window.Bounds.Height >= workingHeight - 16;
    }

    private void ScheduleWindowStateSave()
    {
        _saveWindowStateTimer.Stop();
        _saveWindowStateTimer.Start();
    }

    private Size? CurrentWindowSize(bool preferBounds)
    {
        if (_window is null)
        {
            return null;
        }

        var width = preferBounds
            ? FirstPositive(_window.Bounds.Width, _window.Width)
            : FirstPositive(_window.Width, _window.Bounds.Width);
        var height = preferBounds
            ? FirstPositive(_window.Bounds.Height, _window.Height)
            : FirstPositive(_window.Height, _window.Bounds.Height);
        return width is null || height is null ? null : new Size(width.Value, height.Value);
    }

    private static double? FirstPositive(double primary, double fallback)
    {
        if (double.IsFinite(primary) && primary > 0)
        {
            return primary;
        }

        return double.IsFinite(fallback) && fallback > 0 ? fallback : null;
    }
}

using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Settings;
using Stelliberty.Presentation.ViewModels;

namespace Stelliberty.Desktop.Services;

internal sealed class SystemAccentColorService : IDisposable
{
    private Window? _window;
    private SettingsThemeViewModel? _theme;
    private IPlatformSettings? _platformSettings;

    public void Attach(Window window, SettingsThemeViewModel theme)
    {
        if (_platformSettings is not null)
        {
            _platformSettings.ColorValuesChanged -= OnColorValuesChanged;
        }

        if (_theme is not null)
        {
            _theme.AccentColorChanged -= OnThemeAccentChanged;
            _theme.ThemeChanged -= OnThemeChanged;
        }

        if (_window is not null)
        {
            _window.ActualThemeVariantChanged -= OnActualThemeVariantChanged;
        }

        _window = window;
        _theme = theme;
        _theme.AccentColorChanged += OnThemeAccentChanged;
        _theme.ThemeChanged += OnThemeChanged;
        _window.ActualThemeVariantChanged += OnActualThemeVariantChanged;

        _platformSettings = Avalonia.Application.Current?.PlatformSettings;
        if (_platformSettings is not null)
        {
            _platformSettings.ColorValuesChanged += OnColorValuesChanged;
        }

        Apply();
    }

    public void Reapply()
    {
        Apply();
    }

    public void Dispose()
    {
        if (_platformSettings is not null)
        {
            _platformSettings.ColorValuesChanged -= OnColorValuesChanged;
        }

        if (_theme is not null)
        {
            _theme.AccentColorChanged -= OnThemeAccentChanged;
            _theme.ThemeChanged -= OnThemeChanged;
        }

        if (_window is not null)
        {
            _window.ActualThemeVariantChanged -= OnActualThemeVariantChanged;
        }

        _window = null;
        _theme = null;
        _platformSettings = null;
    }

    private void OnThemeAccentChanged(object? sender, EventArgs args)
    {
        Apply();
    }

    private void OnThemeChanged(object? sender, AppTheme theme)
    {
        Apply();
    }

    private void OnActualThemeVariantChanged(object? sender, EventArgs args)
    {
        Apply();
    }

    private void OnColorValuesChanged(object? sender, PlatformColorValues args)
    {
        if (_theme?.AccentMode == AccentColorMode.System)
        {
            Apply();
        }
    }

    private void Apply()
    {
        if (_window is null || _theme is null)
        {
            return;
        }

        var accent = _theme.AccentMode == AccentColorMode.Custom
            ? Color.Parse(_theme.CustomAccentColor)
            : _platformSettings?.GetColorValues().AccentColor1 ?? Color.Parse(_theme.CustomAccentColor);

        var palette = AccentColorPaletteGenerator.Generate(accent, _window.ActualThemeVariant == ThemeVariant.Light);
        foreach (var (key, brush) in CreateAccentResourceBrushes(palette))
        {
            _window.Resources[key] = brush;
        }
        AppLogger.Info(
            $"Accent color applied: {_theme.AccentMode} source=#{accent.R:X2}{accent.G:X2}{accent.B:X2} accent=#{palette.Accent.R:X2}{palette.Accent.G:X2}{palette.Accent.B:X2}");
    }

    internal static IReadOnlyDictionary<string, SolidColorBrush> CreateAccentResourceBrushes(AccentColorPalette palette)
    {
        return new Dictionary<string, SolidColorBrush>(StringComparer.Ordinal)
        {
            ["AppAccentBrush"] = new(palette.Accent),
            ["AppOnAccentBrush"] = new(palette.OnAccent),
            ["AppAccentSubtleBrush"] = new(palette.Subtle, AccentColorPalette.SubtleOpacity),
            ["AppAccentBorderBrush"] = new(palette.Border, AccentColorPalette.BorderOpacity),
            ["AppAccentTintBrush"] = new(palette.Tint, AccentColorPalette.TintOpacity)
        };
    }
}

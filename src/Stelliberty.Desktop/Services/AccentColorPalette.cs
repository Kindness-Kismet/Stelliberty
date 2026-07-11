using Avalonia.Media;

namespace Stelliberty.Desktop.Services;

internal readonly record struct AccentColorPalette(
    Color Accent,
    Color OnAccent,
    Color Subtle,
    Color Border,
    Color Tint)
{
    public const double SubtleOpacity = 0.42;
    public const double BorderOpacity = 0.72;
    public const double TintOpacity = 0.30;
}

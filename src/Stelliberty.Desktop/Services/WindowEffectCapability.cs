using Stelliberty.Application.Platform;
using Stelliberty.Application.Settings;

namespace Stelliberty.Desktop.Services;

internal sealed class WindowEffectCapability : IWindowEffectCapability
{
    public IReadOnlyList<WindowEffect> SupportedEffects { get; } = ResolveSupportedEffects();

    private static IReadOnlyList<WindowEffect> ResolveSupportedEffects()
    {
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            return [WindowEffect.None, WindowEffect.Mica, WindowEffect.Acrylic];
        }

        if (OperatingSystem.IsMacOS())
        {
            return [WindowEffect.None, WindowEffect.Blur];
        }

        return [WindowEffect.None];
    }
}

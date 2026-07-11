using Stelliberty.Application.Settings;

namespace Stelliberty.Application.Platform;

public interface IWindowEffectCapability
{
    IReadOnlyList<WindowEffect> SupportedEffects { get; }
}

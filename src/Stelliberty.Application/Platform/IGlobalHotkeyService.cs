namespace Stelliberty.Application.Platform;

public interface IGlobalHotkeyService : IDisposable
{
    GlobalHotkeyApplyResult Apply(GlobalHotkeyAction action, string gesture);

    void SetActivationSuppressed(bool isSuppressed);

#if DEBUG
    bool SimulateActivation(GlobalHotkeyAction action);
#endif
}

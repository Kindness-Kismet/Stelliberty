using Stelliberty.Application.Platform;

namespace Stelliberty.Desktop.Services;

internal sealed class UnsupportedGlobalHotkeyService(Action<GlobalHotkeyAction> activated) : IGlobalHotkeyService
{
    private readonly GlobalHotkeyActivationController _activationController = new(activated);

    public GlobalHotkeyApplyResult Apply(GlobalHotkeyAction action, string gesture)
    {
        return string.IsNullOrWhiteSpace(gesture)
            ? GlobalHotkeyApplyResult.Success()
            : GlobalHotkeyApplyResult.Failure(GlobalHotkeyApplyError.Unsupported);
    }

    public void SetActivationSuppressed(bool isSuppressed)
    {
        _activationController.SetSuppressed(isSuppressed);
    }

#if DEBUG
    public bool SimulateActivation(GlobalHotkeyAction action)
    {
        return _activationController.TryActivate(action);
    }
#endif

    public void Dispose()
    {
    }
}

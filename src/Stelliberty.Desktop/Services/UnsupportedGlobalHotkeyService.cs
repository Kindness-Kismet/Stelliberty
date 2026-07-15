using Stelliberty.Application.Platform;

namespace Stelliberty.Desktop.Services;

internal sealed class UnsupportedGlobalHotkeyService : IGlobalHotkeyService
{
    public GlobalHotkeyApplyResult Apply(string gesture)
    {
        return string.IsNullOrWhiteSpace(gesture)
            ? GlobalHotkeyApplyResult.Success()
            : GlobalHotkeyApplyResult.Failure(GlobalHotkeyApplyError.Unsupported);
    }

    public void Dispose()
    {
    }
}

namespace Stelliberty.Application.Platform;

public interface IGlobalHotkeyService : IDisposable
{
    GlobalHotkeyApplyResult Apply(string gesture);
}

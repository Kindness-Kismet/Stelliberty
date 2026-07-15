namespace Stelliberty.Application.Platform;

public enum GlobalHotkeyAction
{
    ToggleWindow = 1,
    ToggleSystemProxy = 2,
    ToggleTun = 3,
}

public enum GlobalHotkeyApplyError
{
    None,
    Invalid,
    Duplicate,
    Conflict,
    Unsupported,
    Failed,
}

public readonly record struct GlobalHotkeyApplyResult(bool IsSuccess, GlobalHotkeyApplyError Error)
{
    public static GlobalHotkeyApplyResult Success() => new(true, GlobalHotkeyApplyError.None);

    public static GlobalHotkeyApplyResult Failure(GlobalHotkeyApplyError error) => new(false, error);
}

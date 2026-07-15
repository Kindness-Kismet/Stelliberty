namespace Stelliberty.Application.Platform;

public enum GlobalHotkeyApplyError
{
    None,
    Invalid,
    Conflict,
    Unsupported,
    Failed,
}

public readonly record struct GlobalHotkeyApplyResult(bool IsSuccess, GlobalHotkeyApplyError Error)
{
    public static GlobalHotkeyApplyResult Success() => new(true, GlobalHotkeyApplyError.None);

    public static GlobalHotkeyApplyResult Failure(GlobalHotkeyApplyError error) => new(false, error);
}

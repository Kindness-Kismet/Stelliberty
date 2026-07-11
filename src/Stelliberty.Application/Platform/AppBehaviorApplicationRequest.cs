namespace Stelliberty.Application.Platform;

public sealed record AppBehaviorApplicationRequest(
    bool IsSilentStartEnabled,
    bool IsMinimizeToTrayEnabled,
    bool IsLazyModeEnabled,
    bool IsAutoStartEnabled);

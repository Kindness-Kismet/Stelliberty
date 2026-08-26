namespace Stelliberty.Application.Platform;

public sealed record AppBehaviorApplicationRequest(
    bool IsSilentStartEnabled,
    bool IsAutoStartEnabled);

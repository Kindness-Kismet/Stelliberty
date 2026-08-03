namespace Stelliberty.Application.Platform;

public sealed record ServiceModeStatus(
    ServiceModeState State,
    string Message,
    TimeSpan? Uptime = null,
    TimeSpan? LastHeartbeatAge = null,
    string? CoreState = null,
    int? CorePid = null,
    string? CoreLastError = null,
    string? InstalledVersion = null)
{
    public bool IsInstalled => State is ServiceModeState.Running or ServiceModeState.Stopped;

    public bool NeedsRepair => State == ServiceModeState.NeedsRepair;

    public bool IsRunning => State == ServiceModeState.Running;

    public bool IsHealthy => IsRunning;

    public static ServiceModeStatus Unavailable(string message)
    {
        return new ServiceModeStatus(ServiceModeState.Unknown, message);
    }
}

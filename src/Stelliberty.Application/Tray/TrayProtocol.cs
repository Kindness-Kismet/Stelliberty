namespace Stelliberty.Application.Tray;

public static class TrayProtocol
{
    public const int Version = 1;

    public const string HelloMethod = "tray.hello";
    public const string HealthMethod = "tray.get_health";
    public const string ShutdownMethod = "tray.shutdown";
    public const string UiActivateMethod = "ui.activate";
    public const string UiRegisterMethod = "ui.register";
    public const string UiUnregisterMethod = "ui.unregister";
    public const string UiActivationEvent = "ui.activate";
}

public sealed record TrayHelloRequest(
    int ProtocolVersion,
    string AppVersion,
    int ProcessId);

public sealed record TrayHello(
    int ProtocolVersion,
    string AppVersion,
    int TrayPid,
    string TrayEpoch,
    string[] Capabilities,
    long CoreGeneration);

public sealed record TrayHealth(
    int TrayPid,
    string TrayEpoch,
    long UptimeMilliseconds,
    int? UiPid,
    bool IsUiLaunchPending);

public sealed record UiActivateRequest(int LauncherPid);

public sealed record UiActivateResult(
    bool WasLaunched,
    bool WasSignaled,
    bool IsPending);

public sealed record UiRegisterRequest(
    int ProtocolVersion,
    string AppVersion,
    int UiPid,
    string SessionToken);

public sealed record UiRegisterResult(
    string SessionId,
    long WatermarkSequence);

public sealed record UiUnregisterRequest(string SessionId);

public sealed record UiUnregisterResult(bool WasRegistered);

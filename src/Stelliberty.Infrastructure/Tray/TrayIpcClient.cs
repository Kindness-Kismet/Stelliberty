using System.Text.Json;
using Stelliberty.Application.Tray;
using Stelliberty.Application.Platform;
using Stelliberty.Application.Runtime;
using Stelliberty.Infrastructure.Core;

namespace Stelliberty.Infrastructure.Tray;

public sealed class TrayIpcClient : IDisposable, IAsyncDisposable
{
    private readonly JsonRpcPipeClient _client;

    public TrayIpcClient(string? endpoint = null)
    {
        _client = new JsonRpcPipeClient(endpoint ?? TrayEndpoint.Current);
        _client.EventReceived += OnEventReceived;
        _client.Disconnected += OnDisconnected;
    }

    public event EventHandler? ActivationRequested;

    public event EventHandler<TrayCoreStatus>? CoreStateChanged;

    public event EventHandler<TrayCoreLogEntry>? CoreLogReceived;

    public event EventHandler<TrayRuntimeSample>? RuntimeSampled;

    public event EventHandler<SystemProxyStatus>? SystemProxyChanged;

    public event EventHandler? Disconnected;

    public Task ConnectAsync(CancellationToken cancellationToken) => _client.ConnectAsync(cancellationToken);

    public async Task<TrayHello> HelloAsync(int processId, CancellationToken cancellationToken)
    {
        var request = new TrayHelloRequest(TrayProtocol.Version, AppMetadata.Version, processId);
        var hello = await RequestAsync<TrayHello>(TrayProtocol.HelloMethod, request, cancellationToken).ConfigureAwait(false);
        if (hello.ProtocolVersion != TrayProtocol.Version)
        {
            throw new TrayProtocolMismatchException(TrayProtocol.Version, hello.ProtocolVersion);
        }

        return hello;
    }

    public Task<TrayHealth> GetHealthAsync(CancellationToken cancellationToken) =>
        RequestAsync<TrayHealth>(TrayProtocol.HealthMethod, new { }, cancellationToken);

    public Task<TrayCoreOperationResult> EnsureCoreStartedAsync(CancellationToken cancellationToken) =>
        RequestAsync<TrayCoreOperationResult>(TrayProtocol.CoreEnsureStartedMethod, new { }, cancellationToken);

    public Task<TrayCoreOperationResult> StopCoreAsync(CancellationToken cancellationToken) =>
        RequestAsync<TrayCoreOperationResult>(TrayProtocol.CoreStopMethod, new { }, cancellationToken);

    public Task<TrayCoreStatus> GetCoreStatusAsync(CancellationToken cancellationToken) =>
        RequestAsync<TrayCoreStatus>(TrayProtocol.CoreSnapshotMethod, new { }, cancellationToken);

    public Task<CoreApplyConfigResult> ApplyCoreConfigAsync(
        CoreApplyConfigRequest request,
        CancellationToken cancellationToken) =>
        RequestAsync<CoreApplyConfigResult>(TrayProtocol.CoreApplyConfigMethod, request, cancellationToken);

    public Task<TrayCoreStatus> RestartCoreAsync(CancellationToken cancellationToken) =>
        RequestAsync<TrayCoreStatus>(TrayProtocol.CoreRestartMethod, new { }, cancellationToken);

    public Task<TrayCoreLogBatch> GetCoreLogsAsync(long afterSequence, CancellationToken cancellationToken) =>
        RequestAsync<TrayCoreLogBatch>(
            TrayProtocol.CoreLogsMethod,
            new TrayCoreLogsRequest(afterSequence),
            cancellationToken);

    public Task<TrayRuntimeSnapshot> GetRuntimeSnapshotAsync(CancellationToken cancellationToken) =>
        RequestAsync<TrayRuntimeSnapshot>(TrayProtocol.RuntimeSnapshotMethod, new { }, cancellationToken);

    public Task<TrayRuntimeSnapshot> ResetRuntimeTrafficAsync(CancellationToken cancellationToken) =>
        RequestAsync<TrayRuntimeSnapshot>(TrayProtocol.RuntimeResetTrafficMethod, new { }, cancellationToken);

    public Task<SystemProxyStatus> GetSystemProxyStatusAsync(CancellationToken cancellationToken) =>
        RequestAsync<SystemProxyStatus>(TrayProtocol.SystemProxyStatusMethod, new { }, cancellationToken);

    public Task<SystemProxyApplyResult> SetSystemProxyEnabledAsync(
        bool isEnabled,
        SystemProxyApplicationRequest? request,
        CancellationToken cancellationToken) =>
        RequestAsync<SystemProxyApplyResult>(
            TrayProtocol.SystemProxySetEnabledMethod,
            new TraySystemProxySetRequest(isEnabled, request),
            cancellationToken);

    public Task<UiActivateResult> ActivateUiAsync(int launcherPid, CancellationToken cancellationToken) =>
        RequestAsync<UiActivateResult>(
            TrayProtocol.UiActivateMethod,
            new UiActivateRequest(launcherPid),
            cancellationToken);

    public Task<UiRegisterResult> RegisterUiAsync(string sessionToken, int uiPid, CancellationToken cancellationToken) =>
        RequestAsync<UiRegisterResult>(
            TrayProtocol.UiRegisterMethod,
            new UiRegisterRequest(TrayProtocol.Version, AppMetadata.Version, uiPid, sessionToken),
            cancellationToken);

    public Task<UiUnregisterResult> UnregisterUiAsync(string sessionId, CancellationToken cancellationToken) =>
        RequestAsync<UiUnregisterResult>(
            TrayProtocol.UiUnregisterMethod,
            new UiUnregisterRequest(sessionId),
            cancellationToken);

    public Task ShutdownAsync(CancellationToken cancellationToken) =>
        RequestAsync<JsonElement>(TrayProtocol.ShutdownMethod, new { }, cancellationToken);

    private async Task<T> RequestAsync<T>(string method, object parameters, CancellationToken cancellationToken)
    {
        var parameterElement = JsonSerializer.SerializeToElement(parameters, TrayJson.Options);
        var result = await _client.RequestAsync(method, parameterElement, cancellationToken).ConfigureAwait(false);
        return result.Deserialize<T>(TrayJson.Options)
            ?? throw new JsonException($"Tray response for {method} is empty.");
    }

    private void OnEventReceived(object? sender, EventNotification notification)
    {
        switch (notification.Event)
        {
            case TrayProtocol.UiActivationEvent:
                ActivationRequested?.Invoke(this, EventArgs.Empty);
                break;
            case TrayProtocol.CoreStateChangedEvent:
                CoreStateChanged?.Invoke(this, DeserializeEvent<TrayCoreStatus>(notification));
                break;
            case TrayProtocol.CoreLogEntryEvent:
                CoreLogReceived?.Invoke(this, DeserializeEvent<TrayCoreLogEntry>(notification));
                break;
            case TrayProtocol.RuntimeSampledEvent:
                RuntimeSampled?.Invoke(this, DeserializeEvent<TrayRuntimeSample>(notification));
                break;
            case TrayProtocol.SystemProxyChangedEvent:
                SystemProxyChanged?.Invoke(this, DeserializeEvent<SystemProxyStatus>(notification));
                break;
        }
    }

    private static T DeserializeEvent<T>(EventNotification notification)
    {
        return notification.Data.Deserialize<T>(TrayJson.Options)
            ?? throw new JsonException($"Tray event {notification.Event} is empty.");
    }

    private void OnDisconnected(object? sender, EventArgs args) => Disconnected?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        _client.EventReceived -= OnEventReceived;
        _client.Disconnected -= OnDisconnected;
        _client.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        _client.EventReceived -= OnEventReceived;
        _client.Disconnected -= OnDisconnected;
        await _client.DisposeAsync().ConfigureAwait(false);
    }
}

public sealed class TrayProtocolMismatchException(int expected, int actual)
    : Exception($"Tray protocol mismatch: expected {expected}, actual {actual}.")
{
    public int Expected { get; } = expected;

    public int Actual { get; } = actual;
}

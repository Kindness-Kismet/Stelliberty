using System.Text.Json;
using Stelliberty.Application.Tray;
using Stelliberty.Application.Platform;
using Stelliberty.Infrastructure.Core;

namespace Stelliberty.Infrastructure.Tray;

public sealed class TrayIpcClient : IDisposable, IAsyncDisposable
{
    private readonly JsonRpcPipeClient _client;

    public TrayIpcClient(string? endpoint = null)
    {
        _client = new JsonRpcPipeClient(endpoint ?? TrayEndpoint.Current);
        _client.EventReceived += OnEventReceived;
    }

    public event EventHandler? ActivationRequested;

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
        if (notification.Event == TrayProtocol.UiActivationEvent)
        {
            ActivationRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        _client.EventReceived -= OnEventReceived;
        _client.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        _client.EventReceived -= OnEventReceived;
        await _client.DisposeAsync().ConfigureAwait(false);
    }
}

public sealed class TrayProtocolMismatchException(int expected, int actual)
    : Exception($"Tray protocol mismatch: expected {expected}, actual {actual}.")
{
    public int Expected { get; } = expected;

    public int Actual { get; } = actual;
}

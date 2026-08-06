using System.Diagnostics;
using System.Collections.Concurrent;
using System.Text.Json;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Tray;
using Stelliberty.Application.Platform;
using Stelliberty.Infrastructure.Tray;

namespace Stelliberty.Tray;

internal sealed class TrayRequestRouter(
    TrayLifetime lifetime,
    UiSessionManager uiSessions)
{
    private readonly TrayLifetime _lifetime = lifetime;
    private readonly UiSessionManager _uiSessions = uiSessions;
    private readonly ConcurrentDictionary<Guid, byte> _handshakes = new();
    private readonly string _trayEpoch = Guid.NewGuid().ToString("N");
    private readonly long _startedAt = Stopwatch.GetTimestamp();

    public async Task<TrayIpcResult> HandleAsync(
        TrayIpcConnection connection,
        TrayIpcRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            if (request.Method != TrayProtocol.HelloMethod && !_handshakes.ContainsKey(connection.Id))
            {
                return TrayIpcResult.Error("tray.handshake_required", "Call tray.hello before other methods.");
            }

            return request.Method switch
            {
                TrayProtocol.HelloMethod => HandleHello(connection, request),
                TrayProtocol.HealthMethod => await HandleHealthAsync(cancellationToken).ConfigureAwait(false),
                TrayProtocol.UiActivateMethod => TrayIpcResult.Success(
                    await _uiSessions.ActivateAsync(cancellationToken).ConfigureAwait(false)),
                TrayProtocol.UiRegisterMethod => TrayIpcResult.Success(
                    await RegisterUiAsync(connection, request, cancellationToken).ConfigureAwait(false)),
                TrayProtocol.UiUnregisterMethod => TrayIpcResult.Success(
                    await UnregisterUiAsync(connection, request, cancellationToken).ConfigureAwait(false)),
                TrayProtocol.ShutdownMethod => HandleShutdown(),
                _ => TrayIpcResult.Error("tray.method_not_found", $"Unknown Tray method: {request.Method}"),
            };
        }
        catch (UiSessionException exception)
        {
            return TrayIpcResult.Error(exception.Code, exception.Message);
        }
        catch (JsonException exception)
        {
            return TrayIpcResult.Error("tray.invalid_params", exception.Message);
        }
        catch (Exception exception) when (
            request.Method == TrayProtocol.UiActivateMethod
            && exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            AppLogger.Error(exception, "Desktop UI launch failed");
            return TrayIpcResult.Error("ui.launch_failed", "Desktop UI could not be started.");
        }
    }

    public Task OnConnectionClosedAsync(Guid connectionId)
    {
        _handshakes.TryRemove(connectionId, out _);
        return _uiSessions.OnConnectionClosedAsync(connectionId);
    }

    private TrayIpcResult HandleHello(TrayIpcConnection connection, TrayIpcRequest request)
    {
        var hello = request.DeserializeParameters<TrayHelloRequest>();
        var error = ValidateClient(hello.ProtocolVersion, hello.AppVersion);
        if (error is not null)
        {
            return error;
        }

        _handshakes[connection.Id] = 0;
        return TrayIpcResult.Success(CreateHello());
    }

    private async Task<TrayIpcResult> HandleHealthAsync(CancellationToken cancellationToken)
    {
        var ui = await _uiSessions.GetStateAsync(cancellationToken).ConfigureAwait(false);
        return TrayIpcResult.Success(new TrayHealth(
            Environment.ProcessId,
            _trayEpoch,
            (long)Stopwatch.GetElapsedTime(_startedAt).TotalMilliseconds,
            ui.UiPid,
            ui.IsLaunchPending));
    }

    private async Task<UiRegisterResult> RegisterUiAsync(
        TrayIpcConnection connection,
        TrayIpcRequest request,
        CancellationToken cancellationToken)
    {
        var register = request.DeserializeParameters<UiRegisterRequest>();
        var error = ValidateClient(register.ProtocolVersion, register.AppVersion);
        if (error is not null)
        {
            throw new UiSessionException(error.ErrorCode!, error.ErrorMessage!);
        }

        return await _uiSessions.RegisterAsync(
            register.SessionToken,
            register.UiPid,
            new IpcUiSessionConnection(connection),
            cancellationToken).ConfigureAwait(false);
    }

    private Task<UiUnregisterResult> UnregisterUiAsync(
        TrayIpcConnection connection,
        TrayIpcRequest request,
        CancellationToken cancellationToken)
    {
        var unregister = request.DeserializeParameters<UiUnregisterRequest>();
        return _uiSessions.UnregisterAsync(unregister.SessionId, connection.Id, cancellationToken);
    }

    private TrayIpcResult HandleShutdown()
    {
        // 先让响应写回客户端，再结束宿主监听。
        _ = Task.Run(async () =>
        {
            await Task.Delay(100).ConfigureAwait(false);
            _lifetime.RequestStop();
        });
        return TrayIpcResult.Success(new { accepted = true });
    }

    private TrayIpcResult? ValidateClient(int protocolVersion, string appVersion)
    {
        if (protocolVersion != TrayProtocol.Version)
        {
            return TrayIpcResult.Error(
                "tray.protocol_mismatch",
                $"Expected protocol {TrayProtocol.Version}, received {protocolVersion}.");
        }

        return appVersion == AppMetadata.Version
            ? null
            : TrayIpcResult.Error(
                "tray.version_mismatch",
                $"Expected app version {AppMetadata.Version}, received {appVersion}.");
    }

    private TrayHello CreateHello() => new(
        TrayProtocol.Version,
        AppMetadata.Version,
        Environment.ProcessId,
        _trayEpoch,
        ["ui_session"],
        0);

    private sealed class IpcUiSessionConnection(TrayIpcConnection connection) : IUiSessionConnection
    {
        public Guid Id => connection.Id;

        public Task RequestActivationAsync(CancellationToken cancellationToken) =>
            connection.SendEventAsync(TrayProtocol.UiActivationEvent, new { }, cancellationToken);
    }
}

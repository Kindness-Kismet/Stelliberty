using System.Diagnostics;
using System.Text.Json;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Platform;
using Stelliberty.Application.Runtime;
using Stelliberty.Domain.CoreLogs;
using Stelliberty.Infrastructure.Proxies;

namespace Stelliberty.Infrastructure.Core;

public sealed class ServiceModeCoreManager : IReadyCoreManager, IDisposable
{
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan ReadyPollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan StatePollInterval = TimeSpan.FromSeconds(2);

    private readonly IServiceModeManager _serviceModeManager;
    private readonly HttpClient _coreClient;
    private readonly CorePipeLogStreamer _logStreamer;
    private readonly Func<string, string> _writeActiveConfig;
    private readonly Action<bool>? _setCoreHostActive;
    private readonly string _corePipe;
    private readonly string _coreBinaryPath;
    private readonly string _coreDirectory;
    private readonly object _monitorGate = new();
    private CancellationTokenSource? _monitorCancellation;
    private Task? _monitorTask;
    private CoreSnapshot? _lastSnapshot;
    private bool _isDisposed;

    public ServiceModeCoreManager(
        IServiceModeManager serviceModeManager,
        string corePipe,
        string coreBinaryPath,
        string coreDirectory,
        Func<string, string> writeActiveConfig,
        Action<bool>? setCoreHostActive = null)
    {
        _serviceModeManager = serviceModeManager;
        _coreClient = PipeCoreProxyClient.CreatePipeHttpClient(corePipe);
        _logStreamer = new CorePipeLogStreamer(corePipe);
        _logStreamer.MessageReceived += OnLogMessageReceived;
        _corePipe = corePipe;
        _coreBinaryPath = coreBinaryPath;
        _coreDirectory = coreDirectory;
        _writeActiveConfig = writeActiveConfig;
        _setCoreHostActive = setCoreHostActive;
    }

    public event EventHandler<CoreSnapshot>? StateChanged;

    public event EventHandler<CoreLogMessage>? CoreLogReceived;

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        StopStatusMonitor();
        _logStreamer.MessageReceived -= OnLogMessageReceived;
        _logStreamer.Dispose();
        _coreClient.Dispose();
    }

    public async Task<CoreSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var status = await _serviceModeManager.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        var snapshot = ToSnapshot(status);
        PublishSnapshot(snapshot);
        return snapshot;
    }

    public async Task EnsureReadyAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var pid = await WaitReadyAsync(cancellationToken).ConfigureAwait(false);
        _logStreamer.Restart();
        StartStatusMonitor();
        PublishState(CoreState.Running, pid, null);
    }

    public async Task<CoreApplyConfigResult> ApplyConfigAsync(CoreApplyConfigRequest request, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var activePath = _writeActiveConfig(await File.ReadAllTextAsync(request.RuntimeYamlPath, cancellationToken).ConfigureAwait(false));
        var serviceRequest = new ServiceModeCoreHostRequest(
            _coreBinaryPath,
            _coreDirectory,
            activePath);
        var result = await _serviceModeManager.StartCoreHostAsync(serviceRequest, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(result.Message);
        }

        var pid = await WaitReadyAsync(cancellationToken).ConfigureAwait(false);
        _logStreamer.Restart();
        StartStatusMonitor();
        PublishState(CoreState.Running, pid, null);
        return new CoreApplyConfigResult(CoreApplyMode.Restart, pid);
    }

    public async Task RestartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var stopwatch = Stopwatch.StartNew();
        var result = await _serviceModeManager.RestartCoreHostAsync(cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(result.Message);
        }

        var pid = await WaitReadyAsync(cancellationToken).ConfigureAwait(false);
        _logStreamer.Restart();
        StartStatusMonitor();
        PublishState(CoreState.Running, pid, null);
        AppLogger.Info($"Service-mode core restart is ready: pid={pid} elapsed={stopwatch.Elapsed.TotalMilliseconds:0}ms");
    }

    private void StartStatusMonitor()
    {
        if (_isDisposed)
        {
            return;
        }

        lock (_monitorGate)
        {
            if (_isDisposed || _monitorCancellation is not null)
            {
                return;
            }

            _monitorCancellation = new CancellationTokenSource();
            _monitorTask = Task.Run(() => MonitorStatusAsync(_monitorCancellation.Token));
        }
    }

    private void StopStatusMonitor()
    {
        CancellationTokenSource? cancellation;
        Task? task;
        lock (_monitorGate)
        {
            cancellation = _monitorCancellation;
            task = _monitorTask;
            _monitorCancellation = null;
            _monitorTask = null;
        }

        if (cancellation is null)
        {
            return;
        }

        cancellation.Cancel();
        DisposeCancellationAfterTask(cancellation, task);
    }

    private static void DisposeCancellationAfterTask(CancellationTokenSource cancellation, Task? task)
    {
        if (task is null || task.IsCompleted)
        {
            cancellation.Dispose();
            return;
        }

        task.ContinueWith(
            _ => cancellation.Dispose(),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void OnLogMessageReceived(object? sender, CoreLogMessage message)
    {
        if (_isDisposed)
        {
            return;
        }

        CoreLogReceived?.Invoke(this, message);
    }

    private async Task MonitorStatusAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(StatePollInterval, cancellationToken).ConfigureAwait(false);
                var status = await _serviceModeManager.GetStatusAsync(cancellationToken).ConfigureAwait(false);
                PublishSnapshot(ToSnapshot(status));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                PublishSnapshot(new CoreSnapshot(CoreState.Unavailable, null, _corePipe, exception.Message));
            }
        }
    }

    private async Task<int> WaitReadyAsync(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < ReadyTimeout)
        {
            var status = await _serviceModeManager.GetStatusAsync(cancellationToken).ConfigureAwait(false);
            // 服务上报的进程号用于确认管道响应来自服务核心。
            if (status.IsRunning
                && status.CoreState == "running"
                && status.CorePid is > 0
                && !string.IsNullOrWhiteSpace(await ProbeVersionAsync(cancellationToken).ConfigureAwait(false)))
            {
                return status.CorePid.Value;
            }

            await Task.Delay(ReadyPollInterval, cancellationToken).ConfigureAwait(false);
        }

        PublishState(CoreState.Crashed, null, "Service-mode core startup timed out");
        throw new TimeoutException($"Service-mode core was not ready within {ReadyTimeout.TotalSeconds:N0} seconds.");
    }

    private void PublishState(CoreState state, int? pid, string? lastError)
    {
        PublishSnapshot(new CoreSnapshot(state, pid, _corePipe, lastError));
    }

    private void PublishSnapshot(CoreSnapshot snapshot)
    {
        if (_isDisposed)
        {
            return;
        }

        if (snapshot.State != CoreState.Running)
        {
            _logStreamer.Stop();
        }
        _setCoreHostActive?.Invoke(snapshot.State == CoreState.Running);
        if (_lastSnapshot == snapshot)
        {
            return;
        }

        _lastSnapshot = snapshot;
        StateChanged?.Invoke(this, snapshot);
    }

    private async Task<string?> ProbeVersionAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _coreClient.GetAsync("version", cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("version", out var version) && version.ValueKind == JsonValueKind.String
                ? version.GetString()
                : null;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException or IOException)
        {
            return null;
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
    }

    private static CoreState ParseCoreState(string? value)
    {
        return value switch
        {
            "running" => CoreState.Running,
            "stopping" => CoreState.Stopping,
            "stopped" => CoreState.Stopped,
            "crashed" => CoreState.Crashed,
            _ => CoreState.Unavailable,
        };
    }

    private CoreSnapshot ToSnapshot(ServiceModeStatus status)
    {
        return new CoreSnapshot(
            ParseCoreState(status.CoreState),
            status.CorePid,
            _corePipe,
            status.CoreLastError);
    }
}

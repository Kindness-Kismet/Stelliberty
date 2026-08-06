using Stelliberty.Application.Tray;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Platform;
using Stelliberty.Application.Runtime;
using Stelliberty.Application.Settings;
using Stelliberty.Application.Subscriptions;
using Stelliberty.Infrastructure.Tray;
using Stelliberty.Infrastructure.Core;
using Stelliberty.Infrastructure.Overrides;
using Stelliberty.Infrastructure.Platform;
using Stelliberty.Infrastructure.Runtime;
using Stelliberty.Infrastructure.Settings;
using Stelliberty.Infrastructure.Subscriptions;
using Stelliberty.Native.Hub;

namespace Stelliberty.Tray;

internal interface ITrayCoreRuntime
{
    event EventHandler<TrayCoreStatus>? StateChanged;

    event EventHandler<TrayCoreLogEntry>? LogReceived;

    TrayCoreStatus CurrentStatus { get; }

    Task<TrayCoreOperationResult> EnsureStartedAsync(CancellationToken cancellationToken);

    Task<TrayCoreOperationResult> StopAsync(CancellationToken cancellationToken);

    Task<CoreApplyConfigResult> ApplyConfigAsync(
        CoreApplyConfigRequest request,
        CancellationToken cancellationToken);

    Task RestartAsync(CancellationToken cancellationToken);
}

internal sealed class TrayCoreRuntimeHost : ITrayCoreRuntime, IAsyncDisposable
{
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly object _stateGate = new();
    private readonly CoreLogJournal _logs;
    private IpcCoreManager? _manager;
    private TrayCoreStatus _status = new(
        new CoreSnapshot(CoreState.Unavailable, null, TrayCoreEndpoints.Core, null),
        0);
    private int? _lastCorePid;
    private bool _isHubStarted;
    private bool _isDisposed;

    public TrayCoreRuntimeHost(CoreLogJournal logs)
    {
        _logs = logs;
    }

    public event EventHandler<TrayCoreStatus>? StateChanged;

    public event EventHandler<TrayCoreLogEntry>? LogReceived;

    public TrayCoreStatus CurrentStatus
    {
        get
        {
            lock (_stateGate)
            {
                return _status;
            }
        }
    }

    public async Task<TrayCoreOperationResult> EnsureStartedAsync(CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (_isHubStarted && _manager is not null)
            {
                var current = await _manager.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
                if (current.State == CoreState.Running)
                {
                    return new TrayCoreOperationResult(
                        true,
                        "Normal core is already running.",
                        UpdateStatus(current));
                }
            }

            Func<BootstrapResult> start = _isHubStarted ? HubBootstrap.StartCore : StartHub;
            var bootstrap = await Task.Run(start, cancellationToken).ConfigureAwait(false);
            if (!bootstrap.Ok)
            {
                var failed = UpdateStatus(new CoreSnapshot(
                    CoreState.Unavailable,
                    null,
                    TrayCoreEndpoints.Core,
                    bootstrap.Message));
                return new TrayCoreOperationResult(false, bootstrap.Message, failed);
            }

            _isHubStarted = true;
            var manager = EnsureManager();
            await manager.EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
            var status = UpdateStatus(await manager.GetSnapshotAsync(cancellationToken).ConfigureAwait(false));
            AppLogger.Info($"Tray owns normal core: pid={status.Snapshot.Pid} generation={status.CoreGeneration}");
            return new TrayCoreOperationResult(true, bootstrap.Message, status);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            AppLogger.Error(exception, "Tray normal core startup failed");
            var failed = UpdateStatus(new CoreSnapshot(
                CoreState.Unavailable,
                null,
                TrayCoreEndpoints.Core,
                exception.Message));
            return new TrayCoreOperationResult(false, exception.Message, failed);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<TrayCoreOperationResult> StopAsync(CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (!_isHubStarted)
            {
                return new TrayCoreOperationResult(true, "Normal core is not started.", CurrentStatus);
            }

            var result = await Task.Run(HubBootstrap.StopCore, cancellationToken).ConfigureAwait(false);
            var snapshot = _manager is null
                ? new CoreSnapshot(CoreState.Stopped, null, TrayCoreEndpoints.Core, null)
                : await _manager.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
            return new TrayCoreOperationResult(result.Ok, result.Message, UpdateStatus(snapshot));
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<CoreApplyConfigResult> ApplyConfigAsync(
        CoreApplyConfigRequest request,
        CancellationToken cancellationToken)
    {
        ValidateRuntimeConfigPath(request.RuntimeYamlPath);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var result = await EnsureManager().ApplyConfigAsync(request, cancellationToken).ConfigureAwait(false);
            UpdateStatus(await _manager!.GetSnapshotAsync(cancellationToken).ConfigureAwait(false));
            return result;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task RestartAsync(CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            await EnsureManager().RestartAsync(cancellationToken).ConfigureAwait(false);
            UpdateStatus(await _manager!.GetSnapshotAsync(cancellationToken).ConfigureAwait(false));
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private IpcCoreManager EnsureManager()
    {
        if (_manager is not null)
        {
            return _manager;
        }

        _manager = new IpcCoreManager(TrayCoreEndpoints.Hub);
        _manager.StateChanged += OnCoreStateChanged;
        _manager.CoreLogReceived += OnCoreLogReceived;
        return _manager;
    }

    private static BootstrapResult StartHub()
    {
        return HubBootstrap.Start(new BootstrapOptions(
            PipeName: TrayCoreEndpoints.Hub,
            CorePath: TrayApplicationLayout.CoreBinaryPath,
            DataCoreDir: TrayApplicationLayout.CoreDirectory,
            UserDataDir: TrayApplicationLayout.AppDataDirectory,
            CorePipe: TrayCoreEndpoints.Core,
            BootstrapYaml: BuildInitialBootstrapYaml()));
    }

    private static string BuildInitialBootstrapYaml()
    {
        try
        {
            var directories = new TrayPlatformDirectories();
            var settingsStore = new JsonAppSettingsStore(directories);
            var selectionStore = new FileSubscriptionSelectionStore(directories.AppDataDirectory);
            var subscriptionStore = new FileSubscriptionStore(directories.AppDataDirectory);
            var overrideStore = new FileOverrideStore(directories.AppDataDirectory);
            var runtimeStore = new FileRuntimeConfigStore(directories.RuntimeDirectory);
            var builder = new StartupBootstrapConfigBuilder(
                settingsStore,
                selectionStore,
                new SelectedRuntimeFallbackGenerator(
                    subscriptionStore,
                    new SubscriptionOverrideSelectionUpdater(subscriptionStore),
                    new SelectedSubscriptionRuntimeGenerator(
                        subscriptionStore,
                        selectionStore,
                        new RuntimeConfigGenerator(new HubOverrideEngine()),
                        overrideStore,
                        runtimeStore)),
                new SubscriptionFailureRecorder(subscriptionStore));
            return builder.Build(TrayCoreEndpoints.Core, CanUseTun());
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"Tray startup config generation failed: {exception.Message}");
            return StartupBootstrapConfigBuilder.BuildDefaultEmptyYaml(TrayCoreEndpoints.Core);
        }
    }

    private static bool CanUseTun()
    {
        return AppSettingsNormalizer.CanUseTun(
            new SystemProcessPrivilegeProbe().Detect(),
            hasServiceTunHost: false);
    }

    private TrayCoreStatus UpdateStatus(CoreSnapshot snapshot)
    {
        TrayCoreStatus status;
        lock (_stateGate)
        {
            var generation = _status.CoreGeneration;
            if (snapshot.Pid is { } pid && pid != _lastCorePid)
            {
                _lastCorePid = pid;
                generation++;
                AppLogger.Info($"Normal core generation advanced: pid={pid} generation={generation}");
            }

            status = new TrayCoreStatus(snapshot, generation);
            _status = status;
        }

        StateChanged?.Invoke(this, status);
        return status;
    }

    private void OnCoreStateChanged(object? sender, CoreSnapshot snapshot) => UpdateStatus(snapshot);

    private void OnCoreLogReceived(object? sender, Stelliberty.Domain.CoreLogs.CoreLogMessage message)
    {
        var entry = _logs.Append(CurrentStatus.CoreGeneration, message);
        LogReceived?.Invoke(this, entry);
    }

    private static void ValidateRuntimeConfigPath(string path)
    {
        var runtimeRoot = Path.GetFullPath(TrayApplicationLayout.RuntimeDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(path);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!fullPath.StartsWith(runtimeRoot, comparison))
        {
            throw new InvalidOperationException("Runtime config must be inside the application runtime directory.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _operationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            if (_manager is not null)
            {
                _manager.StateChanged -= OnCoreStateChanged;
                _manager.CoreLogReceived -= OnCoreLogReceived;
                await _manager.DisposeAsync().ConfigureAwait(false);
            }

            await Task.Run(HubBootstrap.Shutdown).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
            _operationGate.Dispose();
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
    }
}

using Stelliberty.Application.CoreLogs;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Runtime;
using Stelliberty.Domain.CoreLogs;

namespace Stelliberty.Application.Proxies;

public sealed class ProxySelectionRestoringCoreManager : ICoreManager, IDisposable
{
    private readonly ICoreManager _inner;
    private readonly ProxySelectionRestorer _restorer;
    private readonly SemaphoreSlim _resetGate = new(1, 1);
    private readonly object _stateGate = new();
    private bool _hasObservedRunning;
    private bool _isManagedReset;
    private bool _hasPendingObservedReset;
    private string? _pendingObservedSubscriptionId;
    private int? _lastRunningPid;
    private bool _isDisposed;

    public ProxySelectionRestoringCoreManager(ICoreManager inner, ProxySelectionRestorer restorer)
    {
        _inner = inner;
        _restorer = restorer;
        _inner.StateChanged += OnInnerStateChanged;
        _inner.CoreLogReceived += OnInnerCoreLogReceived;
    }

    public event EventHandler<CoreSnapshot>? StateChanged;

    public event EventHandler<CoreLogMessage>? CoreLogReceived;

    public Task<CoreSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        return _inner.GetSnapshotAsync(cancellationToken);
    }

    public Task<CoreApplyConfigResult> ApplyConfigAsync(
        CoreApplyConfigRequest request,
        CancellationToken cancellationToken = default)
    {
        return RunCoreResetAsync(
            "runtime-config-apply",
            request.SubscriptionId,
            token => _inner.ApplyConfigAsync(request, token),
            cancellationToken);
    }

    public Task RestartAsync(CancellationToken cancellationToken = default)
    {
        return RunCoreResetAsync(
            "core-restart",
            token => _inner.RestartAsync(token),
            cancellationToken);
    }

    public Task<T> RunCoreResetAsync<T>(
        string origin,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        return RunCoreResetAsync(origin, expectedSubscriptionId: null, operation, cancellationToken);
    }

    public void NotifyCoreResetStarting(string origin)
    {
        _restorer.SuspendCoreSelectionImport(origin);
    }

    public async Task RestoreCurrentCoreSelectionsAsync(
        string origin,
        CancellationToken cancellationToken = default)
    {
        await _resetGate.WaitAsync(cancellationToken);
        try
        {
            var subscriptionId = _restorer.SuspendCoreSelectionImport(origin);
            await TryRestoreIfRunningAsync(subscriptionId, origin);
        }
        finally
        {
            _resetGate.Release();
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _inner.StateChanged -= OnInnerStateChanged;
        _inner.CoreLogReceived -= OnInnerCoreLogReceived;
    }

    private async Task RunCoreResetAsync(
        string origin,
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        await RunCoreResetAsync(
            origin,
            expectedSubscriptionId: null,
            async token =>
            {
                await operation(token);
                return true;
            },
            cancellationToken);
    }

    private async Task<T> RunCoreResetAsync<T>(
        string origin,
        string? expectedSubscriptionId,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        await _resetGate.WaitAsync(cancellationToken);
        var subscriptionId = _restorer.SuspendCoreSelectionImport(origin);
        if (expectedSubscriptionId is not null)
        {
            subscriptionId = string.IsNullOrWhiteSpace(expectedSubscriptionId) ? null : expectedSubscriptionId;
        }

        SetManagedReset(true);
        try
        {
            var result = await operation(cancellationToken);
            await TryRestoreIfRunningAsync(subscriptionId, origin);
            return result;
        }
        finally
        {
            SetManagedReset(false);
            _resetGate.Release();
        }
    }

    private async Task TryRestoreIfRunningAsync(string? subscriptionId, string origin)
    {
        CoreSnapshot snapshot;
        try
        {
            snapshot = await _inner.GetSnapshotAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"Proxy selection restore deferred: origin={origin} reason=core-status-unavailable message={exception.Message}");
            return;
        }

        if (snapshot.State != CoreState.Running)
        {
            AppLogger.Info($"Proxy selection restore deferred: origin={origin} coreState={snapshot.State}");
            return;
        }

        try
        {
            await _restorer.RestoreSubscriptionAsync(subscriptionId, origin, CancellationToken.None);
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"Proxy selection restore failed: origin={origin} message={exception.Message}");
        }
    }

    private void OnInnerStateChanged(object? sender, CoreSnapshot snapshot)
    {
        string? subscriptionId = null;
        var shouldRestore = false;
        lock (_stateGate)
        {
            if (!_isManagedReset)
            {
                if (snapshot.State != CoreState.Running && _hasObservedRunning)
                {
                    if (!_hasPendingObservedReset)
                    {
                        _pendingObservedSubscriptionId = _restorer.SuspendCoreSelectionImport("core-state-change");
                        _hasPendingObservedReset = true;
                    }
                }
                else if (snapshot.State == CoreState.Running)
                {
                    var processChanged = _hasObservedRunning
                        && snapshot.Pid is not null
                        && snapshot.Pid != _lastRunningPid;
                    if (processChanged && !_hasPendingObservedReset)
                    {
                        _pendingObservedSubscriptionId = _restorer.SuspendCoreSelectionImport("core-process-change");
                        _hasPendingObservedReset = true;
                    }

                    if (_hasPendingObservedReset)
                    {
                        subscriptionId = _pendingObservedSubscriptionId;
                        shouldRestore = true;
                    }
                }
            }

            if (snapshot.State == CoreState.Running)
            {
                _hasObservedRunning = true;
                _lastRunningPid = snapshot.Pid;
            }
        }

        StateChanged?.Invoke(this, snapshot);
        if (shouldRestore)
        {
            _ = RestoreObservedCoreAsync(subscriptionId);
        }
    }

    private async Task RestoreObservedCoreAsync(string? subscriptionId)
    {
        await _resetGate.WaitAsync();
        try
        {
            lock (_stateGate)
            {
                if (!_hasPendingObservedReset || _isManagedReset)
                {
                    return;
                }

                _hasPendingObservedReset = false;
                _pendingObservedSubscriptionId = null;
            }

            await TryRestoreIfRunningAsync(subscriptionId, "core-state-recovery");
        }
        finally
        {
            _resetGate.Release();
        }
    }

    private void OnInnerCoreLogReceived(object? sender, CoreLogMessage message)
    {
        CoreLogReceived?.Invoke(this, message);
    }

    private void SetManagedReset(bool isManagedReset)
    {
        lock (_stateGate)
        {
            _isManagedReset = isManagedReset;
            if (isManagedReset)
            {
                _hasPendingObservedReset = false;
                _pendingObservedSubscriptionId = null;
            }
        }
    }
}

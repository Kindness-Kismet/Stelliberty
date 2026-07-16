using Stelliberty.Application.CoreLogs;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Runtime;
using Stelliberty.Domain.CoreLogs;
using Stelliberty.Infrastructure.Core;

namespace Stelliberty.Desktop.Services;

internal sealed class SwitchableCoreManager : ICoreManager, IDisposable, IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ICoreManager _current;
    private bool _isDisposed;

    public SwitchableCoreManager(ICoreManager initial)
    {
        _current = initial;
        Attach(initial);
    }

    public event EventHandler<CoreSnapshot>? StateChanged;

    public event EventHandler<CoreLogMessage>? CoreLogReceived;

    public Task EnsureReadyAsync(CancellationToken cancellationToken = default)
    {
        return UseAsync(EnsureCoreReadyAsync, cancellationToken);
    }

    public async Task SwitchAsync(ICoreManager next, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            CoreSnapshot snapshot;
            try
            {
                snapshot = await EnsureCoreReadyAsync(next, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                DisposeCore(next);
                throw;
            }

            var previous = _current;
            Detach(previous);
            _current = next;
            Attach(next);
            DisposeCore(previous);
            try
            {
                StateChanged?.Invoke(this, snapshot);
            }
            catch (Exception exception)
            {
                AppLogger.Warning($"Core manager state observer failed during switch: {exception.Message}");
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<CoreSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        return UseAsync((core, token) => core.GetSnapshotAsync(token), cancellationToken);
    }

    public Task<CoreApplyConfigResult> ApplyConfigAsync(CoreApplyConfigRequest request, CancellationToken cancellationToken = default)
    {
        return UseAsync((core, token) => core.ApplyConfigAsync(request, token), cancellationToken);
    }

    public Task RestartAsync(CancellationToken cancellationToken = default)
    {
        return UseAsync((core, token) => core.RestartAsync(token), cancellationToken);
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            Detach(_current);
            DisposeCore(_current);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task UseAsync(Func<ICoreManager, CancellationToken, Task> operation, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            await operation(_current, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<T> UseAsync<T>(Func<ICoreManager, CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            return await operation(_current, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task<CoreSnapshot> EnsureCoreReadyAsync(ICoreManager core, CancellationToken cancellationToken)
    {
        switch (core)
        {
            case IpcCoreManager ipcCoreManager:
                await ipcCoreManager.ConnectAsync(cancellationToken).ConfigureAwait(false);
                break;
            case ServiceModeCoreManager serviceModeCoreManager:
                await serviceModeCoreManager.EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
                break;
        }

        return await core.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
    }

    private void Attach(ICoreManager core)
    {
        core.StateChanged += OnStateChanged;
        core.CoreLogReceived += OnCoreLogReceived;
    }

    private void Detach(ICoreManager core)
    {
        core.StateChanged -= OnStateChanged;
        core.CoreLogReceived -= OnCoreLogReceived;
    }

    private void OnStateChanged(object? sender, CoreSnapshot snapshot)
    {
        StateChanged?.Invoke(this, snapshot);
    }

    private void OnCoreLogReceived(object? sender, CoreLogMessage message)
    {
        CoreLogReceived?.Invoke(this, message);
    }

    private static void DisposeCore(ICoreManager core)
    {
        try
        {
            if (core is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"Core manager dispose failed: {exception.Message}");
        }
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(SwitchableCoreManager));
        }
    }
}

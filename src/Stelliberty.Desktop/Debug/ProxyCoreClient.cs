using Stelliberty.Application.Connections;
using Stelliberty.Domain.Connections;
using Stelliberty.Application.Proxies;
using Stelliberty.Domain.Proxies;

namespace Stelliberty.Desktop.Debug;

#if DEBUG
internal sealed class ProxyCoreClient : IProxyCoreClient, IDisposable
{
    private static readonly object SyncRoot = new();
    private static ProxyChangeRequest? _lastProxyChangeRequest;
    private static string? _lastClearedProxyGroupName;
    private static ConnectionCloseRequest? _lastCloseRequest;
    private readonly IProxyCoreClient? _fallback;

    public ProxyCoreClient(IProxyCoreClient? fallback = null)
    {
        _fallback = fallback;
    }

    public void Dispose()
    {
        if (_fallback is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    public static ProxyChangeRequest? LastProxyChangeRequest
    {
        get
        {
            lock (SyncRoot)
            {
                return _lastProxyChangeRequest;
            }
        }
    }

    public static ConnectionCloseRequest? LastCloseRequest
    {
        get
        {
            lock (SyncRoot)
            {
                return _lastCloseRequest;
            }
        }
    }

    public static string? LastClearedProxyGroupName
    {
        get
        {
            lock (SyncRoot)
            {
                return _lastClearedProxyGroupName;
            }
        }
    }

    public Task<IReadOnlyList<ConnectionInfo>> GetConnectionsAsync(CancellationToken cancellationToken = default)
    {
        return _fallback?.GetConnectionsAsync(cancellationToken) ?? Task.FromResult<IReadOnlyList<ConnectionInfo>>([]);
    }

    public async Task<bool> ChangeProxyAsync(ProxyChangeRequest request, CancellationToken cancellationToken = default)
    {
        lock (SyncRoot)
        {
            _lastProxyChangeRequest = request;
        }

        return _fallback is null || await _fallback.ChangeProxyAsync(request, cancellationToken);
    }

    public async Task<bool> ClearProxySelectionAsync(string groupName, CancellationToken cancellationToken = default)
    {
        lock (SyncRoot)
        {
            _lastClearedProxyGroupName = groupName;
        }

        return _fallback is null || await _fallback.ClearProxySelectionAsync(groupName, cancellationToken);
    }

    public async Task<bool> CloseConnectionsAsync(ConnectionCloseRequest request, CancellationToken cancellationToken = default)
    {
        lock (SyncRoot)
        {
            _lastCloseRequest = request;
        }

        return _fallback is null || await _fallback.CloseConnectionsAsync(request, cancellationToken);
    }

    public Task<ProxyRuntimeSnapshot> GetProxiesAsync(CancellationToken cancellationToken = default)
    {
        return _fallback?.GetProxiesAsync(cancellationToken) ?? Task.FromResult(new ProxyRuntimeSnapshot([]));
    }

    public Task<OutboundMode?> GetOutboundModeAsync(CancellationToken cancellationToken = default)
    {
        return _fallback?.GetOutboundModeAsync(cancellationToken) ?? Task.FromResult<OutboundMode?>(null);
    }

    public Task<bool> SetOutboundModeAsync(OutboundMode mode, CancellationToken cancellationToken = default)
    {
        return _fallback?.SetOutboundModeAsync(mode, cancellationToken) ?? Task.FromResult(false);
    }

    public Task<string?> GetVersionAsync(CancellationToken cancellationToken = default)
    {
        return _fallback?.GetVersionAsync(cancellationToken) ?? Task.FromResult<string?>(null);
    }

    public Task<CoreRuntimeStats?> GetRuntimeStatsAsync(CancellationToken cancellationToken = default)
    {
        return _fallback?.GetRuntimeStatsAsync(cancellationToken) ?? Task.FromResult<CoreRuntimeStats?>(null);
    }

    public Task<CoreTrafficRate?> GetTrafficAsync(CancellationToken cancellationToken = default)
    {
        return _fallback?.GetTrafficAsync(cancellationToken) ?? Task.FromResult<CoreTrafficRate?>(null);
    }
}
#endif

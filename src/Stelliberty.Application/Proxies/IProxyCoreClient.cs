using Stelliberty.Domain.Proxies;
using Stelliberty.Application.Connections;
using Stelliberty.Domain.Connections;

namespace Stelliberty.Application.Proxies;

public interface IProxyCoreClient
{
    Task<IReadOnlyList<ConnectionInfo>> GetConnectionsAsync(CancellationToken cancellationToken = default);

    Task<bool> ChangeProxyAsync(ProxyChangeRequest request, CancellationToken cancellationToken = default);

    Task<bool> ClearProxySelectionAsync(string groupName, CancellationToken cancellationToken = default);

    Task<bool> CloseConnectionsAsync(ConnectionCloseRequest request, CancellationToken cancellationToken = default);

    Task<ProxyRuntimeSnapshot> GetProxiesAsync(CancellationToken cancellationToken = default);

    Task<OutboundMode?> GetOutboundModeAsync(CancellationToken cancellationToken = default);

    Task<bool> SetOutboundModeAsync(OutboundMode mode, CancellationToken cancellationToken = default);

    Task<string?> GetVersionAsync(CancellationToken cancellationToken = default);

    Task<CoreRuntimeStats?> GetRuntimeStatsAsync(CancellationToken cancellationToken = default);

    Task<CoreTrafficRate?> GetTrafficAsync(CancellationToken cancellationToken = default);
}

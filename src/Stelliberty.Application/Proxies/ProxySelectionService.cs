using Stelliberty.Application.Connections;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Subscriptions;
using Stelliberty.Domain.Connections;
using Stelliberty.Domain.Proxies;

namespace Stelliberty.Application.Proxies;

// 代理选择先应用到核心，再由分组类型决定是否清理连接。
public sealed class ProxySelectionService(
    IProxyCoreClient? coreClient = null,
    IProxySelectionStore? selectionStore = null,
    ISubscriptionSelectionStore? subscriptionSelectionStore = null)
{
    // 核心拒绝时返回 null；applyToCore=false 只计算本地状态。
    public async Task<ProxySelectionResult?> SelectNodeAsync(
        ProxyConfig config,
        string groupName,
        string nodeName,
        bool applyToCore,
        CancellationToken cancellationToken = default)
    {
        AppLogger.Info($"Proxy selection requested: group={groupName} proxy={nodeName} applyCore={applyToCore.ToString().ToLowerInvariant()}");
        var result = new ProxyGroupSelector(config).Select(groupName, nodeName);
        if (applyToCore && coreClient is not null)
        {
            if (!await coreClient.ChangeProxyAsync(result.ChangeRequest, cancellationToken))
            {
                AppLogger.Warning($"Proxy selection rejected by core: group={groupName} proxy={nodeName}");
                return null;
            }

            if (result.ShouldCloseConnections)
            {
                await coreClient.CloseConnectionsAsync(new ConnectionCloseRequest(ConnectionCloseMode.All), cancellationToken);
            }
        }

        PersistSelection(groupName, nodeName);
        AppLogger.Info($"Proxy selection completed: group={groupName} proxy={nodeName} closeConnections={result.ShouldCloseConnections.ToString().ToLowerInvariant()}");
        return result;
    }

    private void PersistSelection(string groupName, string nodeName)
    {
        var subscriptionId = subscriptionSelectionStore?.GetCurrentSubscriptionId();
        if (selectionStore is null || string.IsNullOrWhiteSpace(subscriptionId))
        {
            return;
        }

        selectionStore.SetSelection(subscriptionId, groupName, nodeName);
    }
}

using Stelliberty.Application.Proxies;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Domain.Proxies;

namespace Stelliberty.Infrastructure.Proxies;

public sealed class MihomoApiProxyConfigProvider(
    IProxyCoreClient client,
    IProxyGroupIconProvider? iconProvider = null) : IProxyConfigProvider, IProxyRuntimeSnapshotSource
{
    private static readonly HashSet<string> GroupTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Selector", "URLTest", "Fallback", "LoadBalance", "Smart", "Relay",
    };

    public ProxyRuntimeSnapshot? LastSnapshot { get; private set; }

    public async Task<ProxyConfig> LoadAsync(CancellationToken cancellationToken = default)
    {
        LastSnapshot = null;
        // 代理列表和出站模式共同决定最终配置。
        var snapshotTask = client.GetProxiesAsync(cancellationToken);
        var modeTask = client.GetOutboundModeAsync(cancellationToken);
        await Task.WhenAll(snapshotTask, modeTask).ConfigureAwait(false);
        var snapshot = await snapshotTask.ConfigureAwait(false);
        var mode = await modeTask.ConfigureAwait(false);
        LastSnapshot = snapshot;

        var groups = new List<ProxyGroup>();
        var nodes = new Dictionary<string, ProxyNode>(StringComparer.Ordinal);
        foreach (var entry in snapshot.Entries)
        {
            // 核心 history 跨订阅和进程复用，代理页只显示当前会话的延迟测试结果。
            nodes[entry.Name] = new ProxyNode(entry.Name, entry.Type, ProviderName: entry.ProviderName);

            // mihomo 可能给分组返回未知类型；all[] 仍表示分组。
            if (GroupTypes.Contains(entry.Type) || entry.All.Count > 0)
            {
                // 保留核心原始状态；导入可见性由本地状态决定。
                var now = entry.Now;
                var fixedSelection = ProxyGroupTypes.UsesFixedSelection(entry.Type) ? NullIfWhiteSpace(entry.Fixed) : null;
                groups.Add(new ProxyGroup(
                    Name: entry.Name,
                    Type: entry.Type,
                    Now: now,
                    All: entry.All,
                    Fixed: fixedSelection,
                    IsHidden: entry.IsHidden,
                    Icon: null));
            }
        }

        return new ProxyConfig(await EnrichGroupIconsAsync(groups, cancellationToken).ConfigureAwait(false), nodes, mode);
    }

    private static string? NullIfWhiteSpace(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;

    private async Task<IReadOnlyList<ProxyGroup>> EnrichGroupIconsAsync(
        IReadOnlyList<ProxyGroup> groups,
        CancellationToken cancellationToken)
    {
        if (iconProvider is null || !groups.Any(group => string.IsNullOrWhiteSpace(group.Icon)))
        {
            return groups;
        }

        IReadOnlyDictionary<string, string> icons;
        try
        {
            icons = await iconProvider.LoadIconsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            AppLogger.Warning($"Runtime proxy group icon load failed: {exception.Message}");
            return groups;
        }

        if (icons.Count == 0)
        {
            return groups;
        }

        return groups
            .Select(group => string.IsNullOrWhiteSpace(group.Icon)
                && icons.TryGetValue(group.Name, out var icon)
                    ? group with { Icon = icon }
                    : group)
            .ToList();
    }
}

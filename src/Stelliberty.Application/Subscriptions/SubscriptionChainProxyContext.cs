using Stelliberty.Domain.Subscriptions;

namespace Stelliberty.Application.Subscriptions;

// 链式代理对话框以覆写后的配置为准。
public sealed record SubscriptionChainProxyContext(
    IReadOnlyList<string> BuiltinChainProxyNames,
    IReadOnlyList<ChainProxyGroupOption> ProxyGroups,
    IReadOnlyList<ChainProxyHopOption> Candidates);

public sealed record ChainProxyGroupOption(string Name, string Type);

// 代理组候选记录不可作为所属组的范围，避免生成混合循环。
public sealed record ChainProxyHopOption(
    SubscriptionChainProxyHop Hop,
    string Type,
    IReadOnlyList<string>? BlockedProxyGroupNames = null)
{
    public IReadOnlyList<string> BlockedProxyGroupNames { get; init; } = BlockedProxyGroupNames ?? [];

    public string Name => Hop.Name;

    public string Key => $"{Hop.Kind}:{Hop.Name}";

    public bool IsAvailableFor(string? proxyGroupName)
        => !string.IsNullOrWhiteSpace(proxyGroupName)
            && !BlockedProxyGroupNames.Contains(proxyGroupName, StringComparer.Ordinal);
}

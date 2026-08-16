using Stelliberty.Domain.Subscriptions;

namespace Stelliberty.Application.Subscriptions;

// 链式代理对话框以覆写后的配置为准。
public sealed record SubscriptionChainProxyContext(
    IReadOnlyList<string> BuiltinChainProxyNames,
    IReadOnlyList<ChainProxyGroupOption> ProxyGroups,
    IReadOnlyList<ChainProxyHopOption> Candidates);

public sealed record ChainProxyGroupOption(string Name, string Type);

public sealed record ChainProxyHopOption(
    SubscriptionChainProxyHop Hop,
    string Type)
{
    public string Name => Hop.Name;

    public string Key => $"{Hop.Kind}:{Hop.Name}";
}

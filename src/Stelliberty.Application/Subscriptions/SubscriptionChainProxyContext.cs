namespace Stelliberty.Application.Subscriptions;

// 链式代理对话框以覆写后的配置为准。
public sealed record SubscriptionChainProxyContext(
    IReadOnlyList<string> BuiltinChainProxyNames,
    IReadOnlyList<ChainProxyNodeOption> Candidates);

// 候选节点是真实的覆写后代理；Type 只用于展示。
public sealed record ChainProxyNodeOption(string Name, string Type);

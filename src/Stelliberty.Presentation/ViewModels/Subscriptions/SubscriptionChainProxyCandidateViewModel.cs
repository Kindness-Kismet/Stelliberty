namespace Stelliberty.Presentation.ViewModels;

using Stelliberty.Domain.Subscriptions;

// 候选标签：代理组固定作为链首，代理节点按点击顺序追加。
public sealed record SubscriptionChainProxyCandidateViewModel(
    string Key,
    SubscriptionChainProxyHopKind Kind,
    string Name,
    string Type,
    bool IsSelected)
{
    public bool IsProxyGroup => Kind == SubscriptionChainProxyHopKind.ProxyGroup;

    public string AutomationId => $"Subscriptions.ChainProxy.Candidate.{Kind}.{Name}";
}

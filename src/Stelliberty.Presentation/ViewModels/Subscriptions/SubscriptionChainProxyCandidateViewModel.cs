namespace Stelliberty.Presentation.ViewModels;

// 候选标签：选中表示按点击顺序追加，再点会移除。
public sealed record SubscriptionChainProxyCandidateViewModel(string Name, string Type, bool IsSelected)
{
    public string AutomationId => $"Subscriptions.ChainProxy.Candidate.{Name}";
}

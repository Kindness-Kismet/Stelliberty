namespace Stelliberty.Presentation.ViewModels;

using Stelliberty.Domain.Subscriptions;

public sealed record SubscriptionChainProxySlotViewModel(int Index, SubscriptionChainProxyHop Hop)
{
    public string PositionNumber => (Index + 1).ToString();

    public bool IsFirst => Index == 0;

    public string DisplayName => Hop.Name;

    public bool IsProxyGroup => Hop.Kind == SubscriptionChainProxyHopKind.ProxyGroup;

    public string Key => $"{Hop.Kind}:{Hop.Name}";

    public string AutomationId => $"Subscriptions.ChainProxy.Slot.{Hop.Kind}.{Hop.Name}";
}

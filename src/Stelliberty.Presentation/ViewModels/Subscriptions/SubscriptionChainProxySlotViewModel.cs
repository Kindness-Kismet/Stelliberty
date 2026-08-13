namespace Stelliberty.Presentation.ViewModels;

public sealed record SubscriptionChainProxySlotViewModel(int Index, string NodeName)
{
    public string PositionNumber => (Index + 1).ToString();

    public bool IsFirst => Index == 0;

    public string DisplayName => NodeName;

    public string AutomationId => $"Subscriptions.ChainProxy.Slot.{NodeName}";
}

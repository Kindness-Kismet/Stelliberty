namespace Stelliberty.Presentation.ViewModels;

public sealed record SubscriptionChainProxyBuiltinItemViewModel(string Name, bool IsEnabled)
{
    public string ToggleAutomationId => $"Subscriptions.ChainProxy.Builtin.{Name}.Toggle";
}

using Stelliberty.Application.Localization;

namespace Stelliberty.Presentation.ViewModels;

public sealed record SubscriptionChainProxyBuiltinItemViewModel(string Name, bool IsEnabled, ILocalizationService? Localization = null)
{
    public string ToggleAutomationId => $"Subscriptions.ChainProxy.Builtin.{Name}.Toggle";

    public string StatusText => IsEnabled ? Localize("Common.Enabled") : Localize("Common.Disabled");

    private string Localize(string key) => Localization?.GetString(key) ?? key;
}

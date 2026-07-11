using Stelliberty.Application.Localization;

namespace Stelliberty.Presentation.ViewModels;

public sealed record SubscriptionChainProxySlotViewModel(int Index, string NodeName, ILocalizationService? Localization = null)
{
    public string PositionLabel => string.Format(Localize("Subscriptions.ChainProxy.SlotLabel"), Index + 1);

    public string DisplayName => NodeName;

    public string AutomationId => $"Subscriptions.ChainProxy.Slot.{Index}";

    private string Localize(string key) => Localization?.GetString(key) ?? key;
}

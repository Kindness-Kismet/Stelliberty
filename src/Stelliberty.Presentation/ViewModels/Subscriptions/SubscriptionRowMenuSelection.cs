namespace Stelliberty.Presentation.ViewModels;

public sealed record SubscriptionRowMenuSelection(string SubscriptionId, SubscriptionRowMenuAction Action, string DisplayName) : ICardMenuItemViewModel
{
    public string IconType => Action switch
    {
        SubscriptionRowMenuAction.Edit => "EditLine",
        SubscriptionRowMenuAction.EditFile => "CodeLine",
        SubscriptionRowMenuAction.OpenExternalEditor => "ExternalLinkLine",
        SubscriptionRowMenuAction.ChainProxy => "Tree3Line",
        SubscriptionRowMenuAction.Delete => "DeleteLine",
        SubscriptionRowMenuAction.ViewRuntimeConfig => "EyeLine",
        SubscriptionRowMenuAction.OverrideSelector => "PencilRulerLine",
        SubscriptionRowMenuAction.ProviderSelector => "Plugin2Line",
        SubscriptionRowMenuAction.CopyLink => "Copy2Line",
        SubscriptionRowMenuAction.QrCode => "QrcodeLine",
        _ => "More2Line"
    };

    public string AutomationId => $"Subscriptions.Row.{SubscriptionId}.Menu.{Action}";

    public bool IsDanger => Action == SubscriptionRowMenuAction.Delete;
}

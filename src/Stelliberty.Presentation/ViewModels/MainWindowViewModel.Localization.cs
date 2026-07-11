using Stelliberty.Application.Localization;
using Stelliberty.Application.Platform;

namespace Stelliberty.Presentation.ViewModels;

public sealed partial class MainWindowViewModel
{
    public double NavLabelLetterSpacing => _localization.EffectiveLanguage == AppLanguage.En ? 0 : 6;

    public string HomeNavText => Localize("Nav.Home");
    public string ProxyNavText => Localize("Nav.Proxy");
    public string ConnectionsNavText => Localize("Nav.Connections");
    public string CoreLogsNavText => Localize("Nav.CoreLogs");
    public string RulesNavText => Localize("Nav.Rules");
    public string SubscriptionsNavText => Localize("Nav.Subscriptions");
    public string OverridesNavText => Localize("Nav.Overrides");
    public string SettingsNavText => Localize("Nav.Settings");

    public string DialogConfirmText => Localize("Dialog.Confirm");
    public string DialogCancelText => Localize("Dialog.Cancel");

    private void OnLocalizationLanguageChanged(object? sender, EventArgs args)
    {
        RefreshLanguage();
    }

    private void RefreshLanguage()
    {
        OnPropertyChanged(nameof(NavLabelLetterSpacing));
        OnPropertyChanged(nameof(HomeNavText));
        OnPropertyChanged(nameof(ProxyNavText));
        OnPropertyChanged(nameof(ConnectionsNavText));
        OnPropertyChanged(nameof(CoreLogsNavText));
        OnPropertyChanged(nameof(RulesNavText));
        OnPropertyChanged(nameof(SubscriptionsNavText));
        OnPropertyChanged(nameof(OverridesNavText));
        OnPropertyChanged(nameof(SettingsNavText));
        OnPropertyChanged(nameof(DialogConfirmText));
        OnPropertyChanged(nameof(DialogCancelText));
        Settings.RefreshLanguage();
    }
}

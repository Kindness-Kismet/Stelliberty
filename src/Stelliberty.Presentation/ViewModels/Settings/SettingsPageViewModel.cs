using System.Windows.Input;
using Stelliberty.Application.Localization;
using Stelliberty.Application.Platform;
using Stelliberty.Presentation.Commands;

namespace Stelliberty.Presentation.ViewModels;

public sealed class SettingsPageViewModel : ViewModelBase
{
    private readonly ILocalizationService _localization;
    private SettingsSubPage _subPage = SettingsSubPage.Root;
    private SettingsSubPage _backTarget = SettingsSubPage.Root;

    public SettingsPageViewModel(ILocalizationService localization)
    {
        _localization = localization;
        ShowThemeCommand = new RelayCommand(() => SubPage = SettingsSubPage.Theme);
        ShowLanguageCommand = new RelayCommand(() => SubPage = SettingsSubPage.Language);
        ShowClashFeaturesCommand = new RelayCommand(() => SubPage = SettingsSubPage.ClashFeatures);
        ShowAppBehaviorCommand = new RelayCommand(() => SubPage = SettingsSubPage.AppBehavior);
        ShowDataManagementCommand = new RelayCommand(() => SubPage = SettingsSubPage.DataManagement);
        ShowUpdateCommand = new RelayCommand(GoToUpdate);
        ShowAboutCommand = new RelayCommand(() => SubPage = SettingsSubPage.About);
        ShowAppLogCommand = new RelayCommand(GoToAppLog);
        ShowNetworkCommand = new RelayCommand(() => SubPage = SettingsSubPage.Network);
        ShowPortControlCommand = new RelayCommand(() => SubPage = SettingsSubPage.PortControl);
        ShowSystemIntegrationCommand = new RelayCommand(() => SubPage = SettingsSubPage.SystemIntegration);
        ShowDnsCommand = new RelayCommand(() => SubPage = SettingsSubPage.Dns);
        ShowPerformanceCommand = new RelayCommand(() => SubPage = SettingsSubPage.Performance);
        ShowCoreLogCommand = new RelayCommand(() => SubPage = SettingsSubPage.CoreLog);
        BackCommand = new RelayCommand(Back);
    }

    public event EventHandler<SettingsSubPage>? SubPageChanged;

    public SettingsSubPage SubPage
    {
        get => _subPage;
        set
        {
            if (SetProperty(ref _subPage, value))
            {
                RaiseSubPageChanges();
                SubPageChanged?.Invoke(this, value);
            }
        }
    }

    public bool IsRootVisible => SubPage == SettingsSubPage.Root;
    public bool IsThemeVisible => SubPage == SettingsSubPage.Theme;
    public bool IsLanguageVisible => SubPage == SettingsSubPage.Language;
    public bool IsClashFeaturesVisible => SubPage == SettingsSubPage.ClashFeatures;
    public bool IsAppBehaviorVisible => SubPage == SettingsSubPage.AppBehavior;
    public bool IsDataManagementVisible => SubPage == SettingsSubPage.DataManagement;
    public bool IsUpdateVisible => SubPage == SettingsSubPage.Update;
    public bool IsAboutVisible => SubPage == SettingsSubPage.About;
    public bool IsAppLogVisible => SubPage == SettingsSubPage.AppLog;
    public bool IsNetworkVisible => SubPage == SettingsSubPage.Network;
    public bool IsPortControlVisible => SubPage == SettingsSubPage.PortControl;
    public bool IsSystemIntegrationVisible => SubPage == SettingsSubPage.SystemIntegration;
    public bool IsDnsVisible => SubPage == SettingsSubPage.Dns;
    public bool IsPerformanceVisible => SubPage == SettingsSubPage.Performance;
    public bool IsCoreLogVisible => SubPage == SettingsSubPage.CoreLog;
    public bool IsBackVisible => SubPage != SettingsSubPage.Root;

    public string HeaderText => SubPage switch
    {
        SettingsSubPage.Theme => Localize("Settings.Header.Theme"),
        SettingsSubPage.Language => Localize("Settings.Header.Language"),
        SettingsSubPage.ClashFeatures => Localize("Settings.Header.ClashFeatures"),
        SettingsSubPage.AppBehavior => Localize("Settings.Header.AppBehavior"),
        SettingsSubPage.DataManagement => Localize("Settings.Header.DataManagement"),
        SettingsSubPage.Update => Localize("Settings.Header.Update"),
        SettingsSubPage.About => Localize("Settings.Header.About"),
        SettingsSubPage.AppLog => Localize("Settings.Header.AppLog"),
        SettingsSubPage.Network => Localize("Settings.Header.Network"),
        SettingsSubPage.PortControl => Localize("Settings.Header.PortControl"),
        SettingsSubPage.SystemIntegration => Localize("Settings.Header.SystemIntegration"),
        SettingsSubPage.Dns => Localize("Settings.Header.Dns"),
        SettingsSubPage.Performance => Localize("Settings.Header.Performance"),
        SettingsSubPage.CoreLog => Localize("Settings.Header.CoreLog"),
        _ => Localize("Settings.Header.Root")
    };

    public string ThemeEntryTitle => Localize("Settings.Entry.Theme.Title");
    public string ThemeEntryDescription => Localize("Settings.Entry.Theme.Description");
    public string LanguageEntryTitle => Localize("Settings.Entry.Language.Title");
    public string LanguageEntryDescription => Localize("Settings.Entry.Language.Description");
    public string ClashFeaturesEntryTitle => Localize("Settings.Entry.ClashFeatures.Title");
    public string ClashFeaturesEntryDescription => Localize("Settings.Entry.ClashFeatures.Description");
    public string AppBehaviorEntryTitle => Localize("Settings.Entry.AppBehavior.Title");
    public string AppBehaviorEntryDescription => Localize("Settings.Entry.AppBehavior.Description");
    public string DataManagementEntryTitle => Localize("Settings.Entry.DataManagement.Title");
    public string DataManagementEntryDescription => Localize("Settings.Entry.DataManagement.Description");
    public string UpdateEntryTitle => Localize("Settings.Entry.Update.Title");
    public string UpdateEntryDescription => Localize("Settings.Entry.Update.Description");
    public string AboutEntryTitle => Localize("Settings.Entry.About.Title");
    public string AboutEntryDescription => Localize("Settings.Entry.About.Description");
    public string AppLogEntryTitle => Localize("Settings.Entry.AppLog.Title");
    public string AppLogEntryDescription => Localize("Settings.Entry.AppLog.Description");

    public string GroupPersonalizationText => Localize("Settings.Group.Personalization");
    public string GroupAppearanceText => Localize("Settings.Group.Appearance");
    public string GroupRegionText => Localize("Settings.Group.Region");
    public string GroupClashText => Localize("Settings.Group.Clash");
    public string GroupApplicationText => Localize("Settings.Group.Application");
    public string GroupMaintenanceText => Localize("Settings.Group.Maintenance");

    public string AppVersionText => AppMetadata.Version;

    public string ClashNetworkText => Localize("Settings.ClashFeature.Network");
    public string ClashPortControlText => Localize("Settings.ClashFeature.PortControl");
    public string ClashSystemIntegrationText => Localize("Settings.ClashFeature.SystemIntegration");
    public string ClashDnsText => Localize("Settings.ClashFeature.Dns");
    public string ClashPerformanceText => Localize("Settings.ClashFeature.Performance");
    public string ClashCoreLogText => Localize("Settings.ClashFeature.CoreLog");

    public ICommand ShowThemeCommand { get; }
    public ICommand ShowLanguageCommand { get; }
    public ICommand ShowClashFeaturesCommand { get; }
    public ICommand ShowAppBehaviorCommand { get; }
    public ICommand ShowDataManagementCommand { get; }
    public ICommand ShowUpdateCommand { get; }
    public ICommand ShowAboutCommand { get; }
    public ICommand ShowAppLogCommand { get; }
    public ICommand ShowNetworkCommand { get; }
    public ICommand ShowPortControlCommand { get; }
    public ICommand ShowSystemIntegrationCommand { get; }
    public ICommand ShowDnsCommand { get; }
    public ICommand ShowPerformanceCommand { get; }
    public ICommand ShowCoreLogCommand { get; }
    public ICommand BackCommand { get; }

    public void GoToRoot()
    {
        _backTarget = SettingsSubPage.Root;
        SubPage = SettingsSubPage.Root;
    }

    public void RefreshLanguage()
    {
        OnPropertyChanged(nameof(HeaderText));
        OnPropertyChanged(nameof(ThemeEntryTitle));
        OnPropertyChanged(nameof(ThemeEntryDescription));
        OnPropertyChanged(nameof(LanguageEntryTitle));
        OnPropertyChanged(nameof(LanguageEntryDescription));
        OnPropertyChanged(nameof(ClashFeaturesEntryTitle));
        OnPropertyChanged(nameof(ClashFeaturesEntryDescription));
        OnPropertyChanged(nameof(AppBehaviorEntryTitle));
        OnPropertyChanged(nameof(AppBehaviorEntryDescription));
        OnPropertyChanged(nameof(DataManagementEntryTitle));
        OnPropertyChanged(nameof(DataManagementEntryDescription));
        OnPropertyChanged(nameof(UpdateEntryTitle));
        OnPropertyChanged(nameof(UpdateEntryDescription));
        OnPropertyChanged(nameof(AboutEntryTitle));
        OnPropertyChanged(nameof(AboutEntryDescription));
        OnPropertyChanged(nameof(AppLogEntryTitle));
        OnPropertyChanged(nameof(AppLogEntryDescription));
        OnPropertyChanged(nameof(GroupPersonalizationText));
        OnPropertyChanged(nameof(GroupAppearanceText));
        OnPropertyChanged(nameof(GroupRegionText));
        OnPropertyChanged(nameof(GroupClashText));
        OnPropertyChanged(nameof(GroupApplicationText));
        OnPropertyChanged(nameof(GroupMaintenanceText));
        OnPropertyChanged(nameof(AppVersionText));
        OnPropertyChanged(nameof(ClashNetworkText));
        OnPropertyChanged(nameof(ClashPortControlText));
        OnPropertyChanged(nameof(ClashSystemIntegrationText));
        OnPropertyChanged(nameof(ClashDnsText));
        OnPropertyChanged(nameof(ClashPerformanceText));
        OnPropertyChanged(nameof(ClashCoreLogText));
    }

    private void GoToUpdate()
    {
        _backTarget = SubPage == SettingsSubPage.About ? SettingsSubPage.About : SettingsSubPage.Root;
        SubPage = SettingsSubPage.Update;
    }

    private void GoToAppLog()
    {
        _backTarget = SubPage == SettingsSubPage.About ? SettingsSubPage.About : SettingsSubPage.Root;
        SubPage = SettingsSubPage.AppLog;
    }

    private void Back()
    {
        var target = SubPage switch
        {
            _ when IsClashFeatureSubPage(SubPage) => SettingsSubPage.ClashFeatures,
            SettingsSubPage.Update when _backTarget == SettingsSubPage.About => SettingsSubPage.About,
            SettingsSubPage.AppLog when _backTarget == SettingsSubPage.About => SettingsSubPage.About,
            _ => SettingsSubPage.Root
        };
        _backTarget = SettingsSubPage.Root;
        SubPage = target;
    }

    private static bool IsClashFeatureSubPage(SettingsSubPage page) => page is SettingsSubPage.Network
        or SettingsSubPage.PortControl
        or SettingsSubPage.SystemIntegration
        or SettingsSubPage.Dns
        or SettingsSubPage.Performance
        or SettingsSubPage.CoreLog;

    private void RaiseSubPageChanges()
    {
        OnPropertyChanged(nameof(IsRootVisible));
        OnPropertyChanged(nameof(IsThemeVisible));
        OnPropertyChanged(nameof(IsLanguageVisible));
        OnPropertyChanged(nameof(IsClashFeaturesVisible));
        OnPropertyChanged(nameof(IsAppBehaviorVisible));
        OnPropertyChanged(nameof(IsDataManagementVisible));
        OnPropertyChanged(nameof(IsUpdateVisible));
        OnPropertyChanged(nameof(IsAboutVisible));
        OnPropertyChanged(nameof(IsAppLogVisible));
        OnPropertyChanged(nameof(IsNetworkVisible));
        OnPropertyChanged(nameof(IsPortControlVisible));
        OnPropertyChanged(nameof(IsSystemIntegrationVisible));
        OnPropertyChanged(nameof(IsDnsVisible));
        OnPropertyChanged(nameof(IsPerformanceVisible));
        OnPropertyChanged(nameof(IsCoreLogVisible));
        OnPropertyChanged(nameof(HeaderText));
        OnPropertyChanged(nameof(IsBackVisible));
    }

    private string Localize(string key) => _localization.GetString(key);
}

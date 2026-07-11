using Stelliberty.Application.Overrides;
using Stelliberty.Domain.Overrides;
using Stelliberty.Application.Localization;
using AppOverrideUpdateProxyMode = Stelliberty.Domain.Overrides.OverrideUpdateProxyMode;

namespace Stelliberty.Presentation.ViewModels;

internal static class OverrideViewModelMapper
{
    public static OverrideItemViewModel ToOverrideItem(OverrideProfile overrideProfile, ILocalizationService? localization = null)
    {
        return new OverrideItemViewModel(
            overrideProfile.Id,
            overrideProfile.Name,
            overrideProfile.SourceLocation,
            overrideProfile.Format,
            overrideProfile.SourceType == OverrideSourceType.Local,
            ToPresentationProxyMode(overrideProfile.UpdateProxyMode),
            isCreatedBlank: string.IsNullOrWhiteSpace(overrideProfile.SourceLocation),
            lastUpdatedAt: overrideProfile.LastUpdatedAt,
            localization: localization);
    }

    public static OverrideUpdateProxyMode ToPresentationProxyMode(AppOverrideUpdateProxyMode mode)
    {
        return mode switch
        {
            AppOverrideUpdateProxyMode.SystemProxy => OverrideUpdateProxyMode.SystemProxy,
            AppOverrideUpdateProxyMode.Core => OverrideUpdateProxyMode.Core,
            _ => OverrideUpdateProxyMode.Direct
        };
    }

    public static AppOverrideUpdateProxyMode ToApplicationProxyMode(OverrideUpdateProxyMode mode)
    {
        return mode switch
        {
            OverrideUpdateProxyMode.SystemProxy => AppOverrideUpdateProxyMode.SystemProxy,
            OverrideUpdateProxyMode.Core => AppOverrideUpdateProxyMode.Core,
            _ => AppOverrideUpdateProxyMode.Direct
        };
    }
}

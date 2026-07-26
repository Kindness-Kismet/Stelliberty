using Stelliberty.Application.Overrides;
using Stelliberty.Domain.Overrides;
using Stelliberty.Application.Localization;

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
            overrideProfile.UpdateProxyMode,
            isCreatedBlank: string.IsNullOrWhiteSpace(overrideProfile.SourceLocation),
            lastUpdatedAt: overrideProfile.LastUpdatedAt,
            localization: localization);
    }
}

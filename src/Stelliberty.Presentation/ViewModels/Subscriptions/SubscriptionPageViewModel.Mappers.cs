using Stelliberty.Application.Overrides;
using Stelliberty.Domain.Overrides;
using Stelliberty.Application.Subscriptions;
using Stelliberty.Domain.Subscriptions;

namespace Stelliberty.Presentation.ViewModels;

public sealed partial class SubscriptionPageViewModel
{
    private SubscriptionItemViewModel ToSubscriptionItem(Subscription subscription, bool isCurrent = false)
    {
        var trafficUsed = subscription.TrafficInfo?.Upload + subscription.TrafficInfo?.Download ?? 0;
        return new SubscriptionItemViewModel(
            subscription.Id,
            subscription.Name,
            subscription.SourceLocation,
            subscription.IsLocalFile,
            subscription.UserAgent,
            subscription.AutoTestDelayIntervalMinutes,
            ToPresentationAutoUpdateMode(subscription.AutoUpdateMode),
            subscription.AutoUpdateIntervalMinutes,
            ToPresentationUpdateProxyMode(subscription.UpdateProxyMode),
            ageSecretKey: subscription.AgeSecretKey,
            isCurrent: isCurrent,
            createdAt: subscription.CreatedAt,
            lastUpdatedAt: subscription.LastUpdatedAt,
            overrideCount: subscription.OverrideIds.Count,
            chainProxyCount: subscription.BuiltinChainProxyNames.Count + subscription.CustomChainProxies.Count,
            trafficUsed: trafficUsed,
            trafficTotal: subscription.TrafficInfo?.Total ?? 0,
            trafficExpire: subscription.TrafficInfo?.Expire ?? 0,
            lastError: subscription.LastError,
            lastErrorAt: subscription.LastErrorAt,
            sourceFormat: subscription.SourceFormat,
            localization: _localization);
    }

    private static SubscriptionOverrideOptionViewModel ToOverrideOption(OverrideProfile overrideProfile)
    {
        return new SubscriptionOverrideOptionViewModel(
            overrideProfile.Id,
            overrideProfile.Name,
            overrideProfile.Format == OverrideFormat.Yaml ? "YAML" : "JavaScript");
    }

    private static SubscriptionAutoUpdateMode ToPresentationAutoUpdateMode(Domain.Subscriptions.SubscriptionAutoUpdateMode mode)
    {
        return mode switch
        {
            Domain.Subscriptions.SubscriptionAutoUpdateMode.Startup => SubscriptionAutoUpdateMode.Startup,
            Domain.Subscriptions.SubscriptionAutoUpdateMode.Interval => SubscriptionAutoUpdateMode.Interval,
            _ => SubscriptionAutoUpdateMode.Disabled
        };
    }

    private static SubscriptionUpdateProxyMode ToPresentationUpdateProxyMode(Domain.Subscriptions.SubscriptionUpdateProxyMode mode)
    {
        return mode switch
        {
            Domain.Subscriptions.SubscriptionUpdateProxyMode.SystemProxy => SubscriptionUpdateProxyMode.SystemProxy,
            Domain.Subscriptions.SubscriptionUpdateProxyMode.Core => SubscriptionUpdateProxyMode.Core,
            _ => SubscriptionUpdateProxyMode.Direct
        };
    }

    private static Domain.Subscriptions.SubscriptionAutoUpdateMode ToApplicationAutoUpdateMode(SubscriptionAutoUpdateMode mode)
    {
        return mode switch
        {
            SubscriptionAutoUpdateMode.Startup => Domain.Subscriptions.SubscriptionAutoUpdateMode.Startup,
            SubscriptionAutoUpdateMode.Interval => Domain.Subscriptions.SubscriptionAutoUpdateMode.Interval,
            _ => Domain.Subscriptions.SubscriptionAutoUpdateMode.Disabled
        };
    }

    private static Domain.Subscriptions.SubscriptionUpdateProxyMode ToApplicationUpdateProxyMode(SubscriptionUpdateProxyMode mode)
    {
        return mode switch
        {
            SubscriptionUpdateProxyMode.SystemProxy => Domain.Subscriptions.SubscriptionUpdateProxyMode.SystemProxy,
            SubscriptionUpdateProxyMode.Core => Domain.Subscriptions.SubscriptionUpdateProxyMode.Core,
            _ => Domain.Subscriptions.SubscriptionUpdateProxyMode.Direct
        };
    }
}

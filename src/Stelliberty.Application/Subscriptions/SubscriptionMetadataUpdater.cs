using Stelliberty.Domain.Subscriptions;
using Stelliberty.Application.Diagnostics;

namespace Stelliberty.Application.Subscriptions;

public sealed record SubscriptionMetadataEdit(
    string Name,
    string SourceLocation,
    string UserAgent,
    string AgeSecretKey,
    int AutoTestDelayIntervalMinutes,
    SubscriptionAutoUpdateMode AutoUpdateMode,
    int AutoUpdateIntervalMinutes,
    SubscriptionUpdateProxyMode UpdateProxyMode);

public sealed class SubscriptionMetadataUpdater(ISubscriptionStore store)
{
    public Subscription? Save(string subscriptionId, SubscriptionMetadataEdit edit)
    {
        var subscription = store.LoadSubscriptions().FirstOrDefault(item => item.Id == subscriptionId);
        if (subscription is null)
        {
            return null;
        }

        var updated = subscription with
        {
            Name = edit.Name,
            SourceLocation = edit.SourceLocation,
            UserAgent = edit.UserAgent,
            AgeSecretKey = subscription.IsLocalFile ? string.Empty : edit.AgeSecretKey.Trim(),
            AutoTestDelayIntervalMinutes = edit.AutoTestDelayIntervalMinutes,
            AutoUpdateMode = edit.AutoUpdateMode,
            AutoUpdateIntervalMinutes = edit.AutoUpdateIntervalMinutes,
            UpdateProxyMode = edit.UpdateProxyMode
        };
        store.UpdateSubscription(updated);
        AppLogger.Info($"Subscription metadata saved: {subscription.Name}");
        return updated;
    }
}

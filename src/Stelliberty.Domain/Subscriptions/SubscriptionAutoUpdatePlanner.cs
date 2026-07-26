namespace Stelliberty.Domain.Subscriptions;

public sealed class SubscriptionAutoUpdatePlanner
{
    public SubscriptionAutoUpdatePlan PlanStartupUpdates(IReadOnlyList<Subscription> subscriptions)
    {
        return Plan(subscriptions, subscription => !subscription.IsLocalFile && subscription.AutoUpdateMode == SubscriptionAutoUpdateMode.Startup);
    }

    public SubscriptionAutoUpdatePlan PlanDueIntervalUpdates(IReadOnlyList<Subscription> subscriptions, DateTimeOffset now)
    {
        return Plan(subscriptions, subscription => IsDueIntervalSubscription(subscription, now));
    }

    private static SubscriptionAutoUpdatePlan Plan(IReadOnlyList<Subscription> subscriptions, Func<Subscription, bool> shouldUpdate)
    {
        var updateIds = new List<string>();
        foreach (var subscription in subscriptions)
        {
            if (shouldUpdate(subscription))
            {
                updateIds.Add(subscription.Id);
            }
        }

        return new SubscriptionAutoUpdatePlan(updateIds);
    }

    private static bool IsDueIntervalSubscription(Subscription subscription, DateTimeOffset now)
    {
        if (subscription.IsLocalFile
            || subscription.AutoUpdateMode != SubscriptionAutoUpdateMode.Interval
            || subscription.AutoUpdateIntervalMinutes <= 0)
        {
            return false;
        }

        var lastAttemptAt = subscription.LastErrorAt is { } lastErrorAt
            && (subscription.LastUpdatedAt is null || lastErrorAt > subscription.LastUpdatedAt)
                ? lastErrorAt
                : subscription.LastUpdatedAt;
        return lastAttemptAt is null
            || lastAttemptAt.Value.AddMinutes(subscription.AutoUpdateIntervalMinutes) <= now;
    }
}

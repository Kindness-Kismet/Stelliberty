using Stelliberty.Domain.Overrides;
using Stelliberty.Application.Subscriptions;
using Stelliberty.Domain.Subscriptions;

namespace Stelliberty.Application.Overrides;

public sealed class OverrideDeleter(
    IOverrideStore overrideStore,
    ISubscriptionStore subscriptionStore)
{
    public OverrideDeleteResult Delete(string overrideId)
    {
        overrideStore.Delete(overrideId);
        var affectedSubscriptionIds = new List<string>();

        foreach (var subscription in subscriptionStore.LoadSubscriptions())
        {
            var selectedOverrideIds = subscription.OverrideIds.Where(item => item != overrideId).ToList();
            var sortPreference = subscription.OverrideSortPreference.Where(item => item != overrideId).ToList();
            if (selectedOverrideIds.SequenceEqual(subscription.OverrideIds) && sortPreference.SequenceEqual(subscription.OverrideSortPreference))
            {
                continue;
            }

            subscriptionStore.UpdateSubscription(subscription with
            {
                OverrideIds = selectedOverrideIds,
                OverrideSortPreference = sortPreference
            });
            affectedSubscriptionIds.Add(subscription.Id);
        }

        return new OverrideDeleteResult(overrideId, affectedSubscriptionIds);
    }
}

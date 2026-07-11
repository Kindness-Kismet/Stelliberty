using Stelliberty.Application.Overrides;
using Stelliberty.Domain.Subscriptions;
using Stelliberty.Application.Diagnostics;

namespace Stelliberty.Application.Subscriptions;

public sealed class SubscriptionOverrideSelectionUpdater(ISubscriptionStore store, IOverrideStore? overrideStore = null)
{
    public Subscription SaveSelection(
        string subscriptionId,
        IReadOnlyList<string> selectedOverrideIds,
        IReadOnlyList<string> overrideSortPreference)
    {
        var subscription = store.LoadSubscriptions().FirstOrDefault(item => item.Id == subscriptionId)
            ?? throw new InvalidOperationException($"Subscription not found: {subscriptionId}");
        var updated = subscription with
        {
            OverrideIds = selectedOverrideIds.ToList(),
            OverrideSortPreference = overrideSortPreference.ToList()
        };

        store.UpdateSubscription(updated);
        AppLogger.Info($"Subscription override selection saved: {subscription.Name}");
        return updated;
    }

    // 新选择排在前面；旧的未选中项保持相对顺序。
    public Subscription SaveValidatedSelection(string subscriptionId, IReadOnlyList<string> overrideIds)
    {
        var subscription = store.LoadSubscriptions().FirstOrDefault(item => item.Id == subscriptionId)
            ?? throw new InvalidOperationException($"Subscription not found: {subscriptionId}");
        var selectedOverrideIds = overrideIds
            .Where(overrideId => !string.IsNullOrWhiteSpace(overrideId))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (selectedOverrideIds.Count > 0)
        {
            var availableOverrideIds = overrideStore?.LoadOverrides().Select(item => item.Id).ToHashSet(StringComparer.Ordinal)
                ?? throw new InvalidOperationException("Override list is unavailable");
            var missingOverrideId = selectedOverrideIds.FirstOrDefault(overrideId => !availableOverrideIds.Contains(overrideId));
            if (missingOverrideId is not null)
            {
                throw new InvalidOperationException($"Override not found: {missingOverrideId}");
            }
        }

        var selected = selectedOverrideIds.ToHashSet(StringComparer.Ordinal);
        var sortPreference = selectedOverrideIds
            .Concat(subscription.OverrideSortPreference.Where(overrideId => !selected.Contains(overrideId)))
            .ToList();
        return SaveSelection(subscriptionId, selectedOverrideIds, sortPreference);
    }

    public Subscription? DisableOverridesForSubscription(string subscriptionId)
    {
        var subscription = store.LoadSubscriptions().FirstOrDefault(item => item.Id == subscriptionId);
        if (subscription is null || subscription.OverrideIds.Count == 0)
        {
            return null;
        }

        return SaveSelection(subscriptionId, [], subscription.OverrideSortPreference);
    }
}

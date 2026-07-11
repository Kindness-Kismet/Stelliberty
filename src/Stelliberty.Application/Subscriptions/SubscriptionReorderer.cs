using Stelliberty.Domain.Subscriptions;

namespace Stelliberty.Application.Subscriptions;

public sealed class SubscriptionReorderer(ISubscriptionStore store)
{
    // 混入临时行时，整份界面顺序都不能安全保存。
    public void SaveOrder(IReadOnlyList<string> orderedIds)
    {
        var subscriptionsById = store.LoadSubscriptions().ToDictionary(item => item.Id, StringComparer.Ordinal);
        if (orderedIds.Any(id => !subscriptionsById.ContainsKey(id)))
        {
            return;
        }

        store.SaveSubscriptions(orderedIds.Select(id => subscriptionsById[id]).ToList());
    }
}

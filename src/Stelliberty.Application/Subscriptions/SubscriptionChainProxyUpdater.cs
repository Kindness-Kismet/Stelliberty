using Stelliberty.Domain.Subscriptions;
using Stelliberty.Application.Diagnostics;

namespace Stelliberty.Application.Subscriptions;

public sealed class SubscriptionChainProxyUpdater(ISubscriptionStore store)
{
    // 外部删除返回 null，让界面忽略过期操作。
    public Subscription? Save(
        string subscriptionId,
        IReadOnlyList<string> disabledBuiltinNames,
        IReadOnlyList<SubscriptionCustomChainProxy> customChainProxies)
    {
        var subscription = store.LoadSubscriptions().FirstOrDefault(item => item.Id == subscriptionId);
        if (subscription is null)
        {
            return null;
        }

        var updated = subscription with
        {
            DisabledBuiltinChainProxyNames = disabledBuiltinNames.ToList(),
            CustomChainProxies = customChainProxies.ToList()
        };
        store.UpdateSubscription(updated);
        AppLogger.Info($"Subscription chain proxy config saved: {subscription.Name}");
        return updated;
    }
}

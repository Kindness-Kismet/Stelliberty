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

    public Subscription DisableCycles(
        string subscriptionId,
        SubscriptionChainProxyValidationResult validation)
    {
        var subscription = store.LoadSubscriptions().FirstOrDefault(item => item.Id == subscriptionId)
            ?? throw new InvalidOperationException($"Subscription not found: {subscriptionId}");
        var disabledBuiltinNames = subscription.DisabledBuiltinChainProxyNames
            .Concat(validation.CyclicBuiltinNames)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var disabledCustomIds = validation.CyclicCustomChains
            .Select(item => item.Id)
            .ToHashSet(StringComparer.Ordinal);
        var customChainProxies = subscription.CustomChainProxies
            .Select(item => disabledCustomIds.Contains(item.Id) ? item with { IsEnabled = false } : item)
            .ToList();
        var updated = subscription with
        {
            DisabledBuiltinChainProxyNames = disabledBuiltinNames,
            CustomChainProxies = customChainProxies
        };

        store.UpdateSubscription(updated);
        AppLogger.Warning($"Cyclic chain proxies disabled: {subscription.Name}, {string.Join(", ", validation.CyclicChainNames)}");
        return updated;
    }
}

using Stelliberty.Domain.Subscriptions;
using Stelliberty.Application.Proxies;
using Stelliberty.Application.Rules;
namespace Stelliberty.Application.Subscriptions;

public sealed class SubscriptionDeleter(
    ISubscriptionStore subscriptionStore,
    ISubscriptionSelectionStore selectionStore,
    ISelectedSubscriptionRuntimeStore runtimeStore,
    IRuleOverrideStore ruleOverrideStore,
    IProxySelectionStore proxySelectionStore)
{
    public void Delete(string subscriptionId)
    {
        subscriptionStore.Delete(subscriptionId);
        runtimeStore.Delete(subscriptionId);
        ruleOverrideStore.Delete(subscriptionId);
        proxySelectionStore.RemoveSubscription(subscriptionId);

        if (selectionStore.GetCurrentSubscriptionId() != subscriptionId)
        {
            return;
        }

        selectionStore.SetCurrentSubscriptionId(null);
    }
}

using Stelliberty.Domain.Subscriptions;
using Stelliberty.Application.Rules;
namespace Stelliberty.Application.Subscriptions;

public sealed class SubscriptionDeleter(
    ISubscriptionStore subscriptionStore,
    ISubscriptionSelectionStore selectionStore,
    ISelectedSubscriptionRuntimeStore? runtimeStore = null,
    IRuleOverrideStore? ruleOverrideStore = null)
{
    public void Delete(string subscriptionId)
    {
        subscriptionStore.Delete(subscriptionId);
        runtimeStore?.Delete(subscriptionId);
        ruleOverrideStore?.Delete(subscriptionId);

        if (selectionStore.GetCurrentSubscriptionId() != subscriptionId)
        {
            return;
        }

        selectionStore.SetCurrentSubscriptionId(null);
    }
}

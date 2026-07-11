using Stelliberty.Domain.Subscriptions;
namespace Stelliberty.Application.Subscriptions;

public sealed class SubscriptionDeleter(
    ISubscriptionStore subscriptionStore,
    ISubscriptionSelectionStore selectionStore,
    ISelectedSubscriptionRuntimeStore? runtimeStore = null)
{
    public void Delete(string subscriptionId)
    {
        subscriptionStore.Delete(subscriptionId);
        runtimeStore?.Delete(subscriptionId);

        if (selectionStore.GetCurrentSubscriptionId() != subscriptionId)
        {
            return;
        }

        selectionStore.SetCurrentSubscriptionId(null);
    }
}

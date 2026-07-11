using Stelliberty.Domain.Subscriptions;
namespace Stelliberty.Application.Subscriptions;

public interface ISubscriptionSelectionStore
{
    string? GetCurrentSubscriptionId();

    void SetCurrentSubscriptionId(string? subscriptionId);
}

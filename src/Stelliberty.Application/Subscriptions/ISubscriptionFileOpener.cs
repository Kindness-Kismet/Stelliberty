using Stelliberty.Domain.Subscriptions;
namespace Stelliberty.Application.Subscriptions;

public interface ISubscriptionFileOpener
{
    void OpenSubscriptionFile(string subscriptionId);
}

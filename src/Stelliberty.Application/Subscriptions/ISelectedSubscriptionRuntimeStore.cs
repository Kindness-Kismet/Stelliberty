using Stelliberty.Domain.Subscriptions;
namespace Stelliberty.Application.Subscriptions;

public interface ISelectedSubscriptionRuntimeStore
{
    SelectedSubscriptionRuntimePaths Save(Subscription subscription, string originalContent, string runtimeConfigContent);

    string SaveEmpty(string runtimeConfigContent);

    string ReadRuntimeConfig(string subscriptionId);

    void Delete(string subscriptionId);
}

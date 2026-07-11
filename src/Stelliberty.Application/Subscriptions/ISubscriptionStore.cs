using Stelliberty.Domain.Subscriptions;
namespace Stelliberty.Application.Subscriptions;

public interface ISubscriptionStore
{
    void Save(Subscription subscription, string originalContent);

    void UpdateSubscription(Subscription subscription);

    void SaveSubscriptions(IReadOnlyList<Subscription> subscriptions);

    void SaveContent(string subscriptionId, string originalContent);

    IReadOnlyList<Subscription> LoadSubscriptions();

    string ReadContent(string subscriptionId);

    // 为 shell 功能暴露正文文件路径，不泄露目录结构。
    string GetContentPath(string subscriptionId);

    void Delete(string subscriptionId);
}

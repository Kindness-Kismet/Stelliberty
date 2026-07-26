using Stelliberty.Application.Subscriptions;
using Stelliberty.Domain.Subscriptions;

namespace Stelliberty.Infrastructure.Subscriptions;

// 空实现，仅供测试使用
public sealed class InMemorySubscriptionStore : ISubscriptionStore
{
    private readonly List<Subscription> _subscriptions = [];
    private readonly Dictionary<string, string> _contents = new(StringComparer.Ordinal);

    public void Save(Subscription subscription, string originalContent)
    {
        _subscriptions.Add(subscription);
        _contents[subscription.Id] = originalContent;
    }

    public void UpdateSubscription(Subscription subscription)
    {
        var index = _subscriptions.FindIndex(s => s.Id == subscription.Id);
        if (index >= 0)
            _subscriptions[index] = subscription;
    }

    public void SaveSubscriptions(IReadOnlyList<Subscription> subscriptions)
    {
        _subscriptions.Clear();
        _subscriptions.AddRange(subscriptions);
    }

    public void SaveContent(string subscriptionId, string originalContent)
    {
        _contents[subscriptionId] = originalContent;
    }

    public IReadOnlyList<Subscription> LoadSubscriptions() => _subscriptions.ToList();

    public string ReadContent(string subscriptionId) =>
        _contents.TryGetValue(subscriptionId, out var content) ? content : string.Empty;

    public string GetContentPath(string subscriptionId) => $"{subscriptionId}.yaml";

    public void Delete(string subscriptionId)
    {
        _subscriptions.RemoveAll(s => s.Id == subscriptionId);
        _contents.Remove(subscriptionId);
    }
}

using Stelliberty.Application.Subscriptions;

namespace Stelliberty.Infrastructure.Subscriptions;

// 空实现，仅供测试使用
public sealed class InMemorySubscriptionSelectionStore : ISubscriptionSelectionStore
{
    private string? _currentSubscriptionId;

    public string? GetCurrentSubscriptionId() => _currentSubscriptionId;

    public void SetCurrentSubscriptionId(string? subscriptionId)
    {
        _currentSubscriptionId = subscriptionId;
    }
}

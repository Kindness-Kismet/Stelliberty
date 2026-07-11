namespace Stelliberty.Application.Subscriptions;

public sealed class SubscriptionFailureRecorder(ISubscriptionStore subscriptionStore)
{
    public bool MarkFailed(string subscriptionId, string? message)
    {
        var normalizedMessage = string.IsNullOrWhiteSpace(message) ? "Subscription runtime config is unavailable" : message;
        return Update(subscriptionId, normalizedMessage, DateTimeOffset.UtcNow);
    }

    public bool ClearFailure(string subscriptionId)
    {
        return Update(subscriptionId, null, null);
    }

    private bool Update(string subscriptionId, string? lastError, DateTimeOffset? lastErrorAt)
    {
        if (string.IsNullOrWhiteSpace(subscriptionId))
        {
            return false;
        }

        var subscription = subscriptionStore.LoadSubscriptions().FirstOrDefault(item => item.Id == subscriptionId);
        if (subscription is null)
        {
            return false;
        }

        if (subscription.LastError == lastError && subscription.LastErrorAt == lastErrorAt)
        {
            return false;
        }

        subscriptionStore.UpdateSubscription(subscription with
        {
            LastError = lastError,
            LastErrorAt = lastErrorAt
        });
        return true;
    }
}

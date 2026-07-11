namespace Stelliberty.Domain.Subscriptions;

public enum SubscriptionAutoDelayDecision
{
    None,
    Rescheduled,
    Due,
}

// 订阅切换只重新排期；调用方执行到期延迟测试并推进周期。
public sealed class SubscriptionAutoDelayPlanner
{
    private string? _scheduledSubscriptionId;
    private int _scheduledIntervalMinutes;
    private DateTimeOffset? _nextRunAt;

    public SubscriptionAutoDelayDecision Evaluate(string? subscriptionId, int intervalMinutes, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(subscriptionId) || intervalMinutes <= 0)
        {
            Reset();
            return SubscriptionAutoDelayDecision.None;
        }

        if (!string.Equals(_scheduledSubscriptionId, subscriptionId, StringComparison.Ordinal)
            || _scheduledIntervalMinutes != intervalMinutes)
        {
            _scheduledSubscriptionId = subscriptionId;
            _scheduledIntervalMinutes = intervalMinutes;
            _nextRunAt = now.AddMinutes(intervalMinutes);
            return SubscriptionAutoDelayDecision.Rescheduled;
        }

        if (_nextRunAt is null || now < _nextRunAt.Value)
        {
            return SubscriptionAutoDelayDecision.None;
        }

        return SubscriptionAutoDelayDecision.Due;
    }

    // 只在到期动作完成后推进排期。
    public void CompleteRun(int intervalMinutes, DateTimeOffset now)
    {
        _scheduledIntervalMinutes = intervalMinutes;
        _nextRunAt = now.AddMinutes(intervalMinutes);
    }

    public void Reset()
    {
        _scheduledSubscriptionId = null;
        _scheduledIntervalMinutes = 0;
        _nextRunAt = null;
    }
}

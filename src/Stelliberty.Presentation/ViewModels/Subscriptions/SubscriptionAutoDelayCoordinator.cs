using Stelliberty.Application.Diagnostics;
using Stelliberty.Domain.Subscriptions;

namespace Stelliberty.Presentation.ViewModels;

public sealed class SubscriptionAutoDelayCoordinator
{
    private readonly SubscriptionPageViewModel _subscriptionPage;
    private readonly ProxyPageViewModel _proxyPage;
    private readonly Func<DateTimeOffset> _now;
    private readonly SubscriptionAutoDelayPlanner _planner = new();
    private bool _isRunning;

    public SubscriptionAutoDelayCoordinator(
        SubscriptionPageViewModel subscriptionPage,
        ProxyPageViewModel proxyPage,
        Func<DateTimeOffset> now)
    {
        _subscriptionPage = subscriptionPage;
        _proxyPage = proxyPage;
        _now = now;
    }

    public async Task TickAsync()
    {
        if (_subscriptionPage.CurrentSubscriptionAutoTestDelayIntervalMinutes <= 0)
        {
            return;
        }

        await _proxyPage.TestAllDelaysForCurrentSubscriptionAsync();
        AppLogger.Info($"Subscription auto-delay test triggered: {_subscriptionPage.CurrentSubscriptionId}");
    }

    public async Task RunDueAsync()
    {
        if (_isRunning)
        {
            return;
        }

        var interval = _subscriptionPage.CurrentSubscriptionAutoTestDelayIntervalMinutes;
        var decision = _planner.Evaluate(_subscriptionPage.CurrentSubscriptionId, interval, _now());
        if (decision != SubscriptionAutoDelayDecision.Due)
        {
            return;
        }

        _isRunning = true;
        try
        {
            await TickAsync();
            _planner.CompleteRun(interval, _now());
        }
        finally
        {
            _isRunning = false;
        }
    }

    public void Reset()
    {
        _planner.Reset();
    }
}

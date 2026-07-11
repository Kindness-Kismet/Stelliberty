using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Subscriptions;

namespace Stelliberty.Presentation.ViewModels;

public sealed class SubscriptionAutoUpdateCoordinator
{
    private readonly SubscriptionAutoUpdateRunner _runner;
    private readonly SubscriptionPageViewModel _subscriptionPage;
    private readonly Func<DateTimeOffset> _now;
    private bool _isRunning;

    public SubscriptionAutoUpdateCoordinator(
        SubscriptionAutoUpdateRunner runner,
        SubscriptionPageViewModel subscriptionPage,
        Func<DateTimeOffset> now)
    {
        _runner = runner;
        _subscriptionPage = subscriptionPage;
        _now = now;
    }

    public Task RunStartupAsync()
        => RunAsync(_runner.RunStartupUpdatesAsync);

    public Task RunDueAsync()
        => RunAsync(cancellationToken => _runner.RunDueIntervalUpdatesAsync(_now(), cancellationToken));

    private async Task RunAsync(Func<CancellationToken, Task<SubscriptionUpdateResult>> run)
    {
        if (_isRunning)
        {
            return;
        }

        _isRunning = true;
        try
        {
            var result = await run(CancellationToken.None);
            if (result.UpdatedSubscriptionIds.Count > 0 || result.SkippedSubscriptionIds.Count > 0)
            {
                _subscriptionPage.ApplySubscriptionUpdateResult(result);
            }
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"Subscription auto-update scheduler failed: {exception.Message}");
        }
        finally
        {
            _isRunning = false;
        }
    }
}

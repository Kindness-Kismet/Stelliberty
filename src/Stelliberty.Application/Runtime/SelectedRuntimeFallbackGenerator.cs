using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Subscriptions;

namespace Stelliberty.Application.Runtime;

public sealed class SelectedRuntimeFallbackGenerator(
    ISubscriptionStore subscriptionStore,
    SubscriptionOverrideSelectionUpdater overrideSelectionUpdater,
    SelectedSubscriptionRuntimeGenerator runtimeGenerator)
{
    private readonly SubscriptionChainProxyUpdater _chainProxyUpdater = new(subscriptionStore);

    public SelectedRuntimeFallbackResult Generate(string subscriptionId, SelectedSubscriptionRuntimeRequest request)
    {
        try
        {
            return new SelectedRuntimeFallbackResult(runtimeGenerator.Generate(subscriptionId, request), false, []);
        }
        catch (SubscriptionChainProxyCycleException exception)
        {
            return RecoverChainProxyCycle(subscriptionId, request, exception);
        }
        catch (Exception exception)
        {
            var subscription = subscriptionStore.LoadSubscriptions().FirstOrDefault(item => item.Id == subscriptionId);
            if (subscription is null || subscription.OverrideIds.Count == 0)
            {
                throw;
            }

            overrideSelectionUpdater.DisableOverridesForSubscription(subscriptionId);
            AppLogger.Warning($"Runtime generation failed with subscription overrides; disabled them and retried: {subscriptionId}, {exception.Message}");
            try
            {
                return new SelectedRuntimeFallbackResult(runtimeGenerator.Generate(subscriptionId, request), true, []);
            }
            catch (SubscriptionChainProxyCycleException cycleException)
            {
                return RecoverChainProxyCycle(subscriptionId, request, cycleException);
            }
        }
    }

    private SelectedRuntimeFallbackResult RecoverChainProxyCycle(
        string subscriptionId,
        SelectedSubscriptionRuntimeRequest request,
        SubscriptionChainProxyCycleException exception)
    {
        var subscription = subscriptionStore.LoadSubscriptions().FirstOrDefault(item => item.Id == subscriptionId)
            ?? throw exception;
        if (subscription.OverrideIds.Count > 0)
        {
            try
            {
                var withoutOverrides = runtimeGenerator.Generate(subscriptionId, request, includeSelectedOverrides: false);
                overrideSelectionUpdater.DisableOverridesForSubscription(subscriptionId);
                AppLogger.Warning($"Chain proxy cycle introduced by subscription overrides; disabled them: {subscriptionId}");
                return new SelectedRuntimeFallbackResult(withoutOverrides, true, []);
            }
            catch (SubscriptionChainProxyCycleException baselineException)
            {
                return DisableChainsAndRetry(subscriptionId, request, baselineException.Validation);
            }
        }

        return DisableChainsAndRetry(subscriptionId, request, exception.Validation);
    }

    private SelectedRuntimeFallbackResult DisableChainsAndRetry(
        string subscriptionId,
        SelectedSubscriptionRuntimeRequest request,
        SubscriptionChainProxyValidationResult validation)
    {
        _chainProxyUpdater.DisableCycles(subscriptionId, validation);
        try
        {
            return new SelectedRuntimeFallbackResult(
                runtimeGenerator.Generate(subscriptionId, request),
                false,
                validation.CyclicChainNames);
        }
        catch (SubscriptionChainProxyCycleException)
        {
            var withoutOverrides = runtimeGenerator.Generate(subscriptionId, request, includeSelectedOverrides: false);
            overrideSelectionUpdater.DisableOverridesForSubscription(subscriptionId);
            AppLogger.Warning($"Remaining chain proxy cycle introduced by subscription overrides; disabled them: {subscriptionId}");
            return new SelectedRuntimeFallbackResult(withoutOverrides, true, validation.CyclicChainNames);
        }
    }
}

public sealed record SelectedRuntimeFallbackResult(
    SelectedSubscriptionRuntimeResult Runtime,
    bool OverridesDisabled,
    IReadOnlyList<string> DisabledChainProxyNames);

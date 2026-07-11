using Stelliberty.Domain.Subscriptions;
using Stelliberty.Application.Overrides;
using Stelliberty.Application.Runtime;

namespace Stelliberty.Application.Subscriptions;

public sealed class SelectedSubscriptionRuntimeGenerator(
    ISubscriptionStore subscriptionStore,
    ISubscriptionSelectionStore selectionStore,
    RuntimeConfigGenerator runtimeConfigGenerator,
    IOverrideStore? overrideStore = null,
    ISelectedSubscriptionRuntimeStore? runtimeStore = null,
    SubscriptionChainProxyRuntimeApplier? chainProxyApplier = null)
{
    private readonly SubscriptionChainProxyRuntimeApplier _chainProxyApplier = chainProxyApplier ?? new SubscriptionChainProxyRuntimeApplier();
    private readonly SubscriptionOverrideResolver _overrideResolver = new(overrideStore);

    public SelectedSubscriptionRuntimeResult Generate(SelectedSubscriptionRuntimeRequest request)
    {
        var subscriptionId = selectionStore.GetCurrentSubscriptionId()
            ?? throw new InvalidOperationException("No subscription is selected");
        return Generate(subscriptionId, request);
    }

    public SelectedSubscriptionRuntimeResult Generate(string subscriptionId, SelectedSubscriptionRuntimeRequest request)
    {
        var subscription = subscriptionStore.LoadSubscriptions().FirstOrDefault(item => item.Id == subscriptionId)
            ?? throw new InvalidOperationException($"Selected subscription not found: {subscriptionId}");
        var originalContent = ReadOriginalContent(subscription);

        var runtimeConfig = runtimeConfigGenerator.Generate(new RuntimeConfigGenerationRequest(
            BaseConfigContent: originalContent,
            Overrides: _overrideResolver.Resolve(subscription).Concat(request.Overrides).ToList(),
            RuntimeParams: request.RuntimeParams,
        // 链式代理在覆写后定稿，避免脚本抹掉它们。
            PostOverrideTransform: content => _chainProxyApplier.Apply(content, subscription)));
        var paths = runtimeStore?.Save(subscription, originalContent, runtimeConfig.RuntimeConfigContent);

        return new SelectedSubscriptionRuntimeResult(
            subscription,
            runtimeConfig.RuntimeConfigContent,
            paths?.OriginalContentPath,
            paths?.RuntimeConfigPath);
    }

    private string ReadOriginalContent(Subscription subscription)
    {
        try
        {
            return subscriptionStore.ReadContent(subscription.Id);
        }
        catch (IOException exception)
        {
            throw new InvalidOperationException($"Selected subscription content is missing or unreadable: {subscription.Name}", exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new InvalidOperationException($"Selected subscription content is missing or unreadable: {subscription.Name}", exception);
        }
    }
}

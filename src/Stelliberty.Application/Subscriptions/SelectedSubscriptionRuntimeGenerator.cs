using Stelliberty.Domain.Subscriptions;
using Stelliberty.Application.Overrides;
using Stelliberty.Application.Runtime;
using Stelliberty.Application.Rules;

namespace Stelliberty.Application.Subscriptions;

public sealed class SelectedSubscriptionRuntimeGenerator(
    ISubscriptionStore subscriptionStore,
    ISubscriptionSelectionStore selectionStore,
    RuntimeConfigGenerator runtimeConfigGenerator,
    IOverrideStore? overrideStore = null,
    ISelectedSubscriptionRuntimeStore? runtimeStore = null,
    SubscriptionChainProxyRuntimeApplier? chainProxyApplier = null,
    RuleOverrideService? ruleOverrideService = null)
{
    private readonly SubscriptionChainProxyRuntimeApplier _chainProxyApplier = chainProxyApplier ?? new SubscriptionChainProxyRuntimeApplier();
    private readonly SubscriptionOverrideResolver _overrideResolver = new(overrideStore);
    private readonly RuleOverrideService? _ruleOverrideService = ruleOverrideService;

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
        // 本地规则最后定稿，避免订阅覆写改写用户编辑结果。
            PostOverrideTransform: content => ApplyRuntimeRuleOverrides(subscription.Id, content)));
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

    private string ApplyRuntimeRuleOverrides(string subscriptionId, string content)
    {
        var subscription = subscriptionStore.LoadSubscriptions().FirstOrDefault(item => item.Id == subscriptionId)
            ?? throw new InvalidOperationException($"Selected subscription not found: {subscriptionId}");
        var withChainProxies = _chainProxyApplier.Apply(content, subscription);
        return _ruleOverrideService?.Apply(subscriptionId, withChainProxies) ?? withChainProxies;
    }
}

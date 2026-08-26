using Stelliberty.Domain.Subscriptions;
using Stelliberty.Application.Diagnostics;
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
            // 自定义规则最后定稿，避免订阅覆写改写用户编辑结果。
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
        var subscription = DisableBrokenChainProxies(subscriptionId, content);
        var withChainProxies = _chainProxyApplier.Apply(content, subscription);
        _ruleOverrideService?.DisableCustomRulesWithMissingOutbound(subscriptionId, withChainProxies);
        return _ruleOverrideService?.Apply(subscriptionId, withChainProxies) ?? withChainProxies;
    }

    // 失效链式先保存为禁用，再按禁用后的订阅生成配置。
    private Subscription DisableBrokenChainProxies(string subscriptionId, string content)
    {
        var subscription = subscriptionStore.LoadSubscriptions().FirstOrDefault(item => item.Id == subscriptionId)
            ?? throw new InvalidOperationException($"Selected subscription not found: {subscriptionId}");
        var inspection = _chainProxyApplier.Inspect(content, subscription);
        if (!inspection.HasBrokenChains)
        {
            return subscription;
        }

        var invalidIds = inspection.InvalidCustomChainIds.ToHashSet(StringComparer.Ordinal);
        var updated = subscription with
        {
            DisabledBuiltinChainProxyNames = subscription.DisabledBuiltinChainProxyNames
                .Concat(inspection.BrokenBuiltinChainProxyNames)
                .Distinct(StringComparer.Ordinal)
                .ToList(),
            CustomChainProxies = subscription.CustomChainProxies
                .Select(item => invalidIds.Contains(item.Id) ? item with { IsEnabled = false } : item)
                .ToList()
        };
        subscriptionStore.UpdateSubscription(updated);
        AppLogger.Warning(
            $"Chain proxies disabled for {subscription.Name}: "
            + $"builtin=[{string.Join(", ", inspection.BrokenBuiltinChainProxyNames)}], "
            + $"custom=[{string.Join(", ", inspection.InvalidCustomChainIds)}]");
        return updated;
    }
}

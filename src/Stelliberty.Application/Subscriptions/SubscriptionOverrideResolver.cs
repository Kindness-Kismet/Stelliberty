using Stelliberty.Application.Overrides;
using Stelliberty.Application.Runtime;
using Stelliberty.Domain.Overrides;
using Stelliberty.Domain.Subscriptions;

namespace Stelliberty.Application.Subscriptions;

// 按订阅优先的运行时顺序解析启用的覆写。
public sealed class SubscriptionOverrideResolver(IOverrideStore? overrideStore = null)
{
    public IReadOnlyList<RuntimeOverride> Resolve(Subscription subscription)
    {
        if (overrideStore is null || subscription.OverrideIds.Count == 0)
        {
            return [];
        }

        var overrides = overrideStore.LoadOverrides().ToDictionary(item => item.Id, StringComparer.Ordinal);
        var orderedOverrideIds = OrderedSelectedOverrideIds(subscription);
        var missingOverrideId = orderedOverrideIds.FirstOrDefault(overrideId => !overrides.ContainsKey(overrideId));
        if (missingOverrideId is not null)
        {
            throw new InvalidOperationException($"Selected override not found: {missingOverrideId}");
        }

        return orderedOverrideIds
            .Select(overrideId => ToRuntimeOverride(overrides[overrideId]))
            .ToList();
    }

    // 用户顺序优先；剩余启用项保持订阅顺序。
    private static IReadOnlyList<string> OrderedSelectedOverrideIds(Subscription subscription)
    {
        var selected = subscription.OverrideIds.ToHashSet(StringComparer.Ordinal);
        var ordered = subscription.OverrideSortPreference
            .Where(selected.Contains)
            .ToList();
        ordered.AddRange(subscription.OverrideIds.Where(overrideId => !ordered.Contains(overrideId, StringComparer.Ordinal)));
        return ordered;
    }

    private RuntimeOverride ToRuntimeOverride(OverrideProfile overrideProfile)
    {
        return new RuntimeOverride(
            overrideProfile.Id,
            overrideProfile.Name,
            overrideProfile.Format,
            overrideStore?.ReadContent(overrideProfile.Id) ?? string.Empty);
    }
}

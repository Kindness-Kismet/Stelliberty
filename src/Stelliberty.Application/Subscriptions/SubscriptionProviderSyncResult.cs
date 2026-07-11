using Stelliberty.Domain.Subscriptions;
namespace Stelliberty.Application.Subscriptions;

public sealed record SubscriptionProviderSyncResult(
    IReadOnlyList<string> SyncedProviderNames,
    IReadOnlyList<string> SkippedProviderNames)
{
    public IReadOnlyList<string> FailedProviderNames { get; init; } = [];
}

using Stelliberty.Domain.Subscriptions;
namespace Stelliberty.Application.Subscriptions;

public sealed record SubscriptionUpdateResult(
    IReadOnlyList<string> UpdatedSubscriptionIds,
    IReadOnlyList<string> SkippedSubscriptionIds);

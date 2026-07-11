namespace Stelliberty.Domain.Subscriptions;

public sealed record SubscriptionAutoUpdatePlan(IReadOnlyList<string> UpdateSubscriptionIds, IReadOnlyList<string> SkippedSubscriptionIds);

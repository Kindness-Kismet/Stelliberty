namespace Stelliberty.Domain.Subscriptions;

// 计划只包含本轮要执行的订阅；未到期或不可更新的订阅不进入结果
public sealed record SubscriptionAutoUpdatePlan(IReadOnlyList<string> UpdateSubscriptionIds);

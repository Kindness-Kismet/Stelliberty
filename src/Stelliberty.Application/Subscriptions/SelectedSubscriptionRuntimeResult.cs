using Stelliberty.Domain.Subscriptions;
using Stelliberty.Application.Runtime;

namespace Stelliberty.Application.Subscriptions;

public sealed record SelectedSubscriptionRuntimeResult(
    Subscription Subscription,
    string RuntimeConfigContent,
    string? OriginalContentPath = null,
    string? RuntimeConfigPath = null);

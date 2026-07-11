using Stelliberty.Domain.Subscriptions;
using Stelliberty.Application.Runtime;

namespace Stelliberty.Application.Subscriptions;

public sealed record SelectedSubscriptionRuntimeRequest(
    IReadOnlyList<RuntimeOverride> Overrides,
    RuntimeConfigParams RuntimeParams);

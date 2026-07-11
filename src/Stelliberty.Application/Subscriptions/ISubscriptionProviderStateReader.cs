namespace Stelliberty.Application.Subscriptions;

public sealed record SubscriptionProviderRuntimeState(
    string Name,
    string Type,
    int Count,
    DateTimeOffset? UpdatedAt);

public interface ISubscriptionProviderStateReader
{
    Task<IReadOnlyList<SubscriptionProviderRuntimeState>> ReadStatesAsync(CancellationToken cancellationToken = default);
}

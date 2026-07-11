using Stelliberty.Domain.Subscriptions;
namespace Stelliberty.Application.Subscriptions;

public interface ISubscriptionProviderSyncer
{
    Task SyncAsync(SubscriptionProvider provider, CancellationToken cancellationToken = default);
}

using Stelliberty.Domain.Subscriptions;
namespace Stelliberty.Application.Subscriptions;

public interface IRemoteSubscriptionDownloader
{
    Task<RemoteSubscriptionDownloadResult> DownloadAsync(RemoteSubscriptionDownloadRequest request, CancellationToken cancellationToken = default);
}

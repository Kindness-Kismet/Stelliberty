using Stelliberty.Domain.Subscriptions;
namespace Stelliberty.Application.Subscriptions;

public interface ISubscriptionProviderUploader
{
    Task<SubscriptionProviderUploadResult> UploadAsync(SubscriptionProvider provider, string sourcePath, CancellationToken cancellationToken = default);
}

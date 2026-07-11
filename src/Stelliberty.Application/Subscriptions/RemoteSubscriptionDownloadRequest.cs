using Stelliberty.Domain.Subscriptions;
namespace Stelliberty.Application.Subscriptions;

public sealed record RemoteSubscriptionDownloadRequest(
    string SubscriptionId,
    string SourceLocation,
    string UserAgent,
    SubscriptionUpdateProxyMode ProxyMode,
    string AgeSecretKey = "");

using Stelliberty.Domain.Subscriptions;
namespace Stelliberty.Application.Subscriptions;

public sealed record RemoteSubscriptionDownloadResult(string Content, SubscriptionTrafficInfo? TrafficInfo = null);

using Stelliberty.Domain.Subscriptions;
namespace Stelliberty.Application.Subscriptions;

public sealed record RemoteSubscriptionImportRequest(
    string Name,
    string SourceLocation,
    string UserAgent = "",
    int AutoTestDelayIntervalMinutes = 0,
    SubscriptionAutoUpdateMode AutoUpdateMode = SubscriptionAutoUpdateMode.Disabled,
    int AutoUpdateIntervalMinutes = 0,
    SubscriptionUpdateProxyMode UpdateProxyMode = SubscriptionUpdateProxyMode.Direct,
    string AgeSecretKey = "");

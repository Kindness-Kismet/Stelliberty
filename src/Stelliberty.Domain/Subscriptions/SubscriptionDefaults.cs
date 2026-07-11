namespace Stelliberty.Domain.Subscriptions;

public static class SubscriptionDefaults
{
    public const string UserAgent = "clash.meta";

    public static string NormalizeUserAgent(string userAgent)
    {
        return string.IsNullOrWhiteSpace(userAgent) ? UserAgent : userAgent.Trim();
    }
}

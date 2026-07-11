namespace Stelliberty.Application.Subscriptions;

public sealed record SubscriptionProviderUploadResult(bool IsUploaded, string? SkipReason = null)
{
    public static SubscriptionProviderUploadResult Uploaded() => new(true);

    public static SubscriptionProviderUploadResult Skipped(string reason) => new(false, reason);
}

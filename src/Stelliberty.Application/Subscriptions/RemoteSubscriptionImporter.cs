using Stelliberty.Domain.Subscriptions;
using Stelliberty.Application.Diagnostics;

namespace Stelliberty.Application.Subscriptions;

public sealed class RemoteSubscriptionImporter(
    ISubscriptionStore store,
    IRemoteSubscriptionDownloader downloader,
    Func<DateTimeOffset>? now = null,
    ISubscriptionContentDecryptor? contentDecryptor = null)
{
    private readonly SubscriptionConfigValidator _validator = new();
    private readonly Func<DateTimeOffset> _now = now ?? (() => DateTimeOffset.UtcNow);
    private readonly SubscriptionChainProxyAnalyzer _chainProxyAnalyzer = new();
    private readonly SubscriptionContentNormalizer _contentNormalizer = new();
    private readonly ISubscriptionContentDecryptor _contentDecryptor = contentDecryptor ?? new SubscriptionContentDecryptor();

    public async Task<Subscription> ImportAsync(RemoteSubscriptionImportRequest request, CancellationToken cancellationToken = default)
    {
        var importedAt = _now();
        var userAgent = SubscriptionDefaults.NormalizeUserAgent(request.UserAgent);
        var ageSecretKey = request.AgeSecretKey.Trim();
        var subscription = new Subscription(
            Id: Guid.NewGuid().ToString("N"),
            Name: request.Name,
            SourceLocation: request.SourceLocation,
            IsLocalFile: false,
            CreatedAt: importedAt,
            LastUpdatedAt: importedAt,
            UserAgent: userAgent,
            AutoTestDelayIntervalMinutes: request.AutoTestDelayIntervalMinutes,
            AutoUpdateMode: request.AutoUpdateMode,
            AutoUpdateIntervalMinutes: request.AutoUpdateIntervalMinutes,
            UpdateProxyMode: request.UpdateProxyMode,
            AgeSecretKey: ageSecretKey);

        var downloadResult = await downloader.DownloadAsync(new RemoteSubscriptionDownloadRequest(
            subscription.SourceLocation,
            subscription.UserAgent,
            subscription.UpdateProxyMode), cancellationToken);
        var content = _contentDecryptor.DecryptIfNeeded(downloadResult.Content, subscription.AgeSecretKey);
        var sourceFormat = _contentNormalizer.DetectSourceFormat(content);
        var normalizedContent = _contentNormalizer.Normalize(content);
        _validator.Validate(normalizedContent);
        subscription = subscription with
        {
            SourceFormat = sourceFormat,
            TrafficInfo = downloadResult.TrafficInfo,
            BuiltinChainProxyNames = _chainProxyAnalyzer.AnalyzeBuiltinChainProxyNames(normalizedContent)
        };
        store.Save(subscription, normalizedContent);
        AppLogger.Info($"Remote subscription imported: {request.Name}");
        return subscription;
    }
}

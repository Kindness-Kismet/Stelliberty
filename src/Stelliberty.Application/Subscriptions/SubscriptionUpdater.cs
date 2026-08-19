using Stelliberty.Domain.Subscriptions;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Updates;

namespace Stelliberty.Application.Subscriptions;

public sealed class SubscriptionUpdater(
    ISubscriptionStore store,
    IRemoteSubscriptionDownloader downloader,
    Func<DateTimeOffset>? now = null,
    ISubscriptionContentDecryptor? contentDecryptor = null)
{
    private readonly object _syncRoot = new();
    private readonly HashSet<string> _updatingSubscriptionIds = new(StringComparer.Ordinal);
    private readonly SubscriptionConfigValidator _validator = new();
    private readonly Func<DateTimeOffset> _now = now ?? (() => DateTimeOffset.UtcNow);
    private readonly SubscriptionChainProxyAnalyzer _chainProxyAnalyzer = new();
    private readonly SubscriptionContentNormalizer _contentNormalizer = new();
    private readonly ISubscriptionContentDecryptor _contentDecryptor = contentDecryptor ?? new SubscriptionContentDecryptor();

    public async Task<SubscriptionUpdateResult> UpdateAsync(string subscriptionId, CancellationToken cancellationToken = default)
    {
        var subscription = store.LoadSubscriptions().FirstOrDefault(item => item.Id == subscriptionId)
            ?? throw new InvalidOperationException($"Subscription not found: {subscriptionId}");

        return await UpdateSubscriptionsAsync([subscription], cancellationToken);
    }

    public async Task<SubscriptionUpdateResult> UpdateAllAsync(CancellationToken cancellationToken = default)
    {
        return await UpdateSubscriptionsAsync(store.LoadSubscriptions(), cancellationToken);
    }

    public async Task<SubscriptionUpdateResult> UpdateManyAsync(IReadOnlyCollection<string> subscriptionIds, CancellationToken cancellationToken = default)
    {
        var idSet = subscriptionIds.Where(id => !string.IsNullOrWhiteSpace(id)).ToHashSet(StringComparer.Ordinal);
        if (idSet.Count == 0)
        {
            return new SubscriptionUpdateResult([], []);
        }

        var subscriptions = store.LoadSubscriptions().Where(item => idSet.Contains(item.Id)).ToList();
        var result = await UpdateSubscriptionsAsync(subscriptions, cancellationToken);
        var missingIds = idSet.Except(subscriptions.Select(item => item.Id), StringComparer.Ordinal).ToList();
        return missingIds.Count == 0
            ? result
            : new SubscriptionUpdateResult(result.UpdatedSubscriptionIds, result.SkippedSubscriptionIds.Concat(missingIds).ToList());
    }

    private async Task<SubscriptionUpdateResult> UpdateSubscriptionsAsync(IReadOnlyList<Subscription> subscriptions, CancellationToken cancellationToken)
    {
        var updatedIds = new List<string>();
        var skippedIds = new List<string>();
        foreach (var subscription in subscriptions)
        {
            if (subscription.IsLocalFile || !TryStartUpdate(subscription.Id))
            {
                skippedIds.Add(subscription.Id);
                continue;
            }

            try
            {
                var userAgent = SubscriptionDefaults.NormalizeUserAgent(subscription.UserAgent);
                var downloadResult = await downloader.DownloadAsync(new RemoteSubscriptionDownloadRequest(
                    subscription.SourceLocation,
                    userAgent,
                    subscription.UpdateProxyMode), cancellationToken);
                var content = _contentDecryptor.DecryptIfNeeded(downloadResult.Content, subscription.AgeSecretKey);
                var sourceFormat = _contentNormalizer.DetectSourceFormat(content);
                var normalizedContent = _contentNormalizer.Normalize(content);
                _validator.Validate(normalizedContent);
                store.Save(subscription with
                {
                    SourceFormat = sourceFormat,
                    LastUpdatedAt = _now(),
                    LastError = null,
                    LastErrorAt = null,
                    TrafficInfo = downloadResult.TrafficInfo,
                    BuiltinChainProxyNames = _chainProxyAnalyzer.AnalyzeBuiltinChainProxyNames(normalizedContent)
                }, normalizedContent);
                updatedIds.Add(subscription.Id);
                AppLogger.Info($"Remote subscription updated: {subscription.Name}");
            }
            catch (Exception exception)
            {
                var failedSubscription = subscription with { LastError = exception.Message, LastErrorAt = _now() };
                if (IsPermanentUpdateError(exception.Message))
                {
                    failedSubscription = failedSubscription with { AutoUpdateMode = SubscriptionAutoUpdateMode.Disabled };
                }

                store.UpdateSubscription(failedSubscription);
                skippedIds.Add(subscription.Id);
                AppLogger.Error(exception, $"Remote subscription update failed: {subscription.Name}");
            }
            finally
            {
                CompleteUpdate(subscription.Id);
            }
        }

        return new SubscriptionUpdateResult(updatedIds, skippedIds);
    }

    private static bool IsPermanentUpdateError(string message)
    {
        var lowerMessage = message.ToLowerInvariant();
        // 永久性的来源、内容或解密错误会关闭自动更新。
        return lowerMessage.Contains("age", StringComparison.Ordinal)
            || lowerMessage.Contains("decrypt", StringComparison.Ordinal)
            || lowerMessage.Contains("identity", StringComparison.Ordinal)
            || lowerMessage.Contains("secret key", StringComparison.Ordinal)
            || lowerMessage.Contains("404", StringComparison.Ordinal)
            || lowerMessage.Contains("403", StringComparison.Ordinal)
            || lowerMessage.Contains("401", StringComparison.Ordinal)
            || lowerMessage.Contains("configuration file", StringComparison.Ordinal)
            || lowerMessage.Contains("format", StringComparison.Ordinal)
            || lowerMessage.Contains("parse", StringComparison.Ordinal)
            || lowerMessage.Contains("yaml", StringComparison.Ordinal)
            || lowerMessage.Contains("proxies", StringComparison.Ordinal);
    }

    private bool TryStartUpdate(string subscriptionId)
    {
        lock (_syncRoot)
        {
            return _updatingSubscriptionIds.Add(subscriptionId);
        }
    }

    private void CompleteUpdate(string subscriptionId)
    {
        lock (_syncRoot)
        {
            _updatingSubscriptionIds.Remove(subscriptionId);
        }
    }
}

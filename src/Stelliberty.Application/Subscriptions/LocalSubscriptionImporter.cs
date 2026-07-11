using Stelliberty.Domain.Subscriptions;
using Stelliberty.Application.Diagnostics;

namespace Stelliberty.Application.Subscriptions;

public sealed class LocalSubscriptionImporter(
    ISubscriptionStore store,
    SubscriptionConfigValidator? validator = null,
    SubscriptionContentNormalizer? contentNormalizer = null,
    SubscriptionChainProxyAnalyzer? chainProxyAnalyzer = null)
{
    private readonly SubscriptionConfigValidator _validator = validator ?? new SubscriptionConfigValidator();
    private readonly SubscriptionContentNormalizer _contentNormalizer = contentNormalizer ?? new SubscriptionContentNormalizer();
    private readonly SubscriptionChainProxyAnalyzer _chainProxyAnalyzer = chainProxyAnalyzer ?? new SubscriptionChainProxyAnalyzer();

    public Subscription Import(
        string name,
        string content,
        string sourceLocation = "local",
        int autoTestDelayIntervalMinutes = 0)
    {
        var sourceFormat = _contentNormalizer.DetectSourceFormat(content);
        var normalizedContent = _contentNormalizer.Normalize(content);
        _validator.Validate(normalizedContent);

        var subscription = new Subscription(
            Id: Guid.NewGuid().ToString("N"),
            Name: name,
            SourceLocation: sourceLocation,
            IsLocalFile: true,
            CreatedAt: DateTimeOffset.UtcNow,
            LastUpdatedAt: DateTimeOffset.UtcNow,
            AutoTestDelayIntervalMinutes: autoTestDelayIntervalMinutes,
            BuiltinChainProxyNames: _chainProxyAnalyzer.AnalyzeBuiltinChainProxyNames(normalizedContent),
            SourceFormat: sourceFormat);

        store.Save(subscription, normalizedContent);
        AppLogger.Info($"Local subscription imported: {name}");
        return subscription;
    }
}

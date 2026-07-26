using System.Text.Json;
using System.Text.Json.Serialization;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Subscriptions;
using Stelliberty.Domain.Subscriptions;
using Stelliberty.Infrastructure.Storage;

namespace Stelliberty.Infrastructure.Subscriptions;

public sealed class FileSubscriptionStore(string rootDirectory) : ISubscriptionStore
{
    private readonly string _subscriptionsDirectory = Path.Combine(rootDirectory, "subscriptions");
    private readonly string _listPath = Path.Combine(rootDirectory, "subscriptions", "subscriptions_list.json");

    public void Save(Subscription subscription, string originalContent)
    {
        Directory.CreateDirectory(_subscriptionsDirectory);
        AtomicFile.WriteAllText(GetContentPath(subscription.Id), originalContent);

        var subscriptions = LoadSubscriptions().ToList();
        var index = subscriptions.FindIndex(item => item.Id == subscription.Id);
        if (index < 0)
        {
            subscriptions.Add(subscription);
        }
        else
        {
            subscriptions[index] = subscription;
        }

        SaveSubscriptionList(subscriptions);
        AppLogger.Info($"Subscription content saved: {subscription.Name}");
    }

    public void UpdateSubscription(Subscription subscription)
    {
        var subscriptions = LoadSubscriptions().ToList();
        var index = subscriptions.FindIndex(item => item.Id == subscription.Id);
        if (index < 0)
        {
            throw new InvalidOperationException($"Subscription not found: {subscription.Id}");
        }

        subscriptions[index] = subscription;
        SaveSubscriptionList(subscriptions);
        AppLogger.Info($"Subscription metadata saved: {subscription.Name}");
    }

    public void SaveSubscriptions(IReadOnlyList<Subscription> subscriptions)
    {
        SaveSubscriptionList(subscriptions);
        AppLogger.Info("Subscription list order saved");
    }

    public void SaveContent(string subscriptionId, string originalContent)
    {
        Directory.CreateDirectory(_subscriptionsDirectory);
        AtomicFile.WriteAllText(GetContentPath(subscriptionId), originalContent);
        AppLogger.Info($"Subscription content saved: {subscriptionId}");
    }

    public IReadOnlyList<Subscription> LoadSubscriptions()
    {
        var list = JsonFileRecovery.ReadOrRecover<StoredSubscriptionListFile>(_listPath) ?? new StoredSubscriptionListFile([]);
        return (list.Subscriptions ?? []).Select(subscription => subscription.ToSubscription()).ToList();
    }

    public string ReadContent(string subscriptionId)
    {
        return File.ReadAllText(GetContentPath(subscriptionId));
    }

    public void Delete(string subscriptionId)
    {
        var subscriptions = LoadSubscriptions().Where(item => item.Id != subscriptionId).ToList();
        SaveSubscriptionList(subscriptions);

        var contentPath = GetContentPath(subscriptionId);
        if (File.Exists(contentPath))
        {
            File.Delete(contentPath);
        }

        AppLogger.Info($"Subscription deleted: {subscriptionId}");
    }

    private void SaveSubscriptionList(IReadOnlyList<Subscription> subscriptions)
    {
        var json = JsonSerializer.Serialize(new SubscriptionListFile(subscriptions), new JsonSerializerOptions
        {
            WriteIndented = true
        });
        AtomicFile.WriteAllText(_listPath, json);
    }

    public string GetContentPath(string subscriptionId)
    {
        return Path.Combine(_subscriptionsDirectory, $"{subscriptionId}.yaml");
    }

    private sealed record SubscriptionListFile(IReadOnlyList<Subscription> Subscriptions);

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record StoredSubscriptionListFile(IReadOnlyList<StoredSubscription>? Subscriptions);

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record StoredSubscription(
        string Id,
        string Name,
        string SourceLocation,
        bool IsLocalFile,
        DateTimeOffset CreatedAt,
        DateTimeOffset? LastUpdatedAt = null,
        string UserAgent = "",
        bool IsAutoTestDelayEnabled = false,
        int? AutoTestDelayIntervalMinutes = null,
        SubscriptionAutoUpdateMode AutoUpdateMode = SubscriptionAutoUpdateMode.Disabled,
        int AutoUpdateIntervalMinutes = 0,
        SubscriptionUpdateProxyMode UpdateProxyMode = SubscriptionUpdateProxyMode.Direct,
        string AgeSecretKey = "",
        IReadOnlyList<string>? OverrideIds = null,
        IReadOnlyList<string>? OverrideSortPreference = null,
        string? LastError = null,
        DateTimeOffset? LastErrorAt = null,
        SubscriptionTrafficInfo? TrafficInfo = null,
        IReadOnlyList<string>? BuiltinChainProxyNames = null,
        IReadOnlyList<string>? DisabledBuiltinChainProxyNames = null,
        IReadOnlyList<SubscriptionCustomChainProxy>? CustomChainProxies = null,
        SubscriptionSourceFormat SourceFormat = SubscriptionSourceFormat.StandardClash)
    {
        public Subscription ToSubscription() => new(
            Id,
            Name,
            SourceLocation,
            IsLocalFile,
            CreatedAt,
            LastUpdatedAt,
            UserAgent,
            AutoTestDelayIntervalMinutes ?? (IsAutoTestDelayEnabled ? 1 : 0),
            AutoUpdateMode,
            AutoUpdateIntervalMinutes,
            UpdateProxyMode,
            AgeSecretKey,
            OverrideIds,
            OverrideSortPreference,
            LastError,
            LastErrorAt,
            TrafficInfo,
            BuiltinChainProxyNames,
            DisabledBuiltinChainProxyNames,
            CustomChainProxies,
            SourceFormat);
    }
}

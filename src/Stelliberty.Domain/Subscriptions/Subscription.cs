namespace Stelliberty.Domain.Subscriptions;

public enum SubscriptionChainProxyHopKind
{
    Proxy,
    ProxyGroup
}

public sealed record SubscriptionChainProxyHop(
    SubscriptionChainProxyHopKind Kind,
    string Name);

public sealed record SubscriptionCustomChainProxy(
    string Id,
    string DisplayName,
    string ProxyGroupName,
    IReadOnlyList<SubscriptionChainProxyHop>? Hops = null,
    bool IsEnabled = true)
{
    public IReadOnlyList<SubscriptionChainProxyHop> Hops { get; init; } = Hops ?? [];
}

public sealed record Subscription(
    string Id,
    string Name,
    string SourceLocation,
    bool IsLocalFile,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastUpdatedAt = null,
    string UserAgent = SubscriptionDefaults.UserAgent,
    int AutoTestDelayIntervalMinutes = 0,
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
    public bool IsAutoTestDelayEnabled => AutoTestDelayIntervalMinutes > 0;

    public IReadOnlyList<string> OverrideIds { get; init; } = OverrideIds ?? [];

    public IReadOnlyList<string> OverrideSortPreference { get; init; } = OverrideSortPreference ?? [];

    public IReadOnlyList<string> BuiltinChainProxyNames { get; init; } = BuiltinChainProxyNames ?? [];

    public IReadOnlyList<string> DisabledBuiltinChainProxyNames { get; init; } = DisabledBuiltinChainProxyNames ?? [];

    public IReadOnlyList<SubscriptionCustomChainProxy> CustomChainProxies { get; init; } = CustomChainProxies ?? [];
}

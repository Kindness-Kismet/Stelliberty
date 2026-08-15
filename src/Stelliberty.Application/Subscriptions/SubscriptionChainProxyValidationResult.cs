namespace Stelliberty.Application.Subscriptions;

public sealed record SubscriptionCustomChainProxyCycle(string Id, string DisplayName);

public sealed record SubscriptionChainProxyValidationResult(
    IReadOnlyList<string> CyclicBuiltinNames,
    IReadOnlyList<SubscriptionCustomChainProxyCycle> CyclicCustomChains)
{
    public static SubscriptionChainProxyValidationResult Valid { get; } = new([], []);

    public bool IsValid => CyclicBuiltinNames.Count == 0 && CyclicCustomChains.Count == 0;

    public IReadOnlyList<string> CyclicChainNames => CyclicBuiltinNames
        .Concat(CyclicCustomChains.Select(item => item.DisplayName))
        .ToList();
}

public sealed class SubscriptionChainProxyCycleException : InvalidOperationException
{
    public SubscriptionChainProxyCycleException(SubscriptionChainProxyValidationResult validation)
        : base($"Chain proxy cycle detected: {string.Join(", ", validation.CyclicChainNames)}")
    {
        Validation = validation;
    }

    public SubscriptionChainProxyValidationResult Validation { get; }
}

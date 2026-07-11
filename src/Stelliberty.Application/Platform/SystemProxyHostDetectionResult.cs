namespace Stelliberty.Application.Platform;

public sealed record SystemProxyHostDetectionResult(string? HostName, IReadOnlyList<string> NetworkAddresses);

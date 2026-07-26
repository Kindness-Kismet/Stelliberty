namespace Stelliberty.Application.Platform;

public static class SystemProxyHostCandidates
{
    public static IReadOnlyList<string> Build(string? hostName, IReadOnlyList<string> networkAddresses)
    {
        var candidates = new List<string> { "127.0.0.1", "localhost" };
        if (!string.IsNullOrWhiteSpace(hostName)
            && !string.Equals(hostName, "localhost", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(hostName, "127.0.0.1", StringComparison.Ordinal))
        {
            candidates.Add($"{hostName}.local");
        }

        candidates.AddRange(networkAddresses.Select(RemoveScopeId));
        return candidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string RemoveScopeId(string value)
    {
        var scopeIndex = value.IndexOf('%');
        return scopeIndex < 0 ? value : value[..scopeIndex];
    }
}

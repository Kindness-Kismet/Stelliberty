namespace Stelliberty.Application.Platform;

public static class UwpLoopbackConfiguration
{
    public static string ResolveDisplayName(string displayName, string appContainerName, string packageFamilyName)
    {
        if (!string.IsNullOrWhiteSpace(displayName) && !displayName.StartsWith("@{", StringComparison.Ordinal))
        {
            return displayName;
        }

        if (!string.IsNullOrWhiteSpace(appContainerName) && !appContainerName.StartsWith("@{", StringComparison.Ordinal))
        {
            return appContainerName;
        }

        return packageFamilyName;
    }

    public static IReadOnlyList<byte[]> BuildNextLoopbackSids(IEnumerable<byte[]> currentSids, byte[] targetSid, bool isEnabled)
    {
        var result = currentSids
            .Where(sid => !sid.SequenceEqual(targetSid))
            .Select(sid => sid.ToArray())
            .ToList();

        if (isEnabled)
        {
            result.Add(targetSid.ToArray());
        }

        return result;
    }
}

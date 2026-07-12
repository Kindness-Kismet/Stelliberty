namespace Stelliberty.Application.Updates;

public sealed record AppUpdateReleaseInfo(string Version, string Url, bool IsPreRelease, bool IsDraft = false);

public static class AppUpdateReleaseSelector
{
    public static AppUpdateReleaseInfo? Select(IEnumerable<AppUpdateReleaseInfo> releases, string channel, string currentVersion)
    {
        var includePreReleases = string.Equals(channel, "beta", StringComparison.OrdinalIgnoreCase);
        AppUpdateReleaseInfo? best = null;
        foreach (var release in releases)
        {
            if (release.IsDraft || string.IsNullOrWhiteSpace(release.Version))
            {
                continue;
            }

            if (!includePreReleases && (release.IsPreRelease || AppVersionComparer.IsPreRelease(release.Version)))
            {
                continue;
            }

            if (best is null || AppVersionComparer.IsNewer(release.Version, best.Version))
            {
                best = release;
            }
        }

        if (best is null)
        {
            return null;
        }

        return AppVersionComparer.IsNewer(best.Version, currentVersion) ? best : null;
    }
}

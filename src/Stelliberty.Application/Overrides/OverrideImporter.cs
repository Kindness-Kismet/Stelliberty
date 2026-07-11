using Stelliberty.Domain.Overrides;
using Stelliberty.Application.Diagnostics;

namespace Stelliberty.Application.Overrides;

public sealed class OverrideImporter(
    IOverrideStore store,
    IRemoteOverrideDownloader downloader,
    Func<DateTimeOffset>? now = null)
{
    private readonly Func<DateTimeOffset> _now = now ?? (() => DateTimeOffset.UtcNow);

    public async Task<OverrideProfile> ImportRemoteAsync(
        string name,
        string url,
        OverrideFormat format,
        OverrideUpdateProxyMode updateProxyMode = OverrideUpdateProxyMode.Direct,
        CancellationToken cancellationToken = default)
    {
        var overrideProfile = CreateProfile(name, OverrideSourceType.Remote, format, url) with { UpdateProxyMode = updateProxyMode };
        var content = await downloader.DownloadAsync(overrideProfile, cancellationToken);
        var updated = overrideProfile with { LastUpdatedAt = _now() };
        store.Save(updated, content);
        AppLogger.Info($"Remote override imported: {name}");
        return updated;
    }

    public OverrideProfile ImportLocal(string name, string localPath, OverrideFormat format, string content)
    {
        var overrideProfile = CreateProfile(name, OverrideSourceType.Local, format, localPath) with { LastUpdatedAt = _now() };
        store.Save(overrideProfile, content);
        AppLogger.Info($"Local override imported: {name}");
        return overrideProfile;
    }

    public OverrideProfile CreateBlankLocal(string name, OverrideFormat format)
    {
        var overrideProfile = CreateProfile(name, OverrideSourceType.Local, format, string.Empty) with { LastUpdatedAt = _now() };
        store.Save(overrideProfile, string.Empty);
        AppLogger.Info($"Blank override created: {name}");
        return overrideProfile;
    }

    private OverrideProfile CreateProfile(string name, OverrideSourceType sourceType, OverrideFormat format, string sourceLocation)
    {
        return new OverrideProfile(Guid.NewGuid().ToString("N"), name, sourceType, format, sourceLocation, _now());
    }
}

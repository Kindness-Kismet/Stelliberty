using Stelliberty.Domain.Overrides;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Updates;

namespace Stelliberty.Application.Overrides;

public sealed class OverrideUpdater(
    IOverrideStore store,
    IRemoteOverrideDownloader downloader,
    Func<DateTimeOffset>? now = null)
{
    private readonly object _syncRoot = new();
    private readonly HashSet<string> _updatingOverrideIds = new(StringComparer.Ordinal);
    private readonly Func<DateTimeOffset> _now = now ?? (() => DateTimeOffset.UtcNow);

    public async Task<OverrideUpdateResult> UpdateAsync(string overrideId, CancellationToken cancellationToken = default)
    {
        var overrideProfile = store.LoadOverrides().FirstOrDefault(item => item.Id == overrideId)
            ?? throw new InvalidOperationException($"Override not found: {overrideId}");

        return await UpdateOverridesAsync([overrideProfile], cancellationToken);
    }

    public async Task<OverrideUpdateResult> UpdateAllAsync(CancellationToken cancellationToken = default)
    {
        return await UpdateOverridesAsync(store.LoadOverrides().ToList(), cancellationToken);
    }

    public async Task<OverrideUpdateResult> UpdateManyAsync(IReadOnlyCollection<string> overrideIds, CancellationToken cancellationToken = default)
    {
        var idSet = overrideIds.Where(id => !string.IsNullOrWhiteSpace(id)).ToHashSet(StringComparer.Ordinal);
        if (idSet.Count == 0)
        {
            return new OverrideUpdateResult([], []);
        }

        var overrides = store.LoadOverrides().Where(item => idSet.Contains(item.Id)).ToList();
        var result = await UpdateOverridesAsync(overrides, cancellationToken);
        var missingIds = idSet.Except(overrides.Select(item => item.Id), StringComparer.Ordinal).ToList();
        return missingIds.Count == 0
            ? result
            : new OverrideUpdateResult(result.UpdatedOverrideIds, result.SkippedOverrideIds.Concat(missingIds).ToList());
    }

    private async Task<OverrideUpdateResult> UpdateOverridesAsync(IReadOnlyList<OverrideProfile> overrides, CancellationToken cancellationToken)
    {
        var updatedIds = new List<string>();
        var skippedIds = new List<string>();
        foreach (var overrideProfile in overrides)
        {
            if (overrideProfile.SourceType != OverrideSourceType.Remote || !TryStartUpdate(overrideProfile.Id))
            {
                skippedIds.Add(overrideProfile.Id);
                continue;
            }

            try
            {
                var content = await downloader.DownloadAsync(overrideProfile, cancellationToken);
                store.Save(overrideProfile with { LastUpdatedAt = _now() }, content);
                updatedIds.Add(overrideProfile.Id);
                AppLogger.Info($"Remote override updated: {overrideProfile.Name}");
            }
            catch (Exception exception)
            {
                skippedIds.Add(overrideProfile.Id);
                AppLogger.Error(exception, $"Remote override update failed: {overrideProfile.Name}");
            }
            finally
            {
                CompleteUpdate(overrideProfile.Id);
            }
        }

        return new OverrideUpdateResult(updatedIds, skippedIds);
    }

    private bool TryStartUpdate(string overrideId)
    {
        lock (_syncRoot)
        {
            return _updatingOverrideIds.Add(overrideId);
        }
    }

    private void CompleteUpdate(string overrideId)
    {
        lock (_syncRoot)
        {
            _updatingOverrideIds.Remove(overrideId);
        }
    }
}

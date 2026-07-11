using System.Text.Json;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Overrides;
using Stelliberty.Domain.Overrides;
using Stelliberty.Infrastructure.Storage;

namespace Stelliberty.Infrastructure.Overrides;

public sealed class FileOverrideStore(string rootDirectory) : IOverrideStore
{
    private readonly string _overridesDirectory = Path.Combine(rootDirectory, "overrides");
    private readonly string _listPath = Path.Combine(rootDirectory, "overrides", "overrides_list.json");

    public void Save(OverrideProfile overrideProfile, string content)
    {
        Directory.CreateDirectory(_overridesDirectory);
        var overrides = LoadOverrides().ToList();
        var index = overrides.FindIndex(item => item.Id == overrideProfile.Id);
        var oldContentPath = index >= 0 ? ContentPath(overrides[index]) : null;
        var newContentPath = ContentPath(overrideProfile);

        File.WriteAllText(newContentPath, content);

        if (index < 0)
        {
            overrides.Add(overrideProfile);
        }
        else
        {
            overrides[index] = overrideProfile;
        }

        SaveOverrides(overrides);
        if (oldContentPath is not null
            && !string.Equals(oldContentPath, newContentPath, StringComparison.Ordinal)
            && File.Exists(oldContentPath))
        {
            File.Delete(oldContentPath);
        }
        AppLogger.Info($"Override saved: {overrideProfile.Name}");
    }

    public IReadOnlyList<OverrideProfile> LoadOverrides()
    {
        var list = JsonFileRecovery.ReadOrRecover<OverrideListFile>(_listPath) ?? new OverrideListFile([]);
        return list.Overrides;
    }

    public string ReadContent(string overrideId)
    {
        var overrideProfile = LoadOverrides().FirstOrDefault(item => item.Id == overrideId)
            ?? throw new InvalidOperationException($"Override not found: {overrideId}");
        return File.ReadAllText(ContentPath(overrideProfile));
    }

    public string GetContentPath(string overrideId)
    {
        var overrideProfile = LoadOverrides().FirstOrDefault(item => item.Id == overrideId)
            ?? throw new InvalidOperationException($"Override not found: {overrideId}");
        return ContentPath(overrideProfile);
    }

    public void Delete(string overrideId)
    {
        var overrides = LoadOverrides().ToList();
        var overrideProfile = overrides.FirstOrDefault(item => item.Id == overrideId);
        if (overrideProfile is null)
        {
            return;
        }

        overrides.Remove(overrideProfile);
        SaveOverrides(overrides);
        var contentPath = ContentPath(overrideProfile);
        if (File.Exists(contentPath))
        {
            File.Delete(contentPath);
        }

        AppLogger.Info($"Override deleted: {overrideId}");
    }

    public void SaveOverrides(IReadOnlyList<OverrideProfile> overrides)
    {
        Directory.CreateDirectory(_overridesDirectory);
        var json = JsonSerializer.Serialize(new OverrideListFile(overrides), new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(_listPath, json);
        AppLogger.Info("Override list order saved");
    }

    private string ContentPath(OverrideProfile overrideProfile)
    {
        return Path.Combine(_overridesDirectory, $"{overrideProfile.Id}.{Extension(overrideProfile.Format)}");
    }

    private static string Extension(OverrideFormat format)
    {
        return format == OverrideFormat.Yaml ? "yaml" : "js";
    }

    private sealed record OverrideListFile(IReadOnlyList<OverrideProfile> Overrides);
}

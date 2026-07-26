using Stelliberty.Application.Overrides;
using Stelliberty.Domain.Overrides;

namespace Stelliberty.Infrastructure.Overrides;

// 空实现，仅供测试使用
public sealed class InMemoryOverrideStore : IOverrideStore
{
    private readonly List<OverrideProfile> _overrides = [];
    private readonly Dictionary<string, string> _contents = new(StringComparer.Ordinal);

    public void Save(OverrideProfile @override, string originalContent)
    {
        _overrides.Add(@override);
        _contents[@override.Id] = originalContent;
    }

    public void SaveOverrides(IReadOnlyList<OverrideProfile> overrides)
    {
        _overrides.Clear();
        _overrides.AddRange(overrides);
    }

    public void SaveContent(string overrideId, string originalContent)
    {
        _contents[overrideId] = originalContent;
    }

    public IReadOnlyList<OverrideProfile> LoadOverrides() => _overrides.ToList();

    public string ReadContent(string overrideId) =>
        _contents.TryGetValue(overrideId, out var content) ? content : string.Empty;

    public string GetContentPath(string overrideId) => $"{overrideId}.yaml";

    public void Delete(string overrideId)
    {
        _overrides.RemoveAll(o => o.Id == overrideId);
        _contents.Remove(overrideId);
    }
}

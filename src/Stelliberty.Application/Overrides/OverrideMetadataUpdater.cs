using Stelliberty.Domain.Overrides;
using Stelliberty.Application.Diagnostics;

namespace Stelliberty.Application.Overrides;

public sealed record OverrideMetadataEdit(
    string Name,
    string SourceLocation,
    OverrideFormat Format,
    OverrideUpdateProxyMode UpdateProxyMode);

public sealed class OverrideMetadataUpdater(IOverrideStore store)
{
    // 外部删除返回 null；内容为 null 时保留当前文件。
    public OverrideProfile? Save(string overrideId, OverrideMetadataEdit edit, string? content = null)
    {
        var persisted = store.LoadOverrides().FirstOrDefault(item => item.Id == overrideId);
        if (persisted is null)
        {
            return null;
        }

        var updated = persisted with
        {
            Name = edit.Name,
            SourceLocation = edit.SourceLocation,
            Format = edit.Format,
            UpdateProxyMode = edit.UpdateProxyMode
        };
        store.Save(updated, content ?? store.ReadContent(overrideId));
        AppLogger.Info($"Override metadata saved: {edit.Name}");
        return updated;
    }
}

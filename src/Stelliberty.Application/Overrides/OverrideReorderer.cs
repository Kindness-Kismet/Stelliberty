namespace Stelliberty.Application.Overrides;

public sealed class OverrideReorderer(IOverrideStore store)
{
    // 跳过临时行，只用已保存的覆写确定顺序。
    public void SaveOrder(IReadOnlyList<string> orderedIds)
    {
        var overridesById = store.LoadOverrides().ToDictionary(item => item.Id, StringComparer.Ordinal);
        store.SaveOverrides(orderedIds.Where(overridesById.ContainsKey).Select(id => overridesById[id]).ToList());
    }
}

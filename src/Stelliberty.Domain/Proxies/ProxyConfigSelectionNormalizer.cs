namespace Stelliberty.Domain.Proxies;

public static class ProxyConfigSelectionNormalizer
{
    public static ProxyConfig EnsureManualSelections(ProxyConfig config)
    {
        var entryNames = BuildEntryNames(config);
        var groups = config.Groups
            .Select(group => EnsureGroupSelection(group, entryNames))
            .ToList();

        return config with { Groups = groups };
    }

    public static bool HasEntry(ProxyConfig config, string name)
    {
        return config.Nodes.ContainsKey(name)
            || config.Groups.Any(group => string.Equals(group.Name, name, StringComparison.Ordinal));
    }

    private static ProxyGroup EnsureGroupSelection(ProxyGroup group, ISet<string> entryNames)
    {
        if (!group.IsManualSelectable)
        {
            return group;
        }

        if (group.UsesFixedSelection)
        {
            if (IsValidSelection(group, group.Fixed, entryNames))
            {
                return group;
            }

            return group with { Fixed = null };
        }

        if (IsValidSelection(group, group.Now, entryNames))
        {
            return group;
        }

        if (!group.RequiresDefaultSelection)
        {
            return group;
        }

        var fallback = group.All.FirstOrDefault(entryNames.Contains);
        return fallback is null ? group : group with { Now = fallback };
    }

    private static bool IsValidSelection(ProxyGroup group, string? name, ISet<string> entryNames)
    {
        return !string.IsNullOrWhiteSpace(name)
            && group.All.Contains(name, StringComparer.Ordinal)
            && entryNames.Contains(name);
    }

    private static HashSet<string> BuildEntryNames(ProxyConfig config)
    {
        var names = new HashSet<string>(config.Nodes.Keys, StringComparer.Ordinal);
        foreach (var group in config.Groups)
        {
            names.Add(group.Name);
        }

        return names;
    }
}

namespace Stelliberty.Domain.Proxies;

public sealed class ProxyGroupSelector(ProxyConfig config)
{
    public ProxySelectionResult Select(string groupName, string proxyName)
    {
        var group = config.Groups.FirstOrDefault(item => item.Name == groupName)
            ?? throw new InvalidOperationException($"Proxy group does not exist: {groupName}");
        if (!group.IsManualSelectable)
        {
            throw new InvalidOperationException($"Proxy group does not support manual selection: {groupName}");
        }

        if (!ProxyConfigSelectionNormalizer.HasEntry(config, proxyName))
        {
            throw new InvalidOperationException($"Proxy entry does not exist: {proxyName}");
        }

        if (!group.All.Contains(proxyName))
        {
            throw new InvalidOperationException($"Proxy entry is not part of the group: {proxyName}");
        }

        var groups = config.Groups
            .Select(item => item.Name == groupName ? SelectGroupEntry(item, proxyName) : item)
            .ToList();
        return new ProxySelectionResult(
            config with { Groups = groups },
            new ProxyChangeRequest(groupName, proxyName),
            ShouldCloseConnections: true);
    }

    private static ProxyGroup SelectGroupEntry(ProxyGroup group, string proxyName)
    {
        return group.UsesFixedSelection
            ? group with { Fixed = proxyName }
            : group with { Now = proxyName };
    }
}

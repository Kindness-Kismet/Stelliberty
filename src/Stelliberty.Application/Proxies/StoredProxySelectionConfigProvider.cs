using Stelliberty.Application.Subscriptions;
using Stelliberty.Domain.Proxies;

namespace Stelliberty.Application.Proxies;

public sealed class StoredProxySelectionConfigProvider(
    IProxyConfigProvider inner,
    IProxySelectionStore selectionStore,
    ISubscriptionSelectionStore subscriptionSelectionStore,
    ProxySelectionSyncState? syncState = null,
    bool importCoreSelections = false) : IProxyConfigProvider, IProxyRuntimeSnapshotSource
{
    public ProxyRuntimeSnapshot? LastSnapshot => (inner as IProxyRuntimeSnapshotSource)?.LastSnapshot;

    public async Task<ProxyConfig> LoadAsync(CancellationToken cancellationToken = default)
    {
        var config = await inner.LoadAsync(cancellationToken);
        var subscriptionId = subscriptionSelectionStore.GetCurrentSubscriptionId();
        if (string.IsNullOrWhiteSpace(subscriptionId))
        {
            return ApplyStoredSelections(config, new Dictionary<string, string>(StringComparer.Ordinal));
        }

        var selections = new Dictionary<string, string>(
            selectionStore.GetSelections(subscriptionId),
            StringComparer.Ordinal);
        // 导入会删除核心侧查不到的固定选择，过渡期快照会误删用户实际有效的选择。
        if (importCoreSelections && syncState?.CanImportCoreSelections == true && config.IsFullyResolved)
        {
            ImportCoreSelections(config, selections, subscriptionId);
        }

        return ApplyStoredSelections(config, selections);
    }

    public ProxyConfig ApplyStoredSelections(ProxyConfig config, string subscriptionId)
    {
        return ApplyStoredSelections(
            config,
            selectionStore.GetSelections(subscriptionId));
    }

    public void PruneInvalidStoredSelections(ProxyConfig config, string subscriptionId)
    {
        // 快照不完整时无法区分“节点已删除”和“候选项还没解析出来”。
        if (!config.IsFullyResolved)
        {
            return;
        }

        var selections = new Dictionary<string, string>(
            selectionStore.GetSelections(subscriptionId),
            StringComparer.Ordinal);
        RemoveInvalidStoredSelections(config, selections, subscriptionId);
    }

    private void RemoveInvalidStoredSelections(
        ProxyConfig config,
        Dictionary<string, string> selections,
        string subscriptionId)
    {
        var entryNames = BuildEntryNames(config);
        var groupsByName = config.Groups.ToDictionary(group => group.Name, StringComparer.Ordinal);
        foreach (var (groupName, proxyName) in selections.ToList())
        {
            if (!groupsByName.TryGetValue(groupName, out var group)
                || !group.IsManualSelectable
                || ValidSelectionOrNull(group, proxyName, entryNames) is null)
            {
                selections.Remove(groupName);
                selectionStore.RemoveSelection(subscriptionId, groupName);
            }
        }
    }

    private static ProxyConfig ApplyStoredSelections(
        ProxyConfig config,
        IReadOnlyDictionary<string, string> selections)
    {
        var entryNames = BuildEntryNames(config);
        var groups = config.Groups
            .Select(group => ApplyGroupSelection(group, selections, entryNames))
            .ToList();

        return config with { Groups = groups };
    }

    private void ImportCoreSelections(
        ProxyConfig config,
        Dictionary<string, string> selections,
        string subscriptionId)
    {
        var entryNames = BuildEntryNames(config);
        foreach (var group in config.Groups)
        {
            if (!group.IsManualSelectable)
            {
                continue;
            }

            // 固定选择不写入存储；存储里若有记录，只可能来自早期版本。
            if (group.UsesFixedSelection)
            {
                if (selections.Remove(group.Name))
                {
                    selectionStore.RemoveSelection(subscriptionId, group.Name);
                }

                continue;
            }

            var coreSelection = ValidSelectionOrNull(group, group.Now, entryNames);
            if (coreSelection is null)
            {
                continue;
            }

            var expectedSelection = ResolveSelection(group, selections, entryNames);
            if (string.Equals(coreSelection, expectedSelection, StringComparison.Ordinal))
            {
                continue;
            }

            selections[group.Name] = coreSelection;
            selectionStore.SetSelection(subscriptionId, group.Name, coreSelection);
        }
    }

    private static ProxyGroup ApplyGroupSelection(
        ProxyGroup group,
        IReadOnlyDictionary<string, string> selections,
        ISet<string> entryNames)
    {
        if (!group.IsManualSelectable)
        {
            return group;
        }

        if (group.UsesFixedSelection)
        {
            return group with { Fixed = ValidSelectionOrNull(group, group.Fixed, entryNames) };
        }

        var proxyName = ResolveSelection(group, selections, entryNames);
        return string.IsNullOrWhiteSpace(proxyName) ? group with { Now = null } : group with { Now = proxyName };
    }

    private static string? ResolveStoredSelection(
        ProxyGroup group,
        IReadOnlyDictionary<string, string> selections,
        ISet<string> entryNames)
    {
        return selections.TryGetValue(group.Name, out var proxyName)
            ? ValidSelectionOrNull(group, proxyName, entryNames)
            : null;
    }

    private static string? ResolveSelection(
        ProxyGroup group,
        IReadOnlyDictionary<string, string> selections,
        ISet<string> entryNames)
    {
        var storedSelection = ResolveStoredSelection(group, selections, entryNames);
        if (storedSelection is not null)
        {
            return storedSelection;
        }

        if (!group.IsManualSelectable)
        {
            return null;
        }

        var defaultProxyName = group.All.FirstOrDefault(entryNames.Contains);
        return string.IsNullOrWhiteSpace(defaultProxyName) ? null : defaultProxyName;
    }

    private static string? ValidSelectionOrNull(
        ProxyGroup group,
        string? proxyName,
        ISet<string> entryNames)
    {
        return !string.IsNullOrWhiteSpace(proxyName)
            && group.All.Contains(proxyName, StringComparer.Ordinal)
            && entryNames.Contains(proxyName)
            ? proxyName
            : null;
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

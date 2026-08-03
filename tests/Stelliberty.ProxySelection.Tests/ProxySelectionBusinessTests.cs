using Stelliberty.Application.Connections;
using Stelliberty.Application.CoreLogs;
using Stelliberty.Application.Proxies;
using Stelliberty.Application.Runtime;
using Stelliberty.Application.Subscriptions;
using Stelliberty.Domain.CoreLogs;
using Stelliberty.Domain.Connections;
using Stelliberty.Domain.Proxies;
using Xunit;

namespace Stelliberty.ProxySelection.Tests;

public sealed class ProxySelectionBusinessTests
{
    [Fact(DisplayName = "Proxy group types define manual selection semantics")]
    public void ProxyGroupTypesDefineManualSelectionSemantics()
    {
        Assert.True(ProxyGroupTypes.IsManualSelectable("select"));
        Assert.True(ProxyGroupTypes.IsManualSelectable("selector"));
        Assert.True(ProxyGroupTypes.IsManualSelectable("url-test"));
        Assert.True(ProxyGroupTypes.IsManualSelectable("fallback"));
        Assert.False(ProxyGroupTypes.IsManualSelectable("load-balance"));
        Assert.False(ProxyGroupTypes.IsManualSelectable("relay"));
        Assert.True(ProxyGroupTypes.UsesFixedSelection("url-test"));
        Assert.True(ProxyGroupTypes.UsesFixedSelection("fallback"));
        Assert.False(ProxyGroupTypes.UsesFixedSelection("select"));
    }

    [Fact(DisplayName = "Normalizer defaults select groups and clears invalid fixed selections")]
    public void NormalizerDefaultsSelectGroupsAndClearsInvalidFixedSelections()
    {
        var config = TestConfig(
        [
            new ProxyGroup("Auto", ProxyGroupTypes.UrlTest, "NodeA", ["NodeA", "NodeB"], "Missing"),
            new ProxyGroup("Main", ProxyGroupTypes.Select, "Missing", ["NodeA", "NodeB"]),
            new ProxyGroup("Relay", "relay", "NodeA", ["NodeA", "NodeB"])
        ]);

        var normalized = ProxyConfigSelectionNormalizer.EnsureManualSelections(config);

        Assert.Null(normalized.Groups[0].Fixed);
        Assert.Equal("NodeA", normalized.Groups[0].DisplaySelectionName);
        Assert.Equal("NodeA", normalized.Groups[1].Now);
        Assert.Equal("NodeA", normalized.Groups[2].Now);
    }

    [Fact(DisplayName = "Selector writes now or fixed by group type")]
    public void SelectorWritesNowOrFixedByGroupType()
    {
        var config = TestConfig(
        [
            new ProxyGroup("Main", ProxyGroupTypes.Select, "NodeA", ["NodeA", "NodeB"]),
            new ProxyGroup("Auto", ProxyGroupTypes.UrlTest, "NodeA", ["NodeA", "NodeB"])
        ]);
        var selector = new ProxyGroupSelector(config);

        var selectResult = selector.Select("Main", "NodeB");
        var fixedResult = selector.Select("Auto", "NodeB");

        Assert.Equal("NodeB", selectResult.Config.Groups[0].Now);
        Assert.Equal("NodeB", fixedResult.Config.Groups[1].Fixed);
        Assert.Equal("NodeA", fixedResult.Config.Groups[1].Now);
        Assert.True(selectResult.ShouldCloseConnections);
    }

    [Fact(DisplayName = "Selector rejects unsupported groups and foreign nodes")]
    public void SelectorRejectsUnsupportedGroupsAndForeignNodes()
    {
        var config = TestConfig(
        [
            new ProxyGroup("Main", ProxyGroupTypes.Select, "NodeA", ["NodeA"]),
            new ProxyGroup("Balance", ProxyGroupTypes.LoadBalance, "NodeA", ["NodeA", "NodeB"])
        ]);
        var selector = new ProxyGroupSelector(config);

        Assert.Throws<InvalidOperationException>(() => selector.Select("Balance", "NodeB"));
        Assert.Throws<InvalidOperationException>(() => selector.Select("Main", "NodeB"));
        Assert.Throws<InvalidOperationException>(() => selector.Select("Missing", "NodeA"));
    }

    [Fact(DisplayName = "Stored provider uses local selections before core selections")]
    public async Task StoredProviderUsesLocalSelectionsBeforeCoreSelections()
    {
        var inner = new FakeProxyConfigProvider(TestConfig(
        [
            new ProxyGroup("Main", ProxyGroupTypes.Select, "NodeA", ["NodeA", "NodeB"]),
            new ProxyGroup("Auto", ProxyGroupTypes.UrlTest, "NodeA", ["NodeA", "NodeB"], "NodeA")
        ]));
        var selectionStore = new FakeProxySelectionStore();
        var subscriptionStore = new FakeSubscriptionSelectionStore("sub-1");
        selectionStore.SetSelection("sub-1", "Main", "NodeB");
        var provider = new StoredProxySelectionConfigProvider(inner, selectionStore, subscriptionStore, importCoreSelections: true);

        var config = await provider.LoadAsync();

        Assert.Equal("NodeB", config.Groups[0].Now);
        Assert.Equal("NodeA", config.Groups[1].Fixed);
        Assert.Equal("NodeB", selectionStore.GetSelections("sub-1")["Main"]);
        Assert.False(selectionStore.GetSelections("sub-1").ContainsKey("Auto"));
    }

    [Fact(DisplayName = "Stored provider imports core selections only when import switch is enabled")]
    public async Task StoredProviderImportsCoreSelectionsOnlyWhenImportSwitchIsEnabled()
    {
        var inner = new FakeProxyConfigProvider(TestConfig(
        [
            new ProxyGroup("Main", ProxyGroupTypes.Select, "NodeB", ["NodeA", "NodeB"]),
            new ProxyGroup("Auto", ProxyGroupTypes.UrlTest, "NodeA", ["NodeA", "NodeB"], "NodeB")
        ]));
        var selectionStore = new FakeProxySelectionStore();
        var subscriptionStore = new FakeSubscriptionSelectionStore("sub-1");
        var syncState = new ProxySelectionSyncState();

        var blockedProvider = new StoredProxySelectionConfigProvider(inner, selectionStore, subscriptionStore, syncState, importCoreSelections: true);
        await blockedProvider.LoadAsync();
        Assert.Empty(selectionStore.GetSelections("sub-1"));

        syncState.EnableCoreSelectionImport();
        var importingProvider = new StoredProxySelectionConfigProvider(inner, selectionStore, subscriptionStore, syncState, importCoreSelections: true);
        var imported = await importingProvider.LoadAsync();

        Assert.Equal("NodeB", imported.Groups[0].Now);
        Assert.Equal("NodeB", imported.Groups[1].Fixed);
        Assert.Equal("NodeB", selectionStore.GetSelections("sub-1")["Main"]);
        Assert.Equal("NodeB", selectionStore.GetSelections("sub-1")["Auto"]);
    }

    [Fact(DisplayName = "Stored provider does not import selections without current subscription")]
    public async Task StoredProviderDoesNotImportSelectionsWithoutCurrentSubscription()
    {
        var inner = new FakeProxyConfigProvider(TestConfig(
        [
            new ProxyGroup("Main", ProxyGroupTypes.Select, "NodeB", ["NodeA", "NodeB"]),
            new ProxyGroup("Auto", ProxyGroupTypes.UrlTest, "NodeA", ["NodeA", "NodeB"], "NodeB")
        ]));
        var selectionStore = new FakeProxySelectionStore();
        var syncState = new ProxySelectionSyncState();
        syncState.EnableCoreSelectionImport();
        var provider = new StoredProxySelectionConfigProvider(
            inner,
            selectionStore,
            new FakeSubscriptionSelectionStore(null),
            syncState,
            importCoreSelections: true);

        var config = await provider.LoadAsync();

        Assert.Equal("NodeA", config.Groups[0].Now);
        Assert.Equal("NodeB", config.Groups[1].Fixed);
        Assert.Empty(selectionStore.AllSubscriptionIds);
    }

    [Fact(DisplayName = "Stored provider removes stored selection when node leaves group")]
    public async Task StoredProviderRemovesStoredSelectionWhenNodeLeavesGroup()
    {
        var inner = new FakeProxyConfigProvider(TestConfig(
        [
            new ProxyGroup("Main", ProxyGroupTypes.Select, "NodeA", ["NodeA"])
        ]));
        var selectionStore = new FakeProxySelectionStore();
        var subscriptionStore = new FakeSubscriptionSelectionStore("sub-1");
        selectionStore.SetSelection("sub-1", "Main", "NodeB");
        var provider = new StoredProxySelectionConfigProvider(inner, selectionStore, subscriptionStore);

        var config = await provider.LoadAsync();

        Assert.Equal("NodeA", config.Groups[0].Now);
        Assert.Empty(selectionStore.GetSelections("sub-1"));
    }

    [Fact(DisplayName = "Stored provider removes stored selection when nested group leaves config")]
    public async Task StoredProviderRemovesStoredSelectionWhenNestedGroupLeavesConfig()
    {
        var inner = new FakeProxyConfigProvider(TestConfig(
        [
            new ProxyGroup("Main", ProxyGroupTypes.Select, "NodeA", ["GroupA", "NodeA"])
        ]));
        var selectionStore = new FakeProxySelectionStore();
        var subscriptionStore = new FakeSubscriptionSelectionStore("sub-1");
        selectionStore.SetSelection("sub-1", "Main", "GroupA");
        var provider = new StoredProxySelectionConfigProvider(inner, selectionStore, subscriptionStore);

        var config = await provider.LoadAsync();

        Assert.Equal("NodeA", config.Groups[0].Now);
        Assert.Empty(selectionStore.GetSelections("sub-1"));
    }

    [Fact(DisplayName = "Stored provider removes stored fixed selection when core fixed selection is empty")]
    public async Task StoredProviderRemovesStoredFixedSelectionWhenCoreFixedSelectionIsEmpty()
    {
        var inner = new FakeProxyConfigProvider(TestConfig(
        [
            new ProxyGroup("Auto", ProxyGroupTypes.UrlTest, "NodeA", ["NodeA", "NodeB"])
        ]));
        var selectionStore = new FakeProxySelectionStore();
        var subscriptionStore = new FakeSubscriptionSelectionStore("sub-1");
        var syncState = new ProxySelectionSyncState();
        syncState.EnableCoreSelectionImport();
        selectionStore.SetSelection("sub-1", "Auto", "NodeB");
        var provider = new StoredProxySelectionConfigProvider(inner, selectionStore, subscriptionStore, syncState, importCoreSelections: true);

        var config = await provider.LoadAsync();

        Assert.Null(config.Groups[0].Fixed);
        Assert.Equal("NodeA", config.Groups[0].DisplaySelectionName);
        Assert.Empty(selectionStore.GetSelections("sub-1"));
    }

    [Fact(DisplayName = "Selection service does not persist when core rejects change")]
    public async Task SelectionServiceDoesNotPersistWhenCoreRejectsChange()
    {
        var core = new FakeProxyCoreClient { ChangeResult = false };
        var selections = new FakeProxySelectionStore();
        var subscriptions = new FakeSubscriptionSelectionStore("sub-1");
        var service = new ProxySelectionService(core, selections, subscriptions);
        var config = TestConfig([new ProxyGroup("Main", ProxyGroupTypes.Select, "NodeA", ["NodeA", "NodeB"])]);

        var result = await service.SelectNodeAsync(config, "Main", "NodeB", applyToCore: true);

        Assert.Null(result);
        Assert.Empty(selections.GetSelections("sub-1"));
        Assert.Equal(0, core.CloseConnectionCount);
    }

    [Fact(DisplayName = "Selection service persists and closes connections after core accepts change")]
    public async Task SelectionServicePersistsAndClosesConnectionsAfterCoreAcceptsChange()
    {
        var core = new FakeProxyCoreClient { ChangeResult = true };
        var selections = new FakeProxySelectionStore();
        var subscriptions = new FakeSubscriptionSelectionStore("sub-1");
        var service = new ProxySelectionService(core, selections, subscriptions);
        var config = TestConfig([new ProxyGroup("Main", ProxyGroupTypes.Select, "NodeA", ["NodeA", "NodeB"])]);

        var result = await service.SelectNodeAsync(config, "Main", "NodeB", applyToCore: true);

        Assert.NotNull(result);
        Assert.Equal("NodeB", selections.GetSelections("sub-1")["Main"]);
        Assert.Equal(1, core.CloseConnectionCount);
        Assert.Equal(ConnectionCloseMode.All, core.LastCloseRequest?.Mode);
    }

    [Fact(DisplayName = "Core config apply restores the saved selection before core import resumes")]
    public async Task CoreConfigApplyRestoresSavedSelectionBeforeCoreImportResumes()
    {
        var configProvider = new FakeProxyConfigProvider(TestConfig(
        [
            new ProxyGroup("Main", ProxyGroupTypes.Select, "NodeA", ["NodeA", "NodeB"])
        ]));
        var coreClient = new FakeProxyCoreClient();
        var selections = new FakeProxySelectionStore();
        var subscriptions = new FakeSubscriptionSelectionStore("sub-1");
        var syncState = new ProxySelectionSyncState();
        syncState.EnableCoreSelectionImport();
        selections.SetSelection("sub-1", "Main", "NodeB");
        var storedProvider = new StoredProxySelectionConfigProvider(
            configProvider,
            selections,
            subscriptions,
            syncState,
            importCoreSelections: true);
        var restorer = new ProxySelectionRestorer(
            coreClient: coreClient,
            coreConfigProvider: configProvider,
            selectedRuntimeConfigProvider: configProvider,
            selectionProvider: storedProvider,
            syncState: syncState,
            subscriptionSelectionStore: subscriptions);
        var coreManager = new FakeCoreManager
        {
            ApplyHandler = request =>
            {
                Assert.False(syncState.CanImportCoreSelections);
                return Task.FromResult(new CoreApplyConfigResult(CoreApplyMode.Restart, 42));
            }
        };
        using var manager = new ProxySelectionRestoringCoreManager(coreManager, restorer);

        await manager.ApplyConfigAsync(new CoreApplyConfigRequest("runtime.yaml", "sub-1"));

        Assert.Contains(coreClient.ChangeRequests, request => request == new ProxyChangeRequest("Main", "NodeB"));
        Assert.Equal("NodeB", selections.GetSelections("sub-1")["Main"]);
        Assert.True(syncState.CanImportCoreSelections);
    }

    [Fact(DisplayName = "Core config apply does not restore another subscription after selection changes")]
    public async Task CoreConfigApplyDoesNotRestoreAnotherSubscriptionAfterSelectionChanges()
    {
        var configProvider = new FakeProxyConfigProvider(TestConfig(
        [
            new ProxyGroup("Main", ProxyGroupTypes.Select, "NodeA", ["NodeA", "NodeB"])
        ]));
        var coreClient = new FakeProxyCoreClient();
        var selections = new FakeProxySelectionStore();
        var subscriptions = new FakeSubscriptionSelectionStore("sub-1");
        var syncState = new ProxySelectionSyncState();
        syncState.EnableCoreSelectionImport();
        selections.SetSelection("sub-1", "Main", "NodeB");
        var storedProvider = new StoredProxySelectionConfigProvider(
            configProvider,
            selections,
            subscriptions,
            syncState,
            importCoreSelections: true);
        var restorer = new ProxySelectionRestorer(
            coreClient: coreClient,
            coreConfigProvider: configProvider,
            selectedRuntimeConfigProvider: configProvider,
            selectionProvider: storedProvider,
            syncState: syncState,
            subscriptionSelectionStore: subscriptions);
        var coreManager = new FakeCoreManager
        {
            ApplyHandler = request =>
            {
                subscriptions.SetCurrentSubscriptionId("sub-2");
                return Task.FromResult(new CoreApplyConfigResult(CoreApplyMode.Restart, 42));
            }
        };
        using var manager = new ProxySelectionRestoringCoreManager(coreManager, restorer);

        await manager.ApplyConfigAsync(new CoreApplyConfigRequest("runtime.yaml", "sub-1"));

        Assert.Empty(coreClient.ChangeRequests);
        Assert.Equal("NodeB", selections.GetSelections("sub-1")["Main"]);
        Assert.False(syncState.CanImportCoreSelections);
    }

    [Fact(DisplayName = "Failed core config apply keeps saved selections protected while core is unavailable")]
    public async Task FailedCoreConfigApplyKeepsSavedSelectionsProtectedWhileCoreIsUnavailable()
    {
        var configProvider = new FakeProxyConfigProvider(TestConfig(
        [
            new ProxyGroup("Main", ProxyGroupTypes.Select, "NodeA", ["NodeA", "NodeB"])
        ]));
        var coreClient = new FakeProxyCoreClient();
        var selections = new FakeProxySelectionStore();
        var subscriptions = new FakeSubscriptionSelectionStore("sub-1");
        var syncState = new ProxySelectionSyncState();
        syncState.EnableCoreSelectionImport();
        selections.SetSelection("sub-1", "Main", "NodeB");
        var storedProvider = new StoredProxySelectionConfigProvider(
            configProvider,
            selections,
            subscriptions,
            syncState,
            importCoreSelections: true);
        var restorer = new ProxySelectionRestorer(
            coreClient: coreClient,
            coreConfigProvider: configProvider,
            selectedRuntimeConfigProvider: configProvider,
            selectionProvider: storedProvider,
            syncState: syncState,
            subscriptionSelectionStore: subscriptions);
        var coreManager = new FakeCoreManager
        {
            Snapshot = new CoreSnapshot(CoreState.Crashed, null, string.Empty, "apply failed"),
            ApplyHandler = _ => throw new InvalidOperationException("apply failed")
        };
        using var manager = new ProxySelectionRestoringCoreManager(coreManager, restorer);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            manager.ApplyConfigAsync(new CoreApplyConfigRequest("runtime.yaml", "sub-1")));

        Assert.Empty(coreClient.ChangeRequests);
        Assert.Equal("NodeB", selections.GetSelections("sub-1")["Main"]);
        Assert.False(syncState.CanImportCoreSelections);
    }

    [Fact(DisplayName = "Stored provider keeps API-expanded include-all selection")]
    public async Task StoredProviderKeepsApiExpandedIncludeAllSelection()
    {
        var inner = new FakeProxyConfigProvider(TestConfig(
        [
            new ProxyGroup("基础选择", ProxyGroupTypes.Select, "直接连接", ["直接连接", "CoolTi US 1", "CoolTi JP 1"])
        ], nodes: new Dictionary<string, ProxyNode>(StringComparer.Ordinal)
        {
            ["CoolTi US 1"] = new("CoolTi US 1", "ss"),
            ["CoolTi JP 1"] = new("CoolTi JP 1", "ss"),
            ["直接连接"] = new("直接连接", "select")
        }));
        var selectionStore = new FakeProxySelectionStore();
        var subscriptionStore = new FakeSubscriptionSelectionStore("sub-1");
        selectionStore.SetSelection("sub-1", "基础选择", "CoolTi US 1");
        var provider = new StoredProxySelectionConfigProvider(inner, selectionStore, subscriptionStore);

        var config = await provider.LoadAsync();

        Assert.Equal("CoolTi US 1", config.Groups[0].Now);
        Assert.Equal("CoolTi US 1", selectionStore.GetSelections("sub-1")["基础选择"]);
    }

    [Fact(DisplayName = "Stored provider defers pruning until explicitly requested")]
    public async Task StoredProviderDefersPruningUntilExplicitlyRequested()
    {
        var inner = new FakeProxyConfigProvider(TestConfig(
        [
            new ProxyGroup("基础选择", ProxyGroupTypes.Select, "直接连接", ["直接连接"])
        ], nodes: new Dictionary<string, ProxyNode>(StringComparer.Ordinal)
        {
            ["CoolTi US 1"] = new("CoolTi US 1", "ss"),
            ["直接连接"] = new("直接连接", "select")
        }));
        var selectionStore = new FakeProxySelectionStore();
        var subscriptionStore = new FakeSubscriptionSelectionStore("sub-1");
        selectionStore.SetSelection("sub-1", "基础选择", "CoolTi US 1");
        var provider = new StoredProxySelectionConfigProvider(
            inner,
            selectionStore,
            subscriptionStore,
            pruneInvalidSelections: false);

        var config = await provider.LoadAsync();

        Assert.Equal("直接连接", config.Groups[0].Now);
        Assert.Equal("CoolTi US 1", selectionStore.GetSelections("sub-1")["基础选择"]);

        provider.PruneInvalidStoredSelections(config);

        Assert.Empty(selectionStore.GetSelections("sub-1"));
    }

    [Fact(DisplayName = "Stored provider keeps selections while config is empty")]
    public async Task StoredProviderKeepsSelectionsWhileConfigIsEmpty()
    {
        var inner = new FakeProxyConfigProvider(new ProxyConfig(
            [],
            new Dictionary<string, ProxyNode>(StringComparer.Ordinal)));
        var selectionStore = new FakeProxySelectionStore();
        var subscriptionStore = new FakeSubscriptionSelectionStore("sub-1");
        selectionStore.SetSelection("sub-1", "基础选择", "CoolTi US 1");
        var provider = new StoredProxySelectionConfigProvider(
            inner,
            selectionStore,
            subscriptionStore,
            pruneInvalidSelections: false);

        var config = await provider.LoadAsync();
        provider.PruneInvalidStoredSelections(config);

        Assert.Equal("CoolTi US 1", selectionStore.GetSelections("sub-1")["基础选择"]);
    }

    [Fact(DisplayName = "Visible groups follow outbound mode")]
    public void VisibleGroupsFollowOutboundMode()
    {
        var groups = new[]
        {
            new ProxyGroup("GLOBAL", ProxyGroupTypes.Select, "NodeA", ["NodeA"]),
            new ProxyGroup("Main", ProxyGroupTypes.Select, "NodeA", ["NodeA"]),
            new ProxyGroup("Hidden", ProxyGroupTypes.Select, "NodeA", ["NodeA"], IsHidden: true)
        };

        Assert.Equal(["Main"], TestConfig(groups, OutboundMode.Rule).VisibleGroups.Select(group => group.Name));
        Assert.Equal(["GLOBAL"], TestConfig(groups, OutboundMode.Global).VisibleGroups.Select(group => group.Name));
        Assert.Empty(TestConfig(groups, OutboundMode.Direct).VisibleGroups);
    }

    private static ProxyConfig TestConfig(
        IReadOnlyList<ProxyGroup> groups,
        OutboundMode? mode = null,
        IReadOnlyDictionary<string, ProxyNode>? nodes = null)
    {
        return new ProxyConfig(
            groups,
            nodes ?? new Dictionary<string, ProxyNode>(StringComparer.Ordinal)
            {
                ["NodeA"] = new("NodeA", "ss"),
                ["NodeB"] = new("NodeB", "ss")
            },
            mode);
    }

    private sealed class FakeProxyConfigProvider(ProxyConfig config) : IProxyConfigProvider
    {
        public Task<ProxyConfig> LoadAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(config);
        }
    }

    private sealed class FakeProxySelectionStore : IProxySelectionStore
    {
        private readonly Dictionary<string, Dictionary<string, string>> _items = new(StringComparer.Ordinal);

        public IReadOnlyList<string> AllSubscriptionIds => _items.Keys.ToList();

        public IReadOnlyDictionary<string, string> GetSelections(string subscriptionId)
        {
            return _items.TryGetValue(subscriptionId, out var selections)
                ? new Dictionary<string, string>(selections, StringComparer.Ordinal)
                : new Dictionary<string, string>(StringComparer.Ordinal);
        }

        public void SetSelection(string subscriptionId, string groupName, string proxyName)
        {
            if (!_items.TryGetValue(subscriptionId, out var selections))
            {
                selections = new Dictionary<string, string>(StringComparer.Ordinal);
                _items[subscriptionId] = selections;
            }

            selections[groupName] = proxyName;
        }

        public void RemoveSelection(string subscriptionId, string groupName)
        {
            if (_items.TryGetValue(subscriptionId, out var selections))
            {
                selections.Remove(groupName);
            }
        }
    }

    private sealed class FakeSubscriptionSelectionStore(string? subscriptionId) : ISubscriptionSelectionStore
    {
        private string? _subscriptionId = subscriptionId;

        public string? GetCurrentSubscriptionId() => _subscriptionId;

        public void SetCurrentSubscriptionId(string? subscriptionId)
        {
            _subscriptionId = subscriptionId;
        }
    }

    private sealed class FakeProxyCoreClient : IProxyCoreClient
    {
        public bool ChangeResult { get; init; } = true;
        public int CloseConnectionCount { get; private set; }
        public ConnectionCloseRequest? LastCloseRequest { get; private set; }
        public List<ProxyChangeRequest> ChangeRequests { get; } = [];

        public Task<IReadOnlyList<ConnectionInfo>?> GetConnectionsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<ConnectionInfo>?>([]);
        }

        public Task<bool> ChangeProxyAsync(ProxyChangeRequest request, CancellationToken cancellationToken = default)
        {
            ChangeRequests.Add(request);
            return Task.FromResult(ChangeResult);
        }

        public Task<bool> ClearProxySelectionAsync(string groupName, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }

        public Task<bool> CloseConnectionsAsync(ConnectionCloseRequest request, CancellationToken cancellationToken = default)
        {
            CloseConnectionCount++;
            LastCloseRequest = request;
            return Task.FromResult(true);
        }

        public Task<ProxyRuntimeSnapshot> GetProxiesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ProxyRuntimeSnapshot([]));
        }

        public Task<OutboundMode?> GetOutboundModeAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<OutboundMode?>(null);
        }

        public Task<bool> SetOutboundModeAsync(OutboundMode mode, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }

        public Task<string?> GetVersionAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<string?>(null);
        }

        public Task<CoreRuntimeStats?> GetRuntimeStatsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<CoreRuntimeStats?>(null);
        }

        public Task<CoreTrafficRate?> GetTrafficAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<CoreTrafficRate?>(null);
        }
    }

    private sealed class FakeCoreManager : ICoreManager
    {
        public event EventHandler<CoreSnapshot>? StateChanged
        {
            add { }
            remove { }
        }

        public event EventHandler<CoreLogMessage>? CoreLogReceived
        {
            add { }
            remove { }
        }

        public CoreSnapshot Snapshot { get; init; } = new(CoreState.Running, 42, string.Empty, null);
        public Func<CoreApplyConfigRequest, Task<CoreApplyConfigResult>>? ApplyHandler { get; init; }

        public Task<CoreSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Snapshot);
        }

        public Task<CoreApplyConfigResult> ApplyConfigAsync(
            CoreApplyConfigRequest request,
            CancellationToken cancellationToken = default)
        {
            return ApplyHandler?.Invoke(request)
                ?? Task.FromResult(new CoreApplyConfigResult(CoreApplyMode.Reload, Snapshot.Pid ?? 0));
        }

        public Task RestartAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}

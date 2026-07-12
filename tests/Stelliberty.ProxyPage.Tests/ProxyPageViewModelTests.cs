using Stelliberty.Application.Connections;
using Stelliberty.Application.Proxies;
using Stelliberty.Domain.Connections;
using Stelliberty.Domain.Proxies;
using Stelliberty.Presentation.ViewModels;
using Xunit;

namespace Stelliberty.ProxyPage.Tests;

public sealed class ProxyPageViewModelTests
{
    private static readonly TimeSpan AsyncTestTimeout = TimeSpan.FromSeconds(2);

    [Fact(DisplayName = "Home statistics require parsing and successful delay tests")]
    public async Task HomeStatisticsRequireParsingAndSuccessfulDelayTests()
    {
        var tester = new FakeProxyDelayTester(new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["JP"] = 40,
            ["KR"] = 60
        });
        var page = new ProxyPageViewModel(delayService: new ProxyDelayService(tester));

        Assert.Null(page.ParsedGroupCount);
        Assert.Null(page.ParsedNodeCount);
        Assert.Null(page.TestedAverageDelay);

        page.LoadConfig(SampleConfig(), subscriptionId: "sub-1");

        Assert.Equal(SampleConfig().Groups.Count, page.ParsedGroupCount);
        Assert.Equal(SampleConfig().Nodes.Count, page.ParsedNodeCount);
        Assert.Equal("sub-1", page.LoadedSubscriptionId);
        Assert.Null(page.TestedAverageDelay);

        await page.TestGroupDelaysAsync("Select");

        Assert.Equal(50, page.TestedAverageDelay);
    }

    [Fact(DisplayName = "Outbound mode rebuilds visible groups")]
    public void OutboundModeRebuildsVisibleGroups()
    {
        var page = new ProxyPageViewModel();
        page.LoadConfig(SampleConfig());

        Assert.Equal(["Auto", "Fallback", "Select", "Balance"], page.VisibleGroups.Select(group => group.Name));

        page.SetOutboundMode(OutboundMode.Global);
        Assert.Equal(["GLOBAL"], page.VisibleGroups.Select(group => group.Name));

        page.SetOutboundMode(OutboundMode.Direct);
        Assert.Empty(page.VisibleGroups);
        Assert.Null(page.SelectedGroup);
        Assert.True(page.IsEmptyVisible);
    }

    [Fact(DisplayName = "Load config clears transient proxy page state")]
    public async Task LoadConfigClearsTransientProxyPageState()
    {
        var page = new ProxyPageViewModel();
        page.LoadConfig(SampleConfig(), shouldChangeCoreOnSelection: false, shouldTestDelaysThroughService: false);
        page.SelectGroup("Select");
        await page.SelectNodeAsync("KR");
        page.SearchKeyword = "trojan";
        page.LocateSelectedNodeCommand.Execute(null);
        page.ScrollToTopCommand.Execute(null);
        await page.TestNodeDelayAsync("KR");

        Assert.Equal("KR", page.LastSelectedNodeName);
        Assert.Equal("KR", page.LocatedNodeName);
        Assert.Contains("KR", page.DelayTestedNodeNames);
        Assert.True(page.HasScrolledToTop);

        page.LoadConfig(SampleConfig());

        Assert.Equal("", page.SearchKeyword);
        Assert.Null(page.LastSelectedNodeName);
        Assert.Null(page.LocatedNodeName);
        Assert.Null(page.LastChangeRequest);
        Assert.Empty(page.DelayTestedNodeNames);
        Assert.Empty(page.BatchDelayTestedNodeNames);
        Assert.False(page.HasScrolledToTop);
        Assert.Equal(0, page.ScrollToTopRequestId);
        Assert.Equal("Auto", page.SelectedGroup?.Name);
    }

    [Fact(DisplayName = "Load config keeps current outbound mode when snapshot has no mode")]
    public void LoadConfigKeepsCurrentOutboundModeWhenSnapshotHasNoMode()
    {
        var page = new ProxyPageViewModel();
        page.LoadConfig(SampleConfig());
        page.SetOutboundMode(OutboundMode.Global);

        page.LoadConfig(SampleConfig() with { Mode = null });

        Assert.Equal(OutboundMode.Global, page.OutboundMode);
        Assert.Equal(["GLOBAL"], page.VisibleGroups.Select(group => group.Name));
        Assert.Equal("GLOBAL", page.SelectedGroup?.Name);

        page.LoadConfig(SampleConfig() with { Mode = OutboundMode.Rule });

        Assert.Equal(OutboundMode.Rule, page.OutboundMode);
        Assert.Equal(["Auto", "Fallback", "Select", "Balance"], page.VisibleGroups.Select(group => group.Name));
        Assert.Equal("Auto", page.SelectedGroup?.Name);
    }

    [Fact(DisplayName = "Selecting a node in a select group applies core state and closes connections")]
    public async Task SelectNodeInSelectGroupAppliesCoreAndClosesConnections()
    {
        var core = new FakeProxyCoreClient();
        var page = new ProxyPageViewModel(coreClient: core, selectionService: new ProxySelectionService(core));
        var closed = false;
        page.NodeSelectionClosedConnections += (_, _) => closed = true;
        page.LoadConfig(SampleConfig());
        page.SelectGroup("Select");

        await page.SelectNodeAsync("KR");

        Assert.Equal("KR", page.SelectedGroup?.DisplaySelectionName);
        Assert.Equal(new ProxyChangeRequest("Select", "KR"), page.LastChangeRequest);
        Assert.True(page.ShouldCloseConnectionsAfterSelection);
        Assert.True(closed);
        Assert.Equal(new ProxyChangeRequest("Select", "KR"), Assert.Single(core.ChangeRequests));
        Assert.Equal(ConnectionCloseMode.All, Assert.Single(core.CloseRequests).Mode);
    }

    [Fact(DisplayName = "Selecting a node in fixed groups updates the fixed selection")]
    public async Task SelectNodeInFixedGroupsUpdatesFixedSelection()
    {
        var page = new ProxyPageViewModel();
        page.LoadConfig(SampleConfig(), shouldChangeCoreOnSelection: false);

        page.SelectGroup("Auto");
        await page.SelectNodeAsync("JP");

        Assert.Equal("JP", page.SelectedGroup?.Fixed);
        Assert.True(page.VisibleNodeRows.Single(row => row.Name == "JP").IsSelected);
        Assert.False(page.VisibleNodeRows.Single(row => row.Name == "KR").IsSelected);

        page.SelectGroup("Fallback");
        await page.SelectNodeAsync("KR");

        Assert.Equal("KR", page.SelectedGroup?.Fixed);
    }

    [Fact(DisplayName = "Selecting a node in a non-manual group is ignored")]
    public async Task SelectNodeInNonManualGroupIsIgnored()
    {
        var core = new FakeProxyCoreClient();
        var page = new ProxyPageViewModel(coreClient: core);
        page.LoadConfig(SampleConfig());
        page.SelectGroup("Balance");

        await page.SelectNodeAsync("KR");

        Assert.Null(page.LastChangeRequest);
        Assert.Empty(core.ChangeRequests);
        Assert.Equal("JP", page.SelectedGroup?.DisplaySelectionName);
    }

    [Fact(DisplayName = "Sync external selections applies latest primary snapshot")]
    public async Task SyncExternalSelectionsAppliesLatestPrimarySnapshot()
    {
        var config = SampleConfig();
        var synced = config with
        {
            Groups = config.Groups
                .Select(group => group.Name == "Select" ? group with { Now = "US" } : group)
                .ToList()
        };
        var provider = new FakeProxyConfigProvider(synced);
        var page = new ProxyPageViewModel(primaryConfigProvider: provider);
        page.LoadConfig(config);
        page.SelectGroup("Select");

        await page.SyncExternalSelectionsAsync();

        Assert.Equal("US", page.SelectedGroup?.DisplaySelectionName);
        Assert.True(page.VisibleNodeRows.Single(row => row.Name == "US").IsSelected);
    }

    [Fact(DisplayName = "Sync external selections applies fixed group selection")]
    public async Task SyncExternalSelectionsAppliesFixedGroupSelection()
    {
        var config = SampleConfig();
        var synced = config with
        {
            Groups = config.Groups
                .Select(group => group.Name == "Auto" ? group with { Fixed = "JP" } : group)
                .ToList()
        };
        var provider = new FakeProxyConfigProvider(synced);
        var page = new ProxyPageViewModel(primaryConfigProvider: provider);
        page.LoadConfig(config);
        page.SelectGroup("Auto");

        await page.SyncExternalSelectionsAsync();

        Assert.Equal("JP", page.SelectedGroup?.Fixed);
        Assert.Equal("JP", page.SelectedGroup?.DisplaySelectionName);
        Assert.True(page.VisibleNodeRows.Single(row => row.Name == "JP").IsSelected);
        Assert.True(page.VisibleNodeRows.Single(row => row.Name == "JP").IsClickable);
    }

    [Fact(DisplayName = "Url-test group follows runtime automatic switch when now changes")]
    public async Task UrlTestGroupFollowsRuntimeAutomaticSwitchWhenNowChanges()
    {
        var config = TestConfig(
        [
            new ProxyGroup("Auto", ProxyGroupTypes.UrlTest, "JP", ["JP", "KR"])
        ]);
        var synced = config with
        {
            Groups = config.Groups
                .Select(group => group.Name == "Auto" ? group with { Now = "KR" } : group)
                .ToList()
        };
        var provider = new FakeProxyConfigProvider(synced);
        var page = new ProxyPageViewModel(primaryConfigProvider: provider);

        page.LoadConfig(config);
        page.SelectGroup("Auto");

        await page.SyncExternalSelectionsAsync();

        Assert.Equal("KR", page.SelectedGroup?.DisplaySelectionName);
        Assert.True(page.VisibleNodeRows.Single(row => row.Name == "KR").IsSelected);
        Assert.False(page.VisibleNodeRows.Single(row => row.Name == "JP").IsSelected);
    }

    [Fact(DisplayName = "Url-test group without fixed selection highlights runtime selected node")]
    public void UrlTestGroupWithoutFixedSelectionHighlightsRuntimeSelectedNode()
    {
        var config = TestConfig(
        [
            new ProxyGroup("Auto", ProxyGroupTypes.UrlTest, "KR", ["JP", "KR"])
        ]);
        var page = new ProxyPageViewModel();

        page.LoadConfig(config);

        Assert.Equal("Auto", page.SelectedGroup?.Name);
        Assert.Null(page.SelectedGroup?.Fixed);
        Assert.Equal("KR", page.SelectedGroup?.DisplaySelectionName);
        Assert.False(page.VisibleNodeRows.Single(row => row.Name == "JP").IsSelected);
        Assert.True(page.VisibleNodeRows.Single(row => row.Name == "KR").IsSelected);
    }

    [Fact(DisplayName = "Refresh falls back to runtime config when primary proxy snapshot fails")]
    public async Task RefreshFallsBackToRuntimeConfigWhenPrimaryProxySnapshotFails()
    {
        var fallback = TestConfig(
        [
            new ProxyGroup("Runtime", ProxyGroupTypes.Select, "US", ["US"])
        ]);
        var primary = new ThrowingProxyConfigProvider();
        var fallbackProvider = new FakeProxyConfigProvider(fallback);
        var page = new ProxyPageViewModel(
            primaryConfigProvider: primary,
            fallbackConfigProvider: fallbackProvider);
        page.LoadConfig(SampleConfig());
        page.SelectGroup("Select");
        page.SearchKeyword = "trojan";
        page.LocateSelectedNodeCommand.Execute(null);
        page.ScrollToTopCommand.Execute(null);

        await page.RefreshProxiesAsync();

        Assert.Equal(1, primary.LoadCount);
        Assert.Equal(1, fallbackProvider.LoadCount);
        Assert.Equal(["Runtime"], page.VisibleGroups.Select(group => group.Name));
        Assert.Equal("Runtime", page.SelectedGroup?.Name);
        Assert.Equal("US", page.SelectedGroup?.DisplaySelectionName);
        Assert.Equal("", page.SearchKeyword);
        Assert.Null(page.LocatedNodeName);
        Assert.False(page.HasScrolledToTop);
    }

    [Fact(DisplayName = "External selection sync ignores concurrent request while load is running")]
    public async Task ExternalSelectionSyncIgnoresConcurrentRequestWhileLoadIsRunning()
    {
        var config = SampleConfig();
        var synced = config with
        {
            Groups = config.Groups
                .Select(group => group.Name == "Select" ? group with { Now = "US" } : group)
                .ToList()
        };
        var provider = new BlockingProxyConfigProvider(synced);
        var page = new ProxyPageViewModel(primaryConfigProvider: provider);
        page.LoadConfig(config);
        page.SelectGroup("Select");

        var firstSync = page.SyncExternalSelectionsAsync();
        try
        {
            await provider.Started.Task.WaitAsync(AsyncTestTimeout);
            var secondSync = page.SyncExternalSelectionsAsync();
            await secondSync.WaitAsync(AsyncTestTimeout);

            Assert.Equal(1, provider.LoadCount);
            Assert.Equal("JP", page.SelectedGroup?.DisplaySelectionName);

            provider.Release.TrySetResult();
            await firstSync.WaitAsync(AsyncTestTimeout);

            Assert.Equal(1, provider.LoadCount);
            Assert.Equal("US", page.SelectedGroup?.DisplaySelectionName);
        }
        finally
        {
            provider.Release.TrySetResult();
        }
    }

    [Fact(DisplayName = "Search and delay sort filter visible node rows")]
    public void SearchAndDelaySortFilterVisibleNodeRows()
    {
        var page = new ProxyPageViewModel();
        page.LoadConfig(SampleConfig());
        page.SelectGroup("Select");

        page.SearchKeyword = "trojan";
        Assert.Equal(["US"], page.VisibleNodeRows.Select(row => row.Name));

        page.SearchKeyword = "443";
        Assert.Equal(["JP", "KR", "US"], page.VisibleNodeRows.Select(row => row.Name));

        page.SetDelaySortCommand.Execute(null);
        Assert.Equal(["KR", "JP", "US"], page.VisibleNodeRows.Select(row => row.Name));
        Assert.True(page.IsSortActive);
    }

    [Fact(DisplayName = "Locate selected node marks current row only within selected group")]
    public void LocateSelectedNodeMarksCurrentRowOnlyWithinSelectedGroup()
    {
        var page = new ProxyPageViewModel();
        page.LoadConfig(SampleConfig());
        page.SelectGroup("Select");

        page.LocateSelectedNodeCommand.Execute(null);

        Assert.Equal("JP", page.LocatedNodeName);
        Assert.True(page.VisibleNodeRows.Single(row => row.Name == "JP").IsLocated);

        page.SelectGroup("Fallback");

        Assert.DoesNotContain(page.VisibleNodeRows, row => row.IsLocated);
    }

    [Fact(DisplayName = "Node delay falls back without delay service")]
    public async Task NodeDelayFallsBackWithoutDelayService()
    {
        var page = new ProxyPageViewModel();
        page.LoadConfig(SampleConfig(), shouldTestDelaysThroughService: false);
        page.SelectGroup("Select");

        await page.TestNodeDelayAsync("KR");

        Assert.Contains("KR", page.DelayTestedNodeNames);
        Assert.Equal("61 ms", page.VisibleNodeRows.Single(row => row.Name == "KR").DelayText);
        Assert.False(page.IsDelayTesting);
    }

    [Fact(DisplayName = "Batch delay records tested failed and skipped nodes")]
    public async Task BatchDelayRecordsTestedFailedAndSkippedNodes()
    {
        var config = TestConfig(
        [
            new ProxyGroup("Mixed", ProxyGroupTypes.Select, "JP", ["JP", "Missing", "KR"])
        ]);
        var tester = new FakeProxyDelayTester(new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["JP"] = 42,
            ["KR"] = -1
        });
        var page = new ProxyPageViewModel(delayService: new ProxyDelayService(tester));
        page.LoadConfig(config);

        await page.TestGroupDelaysAsync("Mixed");

        Assert.Equal(["JP", "KR", "Missing"], page.BatchDelayTestedNodeNames.OrderBy(name => name, StringComparer.Ordinal));
        Assert.Equal("42 ms", page.VisibleNodeRows.Single(row => row.Name == "JP").DelayText);
        Assert.Equal("-1 ms", page.VisibleNodeRows.Single(row => row.Name == "KR").DelayText);
        Assert.Equal("failed", page.VisibleNodeRows.Single(row => row.Name == "KR").DelayState);
        Assert.False(page.IsBatchDelayTesting);
    }

    [Fact(DisplayName = "Group delay deduplicates repeated targets before calling core")]
    public async Task GroupDelayDeduplicatesRepeatedTargetsBeforeCallingCore()
    {
        var config = TestConfig(
        [
            new ProxyGroup("Mixed", ProxyGroupTypes.Select, "JP", ["JP", "JP", "Missing", "Missing", "KR"])
        ]);
        var tester = new CountingProxyDelayTester(new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["JP"] = 42,
            ["KR"] = 60
        });
        var service = new ProxyDelayService(tester);

        var result = await service.TestGroupAsync(config, "Mixed");

        Assert.Equal(["JP", "KR"], result.TestedNodeNames);
        Assert.Equal(["Missing"], result.SkippedNodeNames);
        Assert.Empty(result.FailedNodeNames);
        Assert.Equal(1, tester.CallCounts["JP"]);
        Assert.Equal(1, tester.CallCounts["KR"]);
        Assert.Equal(2, tester.CallCounts.Values.Sum());
    }

    [Fact(DisplayName = "Batch delay skips a node with an active single delay test")]
    public async Task BatchDelaySkipsNodeWithActiveSingleDelayTest()
    {
        var config = new ProxyConfig(
            [new ProxyGroup("Select", ProxyGroupTypes.Select, "JP", ["JP", "KR", "US"])],
            new Dictionary<string, ProxyNode>(StringComparer.Ordinal)
            {
                ["JP"] = new("JP", "ss"),
                ["KR"] = new("KR", "vmess"),
                ["US"] = new("US", "trojan")
            },
            OutboundMode.Rule);
        var tester = new BlockingSingleProxyDelayTester("JP", "KR", 42, 99);
        var page = new ProxyPageViewModel(delayService: new ProxyDelayService(tester));
        page.LoadConfig(config);
        page.SelectGroup("Select");

        var singleTask = page.TestNodeDelayAsync("JP");
        await tester.SingleStarted.Task.WaitAsync(AsyncTestTimeout);
        Assert.True(page.IsDelayTesting);
        Assert.False(page.IsBatchDelayTesting);

        var batchTask = page.TestGroupDelaysAsync("Select");
        await tester.BatchNodeTested.Task.WaitAsync(AsyncTestTimeout);

        Assert.True(page.IsBatchDelayTesting);
        Assert.Contains("JP", page.DelayTestingNodeNames);
        Assert.DoesNotContain("JP", tester.BatchCalls);
        Assert.Contains("KR", tester.BatchCalls);
        Assert.Contains("US", tester.BatchCalls);
        await tester.BatchNodeCompleted.Task.WaitAsync(AsyncTestTimeout);

        tester.SingleRelease.TrySetResult();
        await singleTask.WaitAsync(AsyncTestTimeout);
        Assert.True(page.IsBatchDelayTesting);
        Assert.True(page.IsDelayTesting);
        Assert.DoesNotContain("JP", page.DelayTestingNodeNames);
        Assert.Equal("42 ms", page.VisibleNodeRows.Single(row => row.Name == "JP").DelayText);
        Assert.Equal("99 ms", page.VisibleNodeRows.Single(row => row.Name == "KR").DelayText);

        page.SelectGroup("Select");
        Assert.Equal("99 ms", page.VisibleNodeRows.Single(row => row.Name == "KR").DelayText);

        tester.BatchRelease.TrySetResult();
        await batchTask.WaitAsync(AsyncTestTimeout);
        Assert.False(page.IsBatchDelayTesting);
        Assert.False(page.IsDelayTesting);
        Assert.Contains("JP", page.BatchDelayTestedNodeNames);
        Assert.Equal("99 ms", page.VisibleNodeRows.Single(row => row.Name == "KR").DelayText);
    }

    [Fact(DisplayName = "Single delay does not duplicate a node already covered by a batch")]
    public async Task SingleDelayDoesNotDuplicateNodeCoveredByBatch()
    {
        var tester = new BlockingBatchProxyDelayTester();
        var page = new ProxyPageViewModel(delayService: new ProxyDelayService(tester));
        page.LoadConfig(new ProxyConfig(
            [new ProxyGroup("Select", ProxyGroupTypes.Select, "JP", ["JP"])],
            new Dictionary<string, ProxyNode>(StringComparer.Ordinal)
            {
                ["JP"] = new("JP", "ss")
            },
            OutboundMode.Rule));

        var batchTask = page.TestGroupDelaysAsync("Select");
        await tester.Started.Task.WaitAsync(AsyncTestTimeout);

        await page.TestNodeDelayAsync("JP");

        Assert.Equal(1, tester.CallCount);
        Assert.True(page.IsBatchDelayTesting);

        tester.Release.TrySetResult();
        await batchTask.WaitAsync(AsyncTestTimeout);
        Assert.Equal("42 ms", page.VisibleNodeRows.Single().DelayText);
    }

    [Fact(DisplayName = "External selection sync is skipped while delay testing")]
    public async Task ExternalSelectionSyncIsSkippedWhileDelayTesting()
    {
        var config = SampleConfig();
        var synced = config with
        {
            Groups = config.Groups
                .Select(group => group.Name == "Select" ? group with { Now = "US" } : group)
                .ToList()
        };
        var provider = new FakeProxyConfigProvider(synced);
        var tester = new NonCooperativeBlockingProxyDelayTester(delay: 35);
        var page = new ProxyPageViewModel(
            delayService: new ProxyDelayService(tester),
            primaryConfigProvider: provider);
        page.LoadConfig(config);
        page.SelectGroup("Select");

        var delayTask = page.TestNodeDelayAsync("KR");
        try
        {
            await tester.Started.Task.WaitAsync(AsyncTestTimeout);
            await page.SyncExternalSelectionsAsync();

            Assert.Equal(0, provider.LoadCount);
            Assert.Equal("JP", page.SelectedGroup?.DisplaySelectionName);
            Assert.True(page.IsDelayTesting);
            Assert.True(page.VisibleNodeRows.Single(row => row.Name == "KR").IsDelayTesting);

            tester.Release.TrySetResult();
            await delayTask.WaitAsync(AsyncTestTimeout);

            Assert.Equal("JP", page.SelectedGroup?.DisplaySelectionName);
            Assert.Equal("35 ms", page.VisibleNodeRows.Single(row => row.Name == "KR").DelayText);
            Assert.False(page.IsDelayTesting);
        }
        finally
        {
            tester.Release.TrySetResult();
        }
    }

    [Fact(DisplayName = "External selection sync drops loaded snapshot when delay starts before load returns")]
    public async Task ExternalSelectionSyncDropsLoadedSnapshotWhenDelayStartsBeforeLoadReturns()
    {
        var config = SampleConfig();
        var synced = config with
        {
            Groups = config.Groups
                .Select(group => group.Name == "Select" ? group with { Now = "US" } : group)
                .ToList()
        };
        var provider = new BlockingProxyConfigProvider(synced);
        var tester = new NonCooperativeBlockingProxyDelayTester(delay: 35);
        var page = new ProxyPageViewModel(
            delayService: new ProxyDelayService(tester),
            primaryConfigProvider: provider);
        page.LoadConfig(config);
        page.SelectGroup("Select");

        var syncTask = page.SyncExternalSelectionsAsync();
        Task? delayTask = null;
        try
        {
            await provider.Started.Task.WaitAsync(AsyncTestTimeout);
            delayTask = page.TestNodeDelayAsync("KR");
            await tester.Started.Task.WaitAsync(AsyncTestTimeout);
            provider.Release.TrySetResult();
            await syncTask.WaitAsync(AsyncTestTimeout);

            Assert.Equal(1, provider.LoadCount);
            Assert.Equal("JP", page.SelectedGroup?.DisplaySelectionName);
            Assert.True(page.IsDelayTesting);

            tester.Release.TrySetResult();
            await delayTask.WaitAsync(AsyncTestTimeout);

            Assert.Equal("JP", page.SelectedGroup?.DisplaySelectionName);
            Assert.Equal("35 ms", page.VisibleNodeRows.Single(row => row.Name == "KR").DelayText);
            Assert.False(page.IsDelayTesting);
        }
        finally
        {
            provider.Release.TrySetResult();
            tester.Release.TrySetResult();
        }
    }

    [Fact(DisplayName = "Refresh cancels delay test and ignores stale result")]
    public async Task RefreshCancelsDelayTestAndIgnoresStaleResult()
    {
        var refreshed = TestConfig(
        [
            new ProxyGroup("Select", ProxyGroupTypes.Select, "US", ["US"])
        ]);
        var provider = new FakeProxyConfigProvider(refreshed);
        var tester = new NonCooperativeBlockingProxyDelayTester(delay: 999);
        var page = new ProxyPageViewModel(
            delayService: new ProxyDelayService(tester),
            primaryConfigProvider: provider);
        page.LoadConfig(SampleConfig());
        page.SelectGroup("Select");

        var delayTask = page.TestNodeDelayAsync("KR");
        try
        {
            await tester.Started.Task.WaitAsync(AsyncTestTimeout);
            await page.RefreshProxiesAsync();

            Assert.False(page.IsDelayTesting);
            Assert.True(tester.CancellationObserved);
            Assert.Equal(["US"], page.VisibleNodeRows.Select(row => row.Name));
            Assert.Empty(page.DelayTestedNodeNames);

            tester.Release.TrySetResult();
            await delayTask.WaitAsync(AsyncTestTimeout);

            Assert.Equal(["US"], page.VisibleNodeRows.Select(row => row.Name));
            Assert.Equal("250 ms", page.VisibleNodeRows.Single().DelayText);
            Assert.Empty(page.DelayTestedNodeNames);
        }
        finally
        {
            tester.Release.TrySetResult();
        }
    }

    [Fact(DisplayName = "Layout mode defaults to horizontal and toggle persists")]
    public void LayoutModeDefaultsToHorizontalAndTogglePersists()
    {
        ProxyPageLayout? persisted = null;
        var page = new ProxyPageViewModel(persistLayout: layout => persisted = layout);

        Assert.Equal(ProxyPageLayout.Horizontal, page.LayoutMode);
        Assert.False(page.IsVerticalLayout);

        page.ToggleLayoutCommand.Execute(null);
        Assert.Equal(ProxyPageLayout.Vertical, page.LayoutMode);
        Assert.True(page.IsVerticalLayout);
        Assert.Equal(ProxyPageLayout.Vertical, persisted);

        page.ToggleLayoutCommand.Execute(null);
        Assert.Equal(ProxyPageLayout.Horizontal, page.LayoutMode);
        Assert.Equal(ProxyPageLayout.Horizontal, persisted);
    }

    [Fact(DisplayName = "Vertical group cards reflect visible groups collapsed by default")]
    public void VerticalGroupCardsReflectVisibleGroupsCollapsedByDefault()
    {
        var page = new ProxyPageViewModel(initialLayout: ProxyPageLayout.Vertical);
        page.LoadConfig(SampleConfig());

        Assert.Equal(["Auto", "Fallback", "Select", "Balance"], page.VisibleGroupCards.Select(card => card.Name));
        Assert.All(page.VisibleGroupCards, card => Assert.False(card.IsExpanded));
        Assert.Null(page.ExpandedGroupName);
        Assert.True(page.IsVerticalContentVisible);
        Assert.False(page.IsEmptyVisible);
    }

    [Fact(DisplayName = "Expanding a group is exclusive, drives node rows, and collapses on reclick")]
    public void ExpandingGroupIsExclusiveDrivesNodeRowsAndCollapsesOnReclick()
    {
        var page = new ProxyPageViewModel(initialLayout: ProxyPageLayout.Vertical);
        page.LoadConfig(SampleConfig());

        page.ToggleGroupExpandCommand.Execute("Select");
        Assert.Equal("Select", page.ExpandedGroupName);
        Assert.Equal("Select", page.SelectedGroup?.Name);
        Assert.True(page.VisibleGroupCards.Single(card => card.Name == "Select").IsExpanded);
        Assert.Equal(["JP", "KR", "US"], page.VisibleNodeRows.Select(row => row.Name));

        page.ToggleGroupExpandCommand.Execute("Fallback");
        Assert.Equal("Fallback", page.ExpandedGroupName);
        Assert.False(page.VisibleGroupCards.Single(card => card.Name == "Select").IsExpanded);
        Assert.True(page.VisibleGroupCards.Single(card => card.Name == "Fallback").IsExpanded);
        Assert.Equal(["US", "KR"], page.VisibleNodeRows.Select(row => row.Name));

        page.ToggleGroupExpandCommand.Execute("Fallback");
        Assert.Null(page.ExpandedGroupName);
        Assert.All(page.VisibleGroupCards, card => Assert.False(card.IsExpanded));
    }

    private static ProxyConfig SampleConfig()
    {
        var nodes = new Dictionary<string, ProxyNode>(StringComparer.Ordinal)
        {
            ["JP"] = new("JP", "ss", Delay: 90, Server: "jp.example", Port: 443),
            ["KR"] = new("KR", "vmess", Delay: 60, Server: "kr.example", Port: 443),
            ["US"] = new("US", "trojan", Delay: 250, Server: "us.example", Port: 443),
        };
        var groups = new[]
        {
            new ProxyGroup("GLOBAL", ProxyGroupTypes.Select, "JP", ["JP", "KR", "US"]),
            new ProxyGroup("Auto", ProxyGroupTypes.UrlTest, "JP", ["JP", "KR"], Fixed: "KR"),
            new ProxyGroup("Fallback", ProxyGroupTypes.Fallback, "US", ["US", "KR"], Fixed: "US"),
            new ProxyGroup("Select", ProxyGroupTypes.Select, null, ["JP", "KR", "US"]),
            new ProxyGroup("Balance", ProxyGroupTypes.LoadBalance, "JP", ["JP", "KR"]),
            new ProxyGroup("Hidden", ProxyGroupTypes.Select, "JP", ["JP"], IsHidden: true),
        };
        return new ProxyConfig(groups, nodes, OutboundMode.Rule);
    }

    private static ProxyConfig TestConfig(IReadOnlyList<ProxyGroup> groups)
    {
        return new ProxyConfig(groups, new Dictionary<string, ProxyNode>(StringComparer.Ordinal)
        {
            ["JP"] = new("JP", "ss", Delay: 90, Server: "jp.example", Port: 443),
            ["KR"] = new("KR", "vmess", Delay: 60, Server: "kr.example", Port: 443),
            ["US"] = new("US", "trojan", Delay: 250, Server: "us.example", Port: 443),
        }, OutboundMode.Rule);
    }

    private sealed class FakeProxyConfigProvider(ProxyConfig config) : IProxyConfigProvider
    {
        public int LoadCount { get; private set; }

        public Task<ProxyConfig> LoadAsync(CancellationToken cancellationToken = default)
        {
            LoadCount++;
            return Task.FromResult(config);
        }
    }

    private sealed class ThrowingProxyConfigProvider : IProxyConfigProvider
    {
        public int LoadCount { get; private set; }

        public Task<ProxyConfig> LoadAsync(CancellationToken cancellationToken = default)
        {
            LoadCount++;
            throw new InvalidOperationException("Core proxy list is unavailable");
        }
    }

    private sealed class BlockingProxyConfigProvider(ProxyConfig config) : IProxyConfigProvider
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int LoadCount { get; private set; }

        public async Task<ProxyConfig> LoadAsync(CancellationToken cancellationToken = default)
        {
            LoadCount++;
            Started.TrySetResult();
            await Release.Task;
            return config;
        }
    }

    private sealed class FakeProxyCoreClient : IProxyCoreClient
    {
        public List<ProxyChangeRequest> ChangeRequests { get; } = [];
        public List<ConnectionCloseRequest> CloseRequests { get; } = [];

        public Task<IReadOnlyList<ConnectionInfo>> GetConnectionsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ConnectionInfo>>([]);

        public Task<bool> ChangeProxyAsync(ProxyChangeRequest request, CancellationToken cancellationToken = default)
        {
            ChangeRequests.Add(request);
            return Task.FromResult(true);
        }

        public Task<bool> ClearProxySelectionAsync(string groupName, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<bool> CloseConnectionsAsync(ConnectionCloseRequest request, CancellationToken cancellationToken = default)
        {
            CloseRequests.Add(request);
            return Task.FromResult(true);
        }

        public Task<ProxyRuntimeSnapshot> GetProxiesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new ProxyRuntimeSnapshot([]));

        public Task<OutboundMode?> GetOutboundModeAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<OutboundMode?>(null);

        public Task<bool> SetOutboundModeAsync(OutboundMode mode, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<string?> GetVersionAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);

        public Task<CoreRuntimeStats?> GetRuntimeStatsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<CoreRuntimeStats?>(null);

        public Task<CoreTrafficRate?> GetTrafficAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<CoreTrafficRate?>(null);
    }

    private sealed class FakeProxyDelayTester(IReadOnlyDictionary<string, int> delays) : IProxyDelayTester
    {
        public Task<int> TestDelayAsync(string proxyName, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(delays.TryGetValue(proxyName, out var delay) ? delay : -1);
        }
    }

    private sealed class CountingProxyDelayTester(IReadOnlyDictionary<string, int> delays) : IProxyDelayTester
    {
        public Dictionary<string, int> CallCounts { get; } = new(StringComparer.Ordinal);

        public Task<int> TestDelayAsync(string proxyName, CancellationToken cancellationToken = default)
        {
            CallCounts[proxyName] = CallCounts.GetValueOrDefault(proxyName) + 1;
            return Task.FromResult(delays.TryGetValue(proxyName, out var delay) ? delay : -1);
        }
    }

    private sealed class BlockingSingleProxyDelayTester(
        string singleNodeName,
        string completedBatchNodeName,
        int singleDelay,
        int batchDelay) : IProxyDelayTester
    {
        private int _singleStarted;

        public TaskCompletionSource SingleStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource SingleRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource BatchNodeTested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource BatchNodeCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource BatchRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<string> BatchCalls { get; } = [];

        public async Task<int> TestDelayAsync(string proxyName, CancellationToken cancellationToken = default)
        {
            if (proxyName == singleNodeName && Interlocked.Exchange(ref _singleStarted, 1) == 0)
            {
                SingleStarted.TrySetResult();
                await SingleRelease.Task;
                return singleDelay;
            }

            BatchCalls.Add(proxyName);
            BatchNodeTested.TrySetResult();
            if (proxyName == completedBatchNodeName)
            {
                BatchNodeCompleted.TrySetResult();
                return batchDelay;
            }

            await BatchRelease.Task;
            return batchDelay;
        }
    }

    private sealed class BlockingBatchProxyDelayTester : IProxyDelayTester
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int CallCount { get; private set; }

        public async Task<int> TestDelayAsync(string proxyName, CancellationToken cancellationToken = default)
        {
            CallCount++;
            Started.TrySetResult();
            await Release.Task;
            return 42;
        }
    }

    private sealed class NonCooperativeBlockingProxyDelayTester(int delay) : IProxyDelayTester
    {
        private int _cancellationObserved;

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool CancellationObserved => Volatile.Read(ref _cancellationObserved) == 1;

        public async Task<int> TestDelayAsync(string proxyName, CancellationToken cancellationToken = default)
        {
            using var registration = cancellationToken.Register(() => Volatile.Write(ref _cancellationObserved, 1));
            Started.TrySetResult();
            await Release.Task;
            return delay;
        }
    }
}

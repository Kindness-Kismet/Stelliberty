using Stelliberty.Application.Connections;
using Stelliberty.Application.CoreLogs;
using Stelliberty.Application.Proxies;
using Stelliberty.Application.Rules;
using Stelliberty.Application.Subscriptions;
using Stelliberty.Domain.Connections;
using Stelliberty.Domain.CoreLogs;
using Stelliberty.Domain.Proxies;
using Stelliberty.Domain.Rules;
using Stelliberty.Domain.Subscriptions;
using Stelliberty.Presentation.ViewModels;
using Xunit;

namespace Stelliberty.Monitoring.Tests;

public sealed class MonitoringPageBusinessTests
{
    [Fact(DisplayName = "Connection parser handles core JSON value shapes")]
    public void ConnectionParserHandlesCoreJsonValueShapes()
    {
        var parser = new ConnectionParser(() => DateTimeOffset.UnixEpoch);
        var connections = parser.Parse(
            """
            {
              "connections": [
                {
                  "id": "c1",
                  "upload": "100",
                  "download": 200,
                  "start": "2026-01-01T00:00:00Z",
                  "chains": ["GLOBAL", "HK"],
                  "rule": "DOMAIN",
                  "rulePayload": "example.com",
                  "metadata": {
                    "network": "tcp",
                    "sourceIP": "127.0.0.1",
                    "sourcePort": 5000,
                    "destinationIP": "1.1.1.1",
                    "destinationPort": "443",
                    "destinationGeoIP": ["US"],
                    "host": "example.com",
                    "process": "browser"
                  }
                }
              ]
            }
            """);

        var connection = Assert.Single(connections);
        Assert.Equal("c1", connection.Id);
        Assert.Equal(100, connection.Upload);
        Assert.Equal(200, connection.Download);
        Assert.Equal("443", connection.Metadata.DestinationPort);
        Assert.Equal(["GLOBAL", "HK"], connection.Chains);
        Assert.Equal("HK", connection.ProxyNode);
    }

    [Fact(DisplayName = "Connection reducer freezes when paused and clamps sample window")]
    public void ConnectionReducerFreezesWhenPausedAndClampsSampleWindow()
    {
        var reducer = new ConnectionListReducer();
        var first = reducer.ApplyIncoming(ConnectionListState.Initial, [Connection("c1", upload: 0, download: 0)], DateTimeOffset.UnixEpoch);
        var second = reducer.ApplyIncoming(first, [Connection("c1", upload: 1000, download: 500)], DateTimeOffset.UnixEpoch.AddMilliseconds(100));
        var paused = reducer.TogglePause(second);
        var frozen = reducer.ApplyIncoming(paused, [Connection("c1", upload: 2000, download: 1000)], DateTimeOffset.UnixEpoch.AddSeconds(1));

        Assert.Equal(4000, second.Connections[0].UploadSpeed);
        Assert.Equal(2000, second.Connections[0].DownloadSpeed);
        Assert.Equal(second.Connections[0].Upload, frozen.Connections[0].Upload);
    }

    [Fact(DisplayName = "Connection page filters and applies external close all")]
    public void ConnectionPageFiltersAndAppliesExternalCloseAll()
    {
        var page = new ConnectionPageViewModel(now: () => DateTimeOffset.UnixEpoch);
        page.ApplyIncoming(
        [
            Connection("direct", chains: []),
            Connection("proxy", chains: ["GLOBAL", "HK"], host: "example.com")
        ]);

        page.ShowProxyConnectionsCommand.Execute(null);
        page.SearchKeyword = "example";
        page.ShowDetailCommand.Execute("proxy");
        page.ApplyAllConnectionsClosed();

        Assert.True(page.HasClosedAllConnections);
        Assert.Empty(page.Connections);
        Assert.False(page.IsDetailVisible);
    }

    [Fact(DisplayName = "Connection page refreshes visible detail when same connection ID receives new data")]
    public void ConnectionPageRefreshesVisibleDetailWhenSameConnectionIdReceivesNewData()
    {
        var page = new ConnectionPageViewModel(now: () => DateTimeOffset.UnixEpoch);
        page.ApplyIncoming([Connection("c1", upload: 100, download: 200, host: "old.example")]);
        page.ShowDetailCommand.Execute("c1");

        page.ApplyIncoming([Connection("c1", upload: 300, download: 400, host: "new.example")]);

        Assert.True(page.IsDetailVisible);
        Assert.Equal("new.example", page.SelectedConnection?.Metadata.Host);
        Assert.Equal(300, page.SelectedConnection?.Upload);
        Assert.Equal(400, page.SelectedConnection?.Download);
        Assert.Contains("new.example", page.SelectedConnectionDetailText, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Connection page does not close local state when core rejects single close")]
    public async Task ConnectionPageDoesNotCloseLocalStateWhenCoreRejectsSingleClose()
    {
        var core = new FakeProxyCoreClient { CloseConnectionsResult = false };
        var page = new ConnectionPageViewModel(core);
        page.ApplyIncoming([Connection("c1"), Connection("c2")]);
        page.ShowDetailCommand.Execute("c1");

        await page.CloseConnectionAsync("c1");

        Assert.Equal(2, page.Connections.Count);
        Assert.True(page.IsDetailVisible);
        Assert.Empty(page.ClosedConnectionIds);
        Assert.Equal(ConnectionCloseMode.Single, core.LastCloseRequest?.Mode);
        Assert.Equal("c1", core.LastCloseRequest?.ConnectionId);
    }

    [Fact(DisplayName = "Connection page does not close local state when core rejects close all")]
    public async Task ConnectionPageDoesNotCloseLocalStateWhenCoreRejectsCloseAll()
    {
        var core = new FakeProxyCoreClient { CloseConnectionsResult = false };
        var page = new ConnectionPageViewModel(core);
        page.ApplyIncoming([Connection("c1"), Connection("c2")]);
        page.ShowDetailCommand.Execute("c1");

        await page.CloseAllConnectionsAsync();

        Assert.Equal(["c1", "c2"], page.Connections.Select(connection => connection.Id));
        Assert.True(page.IsDetailVisible);
        Assert.False(page.HasClosedAllConnections);
        Assert.Empty(page.ClosedConnectionIds);
        Assert.Equal(ConnectionCloseMode.All, core.LastCloseRequest?.Mode);
    }

    [Fact(DisplayName = "Connection page ignores close request for connection removed by refresh")]
    public async Task ConnectionPageIgnoresCloseRequestForConnectionRemovedByRefresh()
    {
        var core = new FakeProxyCoreClient();
        var page = new ConnectionPageViewModel(core);
        page.ApplyIncoming([Connection("c1")]);
        page.ApplyIncoming([]);

        await page.CloseConnectionAsync("c1");

        Assert.Empty(page.Connections);
        Assert.Empty(page.ClosedConnectionIds);
        Assert.Null(core.LastCloseRequest);
    }

    [Fact(DisplayName = "Connection page caps closed connection IDs to latest five hundred")]
    public async Task ConnectionPageCapsClosedConnectionIdsToLatestFiveHundred()
    {
        var page = new ConnectionPageViewModel();
        page.ApplyIncoming(Enumerable.Range(0, 501).Select(index => Connection($"c{index}")).ToList());

        foreach (var connection in page.Connections.ToList())
        {
            await page.CloseConnectionAsync(connection.Id);
        }

        Assert.Equal(500, page.ClosedConnectionIds.Count);
        Assert.DoesNotContain("c0", page.ClosedConnectionIds);
        Assert.Equal("c500", page.ClosedConnectionIds[^1]);
    }

    [Fact(DisplayName = "Connection page ignores incoming connections and traffic rate while paused")]
    public void ConnectionPageIgnoresIncomingConnectionsAndTrafficRateWhilePaused()
    {
        var page = new ConnectionPageViewModel(now: () => DateTimeOffset.UnixEpoch);
        page.ApplyIncoming([Connection("c1", upload: 0, download: 0)], new CoreTrafficRate(100, 200));
        page.TogglePauseCommand.Execute(null);

        page.ApplyIncoming([Connection("c2", upload: 1000, download: 1000)], new CoreTrafficRate(900, 800));

        Assert.Equal(["c1"], page.Connections.Select(connection => connection.Id));
        Assert.Equal("100 B/s", page.TotalUploadSpeedText);
        Assert.Equal("200 B/s", page.TotalDownloadSpeedText);

        page.TogglePauseCommand.Execute(null);
        page.ApplyIncoming([Connection("c2", upload: 1000, download: 1000)], new CoreTrafficRate(900, 800));

        Assert.Equal(["c2"], page.Connections.Select(connection => connection.Id));
        Assert.Equal("900 B/s", page.TotalUploadSpeedText);
        Assert.Equal("800 B/s", page.TotalDownloadSpeedText);
    }

    [Fact(DisplayName = "Connection page refresh loads connections and applies traffic rate only when running")]
    public async Task ConnectionPageRefreshLoadsConnectionsAndAppliesTrafficRateOnlyWhenRunning()
    {
        var core = new FakeProxyCoreClient
        {
            Connections =
            [
                Connection("c1", chains: ["GLOBAL", "HK"], host: "example.com")
            ],
            TrafficRate = new CoreTrafficRate(123, 456)
        };
        var page = new ConnectionPageViewModel(core, now: () => DateTimeOffset.UnixEpoch);

        await page.RefreshConnectionsAsync();

        Assert.Equal(1, core.ConnectionReadCount);
        Assert.Equal(1, core.TrafficReadCount);
        Assert.Equal(["c1"], page.Connections.Select(connection => connection.Id));
        Assert.Equal("123 B/s", page.TotalUploadSpeedText);
        Assert.Equal("456 B/s", page.TotalDownloadSpeedText);

        page.TogglePauseCommand.Execute(null);
        core.Connections = [Connection("c2")];
        core.TrafficRate = new CoreTrafficRate(999, 888);
        await page.RefreshConnectionsAsync();

        Assert.Equal(["c1"], page.Connections.Select(connection => connection.Id));
        Assert.Equal("123 B/s", page.TotalUploadSpeedText);
        Assert.Equal("456 B/s", page.TotalDownloadSpeedText);
    }

    [Fact(DisplayName = "Connection page refresh keeps current list when read fails")]
    public async Task ConnectionPageRefreshKeepsCurrentListWhenReadFails()
    {
        var core = new FakeProxyCoreClient
        {
            Connections = [Connection("c1")],
        };
        var page = new ConnectionPageViewModel(core, now: () => DateTimeOffset.UnixEpoch);
        await page.RefreshConnectionsAsync();

        core.FailConnectionRead = true;
        await page.RefreshConnectionsAsync();

        Assert.Equal(2, core.ConnectionReadCount);
        Assert.Equal(["c1"], page.Connections.Select(connection => connection.Id));
    }

    [Fact(DisplayName = "Core log parser handles JSON and text lines")]
    public void CoreLogParserHandlesJsonAndTextLines()
    {
        var parser = new CoreLogParser(() => DateTimeOffset.UnixEpoch);

        var jsonLogs = parser.Parse("""[{"type":"warning","payload":"slow"},{"level":"error","msg":"failed"}]""");
        var textLog = parser.Parse("time=\"2026-01-01T00:00:00Z\" level=debug msg=\"hello world\"");

        Assert.Equal([CoreLogLevel.Warning, CoreLogLevel.Error], jsonLogs.Select(log => log.Level));
        Assert.Equal(CoreLogLevel.Debug, textLog.Single().Level);
        Assert.Equal("hello world", textLog.Single().Payload);
    }

    [Fact(DisplayName = "Core log reducer truncates and honors pause")]
    public void CoreLogReducerTruncatesAndHonorsPause()
    {
        var reducer = new CoreLogReducer();
        var logs = Enumerable.Range(0, 2100)
            .Select(index => new CoreLogMessage("INFO", $"log-{index}", DateTimeOffset.UnixEpoch))
            .ToList();

        var state = reducer.Append(CoreLogState.Initial, logs);
        var paused = reducer.TogglePause(state);
        var frozen = reducer.Append(paused, [new CoreLogMessage("ERROR", "new", DateTimeOffset.UnixEpoch)]);

        Assert.Equal(2000, state.Logs.Count);
        Assert.Equal("log-100", state.Logs[0].Payload);
        Assert.Equal(state.Logs.Count, frozen.Logs.Count);
        Assert.DoesNotContain(frozen.Logs, log => log.Payload == "new");
    }

    [Fact(DisplayName = "Core log page clears logs and resets when core stops")]
    public void CoreLogPageClearsLogsAndResetsWhenCoreStops()
    {
        var page = new CoreLogPageViewModel();
        var cleared = false;
        page.LogsCleared += (_, _) => cleared = true;
        page.AppendLogs([new CoreLogMessage("ERROR", "failed", DateTimeOffset.UnixEpoch)]);

        page.ClearLogs();
        page.AppendLogs([new CoreLogMessage("INFO", "running", DateTimeOffset.UnixEpoch)]);
        page.ApplyCoreRunning(false);

        Assert.True(cleared);
        Assert.Empty(page.Logs);
        Assert.False(page.IsCoreRunning);
    }

    [Fact(DisplayName = "Core log page core stop resets paused filter and search state")]
    public void CoreLogPageCoreStopResetsPausedFilterAndSearchState()
    {
        var page = new CoreLogPageViewModel();
        page.AppendLogs(
        [
            new CoreLogMessage("ERROR", "failed", DateTimeOffset.UnixEpoch),
            new CoreLogMessage("INFO", "connected", DateTimeOffset.UnixEpoch.AddSeconds(1))
        ]);
        page.TogglePauseCommand.Execute(null);
        page.ShowErrorLevelCommand.Execute(null);
        page.SearchKeyword = "failed";

        page.ApplyCoreRunning(false);

        Assert.Empty(page.Logs);
        Assert.False(page.IsMonitoringPaused);
        Assert.Null(page.FilterLevel);
        Assert.Equal("", page.SearchKeyword);
        Assert.True(page.IsAllLevelsSelected);
        Assert.Equal("CoreLogs.Empty.CoreStopped", page.EmptyText);
    }

    [Fact(DisplayName = "Core log page filters by level keyword and shows newest first")]
    public void CoreLogPageFiltersByLevelKeywordAndShowsNewestFirst()
    {
        var page = new CoreLogPageViewModel();
        page.AppendLogs(
        [
            new CoreLogMessage("INFO", "connected", DateTimeOffset.UnixEpoch),
            new CoreLogMessage("ERROR", "proxy failed", DateTimeOffset.UnixEpoch.AddSeconds(1)),
            new CoreLogMessage("WARNING", "proxy slow", DateTimeOffset.UnixEpoch.AddSeconds(2))
        ]);

        page.ShowWarningLevelCommand.Execute(null);
        page.SearchKeyword = "proxy";

        var row = Assert.Single(page.FilteredLogRows);
        Assert.Equal(CoreLogLevel.Warning, row.Level);
        Assert.Equal(1, row.Index);
        Assert.Equal("proxy slow", row.Payload);
        Assert.Equal(1, page.FilteredLogCount);
        Assert.Equal(1, page.ErrorLogCount);
        Assert.Equal(1, page.WarningLogCount);
    }

    [Fact(DisplayName = "Core log page searches type field and clear keeps filters")]
    public void CoreLogPageSearchesTypeFieldAndClearKeepsFilters()
    {
        var page = new CoreLogPageViewModel();
        page.AppendLogs(
        [
            new CoreLogMessage("dns/debug", "query example.com", DateTimeOffset.UnixEpoch),
            new CoreLogMessage("proxy/warning", "slow node", DateTimeOffset.UnixEpoch.AddSeconds(1))
        ]);

        page.ShowDebugLevelCommand.Execute(null);
        page.SearchKeyword = " DNS ";

        var row = Assert.Single(page.FilteredLogRows);
        Assert.Equal(CoreLogLevel.Debug, row.Level);
        Assert.Equal("query example.com", row.Payload);

        page.ClearLogs();

        Assert.Empty(page.Logs);
        Assert.Equal(CoreLogLevel.Debug, page.FilterLevel);
        Assert.Equal(" DNS ", page.SearchKeyword);
        Assert.True(page.IsDebugLevelSelected);
        Assert.Equal("CoreLogs.Empty.NoLogs", page.EmptyText);
    }

    [Fact(DisplayName = "Rule parser handles core JSON and YAML rules")]
    public void RuleParserHandlesCoreJsonAndYamlRules()
    {
        var parser = new RuleParser();
        var coreRules = parser.Parse("""{"rules":[{"type":"DOMAIN","payload":"example.com","proxy":"PROXY"}]}""");
        var yamlRules = parser.Parse(
            """
            rule-providers:
              reject:
                type: http
                path: ./reject.yaml
                ruleCount: 2
            rules:
              - DOMAIN-SUFFIX,example.com,PROXY
              - MATCH,DIRECT
            """);

        Assert.Equal("example.com", coreRules.Single().Payload);
        Assert.Contains(yamlRules, rule => rule.Type == "RULE-PROVIDER" && rule.Payload == "reject" && rule.RuleCount == 2);
        Assert.Contains(yamlRules, rule => rule.Type == "MATCH" && rule.Proxy == "DIRECT");
    }

    [Fact(DisplayName = "Rule page resets filters when core stops but keeps rules")]
    public void RulePageResetsFiltersWhenCoreStopsButKeepsRules()
    {
        var page = new RulePageViewModel();
        page.LoadRules(
        [
            new RuleItem("DOMAIN-SUFFIX", "example.com", "PROXY"),
            new RuleItem("IP-CIDR", "1.1.1.0/24", "DIRECT")
        ]);
        page.SearchKeyword = "example";
        page.SetTypeBucket(RuleTypeBucket.Domain);

        page.ApplyCoreRunning(false);

        Assert.Equal(2, page.Rules.Count);
        Assert.Equal("", page.SearchKeyword);
        Assert.Equal(RuleTypeBucket.All, page.TypeBucket);
        Assert.False(page.IsCoreRunning);
    }

    [Fact(DisplayName = "Rule page restores visible rules after core restarts without reloading")]
    public void RulePageRestoresVisibleRulesAfterCoreRestartsWithoutReloading()
    {
        var page = new RulePageViewModel();
        page.LoadRules(
        [
            new RuleItem("DOMAIN-SUFFIX", "example.com", "PROXY")
        ]);

        page.ApplyCoreRunning(false);

        Assert.True(page.IsEmptyVisible);
        Assert.Equal("Rules.Empty.CoreStopped", page.EmptyText);
        Assert.Equal("Rules.State.CoreStopped", page.MonitorStateText);
        Assert.Equal("warning", page.MonitorSignalTag);

        page.ApplyCoreRunning(true);

        Assert.False(page.IsEmptyVisible);
        Assert.Equal("Rules.State.Monitoring", page.MonitorStateText);
        Assert.Equal("ok", page.MonitorSignalTag);
        Assert.Equal("example.com", page.FilteredRuleRows.Single().Payload);
    }

    [Fact(DisplayName = "Rule page refresh loads rules and raises refresh event")]
    public void RulePageRefreshLoadsRulesAndRaisesRefreshEvent()
    {
        var source = new FakeRuleConfigSource(
            """
            rules:
              - DOMAIN-SUFFIX,example.com,PROXY
              - IP-CIDR,1.1.1.0/24,DIRECT
            """);
        var page = new RulePageViewModel(new RuleListLoader(source, new RuleParser()));
        var refreshCount = 0;
        page.RefreshRequested += (_, _) => refreshCount++;

        page.RefreshRulesCommand.Execute(null);

        Assert.True(page.HasRequestedRefresh);
        Assert.Equal(2, page.Rules.Count);
        Assert.Equal(1, refreshCount);
        Assert.Equal(1, source.ReadCount);
    }

    [Fact(DisplayName = "Rule page searches rule count but still honors selected bucket")]
    public void RulePageSearchesRuleCountButStillHonorsSelectedBucket()
    {
        var page = new RulePageViewModel();
        page.LoadRules(
        [
            new RuleItem("RULE-PROVIDER", "reject", "REJECT", Source: "reject.yaml", RuleCount: 2),
            new RuleItem("DOMAIN-SUFFIX", "example.com", "PROXY", Source: "remote.yaml", RuleCount: 2),
            new RuleItem("IP-CIDR", "1.1.1.0/24", "DIRECT")
        ]);

        page.SetTypeBucket(RuleTypeBucket.RuleSet);
        page.SearchKeyword = " 2 rules ";

        var row = Assert.Single(page.FilteredRuleRows);
        Assert.Equal("RULE-PROVIDER", row.Type);
        Assert.Equal("reject", row.Payload);
        Assert.Equal(1, row.Index);

        page.SearchKeyword = "remote.yaml";

        Assert.Empty(page.FilteredRules);
        Assert.True(page.IsEmptyVisible);
        Assert.Equal("Rules.Empty.NoMatches", page.EmptyText);
    }

    [Fact(DisplayName = "Rule type classifier buckets known rule types")]
    public void RuleTypeClassifierBucketsKnownRuleTypes()
    {
        Assert.Equal(RuleTypeBucket.Domain, RuleTypeClassifier.Classify("DOMAIN-SUFFIX"));
        Assert.Equal(RuleTypeBucket.Ip, RuleTypeClassifier.Classify("GEOIP"));
        Assert.Equal(RuleTypeBucket.Ip, RuleTypeClassifier.Classify("SRC-IP-CIDR"));
        Assert.Equal(RuleTypeBucket.RuleSet, RuleTypeClassifier.Classify("RULE-PROVIDER"));
        Assert.Equal(RuleTypeBucket.Other, RuleTypeClassifier.Classify("MATCH"));
    }

    [Fact(DisplayName = "Rule editor reports field errors without a toast and clears them live")]
    public void RuleEditorReportsFieldErrorsWithoutToastAndClearsThemLive()
    {
        var context = CreateRuleEditorContext();
        var page = context.Page;
        (string Message, ToastType Type)? toast = null;
        DialogInputField? focusedField = null;
        page.ToastRequested += (_, value) => toast = value;
        page.InputFocusRequested += (_, field) => focusedField = field;
        page.AddRuleCommand.Execute(null);
        page.SelectedOutboundTarget = page.OutboundTargets.Single(option => option.IsCustom);
        page.Proxy = "bad,proxy";
        page.Payload = "bad,payload";

        Assert.False(page.IsProxyErrorVisible);
        Assert.False(page.IsPayloadErrorVisible);
        page.SaveRuleCommand.Execute(null);

        Assert.True(page.IsProxyErrorVisible);
        Assert.True(page.IsPayloadErrorVisible);
        Assert.Equal(DialogInputField.Proxy, focusedField);
        Assert.Null(toast);

        page.Proxy = "PROXY";
        page.Payload = "example.net";

        Assert.False(page.IsProxyErrorVisible);
        Assert.False(page.IsPayloadErrorVisible);
    }

    [Fact(DisplayName = "Rule editor assigns duplicate input to payload and runtime failure to toast")]
    public void RuleEditorAssignsDuplicateInputToPayloadAndRuntimeFailureToToast()
    {
        var context = CreateRuleEditorContext(
            new RuleOverrideSet("sub-1", [new EditableRule("existing", "DOMAIN-SUFFIX", "example.com", "DIRECT")]));
        var page = context.Page;
        (string Message, ToastType Type)? toast = null;
        page.ToastRequested += (_, value) => toast = value;
        page.AddRuleCommand.Execute(null);
        page.Payload = "example.com";
        page.SaveRuleCommand.Execute(null);

        Assert.True(page.IsPayloadErrorVisible);
        Assert.Equal("Rules.Error.DuplicateCustom", page.PayloadError);
        Assert.Null(toast);

        page.Payload = "example.net";
        Assert.False(page.IsPayloadErrorVisible);
        context.SubscriptionStore.Delete("sub-1");
        page.SaveRuleCommand.Execute(null);

        Assert.False(page.IsPayloadErrorVisible);
        Assert.Equal(ToastType.Error, toast?.Type);
        Assert.Equal("Rules.Error.SubscriptionNotFound", toast?.Message);
    }

    [Fact(DisplayName = "Rule template validates its name only after save")]
    public void RuleTemplateValidatesItsNameOnlyAfterSave()
    {
        var context = CreateRuleEditorContext(
            new RuleOverrideSet("sub-1", [new EditableRule("existing", "DOMAIN-SUFFIX", "example.com", "DIRECT")]));
        var page = context.Page;
        page.OpenCreateTemplateCommand.Execute(null);

        Assert.False(page.IsTemplateNameErrorVisible);
        Assert.True(page.SaveTemplateCommand.CanExecute(null));
        page.SaveTemplateCommand.Execute(null);

        Assert.True(page.IsTemplateNameErrorVisible);
        page.TemplateName = "Template";
        Assert.False(page.IsTemplateNameErrorVisible);
    }

    [Fact(DisplayName = "Rule template clears validation when cancelled")]
    public void RuleTemplateClearsValidationWhenCancelled()
    {
        var context = CreateRuleEditorContext(
            new RuleOverrideSet("sub-1", [new EditableRule("existing", "DOMAIN-SUFFIX", "example.com", "DIRECT")]));
        var page = context.Page;
        page.OpenCreateTemplateCommand.Execute(null);
        page.SaveTemplateCommand.Execute(null);

        Assert.True(page.IsTemplateNameErrorVisible);
        page.CancelTemplateCommand.Execute(null);

        Assert.False(page.IsTemplateNameErrorVisible);
    }

    private static RuleEditorTestContext CreateRuleEditorContext(RuleOverrideSet? initialSet = null)
    {
        var subscription = new Subscription("sub-1", "Sub", "https://example.com/sub.yaml", false, DateTimeOffset.UnixEpoch);
        var subscriptionStore = new FakeSubscriptionStore(subscription, "rules:\n  - MATCH,DIRECT\n");
        var overrideStore = new FakeRuleOverrideStore(initialSet ?? new RuleOverrideSet("sub-1"));
        var service = new RuleOverrideService(subscriptionStore, new FakeSubscriptionSelectionStore("sub-1"), overrideStore, new RuleParser());
        var page = new RulePageViewModel(service);
        page.LoadEditorSnapshot();
        return new RuleEditorTestContext(page, subscriptionStore);
    }

    private static ConnectionInfo Connection(
        string id,
        long upload = 0,
        long download = 0,
        IReadOnlyList<string>? chains = null,
        string host = "")
    {
        return new ConnectionInfo(
            id,
            upload,
            download,
            Metadata: new ConnectionMetadata(Network: "tcp", Host: host, DestinationPort: "443"),
            Chains: chains ?? []);
    }

    private sealed class FakeProxyCoreClient : IProxyCoreClient
    {
        public bool CloseConnectionsResult { get; init; } = true;
        public ConnectionCloseRequest? LastCloseRequest { get; private set; }
        public IReadOnlyList<ConnectionInfo> Connections { get; set; } = [];
        public CoreTrafficRate? TrafficRate { get; set; }
        public int ConnectionReadCount { get; private set; }
        public int TrafficReadCount { get; private set; }

        public bool FailConnectionRead { get; set; }

        public Task<IReadOnlyList<ConnectionInfo>?> GetConnectionsAsync(CancellationToken cancellationToken = default)
        {
            ConnectionReadCount++;
            return Task.FromResult<IReadOnlyList<ConnectionInfo>?>(FailConnectionRead ? null : Connections);
        }

        public Task<bool> ChangeProxyAsync(ProxyChangeRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<bool> ClearProxySelectionAsync(string groupName, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<bool> CloseConnectionsAsync(ConnectionCloseRequest request, CancellationToken cancellationToken = default)
        {
            LastCloseRequest = request;
            return Task.FromResult(CloseConnectionsResult);
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
        {
            TrafficReadCount++;
            return Task.FromResult(TrafficRate);
        }
    }

    private sealed class FakeRuleConfigSource(string content) : IRuleConfigSource
    {
        public int ReadCount { get; private set; }

        public string ReadRuntimeConfig()
        {
            ReadCount++;
            return content;
        }
    }

    private sealed record RuleEditorTestContext(RulePageViewModel Page, FakeSubscriptionStore SubscriptionStore);

    private sealed class FakeSubscriptionSelectionStore(string? currentSubscriptionId) : ISubscriptionSelectionStore
    {
        public string? GetCurrentSubscriptionId() => currentSubscriptionId;

        public void SetCurrentSubscriptionId(string? subscriptionId)
        {
            currentSubscriptionId = subscriptionId;
        }
    }

    private sealed class FakeSubscriptionStore(Subscription subscription, string content) : ISubscriptionStore
    {
        private readonly List<Subscription> _subscriptions = [subscription];

        public void Save(Subscription value, string originalContent)
        {
            _subscriptions.Add(value);
            content = originalContent;
        }

        public void UpdateSubscription(Subscription value)
        {
            var index = _subscriptions.FindIndex(item => item.Id == value.Id);
            if (index >= 0)
            {
                _subscriptions[index] = value;
            }
        }

        public void SaveSubscriptions(IReadOnlyList<Subscription> values)
        {
            _subscriptions.Clear();
            _subscriptions.AddRange(values);
        }

        public void SaveContent(string subscriptionId, string originalContent) => content = originalContent;

        public IReadOnlyList<Subscription> LoadSubscriptions() => _subscriptions.ToList();

        public string ReadContent(string subscriptionId) => content;

        public string GetContentPath(string subscriptionId) => $"{subscriptionId}.yaml";

        public void Delete(string subscriptionId) => _subscriptions.RemoveAll(item => item.Id == subscriptionId);
    }

    private sealed class FakeRuleOverrideStore(RuleOverrideSet initialSet) : IRuleOverrideStore
    {
        private RuleOverrideSet _set = initialSet;

        public RuleOverrideSet Load(string subscriptionId) => _set;

        public void Save(RuleOverrideSet set) => _set = set;

        public void UpsertTemplate(RuleTemplate template)
        {
            var templates = _set.Templates.Where(item => item.Id != template.Id).Append(template).ToList();
            _set = _set with { Templates = templates };
        }

        public void DeleteTemplate(string templateId)
        {
            _set = _set with { Templates = _set.Templates.Where(item => item.Id != templateId).ToList() };
        }

        public void Delete(string subscriptionId) => _set = new RuleOverrideSet(subscriptionId);
    }
}

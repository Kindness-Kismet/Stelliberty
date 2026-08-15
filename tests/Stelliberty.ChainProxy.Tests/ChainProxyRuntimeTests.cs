using Stelliberty.Application.Subscriptions;
using Stelliberty.Application.Proxies;
using Stelliberty.Application.Runtime;
using Stelliberty.Domain.Subscriptions;
using YamlDotNet.RepresentationModel;
using Xunit;

namespace Stelliberty.ChainProxy.Tests;

public sealed class ChainProxyRuntimeTests
{
    [Fact(DisplayName = "Analyzer finds distinct builtin chain proxy names")]
    public void AnalyzerFindsDistinctBuiltinChainProxyNames()
    {
        var names = new SubscriptionChainProxyAnalyzer().AnalyzeBuiltinChainProxyNames(
            """
            proxies:
              - name: JP
                type: ss
                server: jp.example
                dialer-proxy: HK
              - name: JP
                type: ss
                server: jp-alt.example
                dialer-proxy: HK
              - name: HK
                type: ss
                server: hk.example
              - name: Empty
                type: ss
                dialer-proxy: ''
            """);

        Assert.Equal(["JP"], names);
    }

    [Fact(DisplayName = "Context loader excludes builtin chain nodes from custom candidates")]
    public void ContextLoaderExcludesBuiltinChainNodesFromCustomCandidates()
    {
        var store = new FakeSubscriptionStore(Subscription("sub-1"),
            """
            proxies:
              - name: HK
                type: ss
                server: hk.example
              - name: JP
                type: ss
                server: jp.example
              - name: JP via HK
                type: ss
                server: jp.example
                dialer-proxy: HK
              - name: EmptyChain
                type: ss
                dialer-proxy: ''
            proxy-groups:
              - name: GLOBAL
                type: select
                proxies: [HK, JP]
              - name: Upstream
                type: select
                proxies: [GLOBAL]
            """);
        var loader = new SubscriptionChainProxyContextLoader(store, new PassthroughOverrideEngine());

        var context = loader.Load("sub-1");

        Assert.Equal(["JP via HK"], context.BuiltinChainProxyNames);
        Assert.Equal(["GLOBAL", "Upstream"], context.ProxyGroups.Select(group => group.Name));
        Assert.Equal(["HK", "JP", "EmptyChain", "GLOBAL", "Upstream"], context.Candidates.Select(candidate => candidate.Name));
        Assert.DoesNotContain(context.Candidates, candidate => candidate.Name == "JP via HK");
    }

    [Fact(DisplayName = "Runtime applier removes disabled builtin chain proxy only")]
    public void RuntimeApplierRemovesDisabledBuiltinChainProxyOnly()
    {
        var output = new SubscriptionChainProxyRuntimeApplier().Apply(
            """
            proxies:
              - name: HK
                type: ss
                server: hk.example
              - name: JP
                type: ss
                server: jp.example
              - name: JP via HK
                type: ss
                server: jp.example
                dialer-proxy: HK
            proxy-groups:
              - name: GLOBAL
                type: select
                proxies: [HK, JP via HK, JP]
            rules: []
            """,
            Subscription("sub-1") with
            {
                DisabledBuiltinChainProxyNames = ["JP via HK"]
            });
        var proxies = Proxies(output);

        Assert.DoesNotContain(proxies, proxy => Scalar(proxy, "name") == "JP via HK");
        Assert.Contains(proxies, proxy => Scalar(proxy, "name") == "HK");
        Assert.Contains(proxies, proxy => Scalar(proxy, "name") == "JP");
        Assert.Equal(["HK", "JP"], ProxyGroupEntries(output, "GLOBAL"));
    }

    [Fact(DisplayName = "Runtime applier adds custom chain with internal hop")]
    public void RuntimeApplierAddsCustomChainWithInternalHop()
    {
        var output = new SubscriptionChainProxyRuntimeApplier().Apply(BaseConfig(), Subscription("sub-1") with
        {
            CustomChainProxies =
            [
                Chain("chain-a", "JP via TW via HK", "GLOBAL", "HK", "TW", "JP")
            ]
        });
        var proxies = Proxies(output);
        var internalHop = proxies.Single(proxy => Scalar(proxy, "name") == "__stelliberty_chain_chain-a_1");
        var display = proxies.Single(proxy => Scalar(proxy, "name") == "JP via TW via HK");

        Assert.Equal("HK", Scalar(internalHop, "dialer-proxy"));
        Assert.Equal("__stelliberty_chain_chain-a_1", Scalar(display, "dialer-proxy"));
        Assert.Equal("jp.example", Scalar(display, "server"));
    }

    [Fact(DisplayName = "Runtime applier adds custom chain only to its owning group")]
    public void RuntimeApplierAddsCustomChainOnlyToItsOwningGroup()
    {
        var output = new SubscriptionChainProxyRuntimeApplier().Apply(
            """
            proxies:
              - name: HK
                type: ss
                server: hk.example
              - name: TW
                type: ss
                server: tw.example
              - name: JP
                type: ss
                server: jp.example
            proxy-groups:
              - name: GLOBAL
                type: select
                proxies: [HK, JP]
              - name: Regional
                type: select
                proxies: [HK, TW]
            rules: []
            """,
            Subscription("sub-1") with
            {
                CustomChainProxies =
                [
                    Chain("chain-a", "JP via HK", "Regional", "HK", "JP")
                ]
            });

        Assert.Equal(["HK", "JP"], ProxyGroupEntries(output, "GLOBAL"));
        Assert.Equal(["HK", "TW", "JP via HK"], ProxyGroupEntries(output, "Regional"));
    }

    [Fact(DisplayName = "Runtime applier creates proxies in an owning provider group")]
    public void RuntimeApplierCreatesProxiesInOwningProviderGroup()
    {
        var output = new SubscriptionChainProxyRuntimeApplier().Apply(
            """
            proxies:
              - name: HK
                type: ss
                server: hk.example
              - name: JP
                type: ss
                server: jp.example
            proxy-groups:
              - name: Provider Group
                type: select
                use: [remote]
            rules: []
            """,
            Subscription("sub-1") with
            {
                CustomChainProxies =
                [
                    Chain("chain-a", "JP via HK", "Provider Group", "HK", "JP")
                ]
            });

        Assert.Equal(["JP via HK"], ProxyGroupEntries(output, "Provider Group"));
    }

    [Fact(DisplayName = "Runtime applier overrides leaf dialer proxy")]
    public void RuntimeApplierOverridesLeafDialerProxy()
    {
        var output = new SubscriptionChainProxyRuntimeApplier().Apply(
            """
            proxies:
              - name: HK
                type: ss
                server: hk.example
              - name: JP
                type: ss
                server: jp.example
                dialer-proxy: OLD
            proxy-groups:
              - name: GLOBAL
                type: select
                proxies: [HK, JP]
            rules: []
            """,
            Subscription("sub-1") with
            {
                CustomChainProxies =
                [
                    Chain("chain-a", "JP via HK", "GLOBAL", "HK", "JP")
                ]
            });

        var display = Proxies(output).Single(proxy => Scalar(proxy, "name") == "JP via HK");

        Assert.Equal("HK", Scalar(display, "dialer-proxy"));
        Assert.Equal(["HK", "JP", "JP via HK"], ProxyGroupEntries(output, "GLOBAL"));
    }

    [Fact(DisplayName = "Runtime applier skips custom chain when display name is occupied")]
    public void RuntimeApplierSkipsCustomChainWhenDisplayNameIsOccupied()
    {
        var output = new SubscriptionChainProxyRuntimeApplier().Apply(BaseConfig(), Subscription("sub-1") with
        {
            CustomChainProxies =
            [
                Chain("conflict-node", "JP", "GLOBAL", "HK", "TW"),
                Chain("conflict-group", "GLOBAL", "GLOBAL", "HK", "TW")
            ]
        });

        Assert.DoesNotContain(Proxies(output), proxy => Scalar(proxy, "name").StartsWith("__stelliberty_chain_", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Runtime applier skips invalid custom chains")]
    public void RuntimeApplierSkipsInvalidCustomChains()
    {
        var output = new SubscriptionChainProxyRuntimeApplier().Apply(BaseConfig(), Subscription("sub-1") with
        {
            CustomChainProxies =
            [
                Chain("missing", "Missing chain", "GLOBAL", "HK", "Missing"),
                Chain("single", "Single chain", "GLOBAL", "HK")
            ]
        });

        Assert.DoesNotContain(Proxies(output), proxy => Scalar(proxy, "name") is "Missing chain" or "Single chain");
    }

    [Fact(DisplayName = "Runtime applier avoids internal name conflicts")]
    public void RuntimeApplierAvoidsInternalNameConflicts()
    {
        var output = new SubscriptionChainProxyRuntimeApplier().Apply(
            """
            proxies:
              - name: HK
                type: ss
                server: hk.example
              - name: TW
                type: ss
                server: tw.example
              - name: JP
                type: ss
                server: jp.example
              - name: __stelliberty_chain_chain-a_1
                type: ss
                server: occupied.example
            proxy-groups:
              - name: GLOBAL
                type: select
                proxies: [HK, TW, JP]
            rules: []
            """,
            Subscription("sub-1") with
            {
                CustomChainProxies =
                [
                    Chain("chain-a", "JP via TW via HK", "GLOBAL", "HK", "TW", "JP")
                ]
            });

        Assert.Contains(Proxies(output), proxy => Scalar(proxy, "name") == "__stelliberty_chain_chain-a_1_2");
    }

    [Fact(DisplayName = "Runtime applier supports a proxy group as the first hop")]
    public void RuntimeApplierSupportsProxyGroupAsFirstHop()
    {
        var output = new SubscriptionChainProxyRuntimeApplier().Apply(BaseConfig(), Subscription("sub-1") with
        {
            CustomChainProxies =
            [
                new SubscriptionCustomChainProxy(
                    "chain-group",
                    "JP via regional entry",
                    "GLOBAL",
                    [GroupHop("Regional Entry"), ProxyHop("JP")])
            ]
        });
        var display = Proxies(output).Single(proxy => Scalar(proxy, "name") == "JP via regional entry");

        Assert.Equal("Regional Entry", Scalar(display, "dialer-proxy"));
        Assert.Contains("JP via regional entry", ProxyGroupEntries(output, "GLOBAL"));
    }

    [Fact(DisplayName = "Runtime topology detects core-resolved mixed cycles")]
    public void RuntimeTopologyDetectsCoreResolvedMixedCycles()
    {
        var snapshot = new ProxyRuntimeSnapshot(
        [
            Entry("Cyclic chain", dialerProxy: "Upstream"),
            Entry("Owner", all: ["Cyclic chain"]),
            Entry("Upstream", all: ["Owner"]),
            Entry("Safe")
        ]);

        Assert.True(new SubscriptionChainProxyCycleDetector().HasCycle(snapshot));
    }

    [Fact(DisplayName = "Runtime topology uses core-resolved include-all members")]
    public void RuntimeTopologyUsesCoreResolvedIncludeAllMembers()
    {
        var snapshot = new ProxyRuntimeSnapshot(
        [
            Entry("Bridge", dialerProxy: "Auto Entry"),
            Entry("Auto Entry", all: ["Bridge", "Safe"]),
            Entry("Safe")
        ]);

        Assert.True(new SubscriptionChainProxyCycleDetector().HasCycle(snapshot));
    }

    [Fact(DisplayName = "Runtime topology ignores acyclic core-resolved members")]
    public void RuntimeTopologyIgnoresAcyclicCoreResolvedMembers()
    {
        var snapshot = new ProxyRuntimeSnapshot(
        [
            Entry("JP through Auto Entry", dialerProxy: "Auto Entry"),
            Entry("Owner", all: ["JP through Auto Entry"]),
            Entry("Auto Entry", all: ["HK"]),
            Entry("HK")
        ]);

        Assert.False(new SubscriptionChainProxyCycleDetector().HasCycle(snapshot));
    }

    [Fact(DisplayName = "Runtime topology detects self references")]
    public void RuntimeTopologyDetectsSelfReferences()
    {
        var snapshot = new ProxyRuntimeSnapshot([Entry("Self", all: ["Self"])]);

        Assert.True(new SubscriptionChainProxyCycleDetector().HasCycle(snapshot));
    }

    [Fact(DisplayName = "Runtime applier returns original content when no work can be done")]
    public void RuntimeApplierReturnsOriginalContentWhenNoWorkCanBeDone()
    {
        var content = BaseConfig();
        var invalid = "proxies: [";
        var applier = new SubscriptionChainProxyRuntimeApplier();

        Assert.Equal(content, applier.Apply(content, Subscription("sub-1")));
        Assert.Equal(invalid, applier.Apply(invalid, Subscription("sub-1") with
        {
            DisabledBuiltinChainProxyNames = ["JP via HK"]
        }));
    }

    private static ProxyRuntimeEntry Entry(
        string name,
        IReadOnlyList<string>? all = null,
        string? dialerProxy = null)
    {
        return new ProxyRuntimeEntry(name, "Test", null, null, all ?? [], false, DialerProxy: dialerProxy);
    }

    private static string BaseConfig()
    {
        return """
        proxies:
          - name: HK
            type: ss
            server: hk.example
          - name: TW
            type: ss
            server: tw.example
          - name: JP
            type: ss
            server: jp.example
          - name: JP via HK
            type: ss
            server: jp.example
            dialer-proxy: HK
        proxy-groups:
          - name: GLOBAL
            type: select
            proxies: [HK, TW, JP]
          - name: Regional Entry
            type: select
            proxies: [HK, TW]
        rules: []
        """;
    }

    private static SubscriptionCustomChainProxy Chain(
        string id,
        string displayName,
        string proxyGroupName,
        params string[] proxyNames)
    {
        return new SubscriptionCustomChainProxy(
            id,
            displayName,
            proxyGroupName,
            proxyNames.Select(ProxyHop).ToList());
    }

    private static SubscriptionChainProxyHop ProxyHop(string name)
        => new(SubscriptionChainProxyHopKind.Proxy, name);

    private static SubscriptionChainProxyHop GroupHop(string name)
        => new(SubscriptionChainProxyHopKind.ProxyGroup, name);

    private static Subscription Subscription(string id)
    {
        return new Subscription(id, "Sub", "source", false, DateTimeOffset.UnixEpoch);
    }

    private static IReadOnlyList<YamlMappingNode> Proxies(string content)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(content));
        var root = (YamlMappingNode)stream.Documents[0].RootNode;
        return ((YamlSequenceNode)root.Children[new YamlScalarNode("proxies")]).Children.OfType<YamlMappingNode>().ToList();
    }

    private static IReadOnlyList<string> ProxyGroupEntries(string content, string groupName)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(content));
        var root = (YamlMappingNode)stream.Documents[0].RootNode;
        var groups = ((YamlSequenceNode)root.Children[new YamlScalarNode("proxy-groups")]).Children.OfType<YamlMappingNode>();
        var group = groups.Single(group => Scalar(group, "name") == groupName);
        return ((YamlSequenceNode)group.Children[new YamlScalarNode("proxies")]).Children.Select(node => node.ToString()).ToList();
    }

    private static string Scalar(YamlMappingNode mapping, string key)
    {
        return mapping.Children.TryGetValue(new YamlScalarNode(key), out var value) ? value.ToString() : string.Empty;
    }

    private sealed class FakeSubscriptionStore(Subscription subscription, string content) : ISubscriptionStore
    {
        public void Save(Subscription subscription, string originalContent)
        {
        }

        public void UpdateSubscription(Subscription subscription)
        {
        }

        public void SaveSubscriptions(IReadOnlyList<Subscription> subscriptions)
        {
        }

        public void SaveContent(string subscriptionId, string originalContent)
        {
        }

        public IReadOnlyList<Subscription> LoadSubscriptions() => [subscription];

        public string ReadContent(string subscriptionId) => content;

        public string GetContentPath(string subscriptionId) => $"{subscriptionId}.yaml";

        public void Delete(string subscriptionId)
        {
        }
    }

    private sealed class PassthroughOverrideEngine : IConfigOverrideEngine
    {
        public string Apply(string baseConfigContent, RuntimeOverride runtimeOverride) => baseConfigContent;
    }
}

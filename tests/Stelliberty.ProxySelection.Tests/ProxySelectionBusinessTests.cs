using Stelliberty.Application.Proxies;
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

    private static ProxyConfig TestConfig(IReadOnlyList<ProxyGroup> groups, OutboundMode? mode = null)
    {
        return new ProxyConfig(
            groups,
            new Dictionary<string, ProxyNode>(StringComparer.Ordinal)
            {
                ["NodeA"] = new("NodeA", "ss"),
                ["NodeB"] = new("NodeB", "ss")
            },
            mode);
    }
}

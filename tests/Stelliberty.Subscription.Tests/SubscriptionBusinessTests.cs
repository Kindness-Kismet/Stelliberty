using System.Text;
using Stelliberty.Application.Subscriptions;
using Stelliberty.Domain.Subscriptions;
using Xunit;
using DomainSubscription = Stelliberty.Domain.Subscriptions.Subscription;

namespace Stelliberty.Subscription.Tests;

public sealed class SubscriptionBusinessTests
{
    [Fact(DisplayName = "Auto update planner filters startup and due interval subscriptions")]
    public void AutoUpdatePlannerFiltersStartupAndDueIntervalSubscriptions()
    {
        var now = DateTimeOffset.UnixEpoch.AddHours(2);
        var subscriptions = new[]
        {
            Subscription("startup") with { AutoUpdateMode = SubscriptionAutoUpdateMode.Startup },
            Subscription("local", isLocal: true) with { AutoUpdateMode = SubscriptionAutoUpdateMode.Startup },
            Subscription("due") with { AutoUpdateMode = SubscriptionAutoUpdateMode.Interval, AutoUpdateIntervalMinutes = 30, LastUpdatedAt = now.AddHours(-1) },
            Subscription("fresh") with { AutoUpdateMode = SubscriptionAutoUpdateMode.Interval, AutoUpdateIntervalMinutes = 30, LastUpdatedAt = now.AddMinutes(-5) }
        };
        var planner = new SubscriptionAutoUpdatePlanner();

        var startup = planner.PlanStartupUpdates(subscriptions);
        var interval = planner.PlanDueIntervalUpdates(subscriptions, now);

        Assert.Equal(["startup"], startup.UpdateSubscriptionIds);
        Assert.Equal(["due"], interval.UpdateSubscriptionIds);
        Assert.DoesNotContain("fresh", interval.UpdateSubscriptionIds);
    }

    [Fact(DisplayName = "Auto update planner waits one interval after failed attempt")]
    public void AutoUpdatePlannerWaitsOneIntervalAfterFailedAttempt()
    {
        var now = DateTimeOffset.UnixEpoch.AddHours(2);
        var subscription = Subscription("failed") with
        {
            AutoUpdateMode = SubscriptionAutoUpdateMode.Interval,
            AutoUpdateIntervalMinutes = 30,
            LastUpdatedAt = now.AddHours(-1),
            LastErrorAt = now.AddMinutes(-5)
        };
        var planner = new SubscriptionAutoUpdatePlanner();

        Assert.Empty(planner.PlanDueIntervalUpdates([subscription], now).UpdateSubscriptionIds);
        Assert.Equal(["failed"], planner.PlanDueIntervalUpdates([subscription], now.AddMinutes(25)).UpdateSubscriptionIds);
    }

    [Fact(DisplayName = "Provider parser handles YAML merge and counts")]
    public void ProviderParserHandlesYamlMergeAndCounts()
    {
        var providers = new SubscriptionProviderParser().Parse(
            """
            defaults: &defaults
              type: http
              path: ./provider.yaml
            proxy-providers:
              hk:
                <<: *defaults
                proxies:
                  - name: HK
                  - name: TW
            rule-providers:
              reject:
                <<: *defaults
                ruleCount: 3
            """);

        Assert.Contains(providers, provider => provider.Name == "hk" && provider.Type == "proxy" && provider.VehicleType == "HTTP" && provider.Count == 2);
        Assert.Contains(providers, provider => provider.Name == "reject" && provider.Type == "rule" && provider.Count == 3);
    }

    [Fact(DisplayName = "Content normalizer keeps clash YAML and converts proxy links")]
    public void ContentNormalizerKeepsClashYamlAndConvertsProxyLinks()
    {
        var normalizer = new SubscriptionContentNormalizer();
        var clash = "proxy-groups: []\nproxies: []\nrules: []";
        var converted = normalizer.Normalize("ss://aes-128-gcm:pwd@server.example:8388#HK");

        Assert.Equal(clash, normalizer.Normalize(clash));
        Assert.Contains("proxy-groups", converted, StringComparison.Ordinal);
        Assert.Contains("HK", converted, StringComparison.Ordinal);
        Assert.Contains("MATCH,PROXY", converted, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Content normalizer converts base64 v2ray link subscription variants")]
    public void ContentNormalizerConvertsBase64V2RayLinkSubscriptionVariants()
    {
        var normalizer = new SubscriptionContentNormalizer();
        var vmessJson =
            """
            {"ps":"VMess WS","add":"vmess.example","port":"443","id":"11111111-1111-1111-1111-111111111111","aid":"0","scy":"auto","net":"ws","host":"ws.example","path":"/ws","tls":"tls","sni":"tls.example","fp":"chrome","alpn":"h2,http/1.1","allowInsecure":"1"}
            """;
        var vmess = $"vmess://{Base64UrlNoPadding(vmessJson)}";
        var vmessAead = "vmess://44444444-4444-4444-4444-444444444444@aead.example:443?encryption=auto&security=tls&type=http&host=h2.example&path=/h2&sni=aead.example&fp=chrome#VMess%20AEAD";
        var vless = "vless://22222222-2222-2222-2222-222222222222@vless.example:443?encryption=none&security=reality&sni=reality.example&fp=chrome&pbk=public-key&sid=ab12&type=grpc&serviceName=svc&flow=xtls-rprx-vision&allowInsecure=1&alpn=h2,http/1.1#VLESS%20Reality";
        var trojan = "trojan://secret@trojan.example:443?type=ws&host=trojan-host.example&path=/trojan&sni=trojan.example&fp=firefox&alpn=h2#Trojan%20WS";
        var shadowsocks = $"ss://{Base64UrlNoPadding("aes-128-gcm:pwd@ss.example:8388")}#SS%20Full";
        var hysteria2 = "hy2://hy-pass@hy2.example:443?sni=hy2.example&alpn=h3&pinSHA256=sha256-pin&up=30Mbps&down=100Mbps#HY2";
        var tuic = "tuic://33333333-3333-3333-3333-333333333333:tuic-pass@tuic.example:443?congestion_control=bbr&udp_relay_mode=native&alpn=h3&sni=tuic.example#TUIC";
        var encodedSubscription = Base64UrlNoPadding(string.Join('\n', [vmess, vmessAead, vless, trojan, shadowsocks, hysteria2, tuic]));

        var converted = normalizer.Normalize(encodedSubscription);

        new SubscriptionConfigValidator().Validate(converted);
        Assert.Contains("name: VMess WS", converted, StringComparison.Ordinal);
        Assert.Contains("type: vmess", converted, StringComparison.Ordinal);
        Assert.Contains("client-fingerprint: chrome", converted, StringComparison.Ordinal);
        Assert.Contains("name: VMess AEAD", converted, StringComparison.Ordinal);
        Assert.Contains("h2-opts", converted, StringComparison.Ordinal);
        Assert.Contains("name: VLESS Reality", converted, StringComparison.Ordinal);
        Assert.Contains("type: vless", converted, StringComparison.Ordinal);
        Assert.Contains("encryption: none", converted, StringComparison.Ordinal);
        Assert.Contains("reality-opts", converted, StringComparison.Ordinal);
        Assert.Contains("public-key: public-key", converted, StringComparison.Ordinal);
        Assert.Contains("short-id: ab12", converted, StringComparison.Ordinal);
        Assert.Contains("grpc-service-name: svc", converted, StringComparison.Ordinal);
        Assert.Contains("name: Trojan WS", converted, StringComparison.Ordinal);
        Assert.Contains("type: trojan", converted, StringComparison.Ordinal);
        Assert.Contains("name: SS Full", converted, StringComparison.Ordinal);
        Assert.Contains("server: ss.example", converted, StringComparison.Ordinal);
        Assert.Contains("name: HY2", converted, StringComparison.Ordinal);
        Assert.Contains("fingerprint: sha256-pin", converted, StringComparison.Ordinal);
        Assert.Contains("up: 30Mbps", converted, StringComparison.Ordinal);
        Assert.Contains("down: 100Mbps", converted, StringComparison.Ordinal);
        Assert.Contains("name: TUIC", converted, StringComparison.Ordinal);
        Assert.Contains("congestion-controller: bbr", converted, StringComparison.Ordinal);
        Assert.Contains("udp-relay-mode: native", converted, StringComparison.Ordinal);
        Assert.Contains("MATCH,PROXY", converted, StringComparison.Ordinal);
    }

    private static DomainSubscription Subscription(string id, bool isLocal = false)
    {
        return new DomainSubscription(id, id, isLocal ? "local.yaml" : "https://sub.example/config.yaml", isLocal, DateTimeOffset.UnixEpoch);
    }

    private static string Base64UrlNoPadding(string value)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}

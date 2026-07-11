namespace Stelliberty.Application.Settings;

public static class DnsDefaults
{
    public const string EnhancedMode = "fake-ip";
    public const string FakeIpRange = "198.18.0.1/16";
    public const string FakeIpFilterMode = "blacklist";
    public const string FallbackFilterGeoIpCode = "CN";

    public static IReadOnlyList<string> NameServers { get; } = ["8.8.8.8", "https://doh.pub/dns-query", "https://dns.alidns.com/dns-query"];

    public static IReadOnlyList<string> ProxyServerNameServers { get; } = ["https://doh.pub/dns-query", "https://dns.alidns.com/dns-query", "tls://223.5.5.5"];

    public static IReadOnlyList<string> DefaultNameServers { get; } = ["system", "223.6.6.6", "8.8.8.8", "2400:3200::1", "2001:4860:4860::8888"];

    public static IReadOnlyList<string> FakeIpFilters { get; } = ["*.lan", "*.local", "*.arpa", "time.*.com", "ntp.*.com", "+.market.xiaomi.com", "localhost.ptlogin2.qq.com", "*.msftncsi.com", "www.msftconnecttest.com"];

    public static IReadOnlyList<string> FallbackFilterIpCidrs { get; } = ["240.0.0.0/4", "0.0.0.0/32"];

    public static IReadOnlyList<string> FallbackFilterDomains { get; } = ["+.google.com", "+.facebook.com", "+.youtube.com"];
}

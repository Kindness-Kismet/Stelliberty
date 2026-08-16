#if DEBUG
using System.Globalization;
using Stelliberty.Presentation.ViewModels;

namespace Stelliberty.Desktop.Debug;

internal static partial class DebugCommands
{
    private static async Task<string?> ExecuteClashCommandAsync(MainWindow window, string command)
    {
        var viewModel = RequireViewModel(window);
        var spec = command["clash.".Length..].Trim();
        if (string.Equals(spec, "list keys", StringComparison.OrdinalIgnoreCase))
        {
            return string.Join("|", ClashSettingKeys().Select(key => $"{key}\teffect={ClashSettingEffect(key)}"));
        }

        if (string.Equals(spec, "state", StringComparison.OrdinalIgnoreCase))
        {
            return ClashState(viewModel);
        }

        if (spec.StartsWith("get ", StringComparison.OrdinalIgnoreCase))
        {
            return ReadClashSetting(viewModel, spec["get ".Length..].Trim());
        }

        if (spec.StartsWith("set ", StringComparison.OrdinalIgnoreCase))
        {
            var parts = spec["set ".Length..].Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
            {
                throw new InvalidOperationException("clash.set usage: clash.set <key> <value>");
            }

            var key = parts[0];
            var effect = ClashSettingEffect(key);
            var before = ReadClashSetting(viewModel, key);
            SetClashSetting(viewModel, key, parts[1]);
            if (effect is not ("app-only" or "next-restart"))
            {
                await WaitRuntimeRefreshAsync(viewModel);
            }
            var after = ReadClashSetting(viewModel, key);
            return ClashState(viewModel, key, before, after, effect);
        }

        throw new InvalidOperationException($"Unknown Clash command: {command}");
    }

    private static string ClashState(MainWindowViewModel viewModel)
    {
        return string.Join(";", [
            $"areas={string.Join(',', ClashChangeAreas(viewModel))}",
            $"logLevels={string.Join(',', viewModel.CoreConfig.CoreLogLevelChangeRequests)}",
            $"subscription={viewModel.LastRuntimeRefreshSubscriptionId ?? string.Empty}",
            $"apply={viewModel.LastRuntimeApplyMode}",
            $"pid={viewModel.LastRuntimeApplyPid?.ToString(CultureInfo.InvariantCulture) ?? string.Empty}",
            $"error={viewModel.LastRuntimeApplyError ?? string.Empty}"
        ]);
    }

    private static string ClashState(MainWindowViewModel viewModel, string key, string before, string after, string effect)
    {
        return string.Join(";", [
            $"key={key}",
            $"effect={effect}",
            $"before={before}",
            $"after={after}",
            $"changed={(!string.Equals(before, after, StringComparison.Ordinal)).ToString().ToLowerInvariant()}",
            effect is "app-only" or "next-restart" ? ClashLocalState(viewModel, effect) : ClashState(viewModel)
        ]);
    }

    private static string ClashLocalState(MainWindowViewModel viewModel, string effect)
    {
        return string.Join(";", [
            $"areas={string.Join(',', ClashChangeAreas(viewModel))}",
            $"logLevels={string.Join(',', viewModel.CoreConfig.CoreLogLevelChangeRequests)}",
            $"subscription={viewModel.LastRuntimeRefreshSubscriptionId ?? string.Empty}",
            $"apply={effect}",
            "pid=",
            "error="
        ]);
    }

    private static IEnumerable<string> ClashChangeAreas(MainWindowViewModel viewModel)
    {
        return viewModel.CoreConfig.ChangeAreas
            .Distinct(StringComparer.Ordinal);
    }

    private static IReadOnlyList<string> ClashSettingKeys()
    {
        return
        [
            "network.unified-delay",
            "network.delay-url",
            "network.allow-lan",
            "network.lan-auth-user",
            "network.lan-auth-password",
            "network.lan-allowed-ips",
            "network.lan-disallowed-ips",
            "network.skip-auth-prefixes",
            "network.ipv6",
            "network.tcp-concurrent",
            "port.mixed",
            "port.socks",
            "port.http",
            "port.controller-enabled",
            "port.controller-address",
            "port.controller-secret",
            "tun.enable",
            "tun.stack",
            "tun.device",
            "tun.auto-route",
            "tun.auto-redirect",
            "tun.auto-detect-interface",
            "tun.strict-route",
            "tun.dns-hijack",
            "tun.route-exclude-address",
            "tun.disable-icmp-forwarding",
            "tun.mtu",
            "dns.override",
            "dns.enable",
            "dns.listen",
            "dns.enhanced-mode",
            "dns.fake-ip-range",
            "dns.respect-rules",
            "dns.nameserver",
            "dns.fallback",
            "dns.proxy-server-nameserver",
            "dns.default-nameserver",
            "dns.fake-ip-filter",
            "dns.fallback-filter-geoip-code",
            "dns.hosts",
            "dns.ipv6",
            "dns.use-hosts",
            "dns.use-system-hosts",
            "dns.direct-nameserver",
            "dns.nameserver-policy",
            "dns.prefer-h3",
            "dns.fake-ip-filter-mode",
            "dns.direct-nameserver-follow-policy",
            "dns.fallback-filter-geoip",
            "dns.fallback-filter-ipcidr",
            "dns.fallback-filter-domain",
            "performance.geodata-loader",
            "performance.find-process-mode",
            "performance.tcp-keep-alive",
            "performance.tcp-keep-alive-interval",
            "core-log.level"
        ];
    }

    private static string ClashSettingEffect(string key)
    {
        return key.ToLowerInvariant() switch
        {
            "network.delay-url" => "app-only",
            "port.controller-enabled" => "restart",
            "port.controller-address" or "port.controller-secret" => "next-restart",
            "performance.tcp-keep-alive" or "performance.tcp-keep-alive-interval" => "restart",
            _ => "reload"
        };
    }

    private static string ReadClashSetting(MainWindowViewModel viewModel, string key)
    {
        var config = viewModel.CoreConfig;
        return key.ToLowerInvariant() switch
        {
            "network.unified-delay" => Bool(config.IsUnifiedDelayEnabled),
            "network.delay-url" => config.DelayTestUrl,
            "network.allow-lan" => Bool(config.IsAllowLanEnabled),
            "network.lan-auth-user" => config.LanAuthenticationUserName,
            "network.lan-auth-password" => config.LanAuthenticationPassword,
            "network.lan-allowed-ips" => ListValue(config.LanAllowedIpsText),
            "network.lan-disallowed-ips" => ListValue(config.LanDisallowedIpsText),
            "network.skip-auth-prefixes" => ListValue(config.SkipAuthPrefixesText),
            "network.ipv6" => Bool(config.IsIpv6Enabled),
            "network.tcp-concurrent" => Bool(config.IsTcpConcurrentEnabled),
            "port.mixed" => config.MixedPortText,
            "port.socks" => config.SocksPortText,
            "port.http" => config.HttpPortText,
            "port.controller-enabled" => Bool(config.IsExternalControllerEnabled),
            "port.controller-address" => config.ExternalControllerAddress,
            "port.controller-secret" => config.ExternalControllerSecret,
            "tun.enable" => Bool(config.IsTunEnabled),
            "tun.stack" => config.TunStack,
            "tun.device" => config.TunDevice,
            "tun.auto-route" => Bool(config.IsTunAutoRouteEnabled),
            "tun.auto-redirect" => Bool(config.IsTunAutoRedirectEnabled),
            "tun.auto-detect-interface" => Bool(config.IsTunAutoDetectInterfaceEnabled),
            "tun.strict-route" => Bool(config.IsTunStrictRouteEnabled),
            "tun.dns-hijack" => ListValue(config.TunDnsHijackText),
            "tun.route-exclude-address" => ListValue(config.TunRouteExcludeAddressesText),
            "tun.disable-icmp-forwarding" => Bool(config.IsTunIcmpForwardingDisabled),
            "tun.mtu" => config.TunMtuText,
            "dns.override" => Bool(config.IsDnsOverrideEnabled),
            "dns.enable" => Bool(config.IsDnsEnabled),
            "dns.listen" => config.DnsListen,
            "dns.enhanced-mode" => config.DnsEnhancedMode,
            "dns.fake-ip-range" => config.FakeIpRange,
            "dns.respect-rules" => Bool(config.IsDnsRespectRulesEnabled),
            "dns.nameserver" => ListValue(config.NameServersText),
            "dns.fallback" => ListValue(config.FallbackNameServersText),
            "dns.proxy-server-nameserver" => ListValue(config.ProxyServerNameServersText),
            "dns.default-nameserver" => ListValue(config.DefaultNameServersText),
            "dns.fake-ip-filter" => ListValue(config.FakeIpFiltersText),
            "dns.fallback-filter-geoip-code" => config.FallbackFilterGeoIpCode,
            "dns.hosts" => ListValue(config.HostsText),
            "dns.ipv6" => Bool(config.IsDnsIpv6Enabled),
            "dns.use-hosts" => Bool(config.IsDnsUseHostsEnabled),
            "dns.use-system-hosts" => Bool(config.IsDnsUseSystemHostsEnabled),
            "dns.direct-nameserver" => ListValue(config.DirectNameServersText),
            "dns.nameserver-policy" => ListValue(config.NameServerPolicyText),
            "dns.prefer-h3" => Bool(config.IsDnsPreferH3Enabled),
            "dns.fake-ip-filter-mode" => config.FakeIpFilterMode,
            "dns.direct-nameserver-follow-policy" => Bool(config.IsDirectNameServerFollowPolicyEnabled),
            "dns.fallback-filter-geoip" => Bool(config.IsFallbackFilterGeoIpEnabled),
            "dns.fallback-filter-ipcidr" => ListValue(config.FallbackFilterIpCidrsText),
            "dns.fallback-filter-domain" => ListValue(config.FallbackFilterDomainsText),
            "performance.geodata-loader" => config.GeoDataLoader,
            "performance.find-process-mode" => config.FindProcessMode,
            "performance.tcp-keep-alive" => Bool(config.IsTcpKeepAliveEnabled),
            "performance.tcp-keep-alive-interval" => config.TcpKeepAliveIntervalText,
            "core-log.level" => config.SelectedCoreLogLevelOption.Value,
            _ => throw new InvalidOperationException($"Unknown Clash setting: {key}")
        };
    }

    private static void SetClashSetting(MainWindowViewModel viewModel, string key, string value)
    {
        var config = viewModel.CoreConfig;
        var normalizedValue = NormalizeInputValue(value);
        switch (key.ToLowerInvariant())
        {
            case "network.unified-delay": config.IsUnifiedDelayEnabled = ParseBool(normalizedValue); break;
            case "network.delay-url": config.DelayTestUrl = normalizedValue; break;
            case "network.allow-lan": config.IsAllowLanEnabled = ParseBool(normalizedValue); break;
            case "network.lan-auth-user": config.LanAuthenticationUserName = normalizedValue; break;
            case "network.lan-auth-password": config.LanAuthenticationPassword = normalizedValue; break;
            case "network.lan-allowed-ips": config.LanAllowedIpsText = NormalizeListInput(normalizedValue); break;
            case "network.lan-disallowed-ips": config.LanDisallowedIpsText = NormalizeListInput(normalizedValue); break;
            case "network.skip-auth-prefixes": config.SkipAuthPrefixesText = NormalizeListInput(normalizedValue); break;
            case "network.ipv6": config.IsIpv6Enabled = ParseBool(normalizedValue); break;
            case "network.tcp-concurrent": config.IsTcpConcurrentEnabled = ParseBool(normalizedValue); break;
            case "port.mixed": config.MixedPortText = normalizedValue; break;
            case "port.socks": config.SocksPortText = normalizedValue; break;
            case "port.http": config.HttpPortText = normalizedValue; break;
            case "port.controller-enabled": config.IsExternalControllerEnabled = ParseBool(normalizedValue); break;
            case "port.controller-address": config.ExternalControllerAddress = normalizedValue; break;
            case "port.controller-secret": config.ExternalControllerSecret = normalizedValue; break;
            case "tun.enable": config.IsTunEnabled = ParseBool(normalizedValue); break;
            case "tun.stack": config.TunStack = normalizedValue; break;
            case "tun.device": config.TunDevice = normalizedValue; break;
            case "tun.auto-route": config.IsTunAutoRouteEnabled = ParseBool(normalizedValue); break;
            case "tun.auto-redirect": config.IsTunAutoRedirectEnabled = ParseBool(normalizedValue); break;
            case "tun.auto-detect-interface": config.IsTunAutoDetectInterfaceEnabled = ParseBool(normalizedValue); break;
            case "tun.strict-route": config.IsTunStrictRouteEnabled = ParseBool(normalizedValue); break;
            case "tun.dns-hijack": config.TunDnsHijackText = NormalizeListInput(normalizedValue); break;
            case "tun.route-exclude-address": config.TunRouteExcludeAddressesText = NormalizeListInput(normalizedValue); break;
            case "tun.disable-icmp-forwarding": config.IsTunIcmpForwardingDisabled = ParseBool(normalizedValue); break;
            case "tun.mtu": config.TunMtuText = normalizedValue; break;
            case "dns.override": config.IsDnsOverrideEnabled = ParseBool(normalizedValue); break;
            case "dns.enable": config.IsDnsEnabled = ParseBool(normalizedValue); break;
            case "dns.listen": config.DnsListen = normalizedValue; break;
            case "dns.enhanced-mode": config.DnsEnhancedMode = normalizedValue; break;
            case "dns.fake-ip-range": config.FakeIpRange = normalizedValue; break;
            case "dns.respect-rules": config.IsDnsRespectRulesEnabled = ParseBool(normalizedValue); break;
            case "dns.nameserver": config.NameServersText = NormalizeListInput(normalizedValue); break;
            case "dns.fallback": config.FallbackNameServersText = NormalizeListInput(normalizedValue); break;
            case "dns.proxy-server-nameserver": config.ProxyServerNameServersText = NormalizeListInput(normalizedValue); break;
            case "dns.default-nameserver": config.DefaultNameServersText = NormalizeListInput(normalizedValue); break;
            case "dns.fake-ip-filter": config.FakeIpFiltersText = NormalizeListInput(normalizedValue); break;
            case "dns.fallback-filter-geoip-code": config.FallbackFilterGeoIpCode = normalizedValue; break;
            case "dns.hosts": config.HostsText = NormalizeListInput(normalizedValue); break;
            case "dns.ipv6": config.IsDnsIpv6Enabled = ParseBool(normalizedValue); break;
            case "dns.use-hosts": config.IsDnsUseHostsEnabled = ParseBool(normalizedValue); break;
            case "dns.use-system-hosts": config.IsDnsUseSystemHostsEnabled = ParseBool(normalizedValue); break;
            case "dns.direct-nameserver": config.DirectNameServersText = NormalizeListInput(normalizedValue); break;
            case "dns.nameserver-policy": config.NameServerPolicyText = NormalizeListInput(normalizedValue); break;
            case "dns.prefer-h3": config.IsDnsPreferH3Enabled = ParseBool(normalizedValue); break;
            case "dns.fake-ip-filter-mode": config.FakeIpFilterMode = normalizedValue; break;
            case "dns.direct-nameserver-follow-policy": config.IsDirectNameServerFollowPolicyEnabled = ParseBool(normalizedValue); break;
            case "dns.fallback-filter-geoip": config.IsFallbackFilterGeoIpEnabled = ParseBool(normalizedValue); break;
            case "dns.fallback-filter-ipcidr": config.FallbackFilterIpCidrsText = NormalizeListInput(normalizedValue); break;
            case "dns.fallback-filter-domain": config.FallbackFilterDomainsText = NormalizeListInput(normalizedValue); break;
            case "performance.geodata-loader": config.GeoDataLoader = normalizedValue; break;
            case "performance.find-process-mode": config.FindProcessMode = normalizedValue; break;
            case "performance.tcp-keep-alive": config.IsTcpKeepAliveEnabled = ParseBool(normalizedValue); break;
            case "performance.tcp-keep-alive-interval": config.TcpKeepAliveIntervalText = normalizedValue; break;
            case "core-log.level": config.SelectedCoreLogLevelOption = config.CoreLogLevelOptions.First(option => option.Value == normalizedValue); break;
            default: throw new InvalidOperationException($"Unknown Clash setting: {key}");
        }
    }
}
#endif

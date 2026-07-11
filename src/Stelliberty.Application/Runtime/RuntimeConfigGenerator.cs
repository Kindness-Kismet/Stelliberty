using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Overrides;
using Stelliberty.Domain.Overrides;
using Stelliberty.Application.Settings;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Stelliberty.Application.Runtime;

public sealed class RuntimeConfigGenerator(IConfigOverrideEngine? overrideEngine = null)
{
    private const string EmptyBaseConfigContent = """
proxies: []
proxy-groups: []
rules: []
""";
    private readonly IConfigOverrideEngine? _overrideEngine = overrideEngine;

    public RuntimeConfigGenerationResult Generate(RuntimeConfigGenerationRequest request)
    {
        var config = LoadRootMap(request.BaseConfigContent, "Config");
        config = ApplyOverrides(config, request.Overrides);
        config = ApplyPostOverrideTransform(config, request.PostOverrideTransform);
        InjectRuntimeParams(config, request.RuntimeParams);

        return new RuntimeConfigGenerationResult(Serialize(config));
    }

    public RuntimeConfigGenerationResult GenerateEmpty(RuntimeConfigParams runtimeParams)
    {
        var config = LoadRootMap(EmptyBaseConfigContent, "Empty config");
        InjectRuntimeParams(config, runtimeParams);
        return new RuntimeConfigGenerationResult(Serialize(config));
    }

    private static YamlMappingNode LoadRootMap(string content, string label)
    {
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(content));

            return stream.Documents[0].RootNode as YamlMappingNode
                ?? throw new InvalidOperationException($"{label} root node must be a mapping");
        }
        catch (YamlException exception)
        {
            throw new InvalidOperationException($"{label} is not valid YAML", exception);
        }
    }

    private YamlMappingNode ApplyOverrides(YamlMappingNode config, IReadOnlyList<RuntimeOverride> overrides)
    {
        if (overrides.Count == 0)
        {
            return config;
        }
        if (_overrideEngine is null)
        {
            throw new InvalidOperationException("Config override engine is not initialized");
        }

        var currentConfigContent = Serialize(config);
        foreach (var runtimeOverride in overrides)
        {
            currentConfigContent = _overrideEngine.Apply(currentConfigContent, runtimeOverride);
        }

        return LoadRootMap(currentConfigContent, "Override output config");
    }

    // 覆写后转换依赖覆写结果，必须先于运行时注入。
    private static YamlMappingNode ApplyPostOverrideTransform(YamlMappingNode config, Func<string, string>? transform)
    {
        if (transform is null)
        {
            return config;
        }

        return LoadRootMap(transform(Serialize(config)), "Chain proxy output config");
    }

    private static void InjectRuntimeParams(YamlMappingNode config, RuntimeConfigParams parameters)
    {
        ValidateRuntimePorts(parameters);
        Set(config, "mixed-port", parameters.MixedPort);
        SetOrRemovePositiveInt(config, "port", parameters.HttpPort);
        SetOrRemovePositiveInt(config, "socks-port", parameters.SocksPort);
        Set(config, "allow-lan", parameters.IsAllowLanEnabled);
        Set(config, "bind-address", parameters.IsAllowLanEnabled ? "0.0.0.0" : "127.0.0.1");
        Set(config, "mode", parameters.OutboundMode.ToLowerInvariant());
        Set(config, "ipv6", parameters.IsIpv6Enabled);
        Set(config, "tcp-concurrent", parameters.IsTcpConcurrentEnabled);
        Set(config, "unified-delay", parameters.IsUnifiedDelayEnabled);
        Set(config, "find-process-mode", parameters.FindProcessMode);
        Set(config, "geodata-loader", parameters.GeodataLoader);
        Set(config, "log-level", parameters.ClashCoreLogLevel);
        InjectIpcEndpoint(config);
        InjectExternalController(config, parameters);
        InjectKeepAlive(config, parameters);
        InjectLanSettings(config, parameters);
        Set(config, "tun", CreateTunConfig(parameters));
        InjectDnsSettings(config, parameters);
    }

    private static void ValidateRuntimePorts(RuntimeConfigParams parameters)
    {
        // 端口遵循 TCP/UDP 范围；可选端口用 0 表示关闭。
        ValidateRequiredPort("mixed-port", parameters.MixedPort);
        ValidateOptionalPort("port", parameters.HttpPort);
        ValidateOptionalPort("socks-port", parameters.SocksPort);
    }

    private static void ValidateRequiredPort(string key, int value)
    {
        if (value is < 1 or > 65535)
        {
            throw new InvalidOperationException($"Runtime port is invalid: {key}={value}");
        }
    }

    private static void ValidateOptionalPort(string key, int value)
    {
        if (value is < 0 or > 65535)
        {
            throw new InvalidOperationException($"Runtime port is invalid: {key}={value}");
        }
    }

    private static void InjectIpcEndpoint(YamlMappingNode config)
    {
        // hub 在 apply_config 时写入管道端点；运行时 YAML 会丢弃过期端点。
        Remove(config, "external-controller-pipe");
        Remove(config, "external-controller-unix");
    }

    private static void InjectExternalController(YamlMappingNode config, RuntimeConfigParams parameters)
    {
        if (string.IsNullOrWhiteSpace(parameters.ExternalController))
        {
            Remove(config, "external-controller");
            Remove(config, "secret");
            return;
        }

        Set(config, "external-controller", parameters.ExternalController);
        if (string.IsNullOrWhiteSpace(parameters.ExternalControllerSecret))
        {
            Remove(config, "secret");
            return;
        }

        Set(config, "secret", parameters.ExternalControllerSecret);
    }

    private static void InjectKeepAlive(YamlMappingNode config, RuntimeConfigParams parameters)
    {
        if (parameters.IsKeepAliveEnabled)
        {
            if (parameters.KeepAliveInterval is { } interval)
            {
                Set(config, "keep-alive-interval", interval);
            }

            return;
        }

        Remove(config, "keep-alive-interval");
    }

    private static void InjectLanSettings(YamlMappingNode config, RuntimeConfigParams parameters)
    {
        SetOrRemoveSequence(config, "authentication", parameters.LanAuthentication);
        SetOrRemoveSequence(config, "lan-allowed-ips", parameters.LanAllowedIps);
        SetOrRemoveSequence(config, "lan-disallowed-ips", parameters.LanDisallowedIps);
        SetOrRemoveSequence(config, "skip-auth-prefixes", parameters.SkipAuthPrefixes);
    }

    private static void InjectDnsSettings(YamlMappingNode config, RuntimeConfigParams parameters)
    {
        if (parameters.IsDnsOverrideEnabled)
        {
            if (string.IsNullOrWhiteSpace(parameters.DnsOverrideContent))
            {
                AppLogger.Warning("DNS override is enabled but empty");
                return;
            }

            InjectDnsOverride(config, parameters.DnsOverrideContent);
            return;
        }

        if (parameters.IsTunEnabled)
        {
            InjectTunDefaultDns(config, parameters);
        }
    }

    private static void InjectDnsOverride(YamlMappingNode config, string dnsOverrideContent)
    {
        var dnsOverride = LoadRootMap(dnsOverrideContent, "DNS override");
        if (dnsOverride.Children.TryGetValue(new YamlScalarNode("dns"), out var dns))
        {
            Set(config, "dns", dns);
        }

        if (dnsOverride.Children.TryGetValue(new YamlScalarNode("hosts"), out var hosts))
        {
            Set(config, "hosts", hosts);
        }
    }

    private static void InjectTunDefaultDns(YamlMappingNode config, RuntimeConfigParams parameters)
    {
        var dns = config.Children.TryGetValue(new YamlScalarNode("dns"), out var existingDns)
            && existingDns is YamlMappingNode existingDnsMap
            ? existingDnsMap
            : new YamlMappingNode();

        var enhancedModeKey = new YamlScalarNode("enhanced-mode");
        if (dns.Children.TryGetValue(enhancedModeKey, out var enhancedMode) && !string.Equals(enhancedMode.ToString(), DnsDefaults.EnhancedMode, StringComparison.Ordinal))
        {
            Set(config, "dns", dns);
            return;
        }

        Set(dns, "enable", true);
        SetDefaultScalar(dns, "listen", parameters.DnsListen);
        Set(dns, "ipv6", parameters.IsDnsIpv6Enabled);
        if (!dns.Children.ContainsKey(enhancedModeKey))
        {
            Set(dns, "enhanced-mode", DnsDefaults.EnhancedMode);
        }

        if (!dns.Children.ContainsKey(new YamlScalarNode("fake-ip-range")))
        {
            Set(dns, "fake-ip-range", DnsDefaults.FakeIpRange);
        }

        SetDefaultScalar(dns, "fake-ip-filter-mode", parameters.FakeIpFilterMode);
        SetDefaultScalar(dns, "respect-rules", false);
        SetDefaultScalar(dns, "use-hosts", false);
        SetDefaultScalar(dns, "use-system-hosts", false);
        SetDefaultScalar(dns, "prefer-h3", false);
        SetDefaultScalar(dns, "direct-nameserver-follow-policy", false);
        SetDefaultSequence(dns, "nameserver", DnsDefaults.NameServers);
        SetDefaultSequence(dns, "default-nameserver", DnsDefaults.DefaultNameServers);
        SetDefaultSequence(dns, "proxy-server-nameserver", DnsDefaults.ProxyServerNameServers);
        SetDefaultSequence(dns, "fake-ip-filter", DnsDefaults.FakeIpFilters);
        SetDefaultFallbackFilter(dns);
        Set(config, "dns", dns);
    }

    private static YamlMappingNode CreateTunConfig(RuntimeConfigParams parameters)
    {
        var tun = new YamlMappingNode();
        Set(tun, "enable", parameters.IsTunEnabled);
        Set(tun, "stack", parameters.TunStack);
        Set(tun, "device", parameters.TunDevice);
        Set(tun, "auto-route", parameters.IsTunAutoRouteEnabled);
        Set(tun, "auto-redirect", parameters.IsTunAutoRedirectEnabled);
        Set(tun, "auto-detect-interface", parameters.IsTunAutoDetectInterfaceEnabled);
        Set(tun, "dns-hijack", Sequence(parameters.TunDnsHijacks));
        Set(tun, "strict-route", parameters.IsTunStrictRouteEnabled);
        Set(tun, "mtu", parameters.TunMtu);
        Set(tun, "disable-icmp-forwarding", parameters.IsTunIcmpForwardingDisabled);

        var routeExcludeAddresses = parameters.TunRouteExcludeAddresses.Where(value => !string.IsNullOrWhiteSpace(value)).ToList();
        if (routeExcludeAddresses.Count > 0)
        {
            Set(tun, "route-exclude-address", Sequence(routeExcludeAddresses));
        }

        return tun;
    }

    private static void Set(YamlMappingNode mapping, string key, string value)
    {
        mapping.Children[new YamlScalarNode(key)] = new YamlScalarNode(value);
    }

    private static void Set(YamlMappingNode mapping, string key, int value)
    {
        mapping.Children[new YamlScalarNode(key)] = new YamlScalarNode(value.ToString());
    }

    private static void Set(YamlMappingNode mapping, string key, bool value)
    {
        mapping.Children[new YamlScalarNode(key)] = new YamlScalarNode(value ? "true" : "false");
    }

    private static void Set(YamlMappingNode mapping, string key, YamlNode value)
    {
        mapping.Children[new YamlScalarNode(key)] = value;
    }

    private static void SetDefaultScalar(YamlMappingNode mapping, string key, string value)
    {
        if (!mapping.Children.ContainsKey(new YamlScalarNode(key)))
        {
            Set(mapping, key, value);
        }
    }

    private static void SetDefaultScalar(YamlMappingNode mapping, string key, bool value)
    {
        if (!mapping.Children.ContainsKey(new YamlScalarNode(key)))
        {
            Set(mapping, key, value);
        }
    }

    private static void SetDefaultSequence(YamlMappingNode mapping, string key, IReadOnlyList<string> values)
    {
        if (!mapping.Children.TryGetValue(new YamlScalarNode(key), out var existing)
            || existing is not YamlSequenceNode sequence
            || sequence.Children.Count == 0)
        {
            Set(mapping, key, Sequence(values));
        }
    }

    private static void SetDefaultFallbackFilter(YamlMappingNode dns)
    {
        var key = new YamlScalarNode("fallback-filter");
        if (dns.Children.ContainsKey(key))
        {
            return;
        }

        var fallbackFilter = new YamlMappingNode();
        Set(fallbackFilter, "geoip", true);
        Set(fallbackFilter, "geoip-code", DnsDefaults.FallbackFilterGeoIpCode);
        Set(fallbackFilter, "ipcidr", Sequence(DnsDefaults.FallbackFilterIpCidrs));
        Set(fallbackFilter, "domain", Sequence(DnsDefaults.FallbackFilterDomains));
        Set(dns, "fallback-filter", fallbackFilter);
    }

    private static void SetOrRemovePositiveInt(YamlMappingNode mapping, string key, int value)
    {
        if (value > 0)
        {
            Set(mapping, key, value);
            return;
        }

        Remove(mapping, key);
    }

    private static void SetOrRemoveSequence(YamlMappingNode mapping, string key, IReadOnlyList<string> values)
    {
        var effectiveValues = values.Where(value => !string.IsNullOrWhiteSpace(value)).ToList();
        if (effectiveValues.Count > 0)
        {
            Set(mapping, key, Sequence(effectiveValues));
            return;
        }

        Remove(mapping, key);
    }

    private static void Remove(YamlMappingNode mapping, string key)
    {
        mapping.Children.Remove(new YamlScalarNode(key));
    }

    private static YamlSequenceNode Sequence(IEnumerable<string> values)
    {
        return new YamlSequenceNode(values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => new YamlScalarNode(value)));
    }

    private static string Serialize(YamlMappingNode config)
    {
        var stream = new YamlStream(new YamlDocument(config));
        using var writer = new StringWriter();
        stream.Save(writer, assignAnchors: false);
        return writer.ToString();
    }
}

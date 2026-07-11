using Stelliberty.Application.Settings;

namespace Stelliberty.Application.Platform;

public sealed record SystemProxyApplicationRequest(
    string Host,
    int Port,
    IReadOnlyList<string> BypassRules,
    bool IsPacModeEnabled,
    string? PacScript)
{
    public static string DefaultPacScript(string host, int port)
    {
        return $$"""
function FindProxyForURL(url, host) {
    return "PROXY {{host}}:{{port}}; SOCKS5 {{host}}:{{port}}; DIRECT";
}
""";
    }

    public static SystemProxyApplicationRequest Build(AppSettings settings, SystemProxyPlatform platform)
    {
        var bypassRules = ParseBypassRules(BypassRulesTemplate(settings, platform), platform);
        var pacScript = settings.IsPacModeEnabled
            ? BuildPacScript(settings.ProxyHost, settings.MixedPort, settings.PacScript)
            : null;

        return new SystemProxyApplicationRequest(
            settings.ProxyHost,
            settings.MixedPort,
            bypassRules,
            settings.IsPacModeEnabled,
            pacScript);
    }

    public static string DefaultBypassRules(SystemProxyPlatform platform)
    {
        return platform switch
        {
            SystemProxyPlatform.Windows => "localhost;127.*;192.168.*;10.*;172.16.*;172.17.*;172.18.*;172.19.*;172.20.*;172.21.*;172.22.*;172.23.*;172.24.*;172.25.*;172.26.*;172.27.*;172.28.*;172.29.*;172.30.*;172.31.*;<local>",
            SystemProxyPlatform.Linux => "localhost,127.0.0.1,192.168.0.0/16,10.0.0.0/8,172.16.0.0/12,172.29.0.0/16,::1",
            SystemProxyPlatform.MacOS => "127.0.0.1,192.168.0.0/16,10.0.0.0/8,172.16.0.0/12,172.29.0.0/16,localhost,*.local,*.crashlytics.com,<local>",
            SystemProxyPlatform.Other => "localhost,127.0.0.1",
            _ => throw new ArgumentOutOfRangeException(nameof(platform), platform, "Unknown system proxy platform")
        };
    }

    public static IReadOnlyList<string> ParseBypassRules(string bypassRules, SystemProxyPlatform platform)
    {
        var separator = platform == SystemProxyPlatform.Windows ? ';' : ',';
        return bypassRules
            .Split(separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(rule => !string.IsNullOrWhiteSpace(rule))
            .ToArray();
    }

    private static string BuildPacScript(string host, int port, string customScript)
    {
        var template = string.IsNullOrWhiteSpace(customScript)
            ? DefaultPacScript(host, port)
            : customScript;

        return template
            .Replace("${getProxyHost()}", host, StringComparison.Ordinal)
            .Replace("${ClashDefaults.httpPort}", port.ToString(), StringComparison.Ordinal);
    }

    private static string BypassRulesTemplate(AppSettings settings, SystemProxyPlatform platform)
    {
        return string.IsNullOrWhiteSpace(settings.SystemProxyBypass)
            ? DefaultBypassRules(platform)
            : settings.SystemProxyBypass;
    }
}

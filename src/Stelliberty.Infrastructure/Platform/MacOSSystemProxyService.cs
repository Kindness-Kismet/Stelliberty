using System.Diagnostics;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Platform;

namespace Stelliberty.Infrastructure.Platform;

public sealed class MacOSSystemProxyService(string appDataDirectory) : ISystemProxyService
{
    private const string NetworkSetupPath = "/usr/sbin/networksetup";
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(5);

    public SystemProxyOperationResult Enable(SystemProxyApplicationRequest request)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return new SystemProxyOperationResult(false, "macOS system proxy is not supported in this environment");
        }

        try
        {
            var services = ListEnabledNetworkServices();
            if (services.Length == 0)
            {
                return new SystemProxyOperationResult(false, "No configurable macOS network service was found");
            }

            if (request.IsPacModeEnabled)
            {
                EnablePac(services, request);
            }
            else
            {
                EnableManual(services, request);
            }

            var endpoint = $"{request.Host}:{request.Port}";
            AppLogger.Info($"macOS system proxy enabled: {endpoint}, services={services.Length}");
            return new SystemProxyOperationResult(true, $"System proxy enabled: {endpoint}");
        }
        catch (Exception exception)
        {
            TryDisableQuietly();
            AppLogger.Warning($"macOS system proxy enable failed: {exception.Message}");
            return new SystemProxyOperationResult(false, exception.Message);
        }
    }

    public SystemProxyOperationResult Disable()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return new SystemProxyOperationResult(false, "macOS system proxy is not supported in this environment");
        }

        try
        {
            var services = ListEnabledNetworkServices();
            foreach (var service in services)
            {
                SetManualProxyState(service, "off");
                RunRequired("-setautoproxystate", service, "off");
            }

            AppLogger.Info($"macOS system proxy disabled: services={services.Length}");
            return new SystemProxyOperationResult(true, "System proxy disabled");
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"macOS system proxy disable failed: {exception.Message}");
            return new SystemProxyOperationResult(false, exception.Message);
        }
    }

    private void EnableManual(IReadOnlyList<string> services, SystemProxyApplicationRequest request)
    {
        foreach (var service in services)
        {
            RunRequired("-setautoproxystate", service, "off");
            RunRequired("-setwebproxy", service, request.Host, request.Port.ToString());
            RunRequired("-setsecurewebproxy", service, request.Host, request.Port.ToString());
            RunRequired("-setsocksfirewallproxy", service, request.Host, request.Port.ToString());
            SetBypassRules(service, request.BypassRules);
            SetManualProxyState(service, "on");
        }
    }

    private void EnablePac(IReadOnlyList<string> services, SystemProxyApplicationRequest request)
    {
        var pacPath = Path.Combine(appDataDirectory, "pac.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(pacPath)!);
        File.WriteAllText(pacPath, request.PacScript ?? string.Empty);
        var pacUrl = new Uri(Path.GetFullPath(pacPath)).AbsoluteUri;

        foreach (var service in services)
        {
            SetManualProxyState(service, "off");
            RunRequired("-setautoproxyurl", service, pacUrl);
            SetBypassRules(service, request.BypassRules);
            RunRequired("-setautoproxystate", service, "on");
        }

        AppLogger.Info($"macOS PAC system proxy enabled: {pacUrl}");
    }

    private static string[] ListEnabledNetworkServices()
    {
        var result = RunNetworkSetup("-listallnetworkservices");
        EnsureSuccess(result, "-listallnetworkservices");

        // networksetup 会给禁用服务加 * 前缀，并输出表头。
        return result.Output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static line => !line.StartsWith("An asterisk", StringComparison.Ordinal))
            .Where(static line => !line.StartsWith('*'))
            .Where(static line => !string.IsNullOrWhiteSpace(line))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static void SetManualProxyState(string service, string state)
    {
        RunRequired("-setwebproxystate", service, state);
        RunRequired("-setsecurewebproxystate", service, state);
        RunRequired("-setsocksfirewallproxystate", service, state);
    }

    private static void SetBypassRules(string service, IReadOnlyList<string> bypassRules)
    {
        var arguments = new List<string> { "-setproxybypassdomains", service };
        arguments.AddRange(bypassRules.Count == 0 ? ["Empty"] : bypassRules);
        RunRequired(arguments.ToArray());
    }

    private static void RunRequired(params string[] arguments)
    {
        var result = RunNetworkSetup(arguments);
        EnsureSuccess(result, string.Join(' ', arguments));
    }

    private static void EnsureSuccess(CommandResult result, string command)
    {
        if (result.ExitCode == 0)
        {
            return;
        }

        var message = string.IsNullOrWhiteSpace(result.Error)
            ? result.Output
            : result.Error;
        throw new InvalidOperationException($"{command} failed: {message.Trim()}");
    }

    private static CommandResult RunNetworkSetup(params string[] arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(NetworkSetupPath)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        if (!process.WaitForExit(CommandTimeout))
        {
            try { process.Kill(); }
            catch
            {
                // 临近超时时的退出仍按超时报告给调用方。
            }

            throw new TimeoutException("networksetup timed out");
        }

        return new CommandResult(
            process.ExitCode,
            process.StandardOutput.ReadToEnd(),
            process.StandardError.ReadToEnd());
    }

    private void TryDisableQuietly()
    {
        try
        {
            Disable();
        }
        catch
        {
            // 启用失败后的回滚不能替换原始错误。
        }
    }

    private sealed record CommandResult(int ExitCode, string Output, string Error);
}

using System.Diagnostics;
using System.Text.Json;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Platform;

namespace Stelliberty.Infrastructure.Platform;

public sealed class LinuxSystemProxyService(string appDataDirectory, string? preferredBackend = null) : ISystemProxyService
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(5);
    private readonly string _snapshotPath = Path.Combine(appDataDirectory, "linux-system-proxy.json");

    public SystemProxyOperationResult Enable(SystemProxyApplicationRequest request)
    {
        var canRollback = false;
        if (!OperatingSystem.IsLinux())
        {
            return new SystemProxyOperationResult(false, "Linux system proxy is not supported in this environment");
        }

        try
        {
            var backend = SelectBackend();
            if (backend is null)
            {
                return new SystemProxyOperationResult(false, "No supported Linux desktop proxy backend was detected");
            }

            Directory.CreateDirectory(appDataDirectory);
            var snapshot = backend.Capture();
            File.WriteAllText(_snapshotPath, JsonSerializer.Serialize(snapshot));
            canRollback = true;
            backend.Enable(request);

            AppLogger.Info($"Linux system proxy enabled: {request.Host}:{request.Port}, backend={backend.Name}");
            return new SystemProxyOperationResult(true, $"System proxy enabled: {request.Host}:{request.Port}");
        }
        catch (Exception exception)
        {
            if (canRollback)
            {
                TryDisableQuietly();
            }

            AppLogger.Warning($"Linux system proxy enable failed: {exception.Message}");
            return new SystemProxyOperationResult(false, exception.Message);
        }
    }

    public SystemProxyOperationResult Disable()
    {
        if (!OperatingSystem.IsLinux())
        {
            return new SystemProxyOperationResult(false, "Linux system proxy is not supported in this environment");
        }

        try
        {
            var snapshot = ReadSnapshot();
            var backend = SelectBackend(snapshot?.Backend);
            if (backend is null)
            {
                return new SystemProxyOperationResult(false, "No supported Linux desktop proxy backend was detected");
            }

            if (snapshot is null)
            {
                backend.Disable();
            }
            else
            {
                backend.Restore(snapshot);
                TryDeleteSnapshot();
            }

            AppLogger.Info($"Linux system proxy disabled: backend={backend.Name}");
            return new SystemProxyOperationResult(true, "System proxy disabled");
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"Linux system proxy disable failed: {exception.Message}");
            return new SystemProxyOperationResult(false, exception.Message);
        }
    }

    private ILinuxProxyBackend? SelectBackend(string? backendName = null)
    {
        var requested = backendName ?? preferredBackend;
        if (string.Equals(requested, GnomeProxyBackend.BackendName, StringComparison.OrdinalIgnoreCase))
        {
            return GnomeProxyBackend.IsAvailable() ? new GnomeProxyBackend() : null;
        }

        if (string.Equals(requested, KdeProxyBackend.BackendName, StringComparison.OrdinalIgnoreCase))
        {
            return KdeProxyBackend.TryCreate();
        }

        var desktop = Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? string.Empty;
        if (desktop.Contains("GNOME", StringComparison.OrdinalIgnoreCase) && GnomeProxyBackend.IsAvailable())
        {
            return new GnomeProxyBackend();
        }

        if (desktop.Contains("KDE", StringComparison.OrdinalIgnoreCase) && KdeProxyBackend.TryCreate() is { } kde)
        {
            return kde;
        }

        return null;
    }

    private LinuxProxySnapshot? ReadSnapshot()
    {
        if (!File.Exists(_snapshotPath))
        {
            return null;
        }

        return JsonSerializer.Deserialize<LinuxProxySnapshot>(File.ReadAllText(_snapshotPath));
    }

    private void TryDeleteSnapshot()
    {
        try { File.Delete(_snapshotPath); }
        catch
        {
            // 残留快照不能影响本次关闭结果。
        }
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

    private interface ILinuxProxyBackend
    {
        string Name { get; }

        LinuxProxySnapshot Capture();

        void Enable(SystemProxyApplicationRequest request);

        void Disable();

        void Restore(LinuxProxySnapshot snapshot);
    }

    private sealed class GnomeProxyBackend : ILinuxProxyBackend
    {
        public const string BackendName = "gnome";
        private static readonly string[] SnapshotKeys =
        [
            "org.gnome.system.proxy mode",
            "org.gnome.system.proxy ignore-hosts",
            "org.gnome.system.proxy.http enabled",
            "org.gnome.system.proxy.http host",
            "org.gnome.system.proxy.http port",
            "org.gnome.system.proxy.https host",
            "org.gnome.system.proxy.https port",
            "org.gnome.system.proxy.socks host",
            "org.gnome.system.proxy.socks port"
        ];

        public string Name => BackendName;

        public static bool IsAvailable()
        {
            return CommandExists("gsettings") && RunCommand("gsettings", "list-schemas").Output
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Contains("org.gnome.system.proxy", StringComparer.Ordinal);
        }

        public LinuxProxySnapshot Capture()
        {
            return new LinuxProxySnapshot(Name, SnapshotKeys.ToDictionary(static key => key, ReadKey, StringComparer.Ordinal));
        }

        public void Enable(SystemProxyApplicationRequest request)
        {
            Set("org.gnome.system.proxy", "mode", "manual");
            Set("org.gnome.system.proxy", "ignore-hosts", ToGVariantArray(request.BypassRules));
            Set("org.gnome.system.proxy.http", "enabled", "true");
            Set("org.gnome.system.proxy.http", "host", request.Host);
            Set("org.gnome.system.proxy.http", "port", request.Port.ToString());
            Set("org.gnome.system.proxy.https", "host", request.Host);
            Set("org.gnome.system.proxy.https", "port", request.Port.ToString());
            Set("org.gnome.system.proxy.socks", "host", request.Host);
            Set("org.gnome.system.proxy.socks", "port", request.Port.ToString());
        }

        public void Disable()
        {
            Set("org.gnome.system.proxy", "mode", "none");
        }

        public void Restore(LinuxProxySnapshot snapshot)
        {
            foreach (var key in SnapshotKeys)
            {
                var parts = key.Split(' ', 2);
                if (snapshot.Values.TryGetValue(key, out var value) && value is not null)
                {
                    Set(parts[0], parts[1], value);
                }
            }
        }

        private static string ReadKey(string key)
        {
            var parts = key.Split(' ', 2);
            return RunRequired("gsettings", "get", parts[0], parts[1]).Trim();
        }

        private static void Set(string schema, string key, string value)
        {
            RunRequired("gsettings", "set", schema, key, value);
        }

        private static string ToGVariantArray(IReadOnlyList<string> values)
        {
            var items = values.Select(static value => $"'{value.Replace("'", "\\'", StringComparison.Ordinal)}'");
            return $"[{string.Join(", ", items)}]";
        }
    }

    private sealed class KdeProxyBackend(string writeCommand, string readCommand) : ILinuxProxyBackend
    {
        public const string BackendName = "kde";
        private static readonly string[] SnapshotKeys =
        [
            "ProxyType",
            "httpProxy",
            "httpsProxy",
            "socksProxy",
            "NoProxyFor"
        ];

        public string Name => BackendName;

        public static KdeProxyBackend? TryCreate()
        {
            if (CommandExists("kwriteconfig6") && CommandExists("kreadconfig6"))
            {
                return new KdeProxyBackend("kwriteconfig6", "kreadconfig6");
            }

            if (CommandExists("kwriteconfig5") && CommandExists("kreadconfig5"))
            {
                return new KdeProxyBackend("kwriteconfig5", "kreadconfig5");
            }

            return null;
        }

        public LinuxProxySnapshot Capture()
        {
            var configPath = KioslaveConfigPath();
            var hadConfigFile = File.Exists(configPath);
            var configContent = hadConfigFile ? File.ReadAllText(configPath) : null;

            return new LinuxProxySnapshot(
                Name,
                SnapshotKeys.ToDictionary(static key => key, ReadKey, StringComparer.Ordinal),
                configPath,
                configContent,
                hadConfigFile);
        }

        public void Enable(SystemProxyApplicationRequest request)
        {
            if (request.IsPacModeEnabled)
            {
                throw new NotSupportedException("KDE system proxy does not support PAC mode yet");
            }

            WriteKey("ProxyType", "1");
            WriteKey("httpProxy", $"http://{request.Host}:{request.Port}");
            WriteKey("httpsProxy", $"http://{request.Host}:{request.Port}");
            WriteKey("socksProxy", $"socks://{request.Host}:{request.Port}");
            WriteKey("NoProxyFor", string.Join(',', request.BypassRules));
            NotifyProxyConfigChanged();
        }

        public void Disable()
        {
            WriteKey("ProxyType", "0");
            NotifyProxyConfigChanged();
        }

        public void Restore(LinuxProxySnapshot snapshot)
        {
            if (!string.IsNullOrWhiteSpace(snapshot.ConfigPath))
            {
                if (snapshot.HadConfigFile)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(snapshot.ConfigPath)!);
                    File.WriteAllText(snapshot.ConfigPath, snapshot.ConfigContent ?? string.Empty);
                    NotifyProxyConfigChanged();
                    return;
                }

                if (File.Exists(snapshot.ConfigPath))
                {
                    File.Delete(snapshot.ConfigPath);
                }

                NotifyProxyConfigChanged();
                return;
            }

            foreach (var key in SnapshotKeys)
            {
                if (snapshot.Values.TryGetValue(key, out var value) && value is not null)
                {
                    WriteKey(key, value);
                }
            }

            NotifyProxyConfigChanged();
        }

        private string ReadKey(string key)
        {
            return RunRequired(
                readCommand,
                "--file",
                "kioslaverc",
                "--group",
                "Proxy Settings",
                "--key",
                key).Trim();
        }

        private void WriteKey(string key, string value)
        {
            RunRequired(
                writeCommand,
                "--file",
                "kioslaverc",
                "--group",
                "Proxy Settings",
                "--key",
                key,
                value);
        }

        private static string KioslaveConfigPath()
        {
            var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            if (string.IsNullOrWhiteSpace(configHome))
            {
                var home = Environment.GetEnvironmentVariable("HOME")
                    ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                configHome = Path.Combine(home, ".config");
            }

            return Path.Combine(configHome, "kioslaverc");
        }

        private static void NotifyProxyConfigChanged()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS")))
            {
                return;
            }

            foreach (var command in new[] { "qdbus6", "qdbus-qt6", "qdbus", "qdbus-qt5" })
            {
                if (!CommandExists(command))
                {
                    continue;
                }

                TryRunCommand(command, "org.kde.kded6", "/modules/proxyscout", "org.kde.KDEDModule.reparseConfiguration");
                TryRunCommand(command, "org.kde.kded5", "/modules/proxyscout", "org.kde.KDEDModule.reparseConfiguration");
                return;
            }
        }

        private static void TryRunCommand(string fileName, params string[] arguments)
        {
            try { RunCommand(fileName, arguments); }
            catch
            {
                // KDE 通知只是热重载提示，不保证写入完成。
            }
        }
    }

    public sealed record LinuxProxySnapshot(
        string Backend,
        Dictionary<string, string> Values,
        string? ConfigPath = null,
        string? ConfigContent = null,
        bool HadConfigFile = false);

    private static bool CommandExists(string command)
    {
        try
        {
            return RunCommand("which", command).ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static string RunRequired(string fileName, params string[] arguments)
    {
        var result = RunCommand(fileName, arguments);
        if (result.ExitCode == 0)
        {
            return result.Output;
        }

        var message = string.IsNullOrWhiteSpace(result.Error)
            ? result.Output
            : result.Error;
        throw new InvalidOperationException($"{fileName} failed: {message.Trim()}");
    }

    private static CommandResult RunCommand(string fileName, params string[] arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(fileName)
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

            throw new TimeoutException($"{fileName} timed out");
        }

        return new CommandResult(
            process.ExitCode,
            process.StandardOutput.ReadToEnd(),
            process.StandardError.ReadToEnd());
    }

    private sealed record CommandResult(int ExitCode, string Output, string Error);
}

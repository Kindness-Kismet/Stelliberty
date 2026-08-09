using System.IO.Compression;
using System.Text.Json.Nodes;
using Stelliberty.Application.Settings;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Platform;

namespace Stelliberty.Infrastructure.DataManagement;

public sealed class FileDataBackupService(string appDataDirectory) : IDataManagementService
{
    private static readonly string[] UserDataEntries =
    [
        "subscriptions",
        "overrides",
        "dns.json"
    ];

    private static readonly string[] BackupSettingKeys =
    [
        "Language",
        "Theme",
        "AccentColorMode",
        "AccentColor",
        "IsMinimizeToTrayEnabled",
        "IsTrayDoubleClickEnabled",
        "IsLazyModeEnabled",
        "IsTitleBarFpsVisible",
        "IsAutoCheckUpdateEnabled",
        "AppUpdateCheckInterval",
        "IgnoredUpdateVersion",
        "IsUnifiedDelayEnabled",
        "OutboundMode",
        "ProxyPageLayout",
        "ProxyNodeSortMode",
        "DelayTestUrl",
        "IsAllowLanEnabled",
        "LanAuthenticationUserName",
        "LanAuthenticationPassword",
        "LanAllowedIps",
        "LanDisallowedIps",
        "SkipAuthPrefixes",
        "IsIpv6Enabled",
        "IsTcpConcurrentEnabled",
        "MixedPort",
        "SocksPort",
        "HttpPort",
        "IsExternalControllerEnabled",
        "ExternalControllerAddress",
        "ExternalControllerSecret",
        "IsDnsOverrideEnabled",
        "IsDnsEnabled",
        "DnsListen",
        "DnsEnhancedMode",
        "FakeIpRange",
        "IsDnsRespectRulesEnabled",
        "NameServers",
        "FallbackNameServers",
        "ProxyServerNameServers",
        "DefaultNameServers",
        "FakeIpFilters",
        "FallbackFilterGeoIpCode",
        "Hosts",
        "IsDnsIpv6Enabled",
        "IsDnsUseHostsEnabled",
        "IsDnsUseSystemHostsEnabled",
        "DirectNameServers",
        "NameServerPolicy",
        "IsDnsPreferH3Enabled",
        "FakeIpFilterMode",
        "IsDirectNameServerFollowPolicyEnabled",
        "IsFallbackFilterGeoIpEnabled",
        "FallbackFilterIpCidrs",
        "FallbackFilterDomains",
        "GeoDataLoader",
        "FindProcessMode",
        "IsTcpKeepAliveEnabled",
        "TcpKeepAliveInterval",
        "CoreLogLevel"
    ];

    private static readonly string[] LocalSettingKeys =
    [
        "WindowWidth",
        "WindowHeight",
        "IsWindowMaximized",
        "WindowEffect",
        "IsSilentStartEnabled",
        "IsAutoStartEnabled",
        "IsWebDavBackupEnabled",
        "WebDavUrl",
        "WebDavUserName",
        "WebDavPassword",
        "WebDavRemoteDirectory",
        "WebDavBackupIntervalHours",
        "WebDavBackupRetentionCount",
        "LastWebDavBackupTime",
        "LastCoreVersion",
        "LastAppUpdateCheckTime",
        "ProxyHost",
        "SystemProxyBypass",
        "IsPacModeEnabled",
        "PacScript",
        "IsTunEnabled",
        "TunStack",
        "TunDevice",
        "IsTunAutoRouteEnabled",
        "IsTunAutoRedirectEnabled",
        "IsTunAutoDetectInterfaceEnabled",
        "TunDnsHijack",
        "IsTunStrictRouteEnabled",
        "TunRouteExcludeAddresses",
        "IsTunIcmpForwardingDisabled",
        "TunMtu"
    ];

    public DataManagementOperationResult CreateBackup()
    {
        var backupPath = Path.Combine(
            appDataDirectory,
            "backups",
            $"{AppRuntimeNames.FileNameToken}-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.{AppRuntimeNames.FileNameToken}");
        return CreateBackup(backupPath);
    }

    public DataManagementOperationResult CreateBackup(string backupPath)
    {
        var normalizedBackupPath = Path.GetFullPath(backupPath);
        Directory.CreateDirectory(Path.GetDirectoryName(normalizedBackupPath)!);
        if (File.Exists(normalizedBackupPath))
        {
            File.Delete(normalizedBackupPath);
        }

        using var archive = ZipFile.Open(normalizedBackupPath, ZipArchiveMode.Create);
        AddSettingsEntry(archive);
        foreach (var entry in UserDataEntries)
        {
            AddEntry(archive, entry);
        }

        AppLogger.Info($"Data backup created: {normalizedBackupPath}");
        return new DataManagementOperationResult(true, "Backup created");
    }

    public DataManagementOperationResult RestoreBackup(DataRestoreMode mode)
    {
        var latestBackupPath = Directory.Exists(Path.Combine(appDataDirectory, "backups"))
            ? Directory.EnumerateFiles(Path.Combine(appDataDirectory, "backups"), $"*.{AppRuntimeNames.FileNameToken}").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
            : null;
        if (latestBackupPath is null)
        {
            return new DataManagementOperationResult(false, "No backup file available to restore");
        }

        return RestoreBackup(latestBackupPath, mode);
    }

    public DataManagementOperationResult RestoreBackup(string backupPath, DataRestoreMode mode)
    {
        var (restored, skipped) = RestoreBackupContent(backupPath, mode);
        var message = mode == DataRestoreMode.Merge
            ? $"Merge restore completed: added {restored}, skipped {skipped} existing files"
            : $"Overwrite restore completed: restored {restored} files";
        return new DataManagementOperationResult(true, message);
    }

    private (int Restored, int Skipped) RestoreBackupContent(string backupPath, DataRestoreMode mode)
    {
        using var archive = ZipFile.OpenRead(backupPath);
        var fileEntries = archive.Entries.Where(entry => !string.IsNullOrEmpty(entry.Name)).ToList();
        if (fileEntries.Count == 0)
        {
            throw new InvalidOperationException("Backup file contains no restorable data");
        }

        var restorableEntries = fileEntries
            .Select(entry => new
            {
                Entry = entry,
                DestinationPath = Path.GetFullPath(Path.Combine(appDataDirectory, entry.FullName))
            })
            .Where(item => IsPortableBackupEntry(item.Entry.FullName)
                && IsSafeDestination(item.DestinationPath)
                && IsRestorableDestination(item.DestinationPath))
            .ToList();
        if (restorableEntries.Count == 0)
        {
            throw new InvalidOperationException("Backup file contains no restorable data");
        }

        Directory.CreateDirectory(appDataDirectory);

        // 合并只补缺失文件，现有文件由当前机器继续持有。
        if (mode == DataRestoreMode.Merge)
        {
            var added = 0;
            var skipped = 0;
            foreach (var item in restorableEntries)
            {
                if (IsSettingsDestination(item.DestinationPath))
                {
                    var (merged, ignored) = MergeSettingsEntry(item.Entry);
                    added += merged;
                    skipped += ignored;
                    continue;
                }

                if (File.Exists(item.DestinationPath))
                {
                    skipped++;
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(item.DestinationPath)!);
                item.Entry.ExtractToFile(item.DestinationPath, overwrite: false);
                added++;
            }

            AppLogger.Info($"Data backup merge restored: {backupPath} added={added} skipped={skipped}");
            return (added, skipped);
        }

        // 覆盖模式只替换可迁移数据。
        // 本机运行状态和平台集成状态必须继续由当前机器持有。
        var currentLocalSettings = ReadLocalSettings();
        DeleteUserDataEntry(PathConventions.SettingsFileName);
        foreach (var entryName in UserDataEntries)
        {
            DeleteUserDataEntry(entryName);
        }

        var restored = 0;
        foreach (var item in restorableEntries)
        {
            if (IsSettingsDestination(item.DestinationPath))
            {
                RestorePortableSettingsEntry(item.Entry);
                restored++;
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(item.DestinationPath)!);
            item.Entry.ExtractToFile(item.DestinationPath, overwrite: true);
            restored++;
        }

        RestoreLocalSettings(currentLocalSettings);
        AppLogger.Info($"Data backup overwrite restored: {backupPath} restored={restored}");
        return (restored, 0);
    }

    private Dictionary<string, JsonNode?> ReadLocalSettings()
    {
        var settingsPath = Path.Combine(appDataDirectory, PathConventions.SettingsFileName);
        if (!File.Exists(settingsPath))
        {
            return [];
        }

        try
        {
            var settings = JsonNode.Parse(File.ReadAllText(settingsPath))?.AsObject();
            return settings is null
                ? []
                : LocalSettingKeys
                    .Where(settings.ContainsKey)
                    .ToDictionary(key => key, key => settings[key]?.DeepClone());
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"Current local settings read failed; skipping preservation: {exception.Message}");
            return [];
        }
    }

    private void RestorePortableSettingsEntry(ZipArchiveEntry entry)
    {
        var settingsPath = Path.Combine(appDataDirectory, PathConventions.SettingsFileName);
        using var reader = new StreamReader(entry.Open());
        var backupSettings = JsonNode.Parse(reader.ReadToEnd())?.AsObject();
        if (backupSettings is null)
        {
            return;
        }

        var portableSettings = new JsonObject();
        foreach (var pair in backupSettings)
        {
            if (BackupSettingKeys.Contains(pair.Key, StringComparer.Ordinal))
            {
                portableSettings[pair.Key] = pair.Value?.DeepClone();
            }
        }

        File.WriteAllText(settingsPath, portableSettings.ToJsonString());
    }

    private void RestoreLocalSettings(IReadOnlyDictionary<string, JsonNode?> localSettings)
    {
        if (localSettings.Count == 0)
        {
            return;
        }

        var settingsPath = Path.Combine(appDataDirectory, PathConventions.SettingsFileName);
        try
        {
            // 备份缺失设置时，只用本机状态重建最小 settings.json。
            var settings = File.Exists(settingsPath)
                ? JsonNode.Parse(File.ReadAllText(settingsPath))?.AsObject() ?? new JsonObject()
                : new JsonObject();

            foreach (var pair in localSettings)
            {
                settings[pair.Key] = pair.Value?.DeepClone();
            }

            File.WriteAllText(settingsPath, settings.ToJsonString());
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"Current local settings restore failed: {exception.Message}");
        }
    }

    private (int Merged, int Ignored) MergeSettingsEntry(ZipArchiveEntry entry)
    {
        var settingsPath = Path.Combine(appDataDirectory, PathConventions.SettingsFileName);
        try
        {
            var currentSettings = File.Exists(settingsPath)
                ? JsonNode.Parse(File.ReadAllText(settingsPath))?.AsObject() ?? new JsonObject()
                : new JsonObject();

            using var reader = new StreamReader(entry.Open());
            var backupSettings = JsonNode.Parse(reader.ReadToEnd())?.AsObject();
            if (backupSettings is null)
            {
                return (0, 0);
            }

            var merged = 0;
            var ignored = 0;
            foreach (var pair in backupSettings)
            {
                if (!BackupSettingKeys.Contains(pair.Key, StringComparer.Ordinal) || currentSettings.ContainsKey(pair.Key))
                {
                    ignored++;
                    continue;
                }

                currentSettings[pair.Key] = pair.Value?.DeepClone();
                merged++;
            }

            if (merged > 0)
            {
                File.WriteAllText(settingsPath, currentSettings.ToJsonString());
            }

            return (merged, ignored);
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"Portable settings merge failed; skipping settings: {exception.Message}");
            return (0, 0);
        }
    }

    private void AddSettingsEntry(ZipArchive archive)
    {
        var sourcePath = Path.Combine(appDataDirectory, PathConventions.SettingsFileName);
        if (!File.Exists(sourcePath))
        {
            return;
        }

        try
        {
            var settings = JsonNode.Parse(File.ReadAllText(sourcePath))?.AsObject();
            if (settings is null)
            {
                return;
            }

            var portableSettings = new JsonObject();
            foreach (var key in BackupSettingKeys.Where(settings.ContainsKey))
            {
                portableSettings[key] = settings[key]?.DeepClone();
            }

            var entry = archive.CreateEntry(PathConventions.SettingsFileName);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(portableSettings.ToJsonString());
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"Portable settings backup failed; skipping settings: {exception.Message}");
        }
    }

    private void AddEntry(ZipArchive archive, string relativePath)
    {
        var sourcePath = Path.Combine(appDataDirectory, relativePath);
        if (File.Exists(sourcePath))
        {
            archive.CreateEntryFromFile(sourcePath, NormalizePath(relativePath));
            return;
        }

        if (!Directory.Exists(sourcePath))
        {
            return;
        }

        foreach (var filePath in Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories))
        {
            var relativeFilePath = Path.GetRelativePath(appDataDirectory, filePath);
            if (ShouldSkipBackupFile(relativeFilePath))
            {
                continue;
            }

            archive.CreateEntryFromFile(filePath, NormalizePath(relativeFilePath));
        }
    }

    private static bool ShouldSkipBackupFile(string relativePath)
    {
        return !IsPortableBackupEntry(relativePath);
    }

    private static bool IsPortableBackupEntry(string relativePath)
    {
        var normalized = NormalizePath(relativePath);
        if (string.Equals(normalized, PathConventions.SettingsFileName, StringComparison.Ordinal)
            || string.Equals(normalized, "dns.json", StringComparison.Ordinal))
        {
            return true;
        }

        if (normalized.StartsWith("subscriptions/", StringComparison.Ordinal))
        {
            return string.Equals(normalized, "subscriptions/subscriptions_list.json", StringComparison.Ordinal)
                || normalized.EndsWith(".yaml", StringComparison.Ordinal);
        }

        if (normalized.StartsWith("overrides/", StringComparison.Ordinal))
        {
            return string.Equals(normalized, "overrides/overrides_list.json", StringComparison.Ordinal)
                || normalized.EndsWith(".yaml", StringComparison.Ordinal)
                || normalized.EndsWith(".js", StringComparison.Ordinal);
        }

        return false;
    }

    private bool IsSafeDestination(string destinationPath)
    {
        var appDataRoot = Path.GetFullPath(appDataDirectory) + Path.DirectorySeparatorChar;
        return destinationPath.StartsWith(appDataRoot, StringComparison.Ordinal);
    }

    private bool IsRestorableDestination(string destinationPath)
    {
        return IsSettingsDestination(destinationPath)
            || UserDataEntries.Any(entry => IsAllowedEntryDestination(entry, destinationPath));
    }

    private bool IsSettingsDestination(string destinationPath)
    {
        return destinationPath == Path.GetFullPath(Path.Combine(appDataDirectory, PathConventions.SettingsFileName));
    }

    private bool IsAllowedEntryDestination(string entry, string destinationPath)
    {
        var entryPath = Path.GetFullPath(Path.Combine(appDataDirectory, entry));
        if (File.Exists(entryPath) || Path.HasExtension(entry))
        {
            return destinationPath == entryPath;
        }

        return destinationPath == entryPath || destinationPath.StartsWith(entryPath + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    private void DeleteUserDataEntry(string relativePath)
    {
        var path = Path.Combine(appDataDirectory, relativePath);
        if (File.Exists(path))
        {
            File.Delete(path);
            return;
        }

        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static string NormalizePath(string path)
    {
        return path.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
    }
}

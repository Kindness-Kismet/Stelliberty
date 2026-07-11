using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Platform;
using Stelliberty.Application.Runtime;

namespace Stelliberty.Infrastructure.Core;

public sealed class MihomoCoreUpdater(string coreExecutablePath, ICoreManager coreManager) : ICoreUpdater
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/MetaCubeX/mihomo/releases/latest";
    private static readonly HttpClient Http = CreateClient();

    public async Task<CoreUpdateResult> UpdateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var (platform, arch, ext) = ResolvePlatform();
            var (tag, assetUrl) = await GetLatestReleaseAsync(platform, arch, ext, cancellationToken).ConfigureAwait(false);
            var latest = NormalizeVersion(tag);
            var current = await GetCurrentVersionAsync(cancellationToken).ConfigureAwait(false);
            if (current is not null && CompareVersions(current, latest) >= 0)
            {
                return new CoreUpdateResult(CoreUpdateStatus.UpToDate, current, $"Core is already up to date at v{current}");
            }

            var archive = await Http.GetByteArrayAsync(assetUrl, cancellationToken).ConfigureAwait(false);
            var coreBytes = Extract(archive, ext == ".zip");
            if (coreBytes.Length == 0)
            {
                throw new InvalidOperationException("Extracted core file is empty");
            }

            // 新核心必须能成功重启后，才移除旧核心。
            ReplaceCore(coreBytes);
            try
            {
                await coreManager.RestartAsync(cancellationToken).ConfigureAwait(false);

                if (!await WaitForCoreRunningAsync(cancellationToken).ConfigureAwait(false))
                {
                    throw new InvalidOperationException("New core failed to start");
                }
            }
            catch
            {
                RestoreBackup();
                try { await coreManager.RestartAsync(cancellationToken).ConfigureAwait(false); }
                catch
                {
                    // 旧核心重启失败不能掩盖更新失败。
                }
                throw;
            }

            DeleteBackup();
            AppLogger.Info($"Core updated to v{latest}");
            return new CoreUpdateResult(CoreUpdateStatus.Updated, latest, $"Core updated to v{latest}");
        }
        catch (Exception exception)
        {
            var message = $"Core update failed: {exception.Message}";
            AppLogger.Warning(message);
            return new CoreUpdateResult(CoreUpdateStatus.Failed, null, message);
        }
    }

    private async Task<(string Tag, string AssetUrl)> GetLatestReleaseAsync(string platform, string arch, string ext, CancellationToken cancellationToken)
    {
        await using var stream = await Http.GetStreamAsync(LatestReleaseUrl, cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        var tag = root.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() : null;
        if (string.IsNullOrWhiteSpace(tag))
        {
            throw new InvalidOperationException("Release response is missing tag_name");
        }

        var assetName = $"mihomo-{platform}-{arch}-{tag}{ext}";
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                if (asset.TryGetProperty("name", out var nameProp)
                    && string.Equals(nameProp.GetString(), assetName, StringComparison.Ordinal)
                    && asset.TryGetProperty("browser_download_url", out var urlProp)
                    && urlProp.GetString() is { Length: > 0 } url)
                {
                    return (tag, url);
                }
            }
        }

        throw new InvalidOperationException($"Matching core asset not found: {assetName}");
    }

    private async Task<string?> GetCurrentVersionAsync(CancellationToken cancellationToken)
    {
        try
        {
            // 运行中的核心可用新进程探测版本。
            if (!File.Exists(coreExecutablePath))
            {
                return null;
            }

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo(coreExecutablePath, "-v")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };
            process.Start();
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); }
                catch
                {
                    // 超时竞态按版本探测失败处理。
                }
                return null;
            }

            var output = await outputTask.ConfigureAwait(false);
            // mihomo -v 包含 v1.2.3；只比较数字版本。
            var match = Regex.Match(output, @"v?(\d+\.\d+\.\d+)");
            return match.Success ? match.Groups[1].Value : null;
        }
        catch
        {
            return null;
        }
    }

    private void ReplaceCore(byte[] coreBytes)
    {
        var directory = Path.GetDirectoryName(coreExecutablePath)!;
        Directory.CreateDirectory(directory);
        var backup = coreExecutablePath + ".old";
        if (File.Exists(backup))
        {
            File.Delete(backup);
        }

        if (File.Exists(coreExecutablePath))
        {
            // Windows 允许先重命名运行中的 exe，再写入新核心。
            File.Move(coreExecutablePath, backup);
        }

        File.WriteAllBytes(coreExecutablePath, coreBytes);
        if (!OperatingSystem.IsWindows())
        {
            // 新写入的 Unix 文件需要恢复用户执行权限。
            File.SetUnixFileMode(
                coreExecutablePath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
    }

    private void RestoreBackup()
    {
        var backup = coreExecutablePath + ".old";
        try
        {
            if (!File.Exists(backup))
            {
                return;
            }

            if (File.Exists(coreExecutablePath))
            {
                File.Delete(coreExecutablePath);
            }

            File.Move(backup, coreExecutablePath);
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"Core backup restore failed: {exception.Message}");
        }
    }

    private void DeleteBackup()
    {
        var backup = coreExecutablePath + ".old";
        try
        {
            if (File.Exists(backup))
            {
                File.Delete(backup);
            }
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"Core backup delete failed: {exception.Message}");
        }
    }

    private async Task<bool> WaitForCoreRunningAsync(CancellationToken cancellationToken)
    {
        // 重启确认覆盖冷启动；超时触发回滚。
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(12);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var snapshot = await coreManager.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
            if (snapshot.State == CoreState.Running)
            {
                return true;
            }

            if (snapshot.State is CoreState.Crashed or CoreState.Stopped)
            {
                return false;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(400), cancellationToken).ConfigureAwait(false);
        }

        return false;
    }

    private static byte[] Extract(byte[] archive, bool isZip)
    {
        using var input = new MemoryStream(archive);
        using var output = new MemoryStream();
        if (isZip)
        {
            using var zip = new ZipArchive(input, ZipArchiveMode.Read);
            var entry = zip.Entries.FirstOrDefault(e => e.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                ?? zip.Entries.FirstOrDefault(e => e.Length > 0)
                ?? throw new InvalidOperationException("Core archive is empty");
            using var entryStream = entry.Open();
            entryStream.CopyTo(output);
        }
        else
        {
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            gzip.CopyTo(output);
        }

        return output.ToArray();
    }

    private static (string Platform, string Arch, string Ext) ResolvePlatform()
    {
        var platform = OperatingSystem.IsWindows() ? "windows"
            : OperatingSystem.IsMacOS() ? "darwin"
            : "linux";
        var arch = RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => "amd64",
            Architecture.Arm64 => "arm64",
            Architecture.X86 => "386",
            _ => "amd64",
        };
        var ext = OperatingSystem.IsWindows() ? ".zip" : ".gz";
        return (platform, arch, ext);
    }

    // 数字分段比较保证 v1.10 大于 v1.2。
    private static int CompareVersions(string left, string right)
    {
        var leftParts = NormalizeVersion(left).Split('.');
        var rightParts = NormalizeVersion(right).Split('.');
        var length = Math.Max(leftParts.Length, rightParts.Length);
        for (var index = 0; index < length; index++)
        {
            var leftValue = index < leftParts.Length && int.TryParse(leftParts[index], out var l) ? l : 0;
            var rightValue = index < rightParts.Length && int.TryParse(rightParts[index], out var r) ? r : 0;
            if (leftValue != rightValue)
            {
                return leftValue < rightValue ? -1 : 1;
            }
        }

        return 0;
    }

    private static string NormalizeVersion(string value) => value.Trim().TrimStart('v', 'V');

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        // GitHub API 未带 User-Agent 会返回 403。
        client.DefaultRequestHeaders.UserAgent.ParseAdd(AppRuntimeNames.UserAgent);
        return client;
    }
}

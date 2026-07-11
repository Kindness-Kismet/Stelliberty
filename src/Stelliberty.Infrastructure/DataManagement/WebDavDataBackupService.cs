using Stelliberty.Application.Platform;
using Stelliberty.Application.Settings;

namespace Stelliberty.Infrastructure.DataManagement;

public sealed class WebDavDataBackupService(
    IDataManagementService dataManagementService,
    IWebDavBackupStore backupStore) : IWebDavDataBackupService
{
    private static readonly string TempRoot = Path.Combine(Path.GetTempPath(), "stelliberty-webdav-backups");

    public async Task<DataManagementOperationResult> TestConnectionAsync(WebDavBackupSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            await backupStore.TestConnectionAsync(Normalize(settings), cancellationToken);
            return new DataManagementOperationResult(true, "WebDAV connection succeeded");
        }
        catch (Exception exception)
        {
            return new DataManagementOperationResult(false, exception.Message);
        }
    }

    public async Task<DataManagementOperationResult> CreateBackupAsync(WebDavBackupSettings settings, CancellationToken cancellationToken)
    {
        settings = Normalize(settings);
        try
        {
            using var temp = TemporaryDirectory.Create();
            var fileName = $"{AppRuntimeNames.FileNameToken}-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.{AppRuntimeNames.FileNameToken}";
            var backupPath = Path.Combine(temp.Path, fileName);
            var localResult = dataManagementService.CreateBackup(backupPath);
            if (!localResult.IsSuccess)
            {
                return localResult;
            }

            await using var stream = File.OpenRead(backupPath);
            await backupStore.UploadAsync(settings, fileName, stream, cancellationToken);
            await PruneAsync(settings, cancellationToken);
            return new DataManagementOperationResult(true, "WebDAV backup completed");
        }
        catch (Exception exception)
        {
            return new DataManagementOperationResult(false, exception.Message);
        }
    }

    public async Task<IReadOnlyList<RemoteBackupEntry>> ListBackupsAsync(
        WebDavBackupSettings settings,
        CancellationToken cancellationToken)
    {
        return await backupStore.ListAsync(Normalize(settings), cancellationToken);
    }

    public async Task<DataManagementOperationResult> RestoreBackupAsync(
        WebDavBackupSettings settings,
        string fileName,
        DataRestoreMode mode,
        CancellationToken cancellationToken)
    {
        settings = Normalize(settings);
        try
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return new DataManagementOperationResult(false, "No WebDAV backup file selected");
            }

            using var temp = TemporaryDirectory.Create();
            var backupPath = Path.Combine(temp.Path, Path.GetFileName(fileName));
            await backupStore.DownloadAsync(settings, fileName, backupPath, cancellationToken);
            return dataManagementService.RestoreBackup(backupPath, mode);
        }
        catch (Exception exception)
        {
            return new DataManagementOperationResult(false, exception.Message);
        }
    }

    public async Task<DataManagementOperationResult> RestoreLatestBackupAsync(
        WebDavBackupSettings settings,
        DataRestoreMode mode,
        CancellationToken cancellationToken)
    {
        settings = Normalize(settings);
        try
        {
            var entry = (await backupStore.ListAsync(settings, cancellationToken)).FirstOrDefault();
            if (entry is null)
            {
                return new DataManagementOperationResult(false, "No WebDAV backup file available to restore");
            }

            using var temp = TemporaryDirectory.Create();
            var backupPath = Path.Combine(temp.Path, entry.FileName);
            await backupStore.DownloadAsync(settings, entry.FileName, backupPath, cancellationToken);
            return dataManagementService.RestoreBackup(backupPath, mode);
        }
        catch (Exception exception)
        {
            return new DataManagementOperationResult(false, exception.Message);
        }
    }

    public async Task<DataManagementOperationResult> DeleteBackupAsync(
        WebDavBackupSettings settings,
        string fileName,
        CancellationToken cancellationToken)
    {
        settings = Normalize(settings);
        try
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return new DataManagementOperationResult(false, "No WebDAV backup file selected");
            }

            await backupStore.DeleteAsync(settings, fileName, cancellationToken);
            return new DataManagementOperationResult(true, "WebDAV backup deleted");
        }
        catch (Exception exception)
        {
            return new DataManagementOperationResult(false, exception.Message);
        }
    }

    private async Task PruneAsync(WebDavBackupSettings settings, CancellationToken cancellationToken)
    {
        var retentionCount = Math.Max(1, settings.RetentionCount);
        var entries = await backupStore.ListAsync(settings, cancellationToken);
        foreach (var entry in entries.Skip(retentionCount))
        {
            await backupStore.DeleteAsync(settings, entry.FileName, cancellationToken);
        }
    }

    private static WebDavBackupSettings Normalize(WebDavBackupSettings settings)
    {
        return settings with
        {
            Url = settings.Url.Trim(),
            RemoteDirectory = string.IsNullOrWhiteSpace(settings.RemoteDirectory)
                ? "stelliberty-backups"
                : settings.RemoteDirectory.Trim(),
            UserName = settings.UserName.Trim(),
            RetentionCount = Math.Max(1, settings.RetentionCount)
        };
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string _safeRoot;

        private TemporaryDirectory(string path, string safeRoot)
        {
            Path = path;
            _safeRoot = safeRoot;
        }

        public string Path { get; }

        public static TemporaryDirectory Create()
        {
            var safeRoot = System.IO.Path.GetFullPath(TempRoot);
            Directory.CreateDirectory(safeRoot);
            var path = System.IO.Path.Combine(safeRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TemporaryDirectory(path, safeRoot);
        }

        public void Dispose()
        {
            var fullPath = System.IO.Path.GetFullPath(Path);
            // 只删除本服务创建的临时子目录。
            if (!fullPath.StartsWith(_safeRoot + System.IO.Path.DirectorySeparatorChar, StringComparison.Ordinal)
                || !Directory.Exists(fullPath))
            {
                return;
            }

            Directory.Delete(fullPath, recursive: true);
        }
    }
}

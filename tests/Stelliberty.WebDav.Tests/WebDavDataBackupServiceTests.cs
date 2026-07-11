using System.Text;
using Stelliberty.Application.Platform;
using Stelliberty.Application.Settings;
using Stelliberty.Infrastructure.DataManagement;
using Xunit;

namespace Stelliberty.WebDav.Tests;

public sealed class WebDavDataBackupServiceTests
{
    [Fact(DisplayName = "WebDAV data backup service uploads and prunes old backups")]
    public async Task WebDavDataBackupServiceUploadsAndPrunesOldBackups()
    {
        var local = new FakeDataManagementService();
        var store = new FakeWebDavBackupStore();
        store.AddRemoteBackup($"old-a.{AppRuntimeNames.FileNameToken}", "old-a", DateTimeOffset.UnixEpoch.AddHours(1));
        store.AddRemoteBackup($"old-b.{AppRuntimeNames.FileNameToken}", "old-b", DateTimeOffset.UnixEpoch.AddHours(2));
        var service = new WebDavDataBackupService(local, store);

        var result = await service.CreateBackupAsync(Settings(retentionCount: 1), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, local.CreateCount);
        Assert.Single(store.UploadedFileNames);
        Assert.Equal(2, store.DeletedFileNames.Count);
        Assert.Contains($"old-a.{AppRuntimeNames.FileNameToken}", store.DeletedFileNames);
        Assert.Contains($"old-b.{AppRuntimeNames.FileNameToken}", store.DeletedFileNames);
        Assert.Equal("portable-backup", store.UploadedContent);
    }

    [Fact(DisplayName = "WebDAV data backup service restores latest remote backup")]
    public async Task WebDavDataBackupServiceRestoresLatestRemoteBackup()
    {
        var local = new FakeDataManagementService();
        var store = new FakeWebDavBackupStore();
        store.AddRemoteBackup($"old.{AppRuntimeNames.FileNameToken}", "old-backup", DateTimeOffset.UnixEpoch.AddHours(1));
        store.AddRemoteBackup($"new.{AppRuntimeNames.FileNameToken}", "new-backup", DateTimeOffset.UnixEpoch.AddHours(2));
        var service = new WebDavDataBackupService(local, store);

        var result = await service.RestoreLatestBackupAsync(Settings(), DataRestoreMode.Merge, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, local.RestoreCount);
        Assert.Equal(DataRestoreMode.Merge, local.LastRestoreMode);
        Assert.Equal("new-backup", local.LastRestoredContent);
    }

    private static WebDavBackupSettings Settings(int retentionCount = 5)
    {
        return new WebDavBackupSettings(
            "https://webdav.example/dav",
            "test-data/backups",
            "test-user",
            "<webdav-password>",
            retentionCount);
    }

    private sealed class FakeDataManagementService : IDataManagementService
    {
        public int CreateCount { get; private set; }
        public int RestoreCount { get; private set; }
        public DataRestoreMode LastRestoreMode { get; private set; }
        public string LastRestoredContent { get; private set; } = string.Empty;

        public DataManagementOperationResult CreateBackup()
        {
            throw new NotSupportedException();
        }

        public DataManagementOperationResult CreateBackup(string backupPath)
        {
            CreateCount++;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(backupPath))!);
            File.WriteAllText(backupPath, "portable-backup");
            return new DataManagementOperationResult(true, "created");
        }

        public DataManagementOperationResult RestoreBackup(DataRestoreMode mode)
        {
            throw new NotSupportedException();
        }

        public DataManagementOperationResult RestoreBackup(string backupPath, DataRestoreMode mode)
        {
            RestoreCount++;
            LastRestoreMode = mode;
            LastRestoredContent = File.ReadAllText(backupPath);
            return new DataManagementOperationResult(true, "restored");
        }
    }

    private sealed class FakeWebDavBackupStore : IWebDavBackupStore
    {
        private readonly Dictionary<string, (string Content, DateTimeOffset Modified)> _files = [];

        public List<string> UploadedFileNames { get; } = [];

        public List<string> DeletedFileNames { get; } = [];

        public string UploadedContent { get; private set; } = string.Empty;

        public void AddRemoteBackup(string fileName, string content, DateTimeOffset modified)
        {
            _files[fileName] = (content, modified);
        }

        public Task TestConnectionAsync(WebDavBackupSettings settings, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<RemoteBackupEntry>> ListAsync(WebDavBackupSettings settings, CancellationToken cancellationToken)
        {
            IReadOnlyList<RemoteBackupEntry> entries = _files
                .Select(pair => new RemoteBackupEntry(pair.Key, Encoding.UTF8.GetByteCount(pair.Value.Content), pair.Value.Modified))
                .OrderByDescending(entry => entry.LastModified)
                .ThenByDescending(entry => entry.FileName, StringComparer.Ordinal)
                .ToList();
            return Task.FromResult(entries);
        }

        public async Task UploadAsync(WebDavBackupSettings settings, string fileName, Stream content, CancellationToken cancellationToken)
        {
            using var reader = new StreamReader(content, Encoding.UTF8, leaveOpen: true);
            UploadedContent = await reader.ReadToEndAsync(cancellationToken);
            UploadedFileNames.Add(fileName);
            _files[fileName] = (UploadedContent, DateTimeOffset.UnixEpoch.AddDays(1));
        }

        public Task DownloadAsync(WebDavBackupSettings settings, string fileName, string destinationPath, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destinationPath))!);
            File.WriteAllText(destinationPath, _files[fileName].Content);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(WebDavBackupSettings settings, string fileName, CancellationToken cancellationToken)
        {
            DeletedFileNames.Add(fileName);
            _files.Remove(fileName);
            return Task.CompletedTask;
        }
    }
}

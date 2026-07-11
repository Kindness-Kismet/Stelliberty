namespace Stelliberty.Application.Settings;

public interface IWebDavDataBackupService
{
    Task<DataManagementOperationResult> TestConnectionAsync(WebDavBackupSettings settings, CancellationToken cancellationToken);

    Task<DataManagementOperationResult> CreateBackupAsync(WebDavBackupSettings settings, CancellationToken cancellationToken);

    Task<IReadOnlyList<RemoteBackupEntry>> ListBackupsAsync(WebDavBackupSettings settings, CancellationToken cancellationToken);

    Task<DataManagementOperationResult> RestoreBackupAsync(
        WebDavBackupSettings settings,
        string fileName,
        DataRestoreMode mode,
        CancellationToken cancellationToken);

    Task<DataManagementOperationResult> RestoreLatestBackupAsync(WebDavBackupSettings settings, DataRestoreMode mode, CancellationToken cancellationToken);

    Task<DataManagementOperationResult> DeleteBackupAsync(WebDavBackupSettings settings, string fileName, CancellationToken cancellationToken);
}

namespace Stelliberty.Application.Settings;

public interface IWebDavBackupStore
{
    Task TestConnectionAsync(WebDavBackupSettings settings, CancellationToken cancellationToken);

    Task<IReadOnlyList<RemoteBackupEntry>> ListAsync(WebDavBackupSettings settings, CancellationToken cancellationToken);

    Task UploadAsync(WebDavBackupSettings settings, string fileName, Stream content, CancellationToken cancellationToken);

    Task DownloadAsync(WebDavBackupSettings settings, string fileName, string destinationPath, CancellationToken cancellationToken);

    Task DeleteAsync(WebDavBackupSettings settings, string fileName, CancellationToken cancellationToken);
}

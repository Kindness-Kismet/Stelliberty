namespace Stelliberty.Application.Settings;

public sealed record WebDavBackupSettings(
    string Url,
    string RemoteDirectory,
    string UserName,
    string Password,
    int RetentionCount);

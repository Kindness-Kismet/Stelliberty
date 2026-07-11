namespace Stelliberty.Application.Settings;

public sealed record RemoteBackupEntry(
    string FileName,
    long? Size,
    DateTimeOffset? LastModified);

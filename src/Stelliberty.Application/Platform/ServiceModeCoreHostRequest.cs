namespace Stelliberty.Application.Platform;

public sealed record ServiceModeCoreHostRequest(
    string MihomoPath,
    string DataCoreDir,
    string ConfigPath);

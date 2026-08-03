namespace Stelliberty.Application.Platform;

public sealed record ServiceModeCoreHostRequest(
    string CorePath,
    string DataCoreDir,
    string ConfigPath);

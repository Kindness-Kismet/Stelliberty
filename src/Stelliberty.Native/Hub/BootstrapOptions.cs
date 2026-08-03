namespace Stelliberty.Native.Hub;

public sealed record BootstrapOptions(
    string PipeName,
    string CorePath,
    string DataCoreDir,
    string UserDataDir,
    string CorePipe,
    string BootstrapYaml);

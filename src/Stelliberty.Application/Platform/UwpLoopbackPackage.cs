namespace Stelliberty.Application.Platform;

// AppContainerName/Sid 为空时，未支持平台也能安全显示。
public sealed record UwpLoopbackPackage(
    string PackageFamilyName,
    string DisplayName,
    bool IsLoopbackEnabled,
    string AppContainerName = "",
    string Sid = "");

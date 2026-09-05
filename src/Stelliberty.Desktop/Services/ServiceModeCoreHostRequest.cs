namespace Stelliberty.Desktop.Services;

// 服务进程 spawn mihomo 所需的启动参数，仅供 Desktop 层核心管理使用
public sealed record ServiceModeCoreHostRequest(
    string CorePath,
    string DataCoreDir,
    string ConfigPath);

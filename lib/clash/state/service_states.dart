// 服务模式状态定义

// ============================================
// 服务模式状态
// ============================================

enum ServiceState {
  // 服务未安装
  notInstalled,

  // 服务已安装但未运行
  installed,

  // 服务正在运行
  running,

  // 正在安装服务
  installing,

  // 正在卸载服务
  uninstalling,

  // 状态未知（检测失败）
  unknown,
}

// 服务状态扩展方法
extension ServiceStateExtension on ServiceState {
  // 服务模式是否已安装（卸载中也视为已安装，直到卸载完成）
  bool get isServiceModeInstalled =>
      this == ServiceState.installed ||
      this == ServiceState.running ||
      this == ServiceState.uninstalling;

  // 服务模式是否正在运行
  bool get isServiceModeRunning => this == ServiceState.running;

  // 服务模式是否正在处理操作（安装或卸载）
  bool get isServiceModeProcessing =>
      this == ServiceState.installing || this == ServiceState.uninstalling;
}

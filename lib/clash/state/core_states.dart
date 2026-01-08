// 核心进程状态定义

// ============================================
// Clash 启动模式
// ============================================

enum ClashStartMode {
  // 普通模式（应用直接启动进程）
  sidecar,

  // 服务模式（通过服务启动）
  service,
}

// ============================================
// Clash 内核状态
// ============================================

enum CoreState {
  // 已停止 - 内核未运行
  stopped,

  // 正在启动 - 内核正在启动过程中
  starting,

  // 正在运行 - 内核正常运行中
  running,

  // 正在停止 - 内核正在停止过程中
  stopping,

  // 正在重启 - 内核正在重启过程中
  restarting,
}

// 内核状态扩展方法
extension CoreStateExtension on CoreState {
  // 是否为运行状态
  bool get isRunning => this == CoreState.running;

  // 是否为停止状态
  bool get isStopped => this == CoreState.stopped;

  // 是否为过渡状态（正在执行某个操作）
  bool get isTransitioning => [
    CoreState.starting,
    CoreState.stopping,
    CoreState.restarting,
  ].contains(this);

  // 是否可以启动
  bool get canStart => this == CoreState.stopped;

  // 是否可以停止
  bool get canStop => this == CoreState.running;

  // 是否可以重启
  bool get canRestart => this == CoreState.running;
}

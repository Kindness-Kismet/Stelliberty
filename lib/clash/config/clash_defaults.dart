import 'dart:io';

// Clash 默认配置常量
class ClashDefaults {
  ClashDefaults._();

  // ==================== API 配置 ====================
  static const String apiHost = '127.0.0.1';
  static const int apiPort = 9090;

  // ==================== 端口配置 ====================
  static const int mixedPort = 7777; // 混合端口（HTTP + SOCKS5）
  static const int httpPort = 7778; // 单独 HTTP 端口（可选）
  static const int socksPort = 7779; // 单独 SOCKS5 端口（可选）

  // ==================== 超时配置 ====================
  static const int apiReadyMaxRetries = 30; // API 就绪重试次数（总超时 6s）
  static const int apiReadyCheckTimeout = 300; // API 单次检查超时（ms）
  static const int apiReadyRetryInterval = 200; // API 重试间隔（ms）
  static const int ipcReadyMaxRetries = 10; // IPC 就绪重试次数
  static const int ipcReadyRetryInterval = 200; // IPC 重试间隔（ms）
  static const int processKillTimeout = 5; // 进程停止超时（s）
  static const int subscriptionDownloadTimeout = 60; // 订阅下载超时（s）
  static const int proxyDelayTestTimeout = 8000; // 延迟测试超时（ms）
  static const int apiRequestTimeout = 10; // API 请求超时（s）
  static const int apiLongRequestTimeout = 15; // API 长请求超时（s）

  // ==================== 并发配置 ====================
  static const int delayTestConcurrency = 5; // 延迟测试并发数
  static const int subscriptionUpdateConcurrency = 3; // 订阅更新并发数

  // 动态延迟测试并发数（CPU 核心数 × 15，上限 300）
  // 延迟测试是 I/O 密集型任务，可以使用较高并发
  static int get dynamicDelayTestConcurrency {
    try {
      final cpuCores = Platform.numberOfProcessors;
      return (cpuCores * 15).clamp(20, 300);
    } catch (e) {
      return 50; // 异常时使用保守值
    }
  }

  // ==================== 其他配置 ====================
  static const String defaultTestUrl = 'https://www.gstatic.com/generate_204';
  static const String defaultLogLevel = 'silent';
  static const String defaultOutboundMode =
      'rule'; // 默认出站模式（rule/global/direct）
  static const int defaultKeepAliveInterval = 30; // TCP Keep-Alive 间隔（s）
  static const String defaultUserAgent = 'clash.meta'; // 默认 User-Agent

  // ==================== TUN 配置默认值 ====================
  static const String defaultGeodataLoader = 'memconservative'; // GEO 数据加载模式
  static const String defaultFindProcessMode =
      'off'; // 查找进程模式（off/strict/always）
  static const String defaultTunStack =
      'mixed'; // TUN 网络栈类型（system/gvisor/mixed）
  static const String defaultTunDevice = 'Mihomo'; // TUN 虚拟网卡名称
  static const List<String> defaultTunDnsHijack = ['any:53']; // DNS 劫持规则
  static const int defaultTunMtu = 1500; // TUN MTU 值

  // ==================== 端口范围验证 ====================
  static const int minPort = 1; // 最小端口号
  static const int maxPort = 65535; // 最大端口号
  static const int minTunMtu = 1280; // 最小 MTU 值（IPv6 要求）
  static const int maxTunMtu = 9000; // 最大 MTU 值（支持 Jumbo Frame）

  // ==================== 防抖延迟配置 ====================
  static const int configReloadDebounceMs = 500; // 配置热重载防抖延迟（ms）
  static const int restartDebounceMs = 1000; // 核心重启防抖延迟（ms）
  static const int restartIntervalMs = 300; // 重启间隔延迟（ms）
}

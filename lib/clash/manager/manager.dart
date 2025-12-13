import 'dart:async';
import 'package:flutter/foundation.dart';
import 'package:stelliberty/clash/network/api_client.dart';
import 'package:stelliberty/clash/services/process_service.dart';
import 'package:stelliberty/clash/config/clash_defaults.dart';
import 'package:stelliberty/clash/data/connection_model.dart';
import 'package:stelliberty/clash/data/traffic_data_model.dart';
import 'package:stelliberty/clash/services/traffic_monitor.dart';
import 'package:stelliberty/clash/services/log_service.dart';
import 'package:stelliberty/clash/storage/preferences.dart';
import 'package:stelliberty/src/bindings/signals/signals.dart';
import 'package:stelliberty/utils/logger.dart';
import 'package:stelliberty/clash/manager/lifecycle_manager.dart';
import 'package:stelliberty/clash/manager/config_manager.dart';
import 'package:stelliberty/clash/manager/proxy_manager.dart';
import 'package:stelliberty/clash/manager/connection_manager.dart';
import 'package:stelliberty/clash/manager/system_proxy_manager.dart';

// Clash 管理器（门面模式）
// 协调各个子管理器，提供统一的管理接口
class ClashManager extends ChangeNotifier {
  static final ClashManager _instance = ClashManager._internal();
  static ClashManager get instance => _instance;

  late final ClashApiClient _apiClient;
  final ProcessService _processService = ProcessService();
  final TrafficMonitor _trafficMonitor = TrafficMonitor.instance;
  final ClashLogService _logService = ClashLogService.instance;

  late final LifecycleManager _lifecycleManager;
  late final ConfigManager _configManager;
  late final ProxyManager _proxyManager;
  late final ConnectionManager _connectionManager;
  late final SystemProxyManager _systemProxyManager;

  // 配置重载防抖定时器
  Timer? _configReloadDebounceTimer;

  // 懒惰模式：标记是否为应用启动后的首次核心启动
  static bool _isFirstStartAfterAppLaunch = true;

  ClashApiClient? get apiClient => isCoreRunning ? _apiClient : null;
  Stream<TrafficData>? get trafficStream => _trafficMonitor.trafficStream;

  // 获取当前累计流量
  int get totalUpload => _trafficMonitor.totalUpload;
  int get totalDownload => _trafficMonitor.totalDownload;

  // 获取最后一次的流量数据（用于组件初始化，避免显示零值）
  TrafficData? get lastTrafficData => _trafficMonitor.lastTrafficData;

  // 获取波形图历史数据
  List<double> get uploadHistory => _trafficMonitor.uploadHistory;
  List<double> get downloadHistory => _trafficMonitor.downloadHistory;

  void resetTrafficStats() {
    _trafficMonitor.resetTotalTraffic();
  }

  bool get isCoreRunning => _lifecycleManager.isCoreRunning;
  bool get isCoreRestarting => _lifecycleManager.isCoreRestarting;
  String? get currentConfigPath => _lifecycleManager.currentConfigPath;
  String get coreVersion => _lifecycleManager.coreVersion;

  bool get isAllowLanEnabled => _configManager.isAllowLanEnabled;
  bool get isIpv6Enabled => _configManager.isIpv6Enabled;
  bool get isTcpConcurrentEnabled => _configManager.isTcpConcurrentEnabled;
  bool get isUnifiedDelayEnabled => _configManager.isUnifiedDelayEnabled;
  String get geodataLoader => _configManager.geodataLoader;
  String get findProcessMode => _configManager.findProcessMode;
  String get clashCoreLogLevel => _configManager.clashCoreLogLevel;
  String? get externalController => _configManager.externalController;
  bool get isExternalControllerEnabled =>
      _configManager.isExternalControllerEnabled;
  String get testUrl => _configManager.testUrl;
  bool get tunEnabled => _configManager.tunEnabled;
  String get tunStack => _configManager.tunStack;
  String get tunDevice => _configManager.tunDevice;
  bool get isTunAutoRouteEnabled => _configManager.isTunAutoRouteEnabled;
  bool get isTunAutoRedirectEnabled => _configManager.isTunAutoRedirectEnabled;
  bool get isTunAutoDetectInterfaceEnabled =>
      _configManager.isTunAutoDetectInterfaceEnabled;
  List<String> get tunDnsHijack => _configManager.tunDnsHijack;
  bool get isTunStrictRouteEnabled => _configManager.isTunStrictRouteEnabled;
  List<String> get tunRouteExcludeAddress =>
      _configManager.tunRouteExcludeAddress;
  bool get isTunIcmpForwardingDisabled =>
      _configManager.isTunIcmpForwardingDisabled;
  int get tunMtu => _configManager.tunMtu;
  int get mixedPort => _configManager.mixedPort; // 混合端口
  int? get socksPort => _configManager.socksPort; // SOCKS 端口
  int? get httpPort => _configManager.httpPort; // HTTP 端口
  String get outboundMode => _configManager.outboundMode;

  // TCP Keep-Alive 配置（启动参数，直接从持久化读取）
  bool get isKeepAliveEnabled =>
      ClashPreferences.instance.getKeepAliveEnabled();
  int get keepAliveInterval => ClashPreferences.instance.getKeepAliveInterval();

  bool get isSystemProxyEnabled => _systemProxyManager.isSystemProxyEnabled;

  // 覆写获取回调（从 SubscriptionProvider 注入）
  List<OverrideConfig> Function()? _getOverridesCallback;

  // 覆写失败回调（启动失败时禁用当前订阅的所有覆写）
  Future<void> Function()? _onOverridesFailedCallback;

  // 防重复调用标记
  bool _isHandlingOverridesFailed = false;

  // 获取覆写配置（公开接口，供 ServiceProvider 使用）
  List<OverrideConfig> getOverrides() {
    return _getOverridesCallback?.call() ?? [];
  }

  // 覆写失败处理（公开接口，供 LifecycleManager 调用）
  Future<void> onOverridesFailed() async {
    if (_isHandlingOverridesFailed) {
      Logger.warning('覆写失败回调正在处理中，跳过重复调用');
      return;
    }

    if (_onOverridesFailedCallback == null) {
      Logger.debug('覆写失败回调未设置，跳过处理');
      return;
    }

    _isHandlingOverridesFailed = true;
    try {
      Logger.info('开始执行覆写失败回调');
      await _onOverridesFailedCallback!();
      Logger.info('覆写失败回调执行完成');
    } catch (e) {
      Logger.error('覆写失败回调执行异常：$e');
    } finally {
      _isHandlingOverridesFailed = false;
    }
  }

  ClashManager._internal() {
    _apiClient = ClashApiClient();

    _configManager = ConfigManager(
      apiClient: _apiClient,
      notifyListeners: notifyListeners,
      isCoreRunning: () => isCoreRunning,
    );

    _lifecycleManager = LifecycleManager(
      processService: _processService,
      apiClient: _apiClient,
      trafficMonitor: _trafficMonitor,
      logService: _logService,
      notifyListeners: notifyListeners,
      refreshAllStatusBatch: _configManager.refreshAllStatusBatch,
    );

    _proxyManager = ProxyManager(
      apiClient: _apiClient,
      isCoreRunning: () => isCoreRunning,
      getTestUrl: () => testUrl,
    );

    _connectionManager = ConnectionManager(
      apiClient: _apiClient,
      isCoreRunning: () => isCoreRunning,
    );

    _systemProxyManager = SystemProxyManager(
      isCoreRunning: () => isCoreRunning,
      getHttpPort: () => mixedPort, // 系统代理使用混合端口
      notifyListeners: notifyListeners,
    );
  }

  // 设置覆写获取回调（由 SubscriptionProvider 注入）
  void setOverridesGetter(List<OverrideConfig> Function() callback) {
    _getOverridesCallback = callback;
    Logger.debug('已设置覆写获取回调到 ClashManager');
  }

  // 设置覆写失败回调（由 SubscriptionProvider 注入）
  void setOverridesFailedCallback(Future<void> Function() callback) {
    _onOverridesFailedCallback = callback;
    Logger.debug('已设置覆写失败回调到 ClashManager');
  }

  Future<bool> startCore({
    String? configPath,
    List<OverrideConfig> overrides = const [],
  }) async {
    final success = await _lifecycleManager.startCore(
      configPath: configPath,
      overrides: overrides,
      onOverridesFailed: onOverridesFailed,
      mixedPort: _configManager.mixedPort, // 传递混合端口
      isIpv6Enabled: _configManager.isIpv6Enabled,
      tunEnabled: _configManager.tunEnabled,
      tunStack: _configManager.tunStack,
      tunDevice: _configManager.tunDevice,
      isTunAutoRouteEnabled: _configManager.isTunAutoRouteEnabled,
      isTunAutoRedirectEnabled: _configManager.isTunAutoRedirectEnabled,
      isTunAutoDetectInterfaceEnabled:
          _configManager.isTunAutoDetectInterfaceEnabled,
      tunDnsHijack: _configManager.tunDnsHijack,
      isTunStrictRouteEnabled: _configManager.isTunStrictRouteEnabled,
      tunRouteExcludeAddress: _configManager.tunRouteExcludeAddress,
      isTunIcmpForwardingDisabled: _configManager.isTunIcmpForwardingDisabled,
      tunMtu: _configManager.tunMtu,
      isAllowLanEnabled: _configManager.isAllowLanEnabled,
      isTcpConcurrentEnabled: _configManager.isTcpConcurrentEnabled,
      geodataLoader: _configManager.geodataLoader,
      findProcessMode: _configManager.findProcessMode,
      clashCoreLogLevel: _configManager.clashCoreLogLevel,
      externalController: _configManager.externalController,
      isUnifiedDelayEnabled: _configManager.isUnifiedDelayEnabled,
      outboundMode: _configManager.outboundMode,
      socksPort: _configManager.socksPort,
      httpPort: _configManager.httpPort, // 单独 HTTP 端口
    );

    // 懒惰模式：仅在应用首次启动核心时自动开启系统代理
    if (success && _isFirstStartAfterAppLaunch) {
      final prefs = ClashPreferences.instance;
      if (prefs.getLazyMode()) {
        Logger.info('懒惰模式已启用，自动开启系统代理（应用首次启动）');
        unawaited(enableSystemProxy());
      }
      _isFirstStartAfterAppLaunch = false;
    }

    return success;
  }

  Future<bool> stopCore() async {
    return await _lifecycleManager.stopCore();
  }

  // 重启核心（停止后重新启动）
  // 用于应用配置更改（如端口、外部控制器等需要重启才能生效的配置）
  Future<bool> restartCore({String? configPath}) async {
    Logger.info('开始重启核心');

    // 停止核心
    final stopSuccess = await stopCore();
    if (!stopSuccess) {
      Logger.error('停止核心失败，中止重启');
      return false;
    }

    // 等待一小段时间确保端口完全释放
    await Future.delayed(const Duration(milliseconds: 50));

    // 启动核心
    final startSuccess = await startCore(configPath: configPath);
    if (!startSuccess) {
      Logger.error('启动核心失败');
      return false;
    }

    Logger.info('核心重启成功');
    return true;
  }

  Future<Map<String, dynamic>> getProxies() async {
    return await _proxyManager.getProxies();
  }

  Future<bool> changeProxy(String groupName, String proxyName) async {
    return await _proxyManager.changeProxy(groupName, proxyName);
  }

  Future<int> testProxyDelay(String proxyName, {String? testUrl}) async {
    return await _proxyManager.testProxyDelay(proxyName, testUrl: testUrl);
  }

  Future<Map<String, dynamic>> getConfig() async {
    return await _configManager.getConfig();
  }

  Future<bool> updateConfig(Map<String, dynamic> config) async {
    return await _configManager.updateConfig(config);
  }

  Future<bool> reloadConfig({
    String? configPath,
    List<OverrideConfig> overrides = const [],
  }) async {
    final success = await _configManager.reloadConfig(
      configPath: configPath,
      overrides: overrides,
    );

    // 重载成功后，更新 lifecycle_manager 的配置路径缓存
    if (success && configPath != null) {
      _lifecycleManager.updateConfigPath(configPath);
    }

    return success;
  }

  // 使用默认配置重载核心（用于订阅配置加载失败时的回退）
  Future<bool> reloadWithEmptyConfig() async {
    Logger.info('使用默认配置重载核心');
    return await reloadConfig(configPath: null);
  }

  // 使用默认配置重启核心（用于默认配置重载也失败时的回退）
  Future<bool> restartWithEmptyConfig() async {
    Logger.info('使用默认配置重启核心');
    try {
      await stopCore();
      await Future.delayed(const Duration(seconds: 1));
      return await startCore(configPath: null);
    } catch (e) {
      Logger.error('使用默认配置重启核心失败：$e');
      return false;
    }
  }

  Future<bool> setAllowLan(bool enabled) async {
    final success = await _configManager.setAllowLan(enabled);
    if (success) {
      _scheduleConfigReload('局域网代理');
    }
    return success;
  }

  Future<bool> setIpv6(bool enabled) async {
    final success = await _configManager.setIpv6(enabled);
    if (success) {
      _scheduleConfigReload('IPv6');
    }
    return success;
  }

  Future<bool> setTcpConcurrent(bool enabled) async {
    final success = await _configManager.setTcpConcurrent(enabled);
    if (success) {
      _scheduleConfigReload('TCP 并发设置');
    }
    return success;
  }

  Future<bool> setUnifiedDelay(bool enabled) async {
    final success = await _configManager.setUnifiedDelay(enabled);
    if (success) {
      _scheduleConfigReload('统一延迟设置');
    }
    return success;
  }

  Future<bool> setGeodataLoader(String mode) async {
    final success = await _configManager.setGeodataLoader(mode);
    if (success) {
      _scheduleConfigReload('GEO 数据加载模式');
    }
    return success;
  }

  // 调度配置重载（使用防抖机制）
  // 在指定时间内的多次配置修改只会触发一次重载
  void _scheduleConfigReload(String reason) {
    if (!isCoreRunning || currentConfigPath == null) return;

    // 取消之前的定时器
    _configReloadDebounceTimer?.cancel();

    // 设置新的防抖定时器
    _configReloadDebounceTimer = Timer(
      Duration(milliseconds: ClashDefaults.configReloadDebounceMs),
      () async {
        Logger.info('触发配置重载以应用最新设置（原因：$reason）…');
        try {
          await reloadConfig(configPath: currentConfigPath);
          Logger.info('配置重载完成，所有设置已生效');
        } catch (e) {
          Logger.error('配置重载失败：$e');
        }
      },
    );
  }

  Future<bool> setFindProcessMode(String mode) async {
    final success = await _configManager.setFindProcessMode(mode);
    if (success) {
      _scheduleConfigReload('查找进程模式');
    }
    return success;
  }

  Future<bool> setClashCoreLogLevel(String level) async {
    final success = await _configManager.setClashCoreLogLevel(level);
    if (success) {
      _scheduleConfigReload('日志等级');
    }
    return success;
  }

  Future<bool> setExternalController(bool enabled) async {
    // 使用 ConfigManager 的方法更新配置（同时更新内存和持久化）
    final defaultAddress = ClashPreferences.instance
        .getExternalControllerAddress();
    await _configManager.setExternalController(enabled, defaultAddress);

    if (isCoreRunning) {
      Logger.info('外部控制器配置已更改，重启核心以应用');
      return await restartCore();
    }

    return true;
  }

  Future<bool> setKeepAlive(bool enabled) async {
    await ClashPreferences.instance.setKeepAliveEnabled(enabled);

    if (isCoreRunning) {
      Logger.info('TCP 保持活动配置已更改，重启核心以应用');
      return await restartCore();
    }

    return true;
  }

  Future<bool> setTestUrl(String url) async {
    return await _configManager.setTestUrl(url);
  }

  Future<bool> setMixedPort(int port) async {
    return await _configManager.setMixedPort(port, () {
      unawaited(_systemProxyManager.restartSystemProxy());
    });
  }

  Future<bool> setSocksPort(int? port) async {
    return await _configManager.setSocksPort(port);
  }

  Future<bool> setHttpPort(int? port) async {
    return await _configManager.setHttpPort(port);
  }

  Future<bool> setTunEnabled(bool enabled) async {
    // 先更新本地状态和持久化
    final success = await _configManager.setTunEnabled(enabled);

    // 如果核心正在运行且更新成功，重新生成配置并重载
    // 即使 currentConfigPath 为 null（无订阅），也支持重载（使用默认配置）
    if (success && isCoreRunning) {
      Logger.debug(
        'TUN 状态已更新，重新生成配置文件并重载（${currentConfigPath != null ? "使用订阅配置" : "使用默认配置"}）…',
      );
      await reloadConfig(
        configPath: currentConfigPath, // 可能为 null（无订阅时使用默认配置）
        overrides: getOverrides(),
      );
    }

    return success;
  }

  Future<bool> setTunStack(String stack) async {
    return await _configManager.setTunStack(stack);
  }

  Future<bool> setTunDevice(String device) async {
    return await _configManager.setTunDevice(device);
  }

  Future<bool> setTunAutoRoute(bool enabled) async {
    return await _configManager.setTunAutoRoute(enabled);
  }

  Future<bool> setTunAutoDetectInterface(bool enabled) async {
    return await _configManager.setTunAutoDetectInterface(enabled);
  }

  Future<bool> setTunDnsHijack(List<String> dnsHijack) async {
    return await _configManager.setTunDnsHijack(dnsHijack);
  }

  Future<bool> setTunStrictRoute(bool enabled) async {
    return await _configManager.setTunStrictRoute(enabled);
  }

  Future<bool> setTunAutoRedirect(bool enabled) async {
    return await _configManager.setTunAutoRedirect(enabled);
  }

  Future<bool> setTunRouteExcludeAddress(List<String> addresses) async {
    return await _configManager.setTunRouteExcludeAddress(addresses);
  }

  Future<bool> setTunDisableIcmpForwarding(bool disabled) async {
    return await _configManager.setTunDisableIcmpForwarding(disabled);
  }

  Future<bool> setTunMtu(int mtu) async {
    return await _configManager.setTunMtu(mtu);
  }

  Future<List<ConnectionInfo>> getConnections() async {
    return await _connectionManager.getConnections();
  }

  Future<bool> closeConnection(String connectionId) async {
    return await _connectionManager.closeConnection(connectionId);
  }

  Future<bool> closeAllConnections() async {
    return await _connectionManager.closeAllConnections();
  }

  Future<String> getMode() async {
    if (!isCoreRunning) {
      Logger.warning('Clash 未运行，返回默认模式');
      return ClashDefaults.defaultOutboundMode;
    }

    try {
      return await _apiClient.getMode();
    } catch (e) {
      Logger.error('获取出站模式失败：$e');
      return ClashDefaults.defaultOutboundMode;
    }
  }

  Future<bool> setOutboundMode(String outboundMode) async {
    return await _configManager.setOutboundMode(outboundMode);
  }

  // 从持久化存储重新加载配置到内存
  // 用于备份还原后刷新内存状态
  void reloadFromPreferences() {
    _configManager.reloadFromPreferences();
  }

  // 重启系统代理（先禁用再启用，应用当前配置）
  Future<void> restartSystemProxy() async {
    await _systemProxyManager.restartSystemProxy();
  }

  Future<bool> enableSystemProxy() async {
    return await _systemProxyManager.enableSystemProxy();
  }

  Future<bool> disableSystemProxy() async {
    return await _systemProxyManager.disableSystemProxy();
  }

  @override
  void dispose() {
    _lifecycleManager.dispose();

    Logger.info('应用关闭，检查并清理系统代理…');
    unawaited(disableSystemProxy());

    Logger.info('应用关闭，停止 Clash 核心…');
    unawaited(stopCore());

    super.dispose();
  }
}

import 'dart:async';
import 'dart:io';
import 'dart:ui';
import 'package:stelliberty/clash/client/clash_core_client.dart';
import 'package:stelliberty/clash/services/process_service.dart';
import 'package:stelliberty/clash/config/clash_defaults.dart';
import 'package:stelliberty/clash/model/connection_model.dart';
import 'package:stelliberty/clash/model/traffic_data_model.dart';
import 'package:stelliberty/clash/model/log_message_model.dart';
import 'package:stelliberty/clash/model/rule_model.dart';
import 'package:stelliberty/clash/services/traffic_monitor.dart';
import 'package:stelliberty/clash/services/core_log_service.dart';
import 'package:stelliberty/storage/clash_preferences.dart';
import 'package:stelliberty/src/bindings/signals/signals.dart';
import 'package:stelliberty/services/log_print_service.dart';
import 'package:stelliberty/clash/manager/lifecycle_manager.dart';
import 'package:stelliberty/clash/manager/config_manager.dart';
import 'package:stelliberty/clash/manager/proxy_manager.dart';
import 'package:stelliberty/clash/manager/connection_manager.dart';
import 'package:stelliberty/clash/manager/system_proxy_manager.dart';
import 'package:stelliberty/clash/state/core_states.dart';

// Clash 管理器（门面模式）
// 协调各个子管理器，提供统一的管理接口
class ClashManager {
  static final ClashManager _instance = ClashManager._internal();
  static ClashManager get instance => _instance;

  late final ClashCoreClient _coreClient;
  final ProcessService _processService = ProcessService();

  // 监控服务
  final TrafficMonitor _trafficMonitor = TrafficMonitor.instance;
  final ClashLogService _logService = ClashLogService.instance;

  late final LifecycleManager _lifecycleManager;
  late final ConfigManager _configManager;
  late final ProxyManager _proxyManager;
  late final ConnectionManager _connectionManager;
  late final SystemProxyManager _systemProxyManager;

  // 配置重载防抖定时器
  Timer? _configReloadDebounceTimer;

  // 核心运行状态回调（由 Provider 注入，统一两端状态来源）
  bool Function()? _coreRunningProvider;

  // 配置变更回调（由 Provider 注入，配置修改后通知 UI 刷新）
  VoidCallback? _onConfigChanged;

  // 注入核心运行状态回调（由 ClashProvider 在初始化时调用）
  void setCoreRunningProvider(bool Function() provider) {
    _coreRunningProvider = provider;
  }

  ClashCoreClient? get coreClient => isCoreRunning ? _coreClient : null;
  Stream<TrafficData>? get trafficStream => _trafficMonitor.trafficStream;
  Stream<ClashLogMessage> get logStream => _logService.logStream;

  // Android 端日志监控管理（桌面端由 LifecycleManager 管理）
  Future<void> startLogMonitoring() async {
    await _logService.startMonitoring();
  }

  Future<void> stopLogMonitoring() async {
    await _logService.stopMonitoring();
  }

  bool get isCoreRunning =>
      _coreRunningProvider?.call() ?? _lifecycleManager.isCoreRunning;
  bool get isCoreRestarting => _lifecycleManager.isCoreRestarting;
  String? get currentConfigPath => _lifecycleManager.currentConfigPath;
  String get coreVersion => _lifecycleManager.coreVersion;

  // TCP Keep-Alive 配置（启动参数，直接从持久化读取）
  bool get isKeepAliveEnabled =>
      ClashPreferences.instance.getKeepAliveEnabled();
  int get keepAliveInterval => ClashPreferences.instance.getKeepAliveInterval();

  bool get isSystemProxyEnabled => _systemProxyManager.isSystemProxyEnabled;

  // 覆写获取回调（从 SubscriptionProvider 注入）
  List<OverrideConfig> Function()? _getOverrides;

  // 覆写失败回调（启动失败时禁用当前订阅的所有覆写）
  Future<void> Function()? _onOverridesFailed;

  // 默认配置启动成功回调（清除 currentSubscription，避免应用重启后再次尝试失败的配置）
  Future<void> Function()? _onThirdLevelFallback;

  // 防重复调用标记
  bool _isHandlingOverridesFailed = false;

  // 获取覆写配置
  List<OverrideConfig> getOverrides() {
    return _getOverrides?.call() ?? [];
  }

  // 覆写失败处理
  Future<void> handleOverridesFailed() async {
    if (_isHandlingOverridesFailed) {
      Logger.warning('覆写失败回调正在处理中，跳过重复调用');
      return;
    }

    if (_onOverridesFailed == null) {
      Logger.debug('覆写失败回调未设置，跳过处理');
      return;
    }

    _isHandlingOverridesFailed = true;
    try {
      Logger.info('开始执行覆写失败回调');
      await _onOverridesFailed!();
      Logger.info('覆写失败回调执行完成');
    } catch (e) {
      Logger.error('覆写失败回调执行异常：$e');
    } finally {
      _isHandlingOverridesFailed = false;
    }
  }

  ClashManager._internal() {
    _coreClient = ClashCoreClient.instance;

    _configManager = ConfigManager(
      coreClient: _coreClient,
      isCoreRunning: () => isCoreRunning,
    );

    _lifecycleManager = LifecycleManager(
      processService: _processService,
      coreClient: _coreClient,
    );

    _proxyManager = ProxyManager(
      coreClient: _coreClient,
      isCoreRunning: () => isCoreRunning,
      getTestUrl: () => ClashPreferences.instance.getTestUrl(),
    );

    _connectionManager = ConnectionManager(
      coreClient: _coreClient,
      isCoreRunning: () => isCoreRunning,
    );

    _systemProxyManager = SystemProxyManager(
      isCoreRunning: () => isCoreRunning,
      getHttpPort: () => ClashPreferences.instance.getMixedPort(),
    );
  }

  // 设置覆写获取回调
  void setOverridesGetter(List<OverrideConfig> Function() getter) {
    _getOverrides = getter;
    Logger.debug('已设置覆写获取回调到 ClashManager');
  }

  // 设置覆写失败回调（由 SubscriptionProvider 注入）
  void setOnOverridesFailed(Future<void> Function() handler) {
    _onOverridesFailed = handler;
    Logger.debug('已设置覆写失败回调到 ClashManager');
  }

  // 设置默认配置启动成功回调（由 SubscriptionProvider 注入）
  void setOnThirdLevelFallback(Future<void> Function() handler) {
    _onThirdLevelFallback = handler;
    Logger.debug('已设置默认配置启动成功回调到 ClashManager');
  }

  // 设置状态变化回调（由 ClashProvider 注入）
  void setStateChangeCallbacks({
    Function(CoreState)? onCoreStateChanged,
    Function(String)? onCoreVersionChanged,
    Function(String?)? onConfigPathChanged,
    Function(ClashStartMode?)? onStartModeChanged,
    Function(bool)? onSystemProxyStateChanged,
    VoidCallback? onConfigChanged,
  }) {
    // 包装核心状态回调，在状态变化时管理监控服务
    _lifecycleManager.setOnCoreStateChanged((state) {
      _handleCoreStateChanged(state);
      onCoreStateChanged?.call(state);
    });
    _lifecycleManager.setOnCoreVersionChanged(onCoreVersionChanged);
    _lifecycleManager.setOnConfigPathChanged(onConfigPathChanged);
    _lifecycleManager.setOnStartModeChanged(onStartModeChanged);
    _systemProxyManager.setOnSystemProxyStateChanged(onSystemProxyStateChanged);
    _onConfigChanged = onConfigChanged;
    Logger.debug('已设置状态变化回调到各个 Manager');
  }

  // 处理核心状态变化，统一管理监控服务
  void _handleCoreStateChanged(CoreState state) {
    if (Platform.isAndroid) return; // Android 端监控由 ClashProvider 管理

    if (state == CoreState.running) {
      _startAllMonitoringServices();
    } else if (state == CoreState.stopped) {
      _stopAllMonitoringServices();
    }
  }

  // 启动所有监控服务（桌面端）
  void _startAllMonitoringServices() {
    Logger.info('核心已启动，启动所有监控服务');
    _trafficMonitor.startMonitoring();
    _logService.startMonitoring();
  }

  // 停止所有监控服务（桌面端）
  void _stopAllMonitoringServices() {
    Logger.info('核心已停止，停止所有监控服务');
    _trafficMonitor.stopMonitoring();
    _logService.stopMonitoring();
  }

  Future<bool> startCore({
    String? configPath,
    List<OverrideConfig> overrides = const [],
  }) async {
    // 从持久化存储读取配置参数
    final prefs = ClashPreferences.instance;

    final success = await _lifecycleManager.startCore(
      configPath: configPath,
      overrides: overrides,
      onOverridesFailed: handleOverridesFailed,
      onThirdLevelFallback: _onThirdLevelFallback,
      mixedPort: prefs.getMixedPort(),
      isIpv6Enabled: prefs.getIpv6(),
      isTunEnabled: prefs.getTunEnable(),
      tunStack: prefs.getTunStack(),
      tunDevice: prefs.getTunDevice(),
      isTunAutoRouteEnabled: prefs.getTunAutoRoute(),
      isTunAutoRedirectEnabled: prefs.getTunAutoRedirect(),
      isTunAutoDetectInterfaceEnabled: prefs.getTunAutoDetectInterface(),
      tunDnsHijacks: prefs.getTunDnsHijack(),
      isTunStrictRouteEnabled: prefs.getTunStrictRoute(),
      tunRouteExcludeAddresses: prefs.getTunRouteExcludeAddress(),
      isTunIcmpForwardingDisabled: prefs.getTunDisableIcmpForwarding(),
      tunMtu: prefs.getTunMtu(),
      isAllowLanEnabled: prefs.getAllowLan(),
      isTcpConcurrentEnabled: prefs.getTcpConcurrent(),
      geodataLoader: prefs.getGeodataLoader(),
      findProcessMode: prefs.getFindProcessMode(),
      clashCoreLogLevel: prefs.getCoreLogLevel(),
      externalController: prefs.getExternalControllerEnabled()
          ? prefs.getExternalControllerAddress()
          : '',
      isUnifiedDelayEnabled: prefs.getUnifiedDelayEnabled(),
      outboundMode: prefs.getOutboundMode(),
      socksPort: prefs.getSocksPort(),
      httpPort: prefs.getHttpPort(),
    );

    return success;
  }

  Future<bool> stopCore() async {
    return await _lifecycleManager.stopCore();
  }

  // 强制重置核心状态（服务安装/卸载时调用）
  void forceResetCoreState() {
    _lifecycleManager.forceResetCoreState();
  }

  // 启动服务心跳定时器（仅服务模式使用，代理方法）
  void startServiceHeartbeat() {
    _lifecycleManager.startServiceHeartbeat();
  }

  // 停止服务心跳定时器（仅服务模式使用，代理方法）
  void stopServiceHeartbeat() {
    _lifecycleManager.stopServiceHeartbeat();
  }

  // 重启核心（停止后重新启动）
  // 用于应用配置更改（如端口、外部控制器等需要重启才能生效的配置）
  Future<bool> restartCore({
    String? configPath,
    List<OverrideConfig>? overrides,
  }) async {
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
    final startSuccess = await startCore(
      configPath: configPath ?? currentConfigPath,
      overrides: overrides ?? getOverrides(),
    );
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

  // 测试单个代理节点延迟
  Future<int> testProxyDelayViaRust(String proxyName, {String? testUrl}) async {
    return await _proxyManager.testProxyDelayViaRust(
      proxyName,
      testUrl: testUrl,
    );
  }

  // 批量测试代理节点延迟
  Future<Map<String, int>> testGroupDelays(
    List<String> proxyNames, {
    String? testUrl,
    Function(String nodeName)? onNodeStart,
    Function(String nodeName, int delay)? onNodeComplete,
  }) async {
    return await _proxyManager.testGroupDelays(
      proxyNames,
      testUrl: testUrl,
      onNodeStart: onNodeStart,
      onNodeComplete: onNodeComplete,
    );
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

  Future<bool> setLanAuthentication(String username, String password) async {
    final success = await _configManager.setLanAuthentication(
      username,
      password,
    );
    if (success) {
      _scheduleConfigReload('局域网认证');
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
    if (!isCoreRunning) return;

    // 取消定时器
    _configReloadDebounceTimer?.cancel();

    // 设置防抖定时器
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
    final success = await _configManager.setMixedPort(port);

    // 如果核心正在运行且端口更新成功，重启系统代理以应用新端口
    if (success && isCoreRunning) {
      unawaited(_systemProxyManager.restartSystemProxy());
    }

    return success;
  }

  Future<bool> setSocksPort(int? port) async {
    return await _configManager.setSocksPort(port);
  }

  Future<bool> setHttpPort(int? port) async {
    return await _configManager.setHttpPort(port);
  }

  Future<bool> setTunEnabled(bool enabled) async {
    final success = await _configManager.setTunEnabled(enabled);
    if (success) {
      _scheduleConfigReload('TUN 模式');
      _onConfigChanged?.call();
    }
    return success;
  }

  // 通用的 TUN 子配置修改方法
  Future<bool> _setTunSubConfig(
    Future<bool> Function() setter,
    String configName,
  ) async {
    final success = await setter();
    if (success) {
      _scheduleConfigReload('TUN 配置：$configName');
      _onConfigChanged?.call();
    }
    return success;
  }

  Future<bool> setTunStack(String stack) async {
    return await _setTunSubConfig(
      () => _configManager.setTunStack(stack),
      'stack',
    );
  }

  Future<bool> setTunDevice(String device) async {
    return await _setTunSubConfig(
      () => _configManager.setTunDevice(device),
      'device',
    );
  }

  Future<bool> setTunAutoRoute(bool enabled) async {
    return await _setTunSubConfig(
      () => _configManager.setTunAutoRoute(enabled),
      'auto-route',
    );
  }

  Future<bool> setTunAutoDetectInterface(bool enabled) async {
    return await _setTunSubConfig(
      () => _configManager.setTunAutoDetectInterface(enabled),
      'auto-detect-interface',
    );
  }

  Future<bool> setTunDnsHijack(List<String> dnsHijack) async {
    return await _setTunSubConfig(
      () => _configManager.setTunDnsHijack(dnsHijack),
      'dns-hijack',
    );
  }

  Future<bool> setTunStrictRoute(bool enabled) async {
    return await _setTunSubConfig(
      () => _configManager.setTunStrictRoute(enabled),
      'strict-route',
    );
  }

  Future<bool> setTunAutoRedirect(bool enabled) async {
    return await _setTunSubConfig(
      () => _configManager.setTunAutoRedirect(enabled),
      'auto-redirect',
    );
  }

  Future<bool> setTunRouteExcludeAddress(List<String> addresses) async {
    return await _setTunSubConfig(
      () => _configManager.setTunRouteExcludeAddress(addresses),
      'route-exclude-address',
    );
  }

  // ICMP 转发设置（Mihomo PATCH API 不支持此字段，需重载配置）
  Future<bool> setTunDisableIcmpForwarding(bool disabled) async {
    final success = await _configManager.setTunDisableIcmpForwarding(disabled);
    if (success) {
      _scheduleConfigReload('ICMP 转发');
      _onConfigChanged?.call();
    }
    return success;
  }

  Future<bool> setTunMtu(int mtu) async {
    return await _setTunSubConfig(() => _configManager.setTunMtu(mtu), 'mtu');
  }

  Future<List<ConnectionInfo>> getConnections() async {
    return await _connectionManager.getConnections();
  }

  Future<List<RuleItem>> getRules() async {
    if (!isCoreRunning) {
      return [];
    }
    return await _coreClient.getRules();
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
      return await _coreClient.getMode();
    } catch (e) {
      Logger.error('获取出站模式失败：$e');
      return ClashDefaults.defaultOutboundMode;
    }
  }

  Future<bool> setOutboundMode(String outboundMode) async {
    return await _configManager.setOutboundMode(outboundMode);
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

  // 清理资源（应用关闭时调用）
  void dispose() {
    _configReloadDebounceTimer?.cancel();
    _lifecycleManager.dispose();

    Logger.info('应用关闭，检查并清理系统代理…');
    unawaited(disableSystemProxy());

    Logger.info('应用关闭，停止 Clash 核心…');
    unawaited(stopCore());
  }
}

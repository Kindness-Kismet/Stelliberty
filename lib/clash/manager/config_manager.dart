import 'dart:async';
import 'package:stelliberty/clash/network/api_client.dart';
import 'package:stelliberty/clash/config/config_injector.dart';
import 'package:stelliberty/clash/config/clash_defaults.dart';
import 'package:stelliberty/clash/storage/preferences.dart';
import 'package:stelliberty/utils/logger.dart';
import 'package:stelliberty/src/bindings/signals/signals.dart';

// Clash 配置管理器
// 负责配置的读取、更新、热重载
class ConfigManager {
  final ClashApiClient _apiClient;
  final Function() _notifyListeners;
  final bool Function() _isCoreRunning;

  // 配置状态缓存
  bool _allowLan = false;
  bool _ipv6 = false;
  bool _tcpConcurrent = false;
  bool _unifiedDelay = false;
  String _geodataLoader = ClashDefaults.defaultGeodataLoader;
  String _findProcessMode = ClashDefaults.defaultFindProcessMode;
  String _clashCoreLogLevel = ClashDefaults.defaultLogLevel;
  String _externalController = '';
  String _testUrl = ClashDefaults.defaultTestUrl;
  String _outboundMode = ClashDefaults.defaultOutboundMode;

  // 虚拟网卡模式配置缓存
  bool _tunEnable = false;
  String _tunStack = ClashDefaults.defaultTunStack;
  String _tunDevice = ClashDefaults.defaultTunDevice;
  bool _tunAutoRoute = false;
  bool _tunAutoRedirect = false;
  bool _tunAutoDetectInterface = false;
  List<String> _tunDnsHijack = List.from(ClashDefaults.defaultTunDnsHijack);
  bool _tunStrictRoute = false;
  List<String> _tunRouteExcludeAddress = [];
  bool _tunDisableIcmpForwarding = false;
  int _tunMtu = ClashDefaults.defaultTunMtu;

  // 端口配置
  int _mixedPort = ClashDefaults.mixedPort; // 混合端口 7777
  int? _socksPort; // SOCKS 端口 7779（可选）
  int? _httpPort; // HTTP 端口 7778（可选）

  // Getters
  bool get allowLan => _allowLan;
  bool get ipv6 => _ipv6;
  bool get tcpConcurrent => _tcpConcurrent;
  bool get unifiedDelay => _unifiedDelay;
  String get geodataLoader => _geodataLoader;
  String get findProcessMode => _findProcessMode;
  String get clashCoreLogLevel => _clashCoreLogLevel;
  String get externalController => _externalController;
  String get testUrl => _testUrl;
  String get outboundMode => _outboundMode;
  bool get tunEnable => _tunEnable;
  String get tunStack => _tunStack;
  String get tunDevice => _tunDevice;
  bool get tunAutoRoute => _tunAutoRoute;
  bool get tunAutoRedirect => _tunAutoRedirect;
  bool get tunAutoDetectInterface => _tunAutoDetectInterface;
  List<String> get tunDnsHijack => _tunDnsHijack;
  bool get tunStrictRoute => _tunStrictRoute;
  List<String> get tunRouteExcludeAddress => _tunRouteExcludeAddress;
  bool get tunDisableIcmpForwarding => _tunDisableIcmpForwarding;
  int get tunMtu => _tunMtu;
  int get mixedPort => _mixedPort; // 混合端口
  int? get socksPort => _socksPort; // SOCKS 端口
  int? get httpPort => _httpPort; // HTTP 端口
  bool get isExternalControllerEnabled => _externalController.isNotEmpty;

  ConfigManager({
    required ClashApiClient apiClient,
    required Function() notifyListeners,
    required bool Function() isCoreRunning,
  }) : _apiClient = apiClient,
       _notifyListeners = notifyListeners,
       _isCoreRunning = isCoreRunning {
    _loadPersistedConfig();
  }

  // 加载持久化的配置
  void _loadPersistedConfig() {
    _clashCoreLogLevel = ClashPreferences.instance.getCoreLogLevel();
    _testUrl = ClashPreferences.instance.getTestUrl();
    _externalController =
        ClashPreferences.instance.getExternalControllerEnabled()
        ? ClashPreferences.instance.getExternalControllerAddress()
        : '';

    _mixedPort = ClashPreferences.instance.getMixedPort();
    _socksPort = ClashPreferences.instance.getSocksPort();
    _httpPort = ClashPreferences.instance.getHttpPort();

    _allowLan = ClashPreferences.instance.getAllowLan();
    _ipv6 = ClashPreferences.instance.getIpv6();
    _tcpConcurrent = ClashPreferences.instance.getTcpConcurrent();
    _unifiedDelay = ClashPreferences.instance.getUnifiedDelayEnabled();
    _geodataLoader = ClashPreferences.instance.getGeodataLoader();
    _findProcessMode = ClashPreferences.instance.getFindProcessMode();

    _tunEnable = ClashPreferences.instance.getTunEnable();
    _tunStack = ClashPreferences.instance.getTunStack();
    _tunDevice = ClashPreferences.instance.getTunDevice();
    _tunAutoRoute = ClashPreferences.instance.getTunAutoRoute();
    _tunAutoRedirect = ClashPreferences.instance.getTunAutoRedirect();
    _tunAutoDetectInterface = ClashPreferences.instance
        .getTunAutoDetectInterface();
    _tunDnsHijack = ClashPreferences.instance.getTunDnsHijack();
    _tunStrictRoute = ClashPreferences.instance.getTunStrictRoute();
    _tunRouteExcludeAddress = ClashPreferences.instance
        .getTunRouteExcludeAddress();
    _tunDisableIcmpForwarding = ClashPreferences.instance
        .getTunDisableIcmpForwarding();
    _tunMtu = ClashPreferences.instance.getTunMtu();

    _outboundMode = ClashPreferences.instance.getOutboundMode();

    // 🔍 调试日志：打印加载的配置
    Logger.debug(
      '🔍 ConfigManager 已加载配置: tunEnable=$_tunEnable, tunStack=$_tunStack, tunDevice=$_tunDevice, tunAutoRoute=$_tunAutoRoute, tunAutoDetectInterface=$_tunAutoDetectInterface, tunStrictRoute=$_tunStrictRoute, tunMtu=$_tunMtu',
    );
  }

  // 获取配置
  Future<Map<String, dynamic>> getConfig() async {
    if (!_isCoreRunning()) {
      throw Exception('Clash 未在运行');
    }
    return await _apiClient.getConfig();
  }

  // 更新配置
  Future<bool> updateConfig(Map<String, dynamic> config) async {
    if (!_isCoreRunning()) {
      throw Exception('Clash 未在运行');
    }
    return await _apiClient.updateConfig(config);
  }

  // 热重载配置文件
  Future<bool> reloadConfig({
    String? configPath,
    List<OverrideConfig> overrides = const [],
  }) async {
    try {
      if (!_isCoreRunning()) {
        Logger.warning('Clash 未运行，无法热重载配置');
        return false;
      }

      Logger.debug(
        '热重载参数：configPath=$configPath, tunEnable=$_tunEnable, ipv6=$_ipv6, allowLan=$_allowLan',
      );

      String? actualConfigPath;

      // 始终生成运行时配置（即使 configPath 为 null 也要生成，使用默认配置）
      final runtimeConfigPath = await ConfigInjector.injectCustomConfigParams(
        configPath: configPath, // 可以为 null，ConfigInjector 会使用默认配置
        overrides: overrides,
        httpPort: _mixedPort,
        ipv6: _ipv6,
        tunEnable: _tunEnable,
        tunStack: _tunStack,
        tunDevice: _tunDevice,
        tunAutoRoute: _tunAutoRoute,
        tunAutoRedirect: _tunAutoRedirect,
        tunAutoDetectInterface: _tunAutoDetectInterface,
        tunDnsHijack: _tunDnsHijack,
        tunStrictRoute: _tunStrictRoute,
        tunRouteExcludeAddress: _tunRouteExcludeAddress,
        tunDisableIcmpForwarding: _tunDisableIcmpForwarding,
        tunMtu: _tunMtu,
        allowLan: _allowLan,
        tcpConcurrent: _tcpConcurrent,
        geodataLoader: _geodataLoader,
        findProcessMode: _findProcessMode,
        clashCoreLogLevel: _clashCoreLogLevel,
        externalController: _externalController,
        externalControllerSecret: ClashPreferences.instance
            .getExternalControllerSecret(),
        unifiedDelay: _unifiedDelay,
        outboundMode: _outboundMode,
      );

      if (runtimeConfigPath != null) {
        actualConfigPath = runtimeConfigPath;
      } else {
        Logger.warning('临时配置生成失败，降级使用原始配置：$configPath');
        actualConfigPath = configPath;
      }

      if (actualConfigPath == null) {
        Logger.error('配置路径为空，无法热重载');
        return false;
      }

      final success = await _apiClient.reloadConfig(
        configPath: actualConfigPath,
        force: true,
      );

      if (success) {
        _notifyListeners();
      } else {
        Logger.error('配置热重载失败');
      }

      return success;
    } catch (e) {
      Logger.error('热重载配置文件出错：$e');
      return false;
    }
  }

  // 设置局域网代理状态
  Future<bool> setAllowLan(bool enabled) async {
    try {
      if (!_isCoreRunning()) {
        _allowLan = enabled;
        await ClashPreferences.instance.setAllowLan(enabled);
        _notifyListeners();
        return true;
      }

      final success = await _apiClient.setAllowLan(enabled);
      if (success) {
        _allowLan = enabled;
        await ClashPreferences.instance.setAllowLan(enabled);
        _notifyListeners();
        Logger.info('局域网代理（支持热重载）：${enabled ? "启用" : "禁用"}');
      }
      return success;
    } catch (e) {
      Logger.error('设置局域网代理状态失败：$e');
      return false;
    }
  }

  // 设置 IPv6 状态
  Future<bool> setIpv6(bool enabled) async {
    try {
      if (!_isCoreRunning()) {
        _ipv6 = enabled;
        await ClashPreferences.instance.setIpv6(enabled);
        _notifyListeners();
        return true;
      }

      final success = await _apiClient.setIpv6(enabled);
      if (success) {
        _ipv6 = enabled;
        await ClashPreferences.instance.setIpv6(enabled);
        _notifyListeners();
        Logger.info('IPv6（支持热重载）：${enabled ? "启用" : "禁用"}');
      }
      return success;
    } catch (e) {
      Logger.error('设置 IPv6 状态失败：$e');
      return false;
    }
  }

  // 设置 TCP 并发状态
  Future<bool> setTcpConcurrent(bool enabled) async {
    try {
      if (!_isCoreRunning()) {
        _tcpConcurrent = enabled;
        await ClashPreferences.instance.setTcpConcurrent(enabled);
        _notifyListeners();
        return true;
      }

      final success = await _apiClient.setTcpConcurrent(enabled);
      if (success) {
        _tcpConcurrent = enabled;
        await ClashPreferences.instance.setTcpConcurrent(enabled);
        _notifyListeners();
        Logger.info('TCP 并发配置已更新：${enabled ? "启用" : "禁用"}');
      }
      return success;
    } catch (e) {
      Logger.error('设置 TCP 并发状态失败：$e');
      return false;
    }
  }

  // 设置统一延迟状态
  Future<bool> setUnifiedDelay(bool enabled) async {
    try {
      if (!_isCoreRunning()) {
        _unifiedDelay = enabled;
        await ClashPreferences.instance.setUnifiedDelayEnabled(enabled);
        _notifyListeners();
        return true;
      }

      final success = await _apiClient.setUnifiedDelay(enabled);
      if (success) {
        _unifiedDelay = enabled;
        await ClashPreferences.instance.setUnifiedDelayEnabled(enabled);
        _notifyListeners();
        Logger.info('统一延迟配置已更新：${enabled ? "启用（去除握手延迟）" : "禁用（包含握手延迟）"}');
      }
      return success;
    } catch (e) {
      Logger.error('设置统一延迟状态失败：$e');
      return false;
    }
  }

  // 设置 GEO 数据加载模式
  Future<bool> setGeodataLoader(String mode) async {
    try {
      if (!_isCoreRunning()) {
        _geodataLoader = mode;
        await ClashPreferences.instance.setGeodataLoader(mode);
        _notifyListeners();
        return true;
      }

      final success = await _apiClient.setGeodataLoader(mode);
      if (success) {
        _geodataLoader = mode;
        await ClashPreferences.instance.setGeodataLoader(mode);
        _notifyListeners();
        Logger.info('GEO 数据加载模式（支持热重载）：$mode');
      }
      return success;
    } catch (e) {
      Logger.error('设置 GEO 数据加载模式失败：$e');
      return false;
    }
  }

  // 设置查找进程模式
  Future<bool> setFindProcessMode(String mode) async {
    try {
      if (!_isCoreRunning()) {
        _findProcessMode = mode;
        await ClashPreferences.instance.setFindProcessMode(mode);
        _notifyListeners();
        return true;
      }

      final success = await _apiClient.setFindProcessMode(mode);
      if (success) {
        _findProcessMode = mode;
        await ClashPreferences.instance.setFindProcessMode(mode);
        _notifyListeners();
        Logger.info('查找进程模式（支持热重载）：$mode');
      }
      return success;
    } catch (e) {
      Logger.error('设置查找进程模式失败：$e');
      return false;
    }
  }

  // 设置日志等级
  Future<bool> setClashCoreLogLevel(String level) async {
    try {
      if (!_isCoreRunning()) {
        _clashCoreLogLevel = level;
        await ClashPreferences.instance.setCoreLogLevel(level);
        _notifyListeners();
        return true;
      }

      final success = await _apiClient.setLogLevel(level);
      if (success) {
        _clashCoreLogLevel = level;
        await ClashPreferences.instance.setCoreLogLevel(level);
        _notifyListeners();
        Logger.info('日志等级（支持热重载）：$level');
      }
      return success;
    } catch (e) {
      Logger.error('设置日志等级失败：$e');
      return false;
    }
  }

  // 设置外部控制器
  Future<bool> setExternalController(
    bool enabled,
    String defaultAddress,
  ) async {
    try {
      if (!_isCoreRunning()) {
        _externalController = enabled ? defaultAddress : '';
        await ClashPreferences.instance.setExternalControllerEnabled(enabled);
        _notifyListeners();
        return true;
      }

      final address = enabled ? defaultAddress : '';
      final success = await _apiClient.setExternalController(address);
      if (success) {
        _externalController = address;
        await ClashPreferences.instance.setExternalControllerEnabled(enabled);
        _notifyListeners();
        Logger.info('外部控制器（支持热重载）：${enabled ? "启用" : "禁用"} - $address');
      }
      return success;
    } catch (e) {
      Logger.error('设置外部控制器失败：$e');
      return false;
    }
  }

  // 设置 TCP 保持活动
  Future<bool> setKeepAlive(bool enabled, Function() restartCallback) async {
    try {
      await ClashPreferences.instance.setKeepAliveEnabled(enabled);
      _notifyListeners();

      Logger.info('TCP 保持活动（需要重启核心）：${enabled ? "启用" : "禁用"}');

      if (_isCoreRunning()) {
        Logger.warning('TCP 保持活动配置需要重启核心才能生效，正在安排重启...');
        restartCallback();
      }

      return true;
    } catch (e) {
      Logger.error('设置 TCP 保持活动失败：$e');
      return false;
    }
  }

  // 设置测速链接
  Future<bool> setTestUrl(String url) async {
    try {
      final uri = Uri.tryParse(url);
      if (uri == null || !uri.hasScheme) {
        Logger.error('无效的 URL 格式：$url');
        return false;
      }

      _testUrl = url;
      await ClashPreferences.instance.setTestUrl(url);
      _notifyListeners();

      Logger.info('测速链接（应用层配置）：$url');
      return true;
    } catch (e) {
      Logger.error('设置测速链接失败：$e');
      return false;
    }
  }

  // 设置混合端口
  Future<bool> setMixedPort(
    int port,
    Function() updateSystemProxyCallback,
  ) async {
    try {
      if (port < ClashDefaults.minPort || port > ClashDefaults.maxPort) {
        Logger.error('无效的端口号：$port');
        return false;
      }

      if (!_isCoreRunning()) {
        _mixedPort = port;
        await ClashPreferences.instance.setMixedPort(port);
        _notifyListeners();
        return true;
      }

      final success = await _apiClient.setMixedPort(port);
      if (success) {
        _mixedPort = port;
        await ClashPreferences.instance.setMixedPort(port);

        updateSystemProxyCallback();

        _notifyListeners();
        Logger.info('混合端口（支持热重载）：$port');
      }
      return success;
    } catch (e) {
      Logger.error('设置混合端口失败：$e');
      return false;
    }
  }

  // 设置 SOCKS 端口
  Future<bool> setSocksPort(int? port) async {
    try {
      if (port != null &&
          (port < ClashDefaults.minPort || port > ClashDefaults.maxPort)) {
        Logger.error('无效的端口号：$port');
        return false;
      }

      if (!_isCoreRunning()) {
        _socksPort = port;
        await ClashPreferences.instance.setSocksPort(port);
        _notifyListeners();
        return true;
      }

      final success = await _apiClient.setSocksPort(port ?? 0);
      if (success) {
        _socksPort = port;
        await ClashPreferences.instance.setSocksPort(port);
        _notifyListeners();
        Logger.info('SOCKS 端口（支持热重载）：${port ?? "未设置"}');
      }
      return success;
    } catch (e) {
      Logger.error('设置 SOCKS 端口失败：$e');
      return false;
    }
  }

  // 设置 HTTP 端口
  Future<bool> setHttpPort(int? port) async {
    try {
      if (port != null &&
          (port < ClashDefaults.minPort || port > ClashDefaults.maxPort)) {
        Logger.error('无效的端口号：$port');
        return false;
      }

      if (!_isCoreRunning()) {
        _httpPort = port;
        await ClashPreferences.instance.setHttpPort(port);
        _notifyListeners();
        return true;
      }

      final success = await _apiClient.setHttpPort(port ?? 0);
      if (success) {
        _httpPort = port;
        await ClashPreferences.instance.setHttpPort(port);
        _notifyListeners();
        Logger.info('HTTP 端口（支持热重载）：${port ?? "未设置"}');
      }
      return success;
    } catch (e) {
      Logger.error('设置 HTTP 端口失败：$e');
      return false;
    }
  }

  // 设置虚拟网卡模式启用状态
  Future<bool> setTunEnable(bool enabled) async {
    try {
      Logger.debug(
        '🔍 setTunEnable 被调用: enabled=$enabled, 当前 _tunEnable=$_tunEnable, isRunning=${_isCoreRunning()}',
      );

      // 更新本地状态和持久化（不管核心是否运行）
      _tunEnable = enabled;
      await ClashPreferences.instance.setTunEnable(enabled);
      _notifyListeners();

      Logger.info(
        '虚拟网卡模式配置已更新：${enabled ? "启用" : "禁用"}（${_isCoreRunning() ? "将通过配置热重载生效" : "将在下次启动时生效"}）',
      );
      return true;
    } catch (e) {
      Logger.error('设置虚拟网卡模式失败：$e');
      // 失败时回滚本地状态
      _tunEnable = !enabled;
      _notifyListeners();
      return false;
    }
  }

  // 设置虚拟网卡网络栈类型
  Future<bool> setTunStack(String stack) async {
    try {
      if (!_isCoreRunning()) {
        _tunStack = stack;
        await ClashPreferences.instance.setTunStack(stack);
        _notifyListeners();
        return true;
      }

      final success = await _apiClient.setTunStack(stack);
      if (success) {
        _tunStack = stack;
        await ClashPreferences.instance.setTunStack(stack);
        _notifyListeners();
        Logger.info('虚拟网卡网络栈（支持热重载）：$stack');
      }
      return success;
    } catch (e) {
      Logger.error('设置虚拟网卡网络栈失败：$e');
      return false;
    }
  }

  // 设置虚拟网卡设备名称
  Future<bool> setTunDevice(String device) async {
    try {
      if (!_isCoreRunning()) {
        _tunDevice = device;
        await ClashPreferences.instance.setTunDevice(device);
        _notifyListeners();
        return true;
      }

      final success = await _apiClient.setTunDevice(device);
      if (success) {
        _tunDevice = device;
        await ClashPreferences.instance.setTunDevice(device);
        _notifyListeners();
        Logger.info('虚拟网卡设备名称（支持热重载）：$device');
      }
      return success;
    } catch (e) {
      Logger.error('设置虚拟网卡设备名称失败：$e');
      return false;
    }
  }

  // 设置虚拟网卡自动路由
  Future<bool> setTunAutoRoute(bool enabled) async {
    try {
      if (!_isCoreRunning()) {
        _tunAutoRoute = enabled;
        await ClashPreferences.instance.setTunAutoRoute(enabled);
        _notifyListeners();
        return true;
      }

      final success = await _apiClient.setTunAutoRoute(enabled);
      if (success) {
        _tunAutoRoute = enabled;
        await ClashPreferences.instance.setTunAutoRoute(enabled);
        _notifyListeners();
        Logger.info('虚拟网卡自动路由（支持热重载）：${enabled ? "启用" : "禁用"}');
      }
      return success;
    } catch (e) {
      Logger.error('设置虚拟网卡自动路由失败：$e');
      return false;
    }
  }

  // 设置虚拟网卡自动 TCP 重定向（Linux 专用）
  Future<bool> setTunAutoRedirect(bool enabled) async {
    try {
      if (!_isCoreRunning()) {
        _tunAutoRedirect = enabled;
        await ClashPreferences.instance.setTunAutoRedirect(enabled);
        _notifyListeners();
        return true;
      }

      final success = await _apiClient.setTunAutoRedirect(enabled);
      if (success) {
        _tunAutoRedirect = enabled;
        await ClashPreferences.instance.setTunAutoRedirect(enabled);
        _notifyListeners();
        Logger.info('虚拟网卡自动 TCP 重定向（支持热重载）：${enabled ? "启用" : "禁用"}');
      }
      return success;
    } catch (e) {
      Logger.error('设置虚拟网卡自动 TCP 重定向失败：$e');
      return false;
    }
  }

  // 设置虚拟网卡自动检测接口
  Future<bool> setTunAutoDetectInterface(bool enabled) async {
    try {
      if (!_isCoreRunning()) {
        _tunAutoDetectInterface = enabled;
        await ClashPreferences.instance.setTunAutoDetectInterface(enabled);
        _notifyListeners();
        return true;
      }

      final success = await _apiClient.setTunAutoDetectInterface(enabled);
      if (success) {
        _tunAutoDetectInterface = enabled;
        await ClashPreferences.instance.setTunAutoDetectInterface(enabled);
        _notifyListeners();
        Logger.info('虚拟网卡自动检测接口（支持热重载）：${enabled ? "启用" : "禁用"}');
      }
      return success;
    } catch (e) {
      Logger.error('设置虚拟网卡自动检测接口失败：$e');
      return false;
    }
  }

  // 设置虚拟网卡 DNS 劫持列表
  Future<bool> setTunDnsHijack(List<String> dnsHijack) async {
    try {
      if (!_isCoreRunning()) {
        _tunDnsHijack = dnsHijack;
        await ClashPreferences.instance.setTunDnsHijack(dnsHijack);
        _notifyListeners();
        return true;
      }

      final success = await _apiClient.setTunDnsHijack(dnsHijack);
      if (success) {
        _tunDnsHijack = dnsHijack;
        await ClashPreferences.instance.setTunDnsHijack(dnsHijack);
        _notifyListeners();
        Logger.info('虚拟网卡 DNS 劫持列表（支持热重载）：$dnsHijack');
      }
      return success;
    } catch (e) {
      Logger.error('设置虚拟网卡 DNS 劫持列表失败：$e');
      return false;
    }
  }

  // 设置虚拟网卡严格路由
  Future<bool> setTunStrictRoute(bool enabled) async {
    try {
      if (!_isCoreRunning()) {
        _tunStrictRoute = enabled;
        await ClashPreferences.instance.setTunStrictRoute(enabled);
        _notifyListeners();
        return true;
      }

      final success = await _apiClient.setTunStrictRoute(enabled);
      if (success) {
        _tunStrictRoute = enabled;
        await ClashPreferences.instance.setTunStrictRoute(enabled);
        _notifyListeners();
        Logger.info('虚拟网卡严格路由（支持热重载）：${enabled ? "启用" : "禁用"}');
      }
      return success;
    } catch (e) {
      Logger.error('设置虚拟网卡严格路由失败：$e');
      return false;
    }
  }

  // 设置虚拟网卡排除网段列表
  Future<bool> setTunRouteExcludeAddress(List<String> addresses) async {
    try {
      if (!_isCoreRunning()) {
        _tunRouteExcludeAddress = addresses;
        await ClashPreferences.instance.setTunRouteExcludeAddress(addresses);
        _notifyListeners();
        return true;
      }

      final success = await _apiClient.setTunRouteExcludeAddress(addresses);
      if (success) {
        _tunRouteExcludeAddress = addresses;
        await ClashPreferences.instance.setTunRouteExcludeAddress(addresses);
        _notifyListeners();
        Logger.info('虚拟网卡排除网段列表（支持热重载）：$addresses');
      }
      return success;
    } catch (e) {
      Logger.error('设置虚拟网卡排除网段列表失败：$e');
      return false;
    }
  }

  // 设置虚拟网卡禁用 ICMP 转发
  Future<bool> setTunDisableIcmpForwarding(bool disabled) async {
    try {
      if (!_isCoreRunning()) {
        _tunDisableIcmpForwarding = disabled;
        await ClashPreferences.instance.setTunDisableIcmpForwarding(disabled);
        _notifyListeners();
        return true;
      }

      final success = await _apiClient.setTunDisableIcmpForwarding(disabled);
      if (success) {
        _tunDisableIcmpForwarding = disabled;
        await ClashPreferences.instance.setTunDisableIcmpForwarding(disabled);
        _notifyListeners();
        Logger.info('虚拟网卡 ICMP 转发（支持热重载）：${disabled ? "禁用" : "启用"}');
      }
      return success;
    } catch (e) {
      Logger.error('设置虚拟网卡 ICMP 转发失败：$e');
      return false;
    }
  }

  // 设置虚拟网卡 MTU 值
  Future<bool> setTunMtu(int mtu) async {
    try {
      if (mtu < ClashDefaults.minTunMtu || mtu > ClashDefaults.maxTunMtu) {
        Logger.error(
          '无效的 MTU 值：$mtu（应在 ${ClashDefaults.minTunMtu}-${ClashDefaults.maxTunMtu} 之间）',
        );
        return false;
      }

      if (!_isCoreRunning()) {
        _tunMtu = mtu;
        await ClashPreferences.instance.setTunMtu(mtu);
        _notifyListeners();
        return true;
      }

      final success = await _apiClient.setTunMtu(mtu);
      if (success) {
        _tunMtu = mtu;
        await ClashPreferences.instance.setTunMtu(mtu);
        _notifyListeners();
        Logger.info('虚拟网卡 MTU 值（支持热重载）：$mtu');
      }
      return success;
    } catch (e) {
      Logger.error('设置虚拟网卡 MTU 值失败：$e');
      return false;
    }
  }

  // 设置出站模式（自动判断核心状态）
  Future<bool> setOutboundMode(String outboundMode) async {
    try {
      if (_isCoreRunning()) {
        // 核心运行时，调用 API 设置
        final success = await _apiClient.setMode(outboundMode);
        if (success) {
          _outboundMode = outboundMode;
          await ClashPreferences.instance.setOutboundMode(outboundMode);
          _notifyListeners();
          Logger.info('出站模式已切换：$outboundMode');
        }
        return success;
      } else {
        // 核心未运行时，只保存偏好
        _outboundMode = outboundMode;
        await ClashPreferences.instance.setOutboundMode(outboundMode);
        _notifyListeners();
        Logger.info('出站模式已保存：$outboundMode（将在下次启动时应用）');
        return true;
      }
    } catch (e) {
      Logger.error('设置出站模式失败：$e');
      return false;
    }
  }

  // 批量刷新所有配置状态
  Future<void> refreshAllStatusBatch() async {
    if (!_isCoreRunning()) {
      return;
    }

    try {
      Logger.debug('批量刷新配置状态...');

      final results = await Future.wait([
        _apiClient.getAllowLan(),
        _apiClient.getIpv6(),
        _apiClient.getTcpConcurrent(),
        _apiClient.getUnifiedDelay(),
        _apiClient.getGeodataLoader(),
        _apiClient.getFindProcessMode(),
        _apiClient.getLogLevel(),
        _apiClient.getExternalController(),
        _apiClient.getMode(),
      ]);

      bool hasChanged = false;

      if (_allowLan != results[0]) {
        _allowLan = results[0] as bool;
        hasChanged = true;
      }

      if (_ipv6 != results[1]) {
        _ipv6 = results[1] as bool;
        hasChanged = true;
      }

      if (_tcpConcurrent != results[2]) {
        _tcpConcurrent = results[2] as bool;
        hasChanged = true;
      }

      if (_unifiedDelay != results[3]) {
        _unifiedDelay = results[3] as bool;
        hasChanged = true;
      }

      if (_geodataLoader != results[4]) {
        _geodataLoader = results[4] as String;
        hasChanged = true;
      }

      if (_findProcessMode != results[5]) {
        _findProcessMode = results[5] as String;
        hasChanged = true;
      }

      if (_clashCoreLogLevel != results[6]) {
        _clashCoreLogLevel = results[6] as String;
        hasChanged = true;
      }

      final externalController = results[7] as String?;
      final actualController = externalController ?? '';
      if (_externalController != actualController) {
        _externalController = actualController;
        hasChanged = true;
      }

      if (_outboundMode != results[8]) {
        _outboundMode = results[8] as String;
        hasChanged = true;
      }

      if (hasChanged) {
        _notifyListeners();
        Logger.debug('配置状态已更新');
      }
    } catch (e) {
      Logger.error('批量刷新配置状态失败：$e');
    }
  }
}

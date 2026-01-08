import 'package:flutter/foundation.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:stelliberty/storage/dev_preferences.dart';
import 'package:stelliberty/services/system_proxy_service.dart';
import '../clash/config/clash_defaults.dart';

// Clash 专用持久化配置管理
class ClashPreferences {
  ClashPreferences._();

  static ClashPreferences? _instance;
  static ClashPreferences get instance => _instance ??= ClashPreferences._();

  dynamic _prefs; // SharedPreferences 或 DeveloperPreferences

  // 检查是否为 Dev 模式
  static bool get isDevMode => kDebugMode || kProfileMode;

  // 初始化
  Future<void> init() async {
    if (isDevMode) {
      // Dev 模式：使用开发者偏好 JSON 配置
      await DeveloperPreferences.instance.init();
      _prefs = DeveloperPreferences.instance;
    } else {
      // Release 模式：使用系统 SharedPreferences
      _prefs = await SharedPreferences.getInstance();
    }
  }

  // 确保已初始化
  void _ensureInit() {
    if (_prefs == null) {
      throw Exception('ClashPreferences 未初始化，请先调用 init()');
    }
  }

  // ==================== 泛型辅助方法 ====================

  // 获取 bool 值
  bool _getBool(String key, bool defaultValue) {
    _ensureInit();
    return _prefs!.getBool(key) ?? defaultValue;
  }

  // 设置 bool 值
  Future<void> _setBool(String key, bool value) async {
    _ensureInit();
    await _prefs!.setBool(key, value);
  }

  // 获取 String 值
  String _getString(String key, String defaultValue) {
    _ensureInit();
    return _prefs!.getString(key) ?? defaultValue;
  }

  // 设置 String 值
  Future<void> _setString(String key, String value) async {
    _ensureInit();
    await _prefs!.setString(key, value);
  }

  // 获取 int 值
  int _getInt(String key, int defaultValue) {
    _ensureInit();
    return _prefs!.getInt(key) ?? defaultValue;
  }

  // 设置 int 值
  Future<void> _setInt(String key, int value) async {
    _ensureInit();
    await _prefs!.setInt(key, value);
  }

  // 设置可空 int 值
  Future<void> _setIntNullable(String key, int? value) async {
    _ensureInit();
    if (value != null) {
      await _prefs!.setInt(key, value);
    } else {
      await _prefs!.remove(key);
    }
  }

  // 获取 String 列表
  List<String> _getStringList(String key, List<String> defaultValue) {
    _ensureInit();
    return _prefs!.getStringList(key) ?? defaultValue;
  }

  // 设置 String 列表
  Future<void> _setStringList(String key, List<String> value) async {
    _ensureInit();
    await _prefs!.setStringList(key, value);
  }

  // 获取可空 String 值
  String? _getStringNullable(String key) {
    _ensureInit();
    return _prefs!.getString(key);
  }

  // 设置可空 String 值
  Future<void> _setStringNullable(String key, String? value) async {
    _ensureInit();
    if (value != null) {
      await _prefs!.setString(key, value);
    } else {
      await _prefs!.remove(key);
    }
  }

  // ==================== 存储键 ====================
  static const String _kAllowLan = 'clash_allow_lan';
  static const String _kIpv6 = 'clash_ipv6';
  static const String _kTcpConcurrent = 'clash_tcp_concurrent';
  static const String _kGeodataLoader = 'clash_geodata_loader';
  static const String _kFindProcessMode = 'clash_find_process_mode';
  static const String _kCoreLogLevel = 'clash_core_log_level';
  static const String _kTestUrl = 'clash_test_url';
  static const String _kUnifiedDelayEnabled = 'clash_unified_delay_enabled';
  static const String _kMixedPort = 'clash_mixed_port';
  static const String _kSocksPort = 'clash_socks_port';
  static const String _kHttpPort = 'clash_http_port';
  static const String _kExternalControllerEnabled =
      'clash_external_controller_enabled';
  static const String _kExternalControllerAddress =
      'clash_external_controller_address';
  static const String _kExternalControllerSecret =
      'clash_external_controller_secret';
  static const String _kKeepAliveEnabled = 'clash_keep_alive_enabled';
  static const String _kKeepAliveInterval = 'clash_keep_alive_interval';

  // 虚拟网卡模式配置键
  static const String _kTunEnable = 'clash_tun_enable';
  static const String _kTunStack = 'clash_tun_stack';
  static const String _kTunDevice = 'clash_tun_device';
  static const String _kTunAutoRoute = 'clash_tun_auto_route';
  static const String _kTunAutoRedirect = 'clash_tun_auto_redirect';
  static const String _kTunAutoDetectInterface =
      'clash_tun_auto_detect_interface';
  static const String _kTunDnsHijack = 'clash_tun_dns_hijack';
  static const String _kTunStrictRoute = 'clash_tun_strict_route';
  static const String _kTunRouteExcludeAddress =
      'clash_tun_route_exclude_address';
  static const String _kTunDisableIcmpForwarding =
      'clash_tun_disable_icmp_forwarding';
  static const String _kTunMtu = 'clash_tun_mtu';

  // DNS 配置键
  static const String _kDnsOverrideEnabled = 'clash_dns_override_enabled';

  // 系统代理配置键
  static const String _kProxyHost = 'clash_proxy_host';
  static const String _kSystemProxyBypass = 'clash_system_proxy_bypass';
  static const String _kUseDefaultBypass = 'clash_use_default_bypass';
  static const String _kSystemProxyPacMode = 'clash_system_proxy_pac_mode';
  static const String _kSystemProxyPacScript = 'clash_system_proxy_pac_script';

  // 订阅配置键
  static const String _kCurrentSubscriptionId = 'clash_current_subscription_id';

  // 节点选择配置键前缀（格式：clash_proxy_selection_{订阅ID}_{代理组名}）
  static const String _kProxySelectionPrefix = 'clash_proxy_selection_';

  // 出站模式配置键
  static const String _kOutboundMode = 'clash_outbound_mode';

  // 代理节点排序模式键
  static const String _kProxyNodeSortMode = 'clash_proxy_node_sort_mode';

  // 懒惰模式配置键
  static const String _kLazyMode = 'clash_lazy_mode';

  // ==================== 局域网代理 ====================

  // 获取局域网代理是否启用
  bool getAllowLan() => _getBool(_kAllowLan, false);

  // 保存局域网代理启用状态
  Future<void> setAllowLan(bool enabled) => _setBool(_kAllowLan, enabled);

  // ==================== IPv6 ====================

  // 获取 IPv6 是否启用
  bool getIpv6() => _getBool(_kIpv6, false);

  // 保存 IPv6 启用状态
  Future<void> setIpv6(bool enabled) => _setBool(_kIpv6, enabled);

  // ==================== TCP 并发 ====================

  // 获取 TCP 并发是否启用
  bool getTcpConcurrent() => _getBool(_kTcpConcurrent, false);

  // 保存 TCP 并发启用状态
  Future<void> setTcpConcurrent(bool enabled) =>
      _setBool(_kTcpConcurrent, enabled);

  // ==================== GEO 数据加载模式 ====================

  // 获取 GEO 数据加载模式
  String getGeodataLoader() => _getString(_kGeodataLoader, 'standard');

  // 保存 GEO 数据加载模式
  Future<void> setGeodataLoader(String mode) =>
      _setString(_kGeodataLoader, mode);

  // ==================== 查找进程模式 ====================

  // 获取查找进程模式
  String getFindProcessMode() => _getString(_kFindProcessMode, 'off');

  // 保存查找进程模式
  Future<void> setFindProcessMode(String mode) =>
      _setString(_kFindProcessMode, mode);

  // ==================== 核心日志等级 ====================

  // 获取核心日志等级
  String getCoreLogLevel() =>
      _getString(_kCoreLogLevel, ClashDefaults.defaultLogLevel);

  // 保存核心日志等级
  Future<void> setCoreLogLevel(String level) =>
      _setString(_kCoreLogLevel, level);

  // ==================== 测速链接 ====================

  // 获取测速链接
  String getTestUrl() => _getString(_kTestUrl, ClashDefaults.defaultTestUrl);

  // 保存测速链接
  Future<void> setTestUrl(String url) => _setString(_kTestUrl, url);

  // ==================== 统一延迟 ====================

  // 获取统一延迟是否启用
  bool getUnifiedDelayEnabled() => _getBool(_kUnifiedDelayEnabled, false);

  // 保存统一延迟启用状态
  Future<void> setUnifiedDelayEnabled(bool enabled) =>
      _setBool(_kUnifiedDelayEnabled, enabled);

  // ==================== 端口配置 ====================

  // 获取混合端口
  // Dev 模式默认 2000，Release 默认 7777
  int getMixedPort() {
    final defaultPort = isDevMode ? 2000 : ClashDefaults.mixedPort;
    return _getInt(_kMixedPort, defaultPort);
  }

  // 保存混合端口
  Future<void> setMixedPort(int port) => _setInt(_kMixedPort, port);

  // 获取 SOCKS 端口（可选，默认不启用）
  int? getSocksPort() {
    _ensureInit();
    return _prefs!.getInt(_kSocksPort); // null 表示不启用单独 SOCKS 端口
  }

  // 保存 SOCKS 端口
  Future<void> setSocksPort(int? port) => _setIntNullable(_kSocksPort, port);

  // 获取 HTTP 端口（可选，默认不启用）
  int? getHttpPort() {
    _ensureInit();
    final value = _prefs!.getInt(_kHttpPort);
    return value; // null 表示不启用单独 HTTP 端口
  }

  // 保存 HTTP 端口
  Future<void> setHttpPort(int? port) => _setIntNullable(_kHttpPort, port);

  // ==================== 外部控制器 ====================

  // 获取外部控制器是否启用
  bool getExternalControllerEnabled() =>
      _getBool(_kExternalControllerEnabled, false);

  // 保存外部控制器启用状态
  Future<void> setExternalControllerEnabled(bool enabled) =>
      _setBool(_kExternalControllerEnabled, enabled);

  // 获取外部控制器地址
  String getExternalControllerAddress() => _getString(
    _kExternalControllerAddress,
    '${ClashDefaults.apiHost}:${ClashDefaults.apiPort}',
  );

  // 保存外部控制器地址
  Future<void> setExternalControllerAddress(String address) =>
      _setString(_kExternalControllerAddress, address);

  // 获取外部控制器密钥
  String getExternalControllerSecret() =>
      _getString(_kExternalControllerSecret, '');

  // 保存外部控制器密钥
  Future<void> setExternalControllerSecret(String secret) =>
      _setString(_kExternalControllerSecret, secret);

  // ==================== TCP 保持活动 ====================

  // 获取 TCP 保持活动是否启用
  bool getKeepAliveEnabled() => _getBool(_kKeepAliveEnabled, false);

  // 保存 TCP 保持活动启用状态
  Future<void> setKeepAliveEnabled(bool enabled) =>
      _setBool(_kKeepAliveEnabled, enabled);

  // 获取 TCP 保持活动间隔（秒）
  int getKeepAliveInterval() =>
      _getInt(_kKeepAliveInterval, ClashDefaults.defaultKeepAliveInterval);

  // 保存 TCP 保持活动间隔
  Future<void> setKeepAliveInterval(int interval) =>
      _setInt(_kKeepAliveInterval, interval);

  // ==================== 虚拟网卡模式配置 ====================

  // 获取虚拟网卡模式是否启用
  bool getTunEnable() => _getBool(_kTunEnable, false);

  // 保存虚拟网卡模式启用状态
  Future<void> setTunEnable(bool enabled) => _setBool(_kTunEnable, enabled);

  // 获取虚拟网卡网络栈类型
  String getTunStack() => _getString(_kTunStack, 'mixed');

  // 保存虚拟网卡网络栈类型
  Future<void> setTunStack(String stack) => _setString(_kTunStack, stack);

  // 获取虚拟网卡设备名称
  String getTunDevice() => _getString(_kTunDevice, 'Mihomo');

  // 保存虚拟网卡设备名称
  Future<void> setTunDevice(String device) => _setString(_kTunDevice, device);

  // 获取虚拟网卡自动路由是否启用
  bool getTunAutoRoute() => _getBool(_kTunAutoRoute, false);

  // 保存虚拟网卡自动路由启用状态
  Future<void> setTunAutoRoute(bool enabled) =>
      _setBool(_kTunAutoRoute, enabled);

  // 获取虚拟网卡自动 TCP 重定向是否启用（Linux 专用）
  bool getTunAutoRedirect() => _getBool(_kTunAutoRedirect, false);

  // 保存虚拟网卡自动 TCP 重定向启用状态
  Future<void> setTunAutoRedirect(bool enabled) =>
      _setBool(_kTunAutoRedirect, enabled);

  // 获取虚拟网卡自动检测接口是否启用
  bool getTunAutoDetectInterface() => _getBool(_kTunAutoDetectInterface, true);

  // 保存虚拟网卡自动检测接口启用状态
  Future<void> setTunAutoDetectInterface(bool enabled) =>
      _setBool(_kTunAutoDetectInterface, enabled);

  // 获取虚拟网卡 DNS 劫持列表
  List<String> getTunDnsHijack() => _getStringList(_kTunDnsHijack, ['any:53']);

  // 保存虚拟网卡 DNS 劫持列表
  Future<void> setTunDnsHijack(List<String> dnsHijack) =>
      _setStringList(_kTunDnsHijack, dnsHijack);

  // 获取虚拟网卡严格路由是否启用
  bool getTunStrictRoute() => _getBool(_kTunStrictRoute, false);

  // 保存虚拟网卡严格路由启用状态
  Future<void> setTunStrictRoute(bool enabled) =>
      _setBool(_kTunStrictRoute, enabled);

  // 获取虚拟网卡排除网段列表
  List<String> getTunRouteExcludeAddress() =>
      _getStringList(_kTunRouteExcludeAddress, []);

  // 保存虚拟网卡排除网段列表
  Future<void> setTunRouteExcludeAddress(List<String> addresses) =>
      _setStringList(_kTunRouteExcludeAddress, addresses);

  // 获取虚拟网卡禁用 ICMP 转发状态
  bool getTunDisableIcmpForwarding() =>
      _getBool(_kTunDisableIcmpForwarding, false);

  // 保存虚拟网卡禁用 ICMP 转发状态
  Future<void> setTunDisableIcmpForwarding(bool disabled) =>
      _setBool(_kTunDisableIcmpForwarding, disabled);

  // 获取虚拟网卡 MTU 值
  int getTunMtu() => _getInt(_kTunMtu, 1500);

  // 保存虚拟网卡 MTU 值
  Future<void> setTunMtu(int mtu) => _setInt(_kTunMtu, mtu);

  // ==================== DNS 配置 ====================

  // 获取 DNS 覆写是否启用
  bool getDnsOverrideEnabled() => _getBool(_kDnsOverrideEnabled, false);

  // 保存 DNS 覆写启用状态
  Future<void> setDnsOverrideEnabled(bool enabled) =>
      _setBool(_kDnsOverrideEnabled, enabled);

  // ==================== 系统代理配置 ====================

  // 获取代理主机（默认 127.0.0.1）
  String getProxyHost() => _getString(_kProxyHost, '127.0.0.1');

  // 保存代理主机
  Future<void> setProxyHost(String host) => _setString(_kProxyHost, host);

  // 获取系统代理绕过地址（自定义）
  String? getSystemProxyBypass() => _getStringNullable(_kSystemProxyBypass);

  // 保存系统代理绕过地址
  Future<void> setSystemProxyBypass(String? bypass) =>
      _setStringNullable(_kSystemProxyBypass, bypass);

  // 获取是否使用默认绕过规则（默认 true）
  bool getUseDefaultBypass() => _getBool(_kUseDefaultBypass, true);

  // 保存是否使用默认绕过规则
  Future<void> setUseDefaultBypass(bool useDefault) =>
      _setBool(_kUseDefaultBypass, useDefault);

  // 获取当前应该使用的绕过规则（根据设置）
  String getCurrentBypassRules() {
    if (getUseDefaultBypass()) {
      return SystemProxy.getDefaultBypassRules();
    }
    return getSystemProxyBypass() ?? SystemProxy.getDefaultBypassRules();
  }

  // ==================== 系统代理 PAC 模式配置 ====================

  // 获取是否启用 PAC 模式（默认 false）
  bool getSystemProxyPacMode() => _getBool(_kSystemProxyPacMode, false);

  // 保存是否启用 PAC 模式
  Future<void> setSystemProxyPacMode(bool enabled) =>
      _setBool(_kSystemProxyPacMode, enabled);

  // 获取 PAC 脚本内容
  String getSystemProxyPacScript() =>
      _getString(_kSystemProxyPacScript, getDefaultPacScript());

  // 保存 PAC 脚本内容
  Future<void> setSystemProxyPacScript(String script) =>
      _setString(_kSystemProxyPacScript, script);

  // 获取默认 PAC 脚本（公开方法）
  String getDefaultPacScript() {
    final proxyHost = getProxyHost();
    final proxyPort = getMixedPort();
    return '''function FindProxyForURL(url, host) {
    // 本地地址直连
    if (isPlainHostName(host) ||
        shExpMatch(host, "*.local") ||
        isInNet(dnsResolve(host), "10.0.0.0", "255.0.0.0") ||
        isInNet(dnsResolve(host), "172.16.0.0", "255.240.0.0") ||
        isInNet(dnsResolve(host), "192.168.0.0", "255.255.0.0") ||
        isInNet(dnsResolve(host), "127.0.0.0", "255.0.0.0"))
        return "DIRECT";
    
    // 其他地址使用代理
    return "PROXY $proxyHost:$proxyPort";
}''';
  }

  // ==================== 调试和重置 ====================

  // 获取所有存储的配置 (调试用)
  Map<String, dynamic> getAllSettings() {
    _ensureInit();
    final keys = [
      _kAllowLan,
      _kIpv6,
      _kTcpConcurrent,
      _kGeodataLoader,
      _kFindProcessMode,
      _kCoreLogLevel,
      _kTestUrl,
      _kUnifiedDelayEnabled,
      _kMixedPort,
      _kSocksPort,
      _kHttpPort,
      _kExternalControllerEnabled,
      _kExternalControllerAddress,
      _kExternalControllerSecret,
      _kKeepAliveEnabled,
      _kKeepAliveInterval,
      _kTunEnable,
      _kTunStack,
      _kTunDevice,
      _kTunAutoRoute,
      _kTunAutoRedirect,
      _kTunAutoDetectInterface,
      _kTunDnsHijack,
      _kTunStrictRoute,
      _kTunRouteExcludeAddress,
      _kTunDisableIcmpForwarding,
      _kTunMtu,
      _kDnsOverrideEnabled,
      _kOutboundMode,
      _kProxyNodeSortMode,
      _kLazyMode,
    ];

    final Map<String, dynamic> settings = {};
    for (final key in keys) {
      if (_prefs!.containsKey(key)) {
        settings[key] = _prefs!.get(key);
      }
    }
    return settings;
  }

  // 重置所有 Clash 配置到默认值
  Future<void> resetToDefaults() async {
    _ensureInit();
    final keys = [
      _kAllowLan,
      _kIpv6,
      _kTcpConcurrent,
      _kGeodataLoader,
      _kFindProcessMode,
      _kCoreLogLevel,
      _kTestUrl,
      _kUnifiedDelayEnabled,
      _kMixedPort,
      _kSocksPort,
      _kHttpPort,
      _kExternalControllerEnabled,
      _kExternalControllerAddress,
      _kExternalControllerSecret,
      _kKeepAliveEnabled,
      _kKeepAliveInterval,
      _kTunEnable,
      _kTunStack,
      _kTunDevice,
      _kTunAutoRoute,
      _kTunAutoRedirect,
      _kTunAutoDetectInterface,
      _kTunDnsHijack,
      _kTunStrictRoute,
      _kTunRouteExcludeAddress,
      _kTunDisableIcmpForwarding,
      _kTunMtu,
      _kDnsOverrideEnabled,
      _kOutboundMode,
      _kProxyNodeSortMode,
      _kProxyHost,
      _kSystemProxyBypass,
      _kUseDefaultBypass,
      _kSystemProxyPacMode,
      _kSystemProxyPacScript,
      _kCurrentSubscriptionId,
      _kLazyMode,
    ];

    for (final key in keys) {
      await _prefs!.remove(key);
    }
  }

  // ==================== 订阅配置 ====================

  // 获取当前选中的订阅ID
  String? getCurrentSubscriptionId() =>
      _getStringNullable(_kCurrentSubscriptionId);

  // 设置当前选中的订阅ID
  Future<void> setCurrentSubscriptionId(String? subscriptionId) =>
      _setStringNullable(_kCurrentSubscriptionId, subscriptionId);

  // ==================== 出站模式 ====================

  // 获取出站模式
  String getOutboundMode() => _getString(_kOutboundMode, 'rule');

  // 设置出站模式
  Future<void> setOutboundMode(String mode) => _setString(_kOutboundMode, mode);

  // ==================== 节点选择持久化 ====================

  // 生成节点选择存储键
  // [subscriptionId] 订阅 ID
  // [groupName] 代理组名称
  String _getProxySelectionKey(String subscriptionId, String groupName) {
    return '$_kProxySelectionPrefix${subscriptionId}_$groupName';
  }

  // 保存节点选择
  // [subscriptionId] 订阅 ID
  // [groupName] 代理组名称
  // [proxyName] 选中的节点名称
  Future<void> saveProxySelection(
    String subscriptionId,
    String groupName,
    String proxyName,
  ) async {
    _ensureInit();
    final key = _getProxySelectionKey(subscriptionId, groupName);
    await _prefs!.setString(key, proxyName);
  }

  // 获取节点选择
  // [subscriptionId] 订阅 ID
  // [groupName] 代理组名称
  // 返回选中的节点名称，如果没有保存则返回 null
  String? getProxySelection(String subscriptionId, String groupName) {
    _ensureInit();
    final key = _getProxySelectionKey(subscriptionId, groupName);
    return _prefs!.getString(key);
  }

  // 删除特定订阅的所有节点选择
  // [subscriptionId] 订阅 ID
  Future<void> clearProxySelectionsForSubscription(
    String subscriptionId,
  ) async {
    _ensureInit();
    final keys = _prefs!.getKeys();
    final keysToRemove = keys
        .where(
          (String key) =>
              key.startsWith('$_kProxySelectionPrefix${subscriptionId}_'),
        )
        .toList();

    for (final key in keysToRemove) {
      await _prefs!.remove(key);
    }
  }

  // 清空所有节点选择
  Future<void> clearAllProxySelections() async {
    _ensureInit();
    final keys = _prefs!.getKeys();
    final keysToRemove = keys
        .where((String key) => key.startsWith(_kProxySelectionPrefix))
        .toList();

    for (final key in keysToRemove) {
      await _prefs!.remove(key);
    }
  }

  // ==================== 全局默认 User-Agent ====================

  // 获取全局默认 User-Agent（用于覆写下载和新订阅的初始值）
  String getDefaultUserAgent() => ClashDefaults.defaultUserAgent;

  // ==================== 代理节点排序配置 ====================

  // 获取代理节点排序模式
  // 0: 默认（不排序）
  // 1: A-Z（按名称字母排序）
  // 2: 延迟（按延迟从低到高排序）
  int getProxyNodeSortMode() => _getInt(_kProxyNodeSortMode, 0);

  // 保存代理节点排序模式
  Future<void> setProxyNodeSortMode(int mode) =>
      _setInt(_kProxyNodeSortMode, mode);

  // ==================== 懒惰模式 ====================

  // 获取懒惰模式是否启用（默认 false）
  // 懒惰模式：启动应用后，待内核加载完毕自动开启系统代理
  bool getLazyMode() => _getBool(_kLazyMode, false);

  // 保存懒惰模式启用状态
  Future<void> setLazyMode(bool enabled) => _setBool(_kLazyMode, enabled);

  // ==================== 通用存储方法 ====================
  // 用于备份还原等需要批量操作配置的场景

  // 获取字符串值
  String? getString(String key) {
    _ensureInit();
    return _prefs!.getString(key);
  }

  // 保存字符串值
  Future<void> setString(String key, String value) async {
    _ensureInit();
    await _prefs!.setString(key, value);
  }

  // 获取整数值
  int? getInt(String key) {
    _ensureInit();
    return _prefs!.getInt(key);
  }

  // 保存整数值
  Future<void> setInt(String key, int value) async {
    _ensureInit();
    await _prefs!.setInt(key, value);
  }

  // 获取双精度浮点数值
  double? getDouble(String key) {
    _ensureInit();
    return _prefs!.getDouble(key);
  }

  // 保存双精度浮点数值
  Future<void> setDouble(String key, double value) async {
    _ensureInit();
    await _prefs!.setDouble(key, value);
  }

  // 获取布尔值
  bool? getBool(String key) {
    _ensureInit();
    return _prefs!.getBool(key);
  }

  // 保存布尔值
  Future<void> setBool(String key, bool value) async {
    _ensureInit();
    await _prefs!.setBool(key, value);
  }

  // 删除指定键
  Future<void> remove(String key) async {
    _ensureInit();
    await _prefs!.remove(key);
  }

  // 检查键是否存在
  bool containsKey(String key) {
    _ensureInit();
    return _prefs!.containsKey(key);
  }
}

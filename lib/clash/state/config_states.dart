import 'package:stelliberty/clash/config/clash_defaults.dart';
import 'package:stelliberty/storage/clash_preferences.dart';

// Clash 配置状态
// 包含所有可配置的 Clash 参数
class ConfigState {
  // 通用配置
  final bool isAllowLanEnabled;
  final bool isIpv6Enabled;
  final bool isTcpConcurrentEnabled;
  final bool isUnifiedDelayEnabled;
  final String geodataLoader;
  final String findProcessMode;
  final String clashCoreLogLevel;
  final String externalController;
  final String testUrl;
  final String outboundMode;

  // 虚拟网卡模式配置
  final bool isTunEnabled;
  final String tunStack;
  final String tunDevice;
  final bool isTunAutoRouteEnabled;
  final bool isTunAutoRedirectEnabled;
  final bool isTunAutoDetectInterfaceEnabled;
  final List<String> tunDnsHijack;
  final bool isTunStrictRouteEnabled;
  final List<String> tunRouteExcludeAddress;
  final bool isTunIcmpForwardingDisabled;
  final int tunMtu;

  // 端口配置
  final int mixedPort;
  final int? socksPort;
  final int? httpPort;

  const ConfigState({
    this.isAllowLanEnabled = false,
    this.isIpv6Enabled = false,
    this.isTcpConcurrentEnabled = false,
    this.isUnifiedDelayEnabled = false,
    this.geodataLoader = ClashDefaults.defaultGeodataLoader,
    this.findProcessMode = ClashDefaults.defaultFindProcessMode,
    this.clashCoreLogLevel = ClashDefaults.defaultLogLevel,
    this.externalController = '',
    this.testUrl = ClashDefaults.defaultTestUrl,
    this.outboundMode = ClashDefaults.defaultOutboundMode,
    this.isTunEnabled = false,
    this.tunStack = ClashDefaults.defaultTunStack,
    this.tunDevice = ClashDefaults.defaultTunDevice,
    this.isTunAutoRouteEnabled = false,
    this.isTunAutoRedirectEnabled = false,
    this.isTunAutoDetectInterfaceEnabled = false,
    this.tunDnsHijack = const [],
    this.isTunStrictRouteEnabled = false,
    this.tunRouteExcludeAddress = const [],
    this.isTunIcmpForwardingDisabled = false,
    this.tunMtu = ClashDefaults.defaultTunMtu,
    this.mixedPort = ClashDefaults.mixedPort,
    this.socksPort,
    this.httpPort,
  });

  // 从持久化存储构建配置状态（状态构建逻辑在 state 层）
  static ConfigState fromPreferences(ClashPreferences prefs) {
    return ConfigState(
      isAllowLanEnabled: prefs.getAllowLan(),
      isIpv6Enabled: prefs.getIpv6(),
      isTcpConcurrentEnabled: prefs.getTcpConcurrent(),
      isUnifiedDelayEnabled: prefs.getUnifiedDelayEnabled(),
      geodataLoader: prefs.getGeodataLoader(),
      findProcessMode: prefs.getFindProcessMode(),
      clashCoreLogLevel: prefs.getCoreLogLevel(),
      externalController: prefs.getExternalControllerEnabled()
          ? prefs.getExternalControllerAddress()
          : '',
      testUrl: prefs.getTestUrl(),
      outboundMode: prefs.getOutboundMode(),
      isTunEnabled: prefs.getTunEnable(),
      tunStack: prefs.getTunStack(),
      tunDevice: prefs.getTunDevice(),
      isTunAutoRouteEnabled: prefs.getTunAutoRoute(),
      isTunAutoRedirectEnabled: prefs.getTunAutoRedirect(),
      isTunAutoDetectInterfaceEnabled: prefs.getTunAutoDetectInterface(),
      tunDnsHijack: prefs.getTunDnsHijack(),
      isTunStrictRouteEnabled: prefs.getTunStrictRoute(),
      tunRouteExcludeAddress: prefs.getTunRouteExcludeAddress(),
      isTunIcmpForwardingDisabled: prefs.getTunDisableIcmpForwarding(),
      tunMtu: prefs.getTunMtu(),
      mixedPort: prefs.getMixedPort(),
      socksPort: prefs.getSocksPort(),
      httpPort: prefs.getHttpPort(),
    );
  }

  ConfigState copyWith({
    bool? isAllowLanEnabled,
    bool? isIpv6Enabled,
    bool? isTcpConcurrentEnabled,
    bool? isUnifiedDelayEnabled,
    String? geodataLoader,
    String? findProcessMode,
    String? clashCoreLogLevel,
    String? externalController,
    String? testUrl,
    String? outboundMode,
    bool? isTunEnabled,
    String? tunStack,
    String? tunDevice,
    bool? isTunAutoRouteEnabled,
    bool? isTunAutoRedirectEnabled,
    bool? isTunAutoDetectInterfaceEnabled,
    List<String>? tunDnsHijack,
    bool? isTunStrictRouteEnabled,
    List<String>? tunRouteExcludeAddress,
    bool? isTunIcmpForwardingDisabled,
    int? tunMtu,
    int? mixedPort,
    int? socksPort,
    int? httpPort,
  }) {
    return ConfigState(
      isAllowLanEnabled: isAllowLanEnabled ?? this.isAllowLanEnabled,
      isIpv6Enabled: isIpv6Enabled ?? this.isIpv6Enabled,
      isTcpConcurrentEnabled:
          isTcpConcurrentEnabled ?? this.isTcpConcurrentEnabled,
      isUnifiedDelayEnabled:
          isUnifiedDelayEnabled ?? this.isUnifiedDelayEnabled,
      geodataLoader: geodataLoader ?? this.geodataLoader,
      findProcessMode: findProcessMode ?? this.findProcessMode,
      clashCoreLogLevel: clashCoreLogLevel ?? this.clashCoreLogLevel,
      externalController: externalController ?? this.externalController,
      testUrl: testUrl ?? this.testUrl,
      outboundMode: outboundMode ?? this.outboundMode,
      isTunEnabled: isTunEnabled ?? this.isTunEnabled,
      tunStack: tunStack ?? this.tunStack,
      tunDevice: tunDevice ?? this.tunDevice,
      isTunAutoRouteEnabled:
          isTunAutoRouteEnabled ?? this.isTunAutoRouteEnabled,
      isTunAutoRedirectEnabled:
          isTunAutoRedirectEnabled ?? this.isTunAutoRedirectEnabled,
      isTunAutoDetectInterfaceEnabled:
          isTunAutoDetectInterfaceEnabled ??
          this.isTunAutoDetectInterfaceEnabled,
      tunDnsHijack: tunDnsHijack ?? this.tunDnsHijack,
      isTunStrictRouteEnabled:
          isTunStrictRouteEnabled ?? this.isTunStrictRouteEnabled,
      tunRouteExcludeAddress:
          tunRouteExcludeAddress ?? this.tunRouteExcludeAddress,
      isTunIcmpForwardingDisabled:
          isTunIcmpForwardingDisabled ?? this.isTunIcmpForwardingDisabled,
      tunMtu: tunMtu ?? this.tunMtu,
      mixedPort: mixedPort ?? this.mixedPort,
      socksPort: socksPort ?? this.socksPort,
      httpPort: httpPort ?? this.httpPort,
    );
  }

  // 便捷 getter
  bool get isExternalControllerEnabled => externalController.isNotEmpty;
}

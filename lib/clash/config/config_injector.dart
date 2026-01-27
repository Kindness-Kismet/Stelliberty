import 'dart:io';
import 'package:path/path.dart' as path;
import 'package:stelliberty/storage/clash_preferences.dart';
import 'package:stelliberty/clash/services/dns_service.dart';
import 'package:stelliberty/clash/services/geo_service.dart';
import 'package:stelliberty/services/log_print_service.dart';
import 'package:stelliberty/src/bindings/signals/signals.dart';

// Clash 配置注入器
// 生成运行时配置文件（runtime_config.yaml），不修改订阅源文件
class ConfigInjector {
  // 默认配置内容
  static String getDefaultConfigContent() {
    return 'proxies: []\nproxy-groups: []\nrules: []';
  }

  // 注入运行时参数，生成 runtime_config.yaml
  static Future<String?> injectCustomConfigParams({
    String? configPath,
    String? configContent,
    List<OverrideConfig> overrides = const [],
    required int mixedPort,
    required int? socksPort,
    required int? httpPort,
    required bool isIpv6Enabled,
    required bool isTunEnabled,
    required String tunStack,
    required String tunDevice,
    required bool isTunAutoRouteEnabled,
    required bool isTunAutoRedirectEnabled,
    required bool isTunAutoDetectInterfaceEnabled,
    required List<String> tunDnsHijacks,
    required bool isTunStrictRouteEnabled,
    required List<String> tunRouteExcludeAddresses,
    required bool isTunIcmpForwardingDisabled,
    required int tunMtu,
    required bool isAllowLanEnabled,
    required bool isTcpConcurrentEnabled,
    required String geodataLoader,
    required String findProcessMode,
    required String clashCoreLogLevel,
    required String externalController,
    String? externalControllerSecret,
    required bool isUnifiedDelayEnabled,
    required String outboundMode,
  }) async {
    try {
      // 1. 获取配置内容（优先级：configContent > configPath > 默认）
      String content;

      if (configContent != null && configContent.isNotEmpty) {
        content = configContent;
      } else if (configPath != null && configPath.isNotEmpty) {
        final configFile = File(configPath);
        if (!await configFile.exists()) {
          Logger.warning('配置文件不存在：$configPath');
          content = getDefaultConfigContent();
        } else {
          try {
            content = await configFile.readAsString();
          } catch (e) {
            Logger.error('读取配置失败：$e');
            content = getDefaultConfigContent();
          }
        }
      } else {
        Logger.info('使用默认配置启动核心');
        content = getDefaultConfigContent();
      }

      // 2. 构建运行时参数
      final isKeepAliveEnabled = ClashPreferences.instance
          .getKeepAliveEnabled();
      final keepAliveInterval = isKeepAliveEnabled
          ? ClashPreferences.instance.getKeepAliveInterval()
          : null;

      // 读取 DNS 覆写
      final isDnsOverrideEnabled = ClashPreferences.instance
          .getDnsOverrideEnabled();
      String? dnsOverrideContent;
      if (isDnsOverrideEnabled && DnsService.instance.configExists()) {
        try {
          final dnsConfigPath = DnsService.instance.getConfigPath();
          dnsOverrideContent = await File(dnsConfigPath).readAsString();
        } catch (e) {
          Logger.error('读取 DNS 覆写失败：$e');
        }
      }

      final params = RuntimeConfigParams(
        mixedPort: mixedPort,
        socksPort: socksPort ?? 0,
        httpPort: httpPort ?? 0,
        isIpv6Enabled: isIpv6Enabled,
        isAllowLanEnabled: isAllowLanEnabled,
        isTcpConcurrentEnabled: isTcpConcurrentEnabled,
        isUnifiedDelayEnabled: isUnifiedDelayEnabled,
        outboundMode: outboundMode,
        isTunEnabled: isTunEnabled,
        tunStack: tunStack,
        tunDevice: tunDevice,
        isTunAutoRouteEnabled: isTunAutoRouteEnabled,
        isTunAutoRedirectEnabled: isTunAutoRedirectEnabled,
        isTunAutoDetectInterfaceEnabled: true, // 固定为 true
        tunDnsHijacks: tunDnsHijacks,
        isTunStrictRouteEnabled: isTunStrictRouteEnabled,
        tunRouteExcludeAddresses: tunRouteExcludeAddresses,
        isTunIcmpForwardingDisabled: isTunIcmpForwardingDisabled,
        tunMtu: tunMtu,
        geodataLoader: geodataLoader,
        findProcessMode: findProcessMode,
        clashCoreLogLevel: clashCoreLogLevel,
        externalController: externalController,
        externalControllerSecret: externalControllerSecret,
        isKeepAliveEnabled: isKeepAliveEnabled,
        keepAliveInterval: keepAliveInterval,
        isDnsOverrideEnabled: isDnsOverrideEnabled,
        dnsOverrideContent: dnsOverrideContent,
      );

      // 3. 调用 Rust 处理
      final request = GenerateRuntimeConfigRequest(
        baseConfigContent: content,
        overrides: overrides,
        runtimeParams: params,
      );

      request.sendSignalToRust();

      final response = await GenerateRuntimeConfigResponse
          .rustSignalStream
          .first
          .timeout(
            const Duration(seconds: 5),
            onTimeout: () {
              throw Exception('Rust 配置生成超时');
            },
          );

      if (!response.message.isSuccessful) {
        Logger.error('配置生成失败：${response.message.errorMessage}');
        return null;
      }

      // 4. 写入 runtime_config.yaml
      final geoDataDir = await GeoService.getGeoDataDir();
      final runtimeConfigPath = path.join(geoDataDir, 'runtime_config.yaml');
      await File(
        runtimeConfigPath,
      ).writeAsString(response.message.resultConfig);

      final sizeKb = (response.message.resultConfig.length / 1024)
          .toStringAsFixed(1);
      Logger.info('运行时配置已生成（${sizeKb}KB）');

      return runtimeConfigPath;
    } catch (e) {
      Logger.error('生成运行时配置失败：$e');
      return null;
    }
  }
}

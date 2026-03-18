import 'dart:io';
import 'package:stelliberty/storage/clash_preferences.dart';
import 'package:stelliberty/clash/services/dns_service.dart';
import 'package:stelliberty/services/log_print_service.dart';
import 'package:stelliberty/services/path_service.dart';
import 'package:stelliberty/src/bindings/signals/signals.dart';

// 运行时配置生成结果
class GeneratedRuntimeConfig {
  final String runtimeConfigPath;
  final String configContent;

  const GeneratedRuntimeConfig({
    required this.runtimeConfigPath,
    required this.configContent,
  });
}

// Clash 配置注入器
// 生成运行时配置文件（runtime_config.yaml），不修改订阅源文件
class ConfigInjector {
  // 默认配置内容
  static String getDefaultConfigContent() {
    return 'proxies: []\nproxy-groups: []\nrules: []';
  }

  // 读取基础配置内容（优先级：configContent > configPath > 默认）。
  static Future<String> _resolveBaseConfigContent({
    required String? configPath,
    required String? configContent,
  }) async {
    if (configContent != null && configContent.isNotEmpty) {
      return configContent;
    }

    if (configPath == null || configPath.isEmpty) {
      Logger.info('使用默认配置启动核心');
      return getDefaultConfigContent();
    }

    final configFile = File(configPath);
    if (!await configFile.exists()) {
      Logger.warning('配置文件不存在：$configPath');
      return getDefaultConfigContent();
    }

    try {
      return await configFile.readAsString();
    } catch (e) {
      Logger.error('读取配置失败：$e');
      return getDefaultConfigContent();
    }
  }

  // 注入运行时参数，生成 runtime_config.yaml
  static Future<GeneratedRuntimeConfig?> generateRuntimeConfig({
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
    List<String> lanAuthentication = const [],
  }) async {
    try {
      // 1. 读取基础配置内容
      final content = await _resolveBaseConfigContent(
        configPath: configPath,
        configContent: configContent,
      );

      // 2. 构建运行时参数
      final prefs = ClashPreferences.instance;
      final isKeepAliveEnabled = prefs.getKeepAliveEnabled();
      final keepAliveInterval = isKeepAliveEnabled
          ? prefs.getKeepAliveInterval()
          : null;

      // 读取 DNS 覆写
      final isDnsOverrideEnabled = prefs.getDnsOverrideEnabled();
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
        isTunAutoDetectInterfaceEnabled: isTunAutoDetectInterfaceEnabled,
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
        lanAuthentication: lanAuthentication,
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
      final resultConfig = response.message.resultConfig;
      final runtimeConfigPath = PathService.instance.getRuntimeConfigPath();
      await File(runtimeConfigPath).writeAsString(resultConfig);

      final sizeKb = (resultConfig.length / 1024).toStringAsFixed(1);
      Logger.info('运行时配置已生成（${sizeKb}KB）');

      return GeneratedRuntimeConfig(
        runtimeConfigPath: runtimeConfigPath,
        configContent: resultConfig,
      );
    } catch (e) {
      Logger.error('生成运行时配置失败：$e');
      return null;
    }
  }
}

import 'dart:io';
import 'package:path/path.dart' as path;
import 'package:stelliberty/clash/storage/preferences.dart';
import 'package:stelliberty/clash/services/geo_service.dart';
import 'package:stelliberty/utils/logger.dart';
import 'package:stelliberty/src/bindings/signals/signals.dart';

// Clash 配置文件注入器，负责将用户配置参数注入到 Clash 配置文件中
//
// 设计原则：
// - 订阅文件（subscriptions/*.yaml）永不修改
// - 生成临时运行时配置文件（runtime_config.yaml）
// - Clash 加载临时配置文件
class ConfigInjector {
  // 获取默认配置内容
  // 包含 Clash 核心必需的基础字段（Rust 端不会注入这些）
  static String getDefaultConfigContent() {
    return 'proxies: []\nproxy-groups: []\nrules: []';
  }

  // 注入用户自定义配置参数到配置文件
  //
  // 新架构：所有 YAML 处理在 Rust 端完成，避免 Dart 重复解析
  // - 从配置文件或配置内容读取基础配置
  // - 调用 Rust 端统一生成运行时配置（覆写 + 参数注入）
  // - 写入 runtime_config.yaml
  // - 返回 runtime_config.yaml 路径
  //
  // 参数：
  // - configPath: 配置文件路径（可选）
  // - configContent: 配置内容（可选，优先使用）
  // - overrides: 覆写列表
  //
  // 返回值：runtime_config.yaml 的绝对路径
  static Future<String?> injectCustomConfigParams({
    String? configPath,
    String? configContent,
    List<OverrideConfig> overrides = const [],
    required int httpPort,
    required bool ipv6,
    required bool tunEnable,
    required String tunStack,
    required String tunDevice,
    required bool tunAutoRoute,
    required bool tunAutoRedirect,
    required bool tunAutoDetectInterface,
    required List<String> tunDnsHijack,
    required bool tunStrictRoute,
    required List<String> tunRouteExcludeAddress,
    required bool tunDisableIcmpForwarding,
    required int tunMtu,
    required bool allowLan,
    required bool tcpConcurrent,
    required String geodataLoader,
    required String findProcessMode,
    required String clashCoreLogLevel,
    required String externalController,
    String? externalControllerSecret,
    required bool unifiedDelay,
    required String outboundMode,
  }) async {
    try {
      // 1. 获取配置内容（优先级：configContent > configPath > 默认配置）
      String content;
      if (configContent != null && configContent.isNotEmpty) {
        content = configContent;
      } else if (configPath != null && configPath.isNotEmpty) {
        final configFile = File(configPath);
        if (!await configFile.exists()) {
          Logger.warning('订阅配置文件不存在：$configPath');
          return null;
        }

        try {
          content = await configFile.readAsString();
        } catch (e) {
          Logger.error('读取配置文件失败：$configPath - $e');
          return null;
        }
      } else {
        // 使用默认配置
        Logger.info('未提供配置路径，使用默认配置启动核心');
        content = getDefaultConfigContent();
      }

      // 2. 构建运行时参数
      final keepAliveEnabled = ClashPreferences.instance.getKeepAliveEnabled();
      final keepAliveInterval = keepAliveEnabled
          ? ClashPreferences.instance.getKeepAliveInterval()
          : null;

      final params = RuntimeConfigParams(
        httpPort: httpPort,
        ipv6: ipv6,
        allowLan: allowLan,
        tcpConcurrent: tcpConcurrent,
        unifiedDelay: unifiedDelay,
        outboundMode: outboundMode,
        tunEnable: tunEnable,
        tunStack: tunStack,
        tunDevice: tunDevice,
        tunAutoRoute: tunAutoRoute,
        tunAutoRedirect: tunAutoRedirect,
        tunAutoDetectInterface: tunAutoDetectInterface,
        tunDnsHijack: tunDnsHijack,
        tunStrictRoute: tunStrictRoute,
        tunRouteExcludeAddress: tunRouteExcludeAddress,
        tunDisableIcmpForwarding: tunDisableIcmpForwarding,
        tunMtu: tunMtu,
        geodataLoader: geodataLoader,
        findProcessMode: findProcessMode,
        clashCoreLogLevel: clashCoreLogLevel,
        externalController: externalController,
        externalControllerSecret: externalControllerSecret,
        keepAliveEnabled: keepAliveEnabled,
        keepAliveInterval: keepAliveInterval,
      );

      // 3. 调用 Rust 统一处理（覆写 + 参数注入 + YAML 序列化）
      final request = GenerateRuntimeConfigRequest(
        baseConfigContent: content,
        overrides: overrides,
        runtimeParams: params,
      );

      request.sendSignalToRust();

      // 【性能优化】降低超时时间到 5 秒（正常情况下 Rust 处理很快）
      final response = await GenerateRuntimeConfigResponse
          .rustSignalStream
          .first
          .timeout(
            const Duration(seconds: 5),
            onTimeout: () {
              throw Exception('Rust 配置生成超时（5秒）');
            },
          );

      if (!response.message.success) {
        Logger.error('Rust 配置生成失败：${response.message.errorMessage}');
        return null;
      }

      // 4. 写入 runtime_config.yaml
      final geoDataDir = await GeoService.getGeoDataDir();
      final runtimeConfigPath = path.join(geoDataDir, 'runtime_config.yaml');
      await File(
        runtimeConfigPath,
      ).writeAsString(response.message.resultConfig);

      Logger.info(
        '运行时配置已生成 (${(response.message.resultConfig.length / 1024).toStringAsFixed(1)}KB，虚拟网卡：${tunEnable ? "启用" : "禁用"})',
      );

      return runtimeConfigPath;
    } catch (e) {
      Logger.error('生成运行时配置失败：$e');
      return null;
    }
  }
}

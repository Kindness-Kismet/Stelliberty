import 'package:stelliberty/clash/model/override_model.dart' as data;
import 'package:stelliberty/clash/services/override_service.dart';
import 'package:stelliberty/services/log_print_service.dart';
import 'package:stelliberty/src/bindings/signals/signals.dart';

// 覆写应用器 - 负责将覆写应用到订阅配置
//
// 现已迁移到 Rust 实现，提供更好的性能和稳定性
class OverrideApplicator {
  final OverrideService _service;

  OverrideApplicator(this._service);

  // 获取覆写服务
  OverrideService get service => _service;

  // 应用覆写列表到订阅配置
  // 返回应用覆写后的配置内容
  Future<String> applyOverrides(
    String baseConfigContent,
    List<data.OverrideConfig> overrides,
  ) async {
    Logger.debug('OverrideApplicator.applyOverrides');
    Logger.debug('基础配置长度：${baseConfigContent.length} 字符');
    Logger.debug('覆写数量：${overrides.length}');

    // 准备覆写配置列表（读取文件内容）
    final overrideConfigs = <OverrideConfig>[];
    for (var i = 0; i < overrides.length; i++) {
      final override = overrides[i];
      try {
        Logger.info(
          '[$i] 准备覆写: ${override.name} (${override.format.displayName})',
        );

        // 读取覆写文件内容
        final overrideContent = await _service.getOverrideContent(
          override.id,
          override.format,
        );

        Logger.debug('[$i] 覆写文件内容长度：${overrideContent.length} 字符');

        if (overrideContent.isEmpty) {
          Logger.warning('[$i] 覆写文件为空，跳过：${override.name}');
          continue;
        }

        // 转换为 Rinf 的 OverrideConfig
        overrideConfigs.add(
          OverrideConfig(
            id: override.id,
            name: override.name,
            format: _convertFormat(override.format),
            content: overrideContent,
          ),
        );
      } catch (e) {
        final errorMsg = '准备覆写失败：${override.name} - $e';
        Logger.error('[$i] $errorMsg');
        // 继续处理下一个覆写，不中断整个流程
      }
    }

    if (overrideConfigs.isEmpty) {
      Logger.info('没有有效的覆写配置，返回原始配置');
      return baseConfigContent;
    }

    // 调用 Rust 处理所有覆写
    Logger.info('调用 Rust 处理 ${overrideConfigs.length} 个覆写…');
    try {
      final request = ApplyOverridesRequest(
        baseConfigContent: baseConfigContent,
        overrides: overrideConfigs,
      );

      // 发送请求到 Rust
      request.sendSignalToRust();

      // 等待 Rust 响应
      final response = await ApplyOverridesResponse.rustSignalStream.first;
      final result = response.message;

      if (!result.isSuccessful) {
        Logger.error('Rust 覆写处理失败：${result.errorMessage}');
        throw Exception('Rust 覆写处理失败：${result.errorMessage}');
      }

      Logger.info('Rust 覆写处理成功');
      Logger.debug('最终配置长度：${result.resultConfig.length} 字符');

      return result.resultConfig;
    } catch (e) {
      final errorMsg = 'Rust 覆写处理异常：$e';
      Logger.error(errorMsg);

      throw Exception(errorMsg);
    }
  }

  // 转换 Dart OverrideFormat 到 Rinf OverrideFormat
  OverrideFormat _convertFormat(data.OverrideFormat format) {
    switch (format) {
      case data.OverrideFormat.yaml:
        return OverrideFormat.yaml;
      case data.OverrideFormat.js:
        return OverrideFormat.javascript;
    }
  }

  // 应用 YAML 覆写（从 Map）
  //
  // 用于 DNS 覆写等场景，将 Map 直接合并到配置中
  Future<String> applyYamlOverride(
    String baseContent,
    Map<String, dynamic> overrideMap,
  ) async {
    Logger.debug('applyYamlOverride (from Map)');
    Logger.debug('基础配置长度：${baseContent.length} 字符');
    Logger.debug('覆写 Map 键：${overrideMap.keys.toList()}');

    // 将 Map 转换为简单的 YAML 字符串
    final yamlContent = _mapToYaml(overrideMap);
    Logger.debug('生成的 YAML 长度：${yamlContent.length} 字符');

    // 创建临时覆写配置
    final tempOverride = OverrideConfig(
      id: 'temp_map_override',
      name: 'Map Override',
      format: OverrideFormat.yaml,
      content: yamlContent,
    );

    // 调用 Rust 处理
    try {
      final request = ApplyOverridesRequest(
        baseConfigContent: baseContent,
        overrides: [tempOverride],
      );

      // 发送请求到 Rust
      request.sendSignalToRust();

      // 等待 Rust 响应
      final response = await ApplyOverridesResponse.rustSignalStream.first;
      final result = response.message;

      if (!result.isSuccessful) {
        Logger.error('Rust YAML 覆写失败：${result.errorMessage}');
        throw Exception('Rust YAML 覆写失败：${result.errorMessage}');
      }

      Logger.info('Rust YAML 覆写成功');
      Logger.debug('最终配置长度：${result.resultConfig.length} 字符');

      return result.resultConfig;
    } catch (e) {
      final errorMsg = 'Rust YAML 覆写异常：$e';
      Logger.error(errorMsg);
      throw Exception(errorMsg);
    }
  }

  // 将 Map 转换为简单的 YAML 字符串
  String _mapToYaml(Map<String, dynamic> map) {
    final buffer = StringBuffer();
    for (final entry in map.entries) {
      final key = entry.key;
      final value = entry.value;
      buffer.writeln('$key: ${_formatYamlValue(value)}');
    }
    return buffer.toString();
  }

  // 格式化 YAML 值
  String _formatYamlValue(dynamic value) {
    if (value == null) return 'null';
    if (value is String) {
      // 简单处理字符串引号
      if (value.contains(':') || value.contains('#')) {
        return '"${value.replaceAll('"', '\\"')}"';
      }
      return value;
    }
    if (value is bool) return value.toString();
    if (value is num) return value.toString();
    if (value is List) {
      return '\n${value.map((item) => '  - ${_formatYamlValue(item)}').join('\n')}';
    }
    if (value is Map) {
      return '\n${(value as Map<String, dynamic>).entries.map((e) => '  ${e.key}: ${_formatYamlValue(e.value)}').join('\n')}';
    }
    return value.toString();
  }
}

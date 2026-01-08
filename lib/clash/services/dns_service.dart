import 'dart:io';
import 'package:yaml/yaml.dart';
import 'package:stelliberty/clash/model/dns_config_model.dart';
import 'package:stelliberty/services/path_service.dart';
import 'package:stelliberty/services/log_print_service.dart';

// DNS 配置服务
//
// 负责管理独立的 DNS 配置文件，不影响用户订阅配置
class DnsService {
  static final DnsService _instance = DnsService._internal();
  static DnsService get instance => _instance;

  DnsService._internal();

  // DNS 配置文件路径（从 PathService 获取）
  String get _dnsConfigPath => PathService.instance.dnsConfigPath;

  // 初始化服务
  Future<void> initialize(String baseDir) async {
    // 如果配置文件不存在，创建默认配置文件
    if (!File(_dnsConfigPath).existsSync()) {
      Logger.info('DNS 配置文件不存在，创建默认配置：$_dnsConfigPath');
      final defaultConfig = DnsConfig.defaultConfig();
      await saveDnsConfig(defaultConfig);
    }

    Logger.info('DNS 服务初始化完成，配置路径：$_dnsConfigPath');
  }

  // 检查 DNS 配置文件是否存在
  bool configExists() {
    return File(_dnsConfigPath).existsSync();
  }

  // 获取 DNS 配置文件路径
  String getConfigPath() {
    return _dnsConfigPath;
  }

  // 保存 DNS 配置
  Future<bool> saveDnsConfig(DnsConfig config) async {
    try {
      final configMap = config.toMap();
      final yamlContent = _mapToYaml(configMap);

      await File(_dnsConfigPath).writeAsString(yamlContent);
      Logger.info('DNS 配置已保存：$_dnsConfigPath');
      return true;
    } catch (e) {
      Logger.error('保存 DNS 配置失败：$e');
      return false;
    }
  }

  // 读取 DNS 配置
  Future<DnsConfig?> loadDnsConfig() async {
    if (!configExists()) {
      Logger.warning('DNS 配置文件不存在，返回默认配置');
      return DnsConfig.defaultConfig();
    }

    try {
      final yamlContent = await File(_dnsConfigPath).readAsString();
      final yamlDoc = loadYaml(yamlContent);
      final configMap = _yamlToMap(yamlDoc);

      final config = DnsConfig.fromMap(configMap);
      Logger.info('DNS 配置已加载');
      return config;
    } catch (e) {
      Logger.error('加载 DNS 配置失败：$e');
      return null;
    }
  }

  // 删除 DNS 配置文件
  Future<bool> deleteDnsConfig() async {
    try {
      if (configExists()) {
        await File(_dnsConfigPath).delete();
        Logger.info('DNS 配置文件已删除');
      }
      return true;
    } catch (e) {
      Logger.error('删除 DNS 配置失败：$e');
      return false;
    }
  }

  // 将 DNS 配置合并到订阅配置中
  //
  // 这个函数会读取订阅配置和 DNS 配置，然后合并生成运行时配置
  // 不会修改原始订阅文件
  Future<Map<String, dynamic>?> mergeDnsConfigToProfile(
    String profilePath, {
    bool enableDns = true,
  }) async {
    try {
      // 读取订阅配置
      final profileYaml = await File(profilePath).readAsString();
      final profileDoc = loadYaml(profileYaml);
      final profileMap = _yamlToMap(profileDoc);

      // 如果不启用 DNS 覆写，直接返回订阅配置
      if (!enableDns || !configExists()) {
        Logger.info('DNS 覆写未启用或配置文件不存在，使用原始订阅配置');
        return profileMap;
      }

      // 读取 DNS 配置
      final dnsConfig = await loadDnsConfig();
      if (dnsConfig == null) {
        Logger.warning('无法加载 DNS 配置，使用原始订阅配置');
        return profileMap;
      }

      // 合并配置
      final mergedConfig = Map<String, dynamic>.from(profileMap);
      final dnsMap = dnsConfig.toMap();

      // 插入或覆盖 DNS 配置
      if (dnsMap.containsKey('dns')) {
        mergedConfig['dns'] = dnsMap['dns'];
        Logger.info('DNS 配置已合并');
      }

      // 插入或覆盖 Hosts 配置
      if (dnsMap.containsKey('hosts')) {
        mergedConfig['hosts'] = dnsMap['hosts'];
        Logger.info('Hosts 配置已合并');
      }

      return mergedConfig;
    } catch (e) {
      Logger.error('合并 DNS 配置失败：$e');
      return null;
    }
  }

  // 将 YAML 文档转换为 Map
  dynamic _yamlToMap(dynamic yaml) {
    if (yaml is YamlMap) {
      final map = <String, dynamic>{};
      for (final entry in yaml.entries) {
        map[entry.key.toString()] = _yamlToMap(entry.value);
      }
      return map;
    } else if (yaml is YamlList) {
      return yaml.map((item) => _yamlToMap(item)).toList();
    } else {
      return yaml;
    }
  }

  // 将 Map 转换为 YAML 字符串
  String _mapToYaml(Map<String, dynamic> map, {int indent = 0}) {
    final buffer = StringBuffer();
    final indentStr = '  ' * indent;

    for (final entry in map.entries) {
      final key = entry.key;
      final value = entry.value;

      if (value is Map) {
        buffer.writeln('$indentStr$key:');
        buffer.write(
          _mapToYaml(value as Map<String, dynamic>, indent: indent + 1),
        );
      } else if (value is List) {
        buffer.writeln('$indentStr$key:');
        for (final item in value) {
          if (item is Map) {
            buffer.writeln('$indentStr  -');
            buffer.write(
              _mapToYaml(item as Map<String, dynamic>, indent: indent + 2),
            );
          } else {
            // 列表项也需要处理特殊字符
            final itemStr = item.toString();
            if (itemStr.contains(':') ||
                itemStr.contains('#') ||
                itemStr.startsWith('*') ||
                itemStr.startsWith('+')) {
              buffer.writeln('$indentStr  - "$itemStr"');
            } else {
              buffer.writeln('$indentStr  - $itemStr');
            }
          }
        }
      } else {
        // 字符串需要加引号（如果包含特殊字符）
        final valueStr = value.toString();
        if (valueStr.contains(':') ||
            valueStr.contains('#') ||
            valueStr.startsWith('*') ||
            valueStr.startsWith('+')) {
          buffer.writeln('$indentStr$key: "$valueStr"');
        } else {
          buffer.writeln('$indentStr$key: $valueStr');
        }
      }
    }

    return buffer.toString();
  }
}

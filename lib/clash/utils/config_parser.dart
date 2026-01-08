import 'dart:io';
import 'package:yaml/yaml.dart';
import 'package:stelliberty/clash/model/clash_model.dart';
import 'package:stelliberty/services/log_print_service.dart';

// Clash 配置文件解析器
// 用于在 Clash 未启动时直接从配置文件读取代理信息
class ConfigParser {
  // 从文件系统加载配置
  static Future<Map<String, dynamic>> loadConfigFromFile(
    String filePath,
  ) async {
    try {
      final file = File(filePath);
      if (!await file.exists()) {
        throw Exception('配置文件不存在: $filePath');
      }

      final yamlString = await file.readAsString();
      final yamlMap = loadYaml(yamlString);
      return _convertYamlToMap(yamlMap);
    } catch (e) {
      Logger.error('从文件加载配置失败：$e');
      rethrow;
    }
  }

  // 从 YAML 字符串加载配置（用于内存中的配置）
  static Map<String, dynamic> loadConfigFromString(String yamlString) {
    try {
      final yamlMap = loadYaml(yamlString);
      return _convertYamlToMap(yamlMap);
    } catch (e) {
      Logger.error('从字符串解析配置失败：$e');
      rethrow;
    }
  }

  // 将 YamlMap 转换为 Map
  static dynamic _convertYamlToMap(dynamic yamlData) {
    if (yamlData is YamlMap) {
      final map = <String, dynamic>{};
      yamlData.forEach((key, value) {
        map[key.toString()] = _convertYamlToMap(value);
      });
      return map;
    } else if (yamlData is List) {
      return yamlData.map((item) => _convertYamlToMap(item)).toList();
    } else if (yamlData is Map) {
      final map = <String, dynamic>{};
      yamlData.forEach((key, value) {
        map[key.toString()] = _convertYamlToMap(value);
      });
      return map;
    } else {
      // 基本类型（String, int, bool 等）直接返回
      return yamlData;
    }
  }

  // 解析配置文件中的代理组和节点
  static ParsedConfig parseConfig(Map<String, dynamic> config) {
    // 先解析所有代理节点（只解析一次）
    final proxies = _parseProxies(config);

    // 解析代理组时传入已解析的代理节点
    final proxyGroups = _parseProxyGroups(config, proxies);

    // 将代理组也添加到 proxyNodes 中（用于支持嵌套代理组）
    for (final group in proxyGroups) {
      proxies[group.name] = ProxyNode(
        name: group.name,
        type: group.type,
        delay: null,
        server: null,
        port: null,
      );
    }

    Logger.info(
      '配置解析完成：${proxies.length - proxyGroups.length} 个代理节点，${proxyGroups.length} 个代理组',
    );

    return ParsedConfig(proxyNodes: proxies, proxyGroups: proxyGroups);
  }

  // 解析代理节点列表
  static Map<String, ProxyNode> _parseProxies(Map<String, dynamic> config) {
    final proxyNodes = <String, ProxyNode>{};

    // 解析 proxies 列表
    final proxiesData = config['proxies'];
    if (proxiesData is List) {
      for (final proxyData in proxiesData) {
        if (proxyData is Map) {
          final name = proxyData['name']?.toString();
          final type = proxyData['type']?.toString() ?? 'Unknown';
          final server = proxyData['server']?.toString();
          final port = proxyData['port'];

          if (name != null) {
            proxyNodes[name] = ProxyNode(
              name: name,
              type: type,
              server: server,
              port: port is int ? port : (int.tryParse(port.toString()) ?? 0),
              delay: null, // 配置文件中没有延迟信息
            );
          }
        }
      }
    }

    return proxyNodes;
  }

  // 解析代理组列表
  static List<ProxyGroup> _parseProxyGroups(
    Map<String, dynamic> config,
    Map<String, ProxyNode> allProxyNodes,
  ) {
    final proxyGroups = <ProxyGroup>[];

    // 解析 proxy-groups 列表
    final proxyGroupsData = config['proxy-groups'];
    if (proxyGroupsData is List) {
      for (final groupData in proxyGroupsData) {
        if (groupData is Map) {
          final name = groupData['name']?.toString();
          final type = groupData['type']?.toString() ?? 'select';
          final hidden = groupData['hidden'] == true;

          // 解析 proxies 列表
          List<String> proxies = [];
          final proxiesData = groupData['proxies'];
          if (proxiesData is List) {
            proxies = proxiesData
                .map((p) => p?.toString())
                .where((p) => p != null)
                .cast<String>()
                .toList();
          }

          // 解析 include-all 和 filter
          final includeAll = groupData['include-all'] == true;
          final filter = groupData['filter']?.toString();
          final excludeFilter = groupData['exclude-filter']?.toString();

          // 如果启用了 include-all，使用传入的代理节点列表
          if (includeAll) {
            var filteredProxies = allProxyNodes.keys.toList();

            // 应用过滤器
            if (filter != null && filter.isNotEmpty) {
              try {
                // 处理 (?i) 大小写不敏感标志
                bool caseSensitive = true;
                String pattern = filter;
                if (filter.startsWith('(?i)')) {
                  caseSensitive = false;
                  pattern = filter.substring(4); // 移除 (?i)
                }

                final regex = RegExp(pattern, caseSensitive: caseSensitive);
                filteredProxies = filteredProxies
                    .where((name) => regex.hasMatch(name))
                    .toList();
              } catch (e) {
                Logger.warning('过滤器正则表达式无效：$filter - $e');
              }
            }

            // 应用排除过滤器
            if (excludeFilter != null && excludeFilter.isNotEmpty) {
              try {
                // 处理 (?i) 大小写不敏感标志
                bool caseSensitive = true;
                String pattern = excludeFilter;
                if (excludeFilter.startsWith('(?i)')) {
                  caseSensitive = false;
                  pattern = excludeFilter.substring(4); // 移除 (?i)
                }

                final regex = RegExp(pattern, caseSensitive: caseSensitive);
                filteredProxies = filteredProxies
                    .where((name) => !regex.hasMatch(name))
                    .toList();
              } catch (e) {
                Logger.warning('排除过滤器正则表达式无效：$excludeFilter - $e');
              }
            }

            // 将过滤后的节点添加到代理组
            proxies.addAll(filteredProxies);
          }

          if (name != null) {
            proxyGroups.add(
              ProxyGroup(
                name: name,
                type: type,
                now: proxies.isNotEmpty ? proxies.first : null,
                all: proxies,
                isHidden: hidden,
              ),
            );
          }
        }
      }
    }

    return proxyGroups;
  }
}

// 解析后的配置数据
class ParsedConfig {
  final Map<String, ProxyNode> proxyNodes;
  final List<ProxyGroup> proxyGroups;

  ParsedConfig({required this.proxyNodes, required this.proxyGroups});
}

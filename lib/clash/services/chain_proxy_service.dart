import 'dart:convert';

import 'package:stelliberty/clash/model/subscription_model.dart';
import 'package:yaml/yaml.dart';

class ChainProxyRuntimeConfig {
  final String configContent;
  final List<String> builtinChainProxyNames;

  const ChainProxyRuntimeConfig({
    required this.configContent,
    required this.builtinChainProxyNames,
  });
}

class ChainProxyService {
  const ChainProxyService();

  ChainProxyRuntimeConfig analyzeAndApply(
    String rawConfig,
    Subscription subscription,
  ) {
    final yamlDoc = loadYaml(rawConfig);
    if (yamlDoc is! YamlMap) {
      return ChainProxyRuntimeConfig(
        configContent: rawConfig,
        builtinChainProxyNames: subscription.builtinChainProxyNames,
      );
    }

    final root = _toPlainMap(yamlDoc);
    final proxies = _extractProxyMaps(root);
    final proxyGroups = _extractProxyGroupMaps(root);
    final builtinChainProxyNames = _collectBuiltinChainProxyNames(proxies);
    final activeBuiltinNames = builtinChainProxyNames
        .where(
          (name) => !subscription.disabledBuiltinChainProxyNames.contains(name),
        )
        .toSet();

    final filteredProxies = proxies.where((proxy) {
      final name = proxy['name']?.toString();
      if (name == null || name.isEmpty) {
        return true;
      }
      if (activeBuiltinNames.contains(name)) {
        return true;
      }
      return !builtinChainProxyNames.contains(name);
    }).toList();

    final filteredProxyGroups = proxyGroups.where((group) {
      final name = group['name']?.toString();
      if (name == null || name.isEmpty) {
        return true;
      }
      return !subscription.customChainProxies.any(
        (customProxy) => customProxy.displayName == name,
      );
    }).toList();

    for (final customProxy in subscription.customChainProxies) {
      final generatedGroup = _buildRuntimeRelayGroup(
        proxies: proxies,
        customProxy: customProxy,
      );
      if (generatedGroup == null) {
        continue;
      }
      filteredProxyGroups.add(generatedGroup);
    }

    root['proxies'] = filteredProxies;
    root['proxy-groups'] = filteredProxyGroups;
    final nextConfig = _mapToYaml(root);
    return ChainProxyRuntimeConfig(
      configContent: nextConfig,
      builtinChainProxyNames: builtinChainProxyNames,
    );
  }

  static List<Map<String, dynamic>> _extractProxyMaps(
    Map<String, dynamic> root,
  ) {
    final proxies = root['proxies'];
    if (proxies is! List) {
      return const [];
    }

    return proxies.whereType<Map>().map((proxy) {
      return Map<String, dynamic>.from(proxy);
    }).toList();
  }

  static List<Map<String, dynamic>> _extractProxyGroupMaps(
    Map<String, dynamic> root,
  ) {
    final proxyGroups = root['proxy-groups'];
    if (proxyGroups is! List) {
      return const [];
    }

    return proxyGroups.whereType<Map>().map((group) {
      return Map<String, dynamic>.from(group);
    }).toList();
  }

  static Map<String, dynamic>? _buildRuntimeRelayGroup({
    required List<Map<String, dynamic>> proxies,
    required CustomChainProxy customProxy,
  }) {
    if (customProxy.nodeNames.length < 2) {
      return null;
    }

    for (final nodeName in customProxy.nodeNames) {
      if (_findProxyByName(proxies, nodeName) == null) {
        return null;
      }
    }

    return <String, dynamic>{
      'name': customProxy.displayName,
      'type': 'relay',
      'proxies': List<String>.from(customProxy.nodeNames),
    };
  }

  static Map<String, dynamic>? _findProxyByName(
    List<Map<String, dynamic>> proxies,
    String name,
  ) {
    for (final proxy in proxies) {
      if (proxy['name']?.toString() == name) {
        return proxy;
      }
    }
    return null;
  }

  static List<String> _collectBuiltinChainProxyNames(
    List<Map<String, dynamic>> proxies,
  ) {
    final names = <String>[];
    for (final proxy in proxies) {
      final dialerProxy = proxy['dialer-proxy'];
      final name = proxy['name'];
      if (dialerProxy is! String || dialerProxy.isEmpty) {
        continue;
      }
      if (name is! String || name.isEmpty) {
        continue;
      }
      names.add(name);
    }
    return names;
  }

  static Map<String, dynamic> _toPlainMap(YamlMap value) {
    final result = <String, dynamic>{};
    value.forEach((key, item) {
      result[key.toString()] = _toPlainValue(item);
    });
    return result;
  }

  static dynamic _toPlainValue(dynamic value) {
    if (value is YamlMap) {
      return _toPlainMap(value);
    }
    if (value is YamlList) {
      return value.map(_toPlainValue).toList();
    }
    return value;
  }

  static String _mapToYaml(Map<String, dynamic> value) {
    final buffer = StringBuffer();
    _writeMap(buffer, value, 0);
    return buffer.toString();
  }

  static void _writeMap(
    StringBuffer buffer,
    Map<String, dynamic> value,
    int indent, {
    bool isListItem = false,
  }) {
    final entries = value.entries.toList();
    for (var i = 0; i < entries.length; i++) {
      final entry = entries[i];
      final entryIndent = isListItem ? indent + 2 : indent;
      final leading = isListItem && i == 0
          ? '${' ' * indent}- ${entry.key}:'
          : '${' ' * entryIndent}${entry.key}:';
      _writeEntry(buffer, leading, entry.value, entryIndent + 2);
    }
  }

  static void _writeEntry(
    StringBuffer buffer,
    String leading,
    dynamic value,
    int childIndent,
  ) {
    if (_isScalar(value)) {
      buffer.writeln('$leading ${_scalarToYaml(value)}');
      return;
    }

    buffer.writeln(leading);
    _writeValue(buffer, value, childIndent);
  }

  static void _writeValue(StringBuffer buffer, dynamic value, int indent) {
    if (value is Map<String, dynamic>) {
      _writeMap(buffer, value, indent);
      return;
    }

    if (value is List) {
      for (final item in value) {
        if (_isScalar(item)) {
          buffer.writeln('${' ' * indent}- ${_scalarToYaml(item)}');
          continue;
        }
        if (item is Map<String, dynamic>) {
          _writeMap(buffer, item, indent, isListItem: true);
          continue;
        }
        if (item is Map) {
          _writeMap(
            buffer,
            Map<String, dynamic>.from(item),
            indent,
            isListItem: true,
          );
          continue;
        }
        if (item is List) {
          buffer.writeln('${' ' * indent}-');
          _writeValue(buffer, item, indent + 2);
          continue;
        }
        throw Exception('不支持的 YAML 列表项类型：${item.runtimeType}');
      }
      return;
    }

    throw Exception('不支持的 YAML 值类型：${value.runtimeType}');
  }

  static bool _isScalar(dynamic value) {
    return value == null || value is String || value is num || value is bool;
  }

  static String _scalarToYaml(dynamic value) {
    if (value == null) {
      return 'null';
    }
    if (value is String) {
      return jsonEncode(value);
    }
    return value.toString();
  }
}

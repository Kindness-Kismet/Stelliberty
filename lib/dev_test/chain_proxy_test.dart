import 'dart:io';

import 'package:path/path.dart' as path;
import 'package:stelliberty/clash/model/subscription_model.dart';
import 'package:stelliberty/clash/services/chain_proxy_service.dart';
import 'package:stelliberty/services/log_print_service.dart';
import 'package:yaml/yaml.dart';

class ChainProxyTest {
  static Future<void> run() async {
    try {
      Logger.info('链式代理结构测试启动');
      await _verifyFixture();
      await _verifyProjectTestConfig();
      Logger.info('链式代理结构测试完成');
      exit(0);
    } catch (e, stack) {
      Logger.error('链式代理结构测试失败: $e');
      Logger.error('堆栈: $stack');
      exit(1);
    }
  }

  static Future<void> _verifyFixture() async {
    const rawConfig = '''
proxies:
  - name: test-ws
    type: vless
    server: example.com
    port: 443
    uuid: 00000000-0000-0000-0000-000000000000
    network: ws
    ws-opts:
      path: /
      headers:
        Host: example.com
  - name: test-tcp
    type: vless
    server: tcp.example.com
    port: 443
    uuid: 11111111-1111-1111-1111-111111111111
    network: tcp
proxy-groups:
  - name: Auto
    type: select
    proxies:
      - test-ws
      - test-tcp
rules: []
''';

    final result = await const ChainProxyService().analyzeAndApply(
      rawConfig,
      const Subscription(
        id: 'chain-proxy-test',
        name: 'Chain Proxy Test',
        url: '',
        customChainProxies: [
          CustomChainProxy(
            id: 'relay-test',
            displayName: 'Relay Test',
            nodeNames: ['test-ws', 'test-tcp'],
          ),
        ],
      ),
    );

    final root = _loadRoot(result.configContent);
    final proxy = _findByName(root['proxies'] as YamlList, 'test-ws');
    _assertWsOptionsIntact(proxy);

    final group = _findByName(root['proxy-groups'] as YamlList, 'Relay Test');
    _assert(group['type'] == 'relay', '自定义链式代理组类型错误');
    Logger.info('最小夹具结构验证通过');
  }

  static Future<void> _verifyProjectTestConfig() async {
    final configFile = File(path.join('assets', 'test', 'config', 'test.yaml'));
    if (!await configFile.exists()) {
      throw Exception('测试配置不存在：${configFile.path}');
    }

    final rawConfig = await configFile.readAsString();
    final result = await const ChainProxyService().analyzeAndApply(
      rawConfig,
      const Subscription(
        id: 'project-chain-test',
        name: 'Project Test',
        url: '',
      ),
    );

    final root = _loadRoot(result.configContent);
    final proxies = root['proxies'];
    if (proxies is! YamlList) {
      throw Exception('测试配置缺少 proxies 列表');
    }

    for (final item in proxies) {
      if (item is! YamlMap || item['network'] != 'ws') {
        continue;
      }
      _assertWsOptionsIntact(item);
    }
    Logger.info('assets/test/config/test.yaml 结构验证通过');
  }

  static YamlMap _loadRoot(String content) {
    final yamlDoc = loadYaml(content);
    if (yamlDoc is! YamlMap) {
      throw Exception('输出配置不是有效 YAML Map');
    }
    return yamlDoc;
  }

  static YamlMap _findByName(YamlList items, String name) {
    for (final item in items) {
      if (item is YamlMap && item['name'] == name) {
        return item;
      }
    }
    throw Exception('未找到条目：$name');
  }

  static void _assertWsOptionsIntact(YamlMap proxy) {
    final wsOptions = proxy['ws-opts'];
    if (wsOptions != null && wsOptions is! YamlMap) {
      throw Exception('${proxy['name']} 的 ws-opts 不是 Map');
    }
    if (wsOptions is YamlMap) {
      _assert(wsOptions['path'] != null, '${proxy['name']} 的 ws-opts.path 缺失');
      final headers = wsOptions['headers'];
      if (headers != null) {
        _assert(
          headers is YamlMap,
          '${proxy['name']} 的 ws-opts.headers 不是 Map',
        );
      }
    }
    _assert(!proxy.containsKey('path'), '${proxy['name']} 的 path 被打平到代理同级');
    _assert(
      !proxy.containsKey('headers'),
      '${proxy['name']} 的 headers 被打平到代理同级',
    );
  }

  static void _assert(bool condition, String message) {
    if (!condition) {
      throw Exception(message);
    }
  }
}

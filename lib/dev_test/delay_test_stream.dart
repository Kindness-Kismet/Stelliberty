import 'dart:async';
import 'dart:io';
import 'package:path/path.dart' as path;
import 'package:stelliberty/clash/config/config_injector.dart';
import 'package:stelliberty/clash/model/clash_model.dart';
import 'package:stelliberty/clash/network/ipc_request_helper.dart';
import 'package:stelliberty/clash/services/delay_test_service.dart';
import 'package:stelliberty/clash/services/process_service.dart';
import 'package:stelliberty/services/log_print_service.dart';

// 延迟测试流：模拟点击测试所有节点。
class DelayTestStream {
  static Future<void> run() async {
    Logger.info('======================================');
    Logger.info('开始延迟测试流');
    Logger.info('======================================');

    var exitCode = 0;
    ProcessService? processService;

    try {
      final configFile = await _resolveTestConfig();
      Logger.info('使用测试配置：${configFile.path}');

      final runtimeConfigPath = await _buildRuntimeConfig(configFile.path);
      if (runtimeConfigPath == null) {
        throw Exception('运行时配置生成失败');
      }

      final executablePath = await _resolveClashExecutable();
      processService = ProcessService();
      await processService.startProcess(
        executablePath: executablePath,
        configPath: runtimeConfigPath,
        apiHost: '127.0.0.1',
        apiPort: 19090,
      );

      Logger.info('✓ Clash 核心已启动');
      await _waitForIpcReady();

      final proxyData = await _loadProxyData();
      final proxyNames = _collectProxyNames(proxyData);

      if (proxyNames.isEmpty) {
        throw Exception('未发现可测试的节点');
      }

      Logger.info('准备测试 ${proxyNames.length} 个节点');
      final stopwatch = Stopwatch()..start();
      var completedCount = 0;
      var successCount = 0;

      final results = await DelayTestService.testGroupDelays(
        proxyNames,
        onNodeComplete: (nodeName, delay) {
          completedCount++;
          if (delay > 0) {
            successCount++;
          }
          Logger.info(
            '进度 $completedCount/${proxyNames.length}: $nodeName -> ${delay}ms',
          );
        },
      );

      stopwatch.stop();
      Logger.info(
        '延迟测试完成：成功 $successCount/${proxyNames.length}，耗时 ${stopwatch.elapsedMilliseconds}ms',
      );
      Logger.info('返回结果 ${results.length} 条');
    } catch (e, stack) {
      exitCode = 1;
      Logger.error('✗ 延迟测试失败: $e');
      Logger.error('堆栈: $stack');
    } finally {
      if (processService != null) {
        try {
          await processService.stopProcess();
          Logger.info('✓ Clash 核心已停止');
        } catch (e) {
          Logger.warning('停止 Clash 核心失败：$e');
        }
      }
    }

    exit(exitCode);
  }

  static Future<File> _resolveTestConfig() async {
    final configDir = Directory(path.join('assets', 'test', 'config'));
    if (!await configDir.exists()) {
      throw Exception('测试配置目录不存在：${configDir.path}');
    }

    final preferred = File(path.join(configDir.path, 'test.yaml'));
    if (await preferred.exists()) {
      return preferred;
    }

    final candidates = await configDir
        .list()
        .where((entity) => entity is File)
        .cast<File>()
        .where(
          (file) => file.path.endsWith('.yaml') || file.path.endsWith('.yml'),
        )
        .toList();

    if (candidates.isEmpty) {
      throw Exception('测试配置目录未找到 YAML 文件');
    }

    candidates.sort((a, b) => a.path.compareTo(b.path));
    return candidates.first;
  }

  static Future<String?> _buildRuntimeConfig(String configPath) async {
    return ConfigInjector.injectCustomConfigParams(
      configPath: configPath,
      mixedPort: 17890,
      socksPort: null,
      httpPort: null,
      isIpv6Enabled: false,
      isTunEnabled: false,
      tunStack: 'mixed',
      tunDevice: 'Stelliberty-Test',
      isTunAutoRouteEnabled: false,
      isTunAutoRedirectEnabled: false,
      isTunAutoDetectInterfaceEnabled: false,
      tunDnsHijacks: const ['any:53'],
      isTunStrictRouteEnabled: false,
      tunRouteExcludeAddresses: const [],
      isTunIcmpForwardingDisabled: false,
      tunMtu: 1500,
      isAllowLanEnabled: false,
      isTcpConcurrentEnabled: false,
      geodataLoader: 'memconservative',
      findProcessMode: 'off',
      clashCoreLogLevel: 'debug',
      externalController: '',
      externalControllerSecret: '',
      isUnifiedDelayEnabled: false,
      outboundMode: 'rule',
    );
  }

  static Future<String> _resolveClashExecutable() async {
    try {
      return await ProcessService.getExecutablePath();
    } catch (e) {
      Logger.warning('内置核心不可用，尝试 assets 目录：$e');
    }

    final fileName = Platform.isWindows ? 'clash-core.exe' : 'clash-core';
    final execPath = path.join('assets', 'clash-core', fileName);
    final file = File(execPath);
    if (!await file.exists()) {
      throw Exception('Clash 核心不存在：$execPath');
    }
    return execPath;
  }

  static Future<void> _waitForIpcReady() async {
    const maxRetries = 5;

    for (var attempt = 1; attempt <= maxRetries; attempt++) {
      try {
        await IpcRequestHelper.instance.get('/version');
        Logger.info('✓ IPC 已就绪');
        return;
      } catch (e) {
        if (attempt == maxRetries) {
          throw Exception('IPC 仍未就绪：$e');
        }
        Logger.info('IPC 尚未就绪（$attempt/$maxRetries），1 秒后重试');
        await Future.delayed(const Duration(seconds: 1));
      }
    }
  }

  static Future<Map<String, dynamic>> _loadProxyData() async {
    const maxRetries = 3;
    Object? lastError;

    for (var attempt = 1; attempt <= maxRetries; attempt++) {
      try {
        final result = await IpcRequestHelper.instance.get('/proxies');
        final proxies = result['proxies'];
        if (proxies is Map<String, dynamic>) {
          return proxies;
        }
        if (proxies is Map) {
          return Map<String, dynamic>.from(proxies);
        }
        throw Exception('代理数据格式错误');
      } catch (e) {
        lastError = e;
        if (attempt == maxRetries) {
          break;
        }
        Logger.warning('获取代理数据失败（$attempt/$maxRetries）：$e');
        await Future.delayed(const Duration(seconds: 1));
      }
    }

    throw Exception('获取代理数据失败：$lastError');
  }

  static List<String> _collectProxyNames(Map<String, dynamic> proxies) {
    // 内置节点，Clash 测试时不遵守 timeout 参数
    const builtinNodes = {
      'DIRECT',
      'REJECT',
      'REJECT-DROP',
      'COMPATIBLE',
      'PASS',
    };

    final groups = _buildProxyGroups(proxies);
    final visibleGroupNames = groups
        .where((group) => !group.isHidden)
        .map((group) => group.name)
        .toSet();
    final names = <String>[];
    final seen = <String>{};

    for (final group in groups) {
      if (!visibleGroupNames.contains(group.name)) {
        continue;
      }
      for (final proxyName in group.all) {
        if (!proxies.containsKey(proxyName)) {
          continue;
        }
        if (builtinNodes.contains(proxyName)) {
          continue;
        }
        if (visibleGroupNames.contains(proxyName)) {
          continue;
        }
        if (seen.add(proxyName)) {
          names.add(proxyName);
        }
      }
    }

    return names;
  }

  static List<ProxyGroup> _buildProxyGroups(Map<String, dynamic> proxies) {
    final groups = <ProxyGroup>[];
    final addedGroups = <String>{};

    final globalData = _normalizeProxyData(proxies['GLOBAL']);
    final hasGlobalAll = globalData?['all'] is List;

    if (globalData != null && hasGlobalAll) {
      groups.add(ProxyGroup.fromJson('GLOBAL', globalData));
      addedGroups.add('GLOBAL');
    }

    if (hasGlobalAll) {
      final orderedNames = List<String>.from(globalData!['all'] as List);
      for (final groupName in orderedNames) {
        if (addedGroups.contains(groupName)) {
          continue;
        }
        final data = _normalizeProxyData(proxies[groupName]);
        if (data == null || data['all'] is! List) {
          continue;
        }
        groups.add(ProxyGroup.fromJson(groupName, data));
        addedGroups.add(groupName);
      }
    }

    proxies.forEach((name, data) {
      if (addedGroups.contains(name)) {
        return;
      }
      final proxyData = _normalizeProxyData(data);
      if (proxyData == null || proxyData['all'] is! List) {
        return;
      }
      groups.add(ProxyGroup.fromJson(name, proxyData));
    });

    return groups;
  }

  static Map<String, dynamic>? _normalizeProxyData(dynamic data) {
    if (data is Map<String, dynamic>) {
      return data;
    }
    if (data is Map) {
      return Map<String, dynamic>.from(data);
    }
    return null;
  }
}

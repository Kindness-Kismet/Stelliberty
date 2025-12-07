import 'package:stelliberty/clash/storage/preferences.dart';
import 'package:stelliberty/clash/network/ipc_request_helper.dart';
import 'package:stelliberty/clash/services/process_service.dart';
import 'package:stelliberty/clash/config/config_injector.dart';
import 'package:stelliberty/services/path_service.dart';
import 'package:stelliberty/utils/logger.dart';
import 'package:stelliberty/src/bindings/signals/signals.dart';
import 'dart:io';
import 'dart:async';

// IPC API 测试
//
// 测试通过 IPC 调用 Clash API
class IpcApiTest {
  static Future<void> run() async {
    Logger.info('======================================');
    Logger.info('开始 IPC API 测试');
    Logger.info('======================================');

    try {
      // 0. 初始化 PathService 和 Preferences
      await PathService.instance.initialize();
      Logger.info('✓ PathService 已初始化');

      await ClashPreferences.instance.init();
      Logger.info('✓ Preferences 已初始化');

      // 1. 启动 Clash 核心（使用项目 assets/test/config/test.yaml）
      const testConfigPath = 'assets/test/config/test.yaml';

      if (!await File(testConfigPath).exists()) {
        throw Exception('测试配置文件不存在：$testConfigPath');
      }
      Logger.info('使用测试配置：$testConfigPath');

      final runtimeConfigPath = await ConfigInjector.injectCustomConfigParams(
        configPath: testConfigPath,
        httpPort: 17890,
        ipv6: false,
        tunEnable: false,
        tunStack: 'mixed',
        tunDevice: 'Stelliberty-Test',
        tunAutoRoute: false,
        tunAutoDetectInterface: false,
        tunDnsHijack: const ['any:53'],
        tunStrictRoute: false,
        tunMtu: 1500,
        tunAutoRedirect: false,
        tunRouteExcludeAddress: const [],
        tunDisableIcmpForwarding: false,
        allowLan: false,
        tcpConcurrent: false,
        geodataLoader: 'memconservative',
        findProcessMode: 'off',
        clashCoreLogLevel: 'debug',
        externalController: '',
        externalControllerSecret: '',
        unifiedDelay: false,
        outboundMode: 'rule',
      );

      if (runtimeConfigPath == null) {
        throw Exception('运行时配置生成失败');
      }

      // 使用项目目录下的 clash-core（不使用构建后的副本）
      const execPath = 'assets/clash-core/clash-core';
      if (!await File(execPath).exists()) {
        throw Exception('Clash 核心不存在：$execPath（请先运行 prebuild.dart）');
      }

      final processService = ProcessService();
      await processService.start(
        executablePath: execPath,
        configPath: runtimeConfigPath,
        apiHost: '127.0.0.1',
        apiPort: 19090,
      );

      Logger.info('✓ Clash 核心已启动');

      // 2. 等待 IPC 端点就绪
      Logger.info('等待 3 秒，确保 IPC 端点就绪...');
      await Future.delayed(const Duration(seconds: 3));

      // 2. 测试 GET /version
      Logger.info('');
      Logger.info('测试 1: GET /version');
      final versionData = await IpcRequestHelper.instance.get('/version');
      Logger.info('✓ 版本信息: $versionData');

      // 3. 测试 GET /configs
      Logger.info('');
      Logger.info('测试 2: GET /configs');
      final configData = await IpcRequestHelper.instance.get('/configs');
      Logger.info(
        '✓ 配置信息: mode=${configData['mode']}, port=${configData['port']}',
      );

      // 4. 测试 PATCH /configs
      Logger.info('');
      Logger.info('测试 3: PATCH /configs (设置 mode=direct)');
      await IpcRequestHelper.instance.patch(
        '/configs',
        body: {'mode': 'direct'},
      );
      Logger.info('✓ 配置已更新');

      // 5. 验证配置更新
      Logger.info('');
      Logger.info('测试 4: 验证配置更新');
      final updatedConfig = await IpcRequestHelper.instance.get('/configs');
      final newMode = updatedConfig['mode'];
      if (newMode == 'direct') {
        Logger.info('✓ 配置更新成功: mode=$newMode');
      } else {
        throw Exception('配置更新失败: mode=$newMode (期望: direct)');
      }

      // 6. 恢复原配置
      Logger.info('');
      Logger.info('测试 5: 恢复配置 (mode=rule)');
      await IpcRequestHelper.instance.patch('/configs', body: {'mode': 'rule'});
      Logger.info('✓ 配置已恢复');

      // 7. 测试 WebSocket 流量监控
      Logger.info('');
      Logger.info('测试 6: WebSocket 流量监控');
      await _testTrafficStream();

      // 8. 测试 WebSocket 日��监控
      Logger.info('');
      Logger.info('测试 7: WebSocket 日志监控');
      await _testLogStream();

      Logger.info('');
      Logger.info('======================================');
      Logger.info('测试结果：全部成功 ✓');
      Logger.info('======================================');

      exit(0);
    } catch (e, stack) {
      Logger.error('✗ IPC API 测试失败: $e');
      Logger.error('堆栈: $stack');
      exit(1);
    }
  }

  // 测试流量监控流
  static Future<void> _testTrafficStream() async {
    final completer = Completer<void>();
    StreamSubscription? resultSubscription;
    StreamSubscription? dataSubscription;
    int dataCount = 0;
    const targetCount = 3; // 收集 3 个数据点

    try {
      // 1. 监听流启动结果
      resultSubscription = StreamResult.rustSignalStream.listen((signal) {
        final result = signal.message;
        if (result.success) {
          Logger.info('  ✓ 流量监控 WebSocket 已连接');
        } else {
          throw Exception('流量监控启动失败: ${result.errorMessage}');
        }
      });

      // 2. 监听流量数据
      dataSubscription = IpcTrafficData.rustSignalStream.listen((signal) {
        final data = signal.message;
        dataCount++;
        Logger.info(
          '  ✓ 流量数据 #$dataCount: upload=${data.upload} bytes, download=${data.download} bytes',
        );

        if (dataCount >= targetCount) {
          completer.complete();
        }
      });

      // 3. 发送启动信号
      const StartTrafficStream().sendSignalToRust();
      Logger.info('  已发送启动流量监控信号...');

      // 4. 等待数据接收完成或超时
      await completer.future.timeout(
        const Duration(seconds: 10),
        onTimeout: () {
          throw Exception('流量监控超时：仅收到 $dataCount/$targetCount 个数据点');
        },
      );

      Logger.info('✓ 流量监控测试通过（收到 $dataCount 个数据点）');
    } finally {
      await resultSubscription?.cancel();
      await dataSubscription?.cancel();
    }
  }

  // 测试日志监控流
  static Future<void> _testLogStream() async {
    final completer = Completer<void>();
    StreamSubscription? resultSubscription;
    StreamSubscription? dataSubscription;
    int dataCount = 0;
    bool connectionEstablished = false;

    try {
      // 1. 监听流启动结果
      resultSubscription = StreamResult.rustSignalStream.listen((signal) {
        final result = signal.message;
        if (result.success) {
          Logger.info('  ✓ 日志监控 WebSocket 已连接');
          connectionEstablished = true;
        } else {
          throw Exception('日志监控启动失败: ${result.errorMessage}');
        }
      });

      // 2. 监听日志数据
      dataSubscription = IpcLogData.rustSignalStream.listen((signal) {
        final data = signal.message;
        dataCount++;
        Logger.info('  ✓ 日志数据 #$dataCount: [${data.logType}] ${data.payload}');

        if (!completer.isCompleted) {
          completer.complete();
        }
      });

      // 3. 发送启动信号
      const StartLogStream().sendSignalToRust();
      Logger.info('  已发送启动日志监控信号...');

      // 4. 等待连接或数据（5秒超时）
      // 注：最小配置可能不产生日志，只验证连接成功即可
      await Future.any([
        completer.future,
        Future.delayed(const Duration(seconds: 5)),
      ]);

      if (connectionEstablished) {
        if (dataCount > 0) {
          Logger.info('✓ 日志监控测试通过（收到 $dataCount 个日志条目）');
        } else {
          Logger.info('✓ 日志监控连接测试通过（无日志产生，属正常情况）');
        }
      } else {
        throw Exception('日志监控 WebSocket 连接未建立');
      }
    } finally {
      await resultSubscription?.cancel();
      await dataSubscription?.cancel();
    }
  }
}

import 'dart:async';
import 'package:stelliberty/clash/data/clash_model.dart';
import 'package:stelliberty/clash/config/clash_defaults.dart';
import 'package:stelliberty/clash/utils/delay_tester.dart';
import 'package:stelliberty/utils/logger.dart';
import 'package:stelliberty/src/bindings/signals/signals.dart' as signals;

// 延迟测试服务
class DelayTestService {
  // 测试代理延迟（支持代理组）
  // 使用 Clash API 进行统一延迟测试（需要 Clash 正在运行）
  // 注意：此方法不修改传入的 proxyNodes Map，仅返回延迟值
  //
  // 重要：不递归解析代理组，直接测试传入的节点名称
  // 如果是代理组，Clash API 会测试该代理组当前选中的节点
  static Future<int> testProxyDelay(
    String proxyName,
    Map<String, ProxyNode> proxyNodes,
    List<ProxyGroup> allProxyGroups,
    Map<String, String> selectedMap, {
    String? testUrl,
  }) async {
    final node = proxyNodes[proxyName];
    if (node == null) {
      Logger.warning('代理节点不存在：$proxyName');
      return -1;
    }

    // 关键：检查 DelayTester 是否可用（即 Clash API 客户端是否已设置）
    if (!DelayTester.isAvailable) {
      Logger.error('Clash 未运行或 API 未就绪，无法进行延迟测试');
      return -1;
    }

    final delay = await DelayTester.testProxyDelay(node, testUrl: testUrl);

    return delay;
  }

  // 批量测试代理组中所有节点的延迟
  // 使用 Rust 层批量测试（通过滑动窗口并发策略）
  // 每个节点测试完成后立即回调，实现真正的流式更新
  static Future<Map<String, int>> testGroupDelays(
    String groupName,
    Map<String, ProxyNode> proxyNodes,
    List<ProxyGroup> allProxyGroups,
    Map<String, String> selectedMap, {
    String? testUrl,
    Function(String nodeName)? onNodeStart,
    Function(String nodeName, int delay)? onNodeComplete,
  }) async {
    final group = allProxyGroups.firstWhere(
      (g) => g.name == groupName,
      orElse: () => throw Exception('Group not found: $groupName'),
    );

    // 获取所有要测试的代理名称（包括代理组和实际节点）
    final proxyNames = group.all.where((proxyName) {
      final node = proxyNodes[proxyName];
      return node != null; // 只要存在就可以测试
    }).toList();

    if (proxyNames.isEmpty) {
      Logger.warning('代理组 $groupName 中没有可测试的节点');
      return {};
    }

    // 关键：检查 DelayTester 是否可用（即 Clash API 客户端是否已设置）
    if (!DelayTester.isAvailable) {
      Logger.error('Clash 未运行或 API 未就绪，无法进行延迟测试');
      return {};
    }

    // 使用动态并发数（基于 CPU 核心数）
    final concurrency = ClashDefaults.dynamicDelayTestConcurrency;
    final timeoutMs = ClashDefaults.proxyDelayTestTimeout;
    final url = testUrl ?? ClashDefaults.defaultTestUrl;

    // 存储所有节点的延迟结果
    final delayResults = <String, int>{};
    final completer = Completer<void>();

    // 监听进度信号（流式更新）
    StreamSubscription? progressSubscription;
    StreamSubscription? completeSubscription;

    try {
      // 订阅进度信号
      progressSubscription = signals.DelayTestProgress.rustSignalStream.listen((
        result,
      ) {
        final nodeName = result.message.nodeName;
        final delayMs = result.message.delayMs;

        // 触发节点测试完成回调
        onNodeComplete?.call(nodeName, delayMs);

        // 保存延迟结果
        delayResults[nodeName] = delayMs;
      });

      // 订阅完成信号
      completeSubscription =
          signals.BatchDelayTestComplete.rustSignalStream.listen((result) {
        final message = result.message;
        if (message.success) {
          completer.complete();
        } else {
          Logger.error(
            '批量延迟测试失败（Rust 层）：${message.errorMessage ?? "未知错误"}',
          );
          completer.completeError(
            Exception(message.errorMessage ?? '批量延迟测试失败'),
          );
        }
      });

      // 发送批量测试请求到 Rust 层
      signals.BatchDelayTestRequest(
        nodeNames: proxyNames,
        testUrl: url,
        timeoutMs: timeoutMs,
        concurrency: concurrency,
      ).sendSignalToRust();

      // 等待测试完成（最多等待：节点数 × 单个超时 + 10秒缓冲）
      final maxWaitTime = Duration(
        milliseconds: proxyNames.length * timeoutMs + 10000,
      );
      await completer.future.timeout(
        maxWaitTime,
        onTimeout: () {
          throw Exception('批量延迟测试超时');
        },
      );

      return delayResults;
    } finally {
      // 取消订阅
      await progressSubscription?.cancel();
      await completeSubscription?.cancel();
    }
  }
}

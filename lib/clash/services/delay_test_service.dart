import 'dart:async';
import 'package:stelliberty/clash/config/clash_defaults.dart';
import 'package:stelliberty/services/log_print_service.dart';
import 'package:stelliberty/src/bindings/signals/signals.dart' as signals;

// 延迟测试服务
// 纯技术实现：发送 Rust 信号、监听响应
class DelayTestService {
  // 测试单个代理节点延迟
  static Future<int> testProxyDelay(String proxyName, {String? testUrl}) async {
    final url = testUrl ?? ClashDefaults.defaultTestUrl;
    final timeoutMs = ClashDefaults.proxyDelayTestTimeout;
    final completer = Completer<int>();

    StreamSubscription? subscription;
    try {
      subscription = signals.SingleDelayTestResult.rustSignalStream.listen((
        result,
      ) {
        final message = result.message;
        if (message.nodeName == proxyName) {
          completer.complete(message.delayMs);
        }
      });

      signals.SingleDelayTestRequest(
        nodeName: proxyName,
        testUrl: url,
        timeoutMs: timeoutMs,
      ).sendSignalToRust();

      final delay = await completer.future.timeout(
        Duration(milliseconds: timeoutMs),
        onTimeout: () {
          Logger.warning('单节点延迟测试超时：$proxyName');
          return -1;
        },
      );

      return delay;
    } finally {
      await subscription?.cancel();
    }
  }

  // 批量测试代理节点延迟
  // 使用 Rust 层批量测试（通过滑动窗口并发策略）
  static Future<Map<String, int>> testGroupDelays(
    List<String> proxyNames, {
    String? testUrl,
    Function(String nodeName)? onNodeStart,
    Function(String nodeName, int delay)? onNodeComplete,
  }) async {
    if (proxyNames.isEmpty) {
      Logger.warning('代理节点列表为空');
      return {};
    }

    final concurrency = ClashDefaults.delayTestConcurrency;
    final timeoutMs = ClashDefaults.proxyDelayTestTimeout;
    final url = testUrl ?? ClashDefaults.defaultTestUrl;

    final delayResults = <String, int>{};
    final completer = Completer<void>();

    StreamSubscription? progressSubscription;
    StreamSubscription? completeSubscription;

    try {
      progressSubscription = signals.DelayTestProgress.rustSignalStream.listen((
        result,
      ) {
        final nodeName = result.message.nodeName;
        final delayMs = result.message.delayMs;

        onNodeComplete?.call(nodeName, delayMs);
        delayResults[nodeName] = delayMs;
      });

      completeSubscription = signals.BatchDelayTestComplete.rustSignalStream
          .listen((result) {
            final message = result.message;
            if (message.isSuccessful) {
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

      signals.BatchDelayTestRequest(
        nodeNames: proxyNames,
        testUrl: url,
        timeoutMs: timeoutMs,
        concurrency: concurrency,
      ).sendSignalToRust();

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
      await progressSubscription?.cancel();
      await completeSubscription?.cancel();
    }
  }
}

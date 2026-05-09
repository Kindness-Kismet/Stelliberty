import 'dart:async';

import 'package:stelliberty/clash/model/subscription_model.dart';
import 'package:stelliberty/services/log_print_service.dart';
import 'package:stelliberty/src/bindings/signals/signals.dart';

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

  Future<ChainProxyRuntimeConfig> analyzeAndApply(
    String rawConfig,
    Subscription subscription,
  ) async {
    final requestId =
        'chain-${DateTime.now().microsecondsSinceEpoch}-${identityHashCode(subscription)}';
    final completer = Completer<BuildChainProxyConfigResponse>();
    final signalSubscription = BuildChainProxyConfigResponse.rustSignalStream
        .listen((signal) {
          final response = signal.message;
          if (response.requestId != requestId || completer.isCompleted) {
            return;
          }
          completer.complete(response);
        });

    try {
      final request = BuildChainProxyConfigRequest(
        requestId: requestId,
        rawConfig: rawConfig,
        fallbackBuiltinChainProxyNames: subscription.builtinChainProxyNames,
        disabledBuiltinChainProxyNames:
            subscription.disabledBuiltinChainProxyNames,
        customChainProxies: subscription.customChainProxies
            .map(
              (customProxy) => ChainProxyCustomConfig(
                displayName: customProxy.displayName,
                nodeNames: customProxy.nodeNames,
              ),
            )
            .toList(),
      );
      request.sendSignalToRust();

      final response = await completer.future.timeout(
        const Duration(seconds: 5),
        onTimeout: () {
          throw Exception('Rust 链式基础配置生成超时');
        },
      );

      if (!response.isSuccessful) {
        throw Exception(response.errorMessage);
      }

      Logger.debug('生成链式基础配置完成: ${response.configContent.length} 字符');
      return ChainProxyRuntimeConfig(
        configContent: response.configContent,
        builtinChainProxyNames: response.builtinChainProxyNames,
      );
    } finally {
      await signalSubscription.cancel();
    }
  }
}

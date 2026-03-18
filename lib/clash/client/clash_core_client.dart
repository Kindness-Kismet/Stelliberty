import 'dart:io';

import 'package:stelliberty/clash/model/connection_model.dart';
import 'package:stelliberty/clash/model/rule_model.dart';
import 'package:stelliberty/clash/client/ipc_core_client.dart';
import 'package:stelliberty/clash/client/jni_core_client.dart';

// Clash 核心客户端抽象接口
// 定义与 Clash 核心交互的通用方法，支持不同平台实现（IPC/JNI）
abstract class ClashCoreClient {
  static ClashCoreClient? _instance;

  // 单例获取
  static ClashCoreClient get instance {
    _instance ??= _create();
    return _instance!;
  }

  // 根据平台创建实例
  static ClashCoreClient _create() {
    if (Platform.isAndroid) {
      return JniCoreClient();
    }
    return IpcCoreClient();
  }

  // 重置实例（用于测试或切换模式）
  static void reset() {
    _instance = null;
  }

  // 设置 Android 配置路径
  static void setConfigPath(String? path) {
    if (_instance is JniCoreClient) {
      (_instance as JniCoreClient).setConfigPath(path);
    }
  }

  // 获取 JniCoreClient 实例（仅 Android 端可用）
  static JniCoreClient? get jniClient {
    if (_instance is JniCoreClient) {
      return _instance as JniCoreClient;
    }
    return null;
  }

  // 基础功能
  Future<bool> checkHealth();
  Future<String> getVersion();

  // 等待 API 就绪（桌面端等待 IPC 通道建立，Android 端直接返回）
  Future<void> waitForReady({
    int maxRetries,
    Duration retryInterval,
    Duration? checkTimeout,
  });

  // 代理管理
  Future<Map<String, dynamic>> getProxies();
  Future<bool> changeProxy(String groupName, String proxyName);
  Future<int> testProxyDelay(
    String proxyName, {
    String? testUrl,
    int? timeoutMs,
  });

  // 连接管理
  Future<List<ConnectionInfo>> getConnections();
  Future<bool> closeConnection(String connectionId);
  Future<bool> closeAllConnections();

  // 配置管理
  Future<Map<String, dynamic>> getConfig();
  Future<bool> updateConfig(Map<String, dynamic> config);
  Future<bool> reloadConfig({
    String? configPath,
    String? configContent,
    bool force = true,
  });

  // 规则
  Future<List<RuleItem>> getRules();

  // 流量（可选实现）
  Future<String> getMode();
  Future<bool> setMode(String mode);

  // 配置项快捷方法
  Future<bool> setAllowLan(bool allow);
  Future<bool> setLanAuthentication(List<String> authentication);
  Future<bool> setIpv6(bool enable);
  Future<bool> setTcpConcurrent(bool enable);
  Future<bool> setUnifiedDelay(bool enable);
  Future<bool> setGeodataLoader(String mode);
  Future<bool> setFindProcessMode(String mode);
  Future<bool> setLogLevel(String level);
  Future<bool> setExternalController(String? address);
  Future<bool> setMixedPort(int port);
  Future<bool> setSocksPort(int port);
  Future<bool> setHttpPort(int port);

  // TUN 配置（桌面端专用，Android 端可空实现）
  Future<bool> setTunEnable(bool enable);
  Future<bool> setTunStack(String stack);
  Future<bool> setTunDevice(String device);
  Future<bool> setTunAutoRoute(bool enable);
  Future<bool> setTunAutoRedirect(bool enable);
  Future<bool> setTunAutoDetectInterface(bool enable);
  Future<bool> setTunDnsHijack(List<String> hijackList);
  Future<bool> setTunStrictRoute(bool enable);
  Future<bool> setTunRouteExcludeAddress(List<String> addresses);
  Future<bool> setTunDisableIcmpForwarding(bool disabled);
  Future<bool> setTunMtu(int mtu);

  // Provider 管理
  Future<Map<String, dynamic>> getProviders();
  Future<Map<String, dynamic>?> getProvider(String providerName);
  Future<bool> updateProvider(String providerName);
  Future<bool> healthCheckProvider(String providerName);

  // 规则 Provider
  Future<Map<String, dynamic>> getRuleProviders();
  Future<Map<String, dynamic>?> getRuleProvider(String providerName);
  Future<bool> updateRuleProvider(String providerName);
}

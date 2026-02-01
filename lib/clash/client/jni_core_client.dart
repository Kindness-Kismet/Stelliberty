import 'dart:convert';

import 'package:stelliberty/clash/model/connection_model.dart';
import 'package:stelliberty/clash/model/rule_model.dart';
import 'package:stelliberty/clash/client/clash_core_client.dart';
import 'package:stelliberty/clash/services/vpn_service.dart';
import 'package:stelliberty/services/log_print_service.dart';

// Android 平台核心客户端实现
// 通过 JNI (invokeAction) 与 Clash 核心交互
class JniCoreClient implements ClashCoreClient {
  // 当前配置文件路径（用于 getConfig）
  String? _configPath;

  // 设置当前配置路径（由 VpnService 启动时调用）
  void setConfigPath(String? path) {
    _configPath = path;
  }

  @override
  Future<bool> checkHealth() async {
    try {
      final version = await getVersion();
      return version.isNotEmpty && version != 'Unknown';
    } catch (e) {
      return false;
    }
  }

  @override
  Future<String> getVersion() async {
    final res = await VpnService.invokeAction(method: 'getVersion');
    if (res == null) return 'Unknown';
    final data = res['data'];
    if (data is String) return data;
    return 'Unknown';
  }

  @override
  Future<void> waitForReady({
    int maxRetries = 1,
    Duration retryInterval = Duration.zero,
    Duration? checkTimeout,
  }) async {
    // Android 端核心已在 VpnService 中初始化，无需等待
    return;
  }

  @override
  Future<Map<String, dynamic>> getProxies() async {
    final proxies = await VpnService.getProxies();
    return proxies ?? {};
  }

  @override
  Future<bool> changeProxy(String groupName, String proxyName) async {
    final success = await VpnService.changeProxy(
      groupName: groupName,
      proxyName: proxyName,
    );
    if (success) {
      // 切换节点后关闭所有连接
      await closeAllConnections();
    }
    return success;
  }

  @override
  Future<int> testProxyDelay(
    String proxyName, {
    String? testUrl,
    int? timeoutMs,
  }) async {
    return await VpnService.testProxyDelay(
      proxyName: proxyName,
      testUrl: testUrl,
      timeoutMs: timeoutMs,
    );
  }

  @override
  Future<List<ConnectionInfo>> getConnections() async {
    try {
      final dataStr = await VpnService.getConnections();
      if (dataStr == null || dataStr.isEmpty) return [];

      final data = jsonDecode(dataStr);
      if (data is! Map<String, dynamic>) return [];

      final connections = data['connections'] as List<dynamic>? ?? [];
      return connections
          .map((conn) => ConnectionInfo.fromJson(conn as Map<String, dynamic>))
          .toList();
    } catch (e) {
      Logger.error('获取连接列表失败：$e');
      return [];
    }
  }

  @override
  Future<bool> closeConnection(String connectionId) async {
    return await VpnService.closeConnection(connectionId);
  }

  @override
  Future<bool> closeAllConnections() async {
    return await VpnService.closeAllConnections();
  }

  @override
  Future<Map<String, dynamic>> getConfig() async {
    // Core 的 getConfig 需要传入配置文件路径
    if (_configPath == null || _configPath!.isEmpty) {
      Logger.warning('Android 端未设置配置路径，无法获取配置');
      return {};
    }

    final res = await VpnService.invokeAction(
      method: 'getConfig',
      data: _configPath,
    );
    if (res == null) return {};

    final code = res['code'];
    if (code != 0) {
      Logger.error('获取配置失败：${res['data']}');
      return {};
    }

    final data = res['data'];
    if (data == null) return {};
    if (data is String) {
      try {
        return jsonDecode(data) as Map<String, dynamic>;
      } catch (e) {
        Logger.error('解析配置失败：$e');
        return {};
      }
    }
    return data as Map<String, dynamic>;
  }

  @override
  Future<bool> updateConfig(Map<String, dynamic> config) async {
    final res = await VpnService.invokeAction(
      method: 'updateConfig',
      data: jsonEncode(config),
    );
    if (res == null) return false;
    final code = res['code'];
    final dataVal = res['data'];
    return code == 0 && (dataVal == '' || dataVal == null || dataVal == true);
  }

  @override
  Future<bool> reloadConfig({
    String? configPath,
    String? configContent,
    bool force = true,
  }) async {
    // Android 端使用 setupConfig 重载配置
    if (configContent != null && configContent.isNotEmpty) {
      Logger.debug('Android 端不支持 payload 重载，将忽略 payload');
    }

    final path = configPath ?? _configPath;
    if (path == null || path.isEmpty) {
      Logger.warning('Android 端未指定配置路径，无法重载配置');
      return false;
    }

    // 传入配置路径让核心直接加载
    final res = await VpnService.invokeAction(
      method: 'setupConfig',
      data: {'config-path': path},
    );
    if (res == null) return false;

    final code = res['code'];
    if (code != 0) {
      Logger.error('重载配置失败：${res['data']}');
      return false;
    }

    // 更新当前配置路径
    _configPath = path;
    Logger.info('Android 端配置重载成功');
    return true;
  }

  // 验证配置文件
  Future<bool> validateConfig(String configPath) async {
    final res = await VpnService.invokeAction(
      method: 'validateConfig',
      data: configPath,
    );
    if (res == null) return false;
    return res['code'] == 0;
  }

  @override
  Future<List<RuleItem>> getRules() async {
    // Core 没有直接获取规则列表的方法
    Logger.debug('Android 端暂不支持独立获取规则列表');
    return [];
  }

  @override
  Future<String> getMode() async {
    final config = await getConfig();
    return (config['mode'] as String?) ?? 'rule';
  }

  @override
  Future<bool> setMode(String mode) async {
    return await updateConfig({'mode': mode});
  }

  @override
  Future<bool> setAllowLan(bool allow) async {
    return await updateConfig({'allow-lan': allow});
  }

  @override
  Future<bool> setIpv6(bool enable) async {
    return await updateConfig({'ipv6': enable});
  }

  @override
  Future<bool> setTcpConcurrent(bool enable) async {
    return await updateConfig({'tcp-concurrent': enable});
  }

  @override
  Future<bool> setUnifiedDelay(bool enable) async {
    return await updateConfig({'unified-delay': enable});
  }

  @override
  Future<bool> setGeodataLoader(String mode) async {
    return await updateConfig({'geodata-loader': mode});
  }

  @override
  Future<bool> setFindProcessMode(String mode) async {
    return await updateConfig({'find-process-mode': mode});
  }

  @override
  Future<bool> setLogLevel(String level) async {
    return await updateConfig({'log-level': level});
  }

  @override
  Future<bool> setExternalController(String? address) async {
    return await updateConfig({'external-controller': address ?? ''});
  }

  @override
  Future<bool> setMixedPort(int port) async {
    return await updateConfig({'mixed-port': port});
  }

  @override
  Future<bool> setSocksPort(int port) async {
    return await updateConfig({'socks-port': port});
  }

  @override
  Future<bool> setHttpPort(int port) async {
    return await updateConfig({'port': port});
  }

  // TUN 配置方法（Android 使用 VPN 而非 TUN，空实现）
  @override
  Future<bool> setTunEnable(bool enable) async {
    Logger.debug('Android 端使用 VPN 模式，忽略 TUN 配置');
    return true;
  }

  @override
  Future<bool> setTunStack(String stack) async => true;

  @override
  Future<bool> setTunDevice(String device) async => true;

  @override
  Future<bool> setTunAutoRoute(bool enable) async => true;

  @override
  Future<bool> setTunAutoRedirect(bool enable) async => true;

  @override
  Future<bool> setTunAutoDetectInterface(bool enable) async => true;

  @override
  Future<bool> setTunDnsHijack(List<String> hijackList) async => true;

  @override
  Future<bool> setTunStrictRoute(bool enable) async => true;

  @override
  Future<bool> setTunRouteExcludeAddress(List<String> addresses) async => true;

  @override
  Future<bool> setTunDisableIcmpForwarding(bool disabled) async => true;

  @override
  Future<bool> setTunMtu(int mtu) async => true;

  // Provider 管理
  @override
  Future<Map<String, dynamic>> getProviders() async {
    final res = await VpnService.getExternalProviders();
    if (res == null) return {};
    // 转换为 Map 格式
    final providers = <String, dynamic>{};
    for (final p in res) {
      final name = p['name'] as String?;
      if (name != null) {
        providers[name] = p;
      }
    }
    return providers;
  }

  @override
  Future<Map<String, dynamic>?> getProvider(String providerName) async {
    final res = await VpnService.invokeAction(
      method: 'getExternalProvider',
      data: providerName,
    );
    if (res == null) return null;
    final data = res['data'];
    if (data == null) return null;
    if (data is String) {
      return jsonDecode(data) as Map<String, dynamic>;
    }
    return data as Map<String, dynamic>;
  }

  @override
  Future<bool> updateProvider(String providerName) async {
    return await VpnService.updateExternalProvider(providerName);
  }

  @override
  Future<bool> healthCheckProvider(String providerName) async {
    // Android 端通过 updateProvider 触发健康检查
    return await updateProvider(providerName);
  }

  @override
  Future<Map<String, dynamic>> getRuleProviders() async {
    // Core 没有独立的规则 Provider 方法
    Logger.debug('Android 端暂不支持获取规则 Provider');
    return {};
  }

  @override
  Future<Map<String, dynamic>?> getRuleProvider(String providerName) async {
    return null;
  }

  @override
  Future<bool> updateRuleProvider(String providerName) async {
    return false;
  }

  // 额外方法：启动日志订阅
  Future<bool> startLog() async {
    final res = await VpnService.invokeAction(method: 'startLog');
    if (res == null) return false;
    return res['code'] == 0;
  }

  // 额外方法：停止日志订阅
  Future<bool> stopLog() async {
    final res = await VpnService.invokeAction(method: 'stopLog');
    if (res == null) return false;
    return res['code'] == 0;
  }

  // 额外方法：获取 IP 归属地
  Future<String?> getCountryCode(String ip) async {
    final res = await VpnService.invokeAction(
      method: 'getCountryCode',
      data: ip,
    );
    if (res == null) return null;
    if (res['code'] != 0) return null;
    final data = res['data'];
    if (data is String) return data;
    return null;
  }

  // 额外方法：侧载外部订阅
  Future<bool> sideLoadExternalProvider(String payload) async {
    final res = await VpnService.invokeAction(
      method: 'sideLoadExternalProvider',
      data: payload,
    );
    if (res == null) return false;
    return res['code'] == 0;
  }

  // 额外方法：删除文件
  Future<bool> deleteFile(String path) async {
    final res = await VpnService.invokeAction(method: 'deleteFile', data: path);
    if (res == null) return false;
    return res['code'] == 0;
  }

  // 额外方法：启动监听器
  Future<bool> startListener() async {
    final res = await VpnService.invokeAction(method: 'startListener');
    if (res == null) return false;
    return res['code'] == 0;
  }

  // 额外方法：停止监听器
  Future<bool> stopListener() async {
    final res = await VpnService.invokeAction(method: 'stopListener');
    if (res == null) return false;
    return res['code'] == 0;
  }
}

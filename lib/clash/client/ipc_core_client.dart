import 'dart:async';
import 'package:stelliberty/services/log_print_service.dart';
import 'package:stelliberty/clash/config/clash_defaults.dart';
import 'package:stelliberty/clash/model/connection_model.dart';
import 'package:stelliberty/clash/model/rule_model.dart';
import 'package:stelliberty/clash/client/clash_core_client.dart';
import 'package:stelliberty/clash/client/ipc_request_helper.dart';
import 'package:stelliberty/storage/clash_preferences.dart';

// 桌面端核心客户端实现
// 通过 IPC（Named Pipe / Unix Socket）与 Rust 服务通信
class IpcCoreClient implements ClashCoreClient {
  IpcCoreClient();

  // getConfig() 缓存机制（优化并发请求）
  Map<String, dynamic>? _configCache;
  DateTime? _configCachedAt;
  Future<Map<String, dynamic>>? _configPendingRequest;
  static const _configCacheDuration = Duration(seconds: 1);

  // 内部 GET 请求
  Future<Map<String, dynamic>> _get(String path) async {
    return await IpcRequestHelper.instance.get(path);
  }

  // 内部 PATCH 请求
  Future<bool> _patch(String path, Map<String, dynamic> body) async {
    await IpcRequestHelper.instance.patch(path, body: body);
    return true;
  }

  // 内部 PUT 请求
  Future<bool> _put(String path, Map<String, dynamic> body) async {
    await IpcRequestHelper.instance.put(path, body: body);
    return true;
  }

  // 内部 DELETE 请求
  Future<bool> _delete(String path) async {
    await IpcRequestHelper.instance.delete(path);
    return true;
  }

  // 注意：Mihomo 的 PATCH /configs 在接收 tun 对象时，会无条件使用 tun.enable。
  // 因此发送 tun 子字段时必须同时带上当前 enable 值，避免意外关闭 TUN。
  Map<String, dynamic> _buildTunPatch(Map<String, dynamic> tunConfig) {
    return {
      'tun': <String, dynamic>{
        'enable': ClashPreferences.instance.getTunEnable(),
        ...tunConfig,
      },
    };
  }

  @override
  Future<bool> checkHealth({
    Duration timeout = const Duration(
      milliseconds: ClashDefaults.apiReadyCheckTimeout,
    ),
  }) async {
    try {
      await _get('/version');
      return true;
    } catch (e) {
      return false;
    }
  }

  @override
  Future<String> getVersion() async {
    try {
      final data = await _get('/version');
      // Mihomo 返回格式: {"meta":true,"premium":true,"version":"Mihomo 1.18.1"}
      return data['version'] ?? 'Unknown';
    } catch (e) {
      Logger.error('获取版本信息出错：$e');
      return 'Unknown';
    }
  }

  @override
  Future<void> waitForReady({
    int maxRetries = ClashDefaults.apiReadyMaxRetries,
    Duration retryInterval = const Duration(
      milliseconds: ClashDefaults.apiReadyRetryInterval,
    ),
    Duration? checkTimeout,
  }) async {
    final timeout =
        checkTimeout ??
        const Duration(milliseconds: ClashDefaults.apiReadyCheckTimeout);
    Object? lastError;

    for (int i = 0; i < maxRetries; i++) {
      try {
        await _get('/version').timeout(timeout);
        return;
      } catch (e) {
        lastError = e;
        // 简化日志：只在第 1 次、每 5 次、最后 3 次打印
        final shouldLog = i == 0 || (i + 1) % 5 == 0 || i >= maxRetries - 3;
        if (shouldLog) {
          // 检查是否为 IPC 未就绪（Named Pipe 还未创建）
          final errorMsg = e.toString();
          final isIpcNotReady =
              errorMsg.contains('系统找不到指定的文件') ||
              errorMsg.contains('os error 2') ||
              errorMsg.contains('os error 111') ||
              errorMsg.contains('os error 61') ||
              errorMsg.contains('Connection refused');

          if (isIpcNotReady) {
            Logger.debug('等待核心就绪…（${i + 1}/$maxRetries）- IPC 尚未就绪');
          } else {
            Logger.debug('等待核心就绪…（${i + 1}/$maxRetries）- 错误: $e');
          }
        }
      }

      await Future.delayed(retryInterval);
    }

    final totalTime =
        (maxRetries *
                (timeout.inMilliseconds + retryInterval.inMilliseconds) /
                1000)
            .toStringAsFixed(1);

    Logger.error('核心等待超时，最后一次错误: $lastError');

    throw TimeoutException('核心在 $totalTime 秒后仍未就绪。最后错误: $lastError');
  }

  @override
  Future<Map<String, dynamic>> getProxies() async {
    try {
      final data = await _get('/proxies');
      return data['proxies'] ?? {};
    } catch (e) {
      Logger.error('获取代理列表出错：$e');
      rethrow;
    }
  }

  @override
  Future<bool> changeProxy(String groupName, String proxyName) async {
    try {
      // URL 编码处理中文和特殊字符
      final encodedGroupName = Uri.encodeComponent(groupName);

      await _put('/proxies/$encodedGroupName', {'name': proxyName});
      return true;
    } catch (e) {
      Logger.error('切换代理出错：$e');
      rethrow;
    }
  }

  @override
  Future<int> testProxyDelay(
    String proxyName, {
    String? testUrl,
    int? timeoutMs,
  }) async {
    final url = testUrl ?? ClashDefaults.defaultTestUrl;
    final timeout = timeoutMs ?? ClashDefaults.proxyDelayTestTimeout;
    try {
      final encodedProxyName = Uri.encodeComponent(proxyName);

      Logger.debug('开始测试代理延迟：$proxyName');

      final data = await _get(
        '/proxies/$encodedProxyName/delay?timeout=$timeout&url=$url',
      );

      final delay = data['delay'] ?? -1;

      if (delay > 0) {
        Logger.info('代理延迟测试：$proxyName - ${delay}ms');
      } else {
        Logger.warning('代理延迟测试失败：$proxyName - 超时');
      }

      return delay;
    } catch (e) {
      Logger.debug('测试代理延迟出错：$e');
      return -1;
    }
  }

  // 清除 getConfig() 缓存
  void _clearConfigCache() {
    _configCache = null;
    _configCachedAt = null;
  }

  // 实际执行获取配置请求
  Future<Map<String, dynamic>> _fetchConfig() async {
    try {
      final data = await _get('/configs');
      return data;
    } catch (e) {
      Logger.error('获取配置出错：$e');
      rethrow;
    }
  }

  @override
  Future<Map<String, dynamic>> getConfig() async {
    final now = DateTime.now();

    // 1. 短期缓存（1 秒内复用，避免频繁请求）
    if (_configCache != null &&
        _configCachedAt != null &&
        now.difference(_configCachedAt!) < _configCacheDuration) {
      return _configCache!;
    }

    // 2. 请求合并（避免并发重复请求）
    if (_configPendingRequest != null) {
      return await _configPendingRequest!;
    }

    // 3. 发起新请求并缓存结果
    _configPendingRequest = _fetchConfig();
    try {
      final result = await _configPendingRequest!;
      _configCache = result;
      _configCachedAt = now;
      return result;
    } finally {
      _configPendingRequest = null;
    }
  }

  @override
  Future<bool> updateConfig(Map<String, dynamic> config) async {
    try {
      await _patch('/configs', config);
      _clearConfigCache();
      return true;
    } catch (e) {
      Logger.error('更新配置出错：$e');
      rethrow;
    }
  }

  @override
  Future<bool> reloadConfig({
    String? configPath,
    String? configContent,
    bool force = true,
  }) async {
    try {
      final path = force ? '/configs?force=true' : '/configs';

      // 兼容两种重载方式：优先使用 payload，其次使用 path。
      // 注：body 为空时等价于让核心使用默认配置路径。
      final body = <String, dynamic>{};
      if (configContent != null && configContent.isNotEmpty) {
        body['payload'] = configContent;
      } else if (configPath != null && configPath.isNotEmpty) {
        body['path'] = configPath;
      }

      await _put(path, body);
      Logger.info('配置文件重载成功');
      _clearConfigCache();
      return true;
    } catch (e) {
      Logger.error('配置重载出错：$e');
      rethrow;
    }
  }

  @override
  Future<bool> setAllowLan(bool allow) async {
    try {
      await _patch('/configs', {'allow-lan': allow});
      _clearConfigCache();
      return true;
    } catch (e) {
      Logger.error('设置局域网代理出错：$e');
      rethrow;
    }
  }

  @override
  Future<bool> setLanAuthentication(List<String> authentication) async {
    try {
      await _patch('/configs', {'authentication': authentication});
      _clearConfigCache();
      return true;
    } catch (e) {
      Logger.error('设置局域网认证出错：$e');
      rethrow;
    }
  }

  @override
  Future<bool> setIpv6(bool enable) async {
    try {
      await _patch('/configs', {'ipv6': enable});
      _clearConfigCache();
      return true;
    } catch (e) {
      Logger.error('设置 IPv6 出错：$e');
      rethrow;
    }
  }

  @override
  Future<bool> setTcpConcurrent(bool enable) async {
    try {
      await _patch('/configs', {'tcp-concurrent': enable});
      _clearConfigCache();
      return true;
    } catch (e) {
      Logger.error('设置 TCP 并发出错：$e');
      rethrow;
    }
  }

  @override
  Future<bool> setUnifiedDelay(bool enable) async {
    try {
      await _patch('/configs', {'unified-delay': enable});
      _clearConfigCache();
      return true;
    } catch (e) {
      Logger.error('设置统一延迟出错：$e');
      rethrow;
    }
  }

  @override
  Future<bool> setGeodataLoader(String mode) async {
    try {
      await _patch('/configs', {'geodata-loader': mode});
      _clearConfigCache();
      return true;
    } catch (e) {
      Logger.error('设置 GEO 数据加载模式出错：$e');
      rethrow;
    }
  }

  @override
  Future<bool> setFindProcessMode(String mode) async {
    try {
      await _patch('/configs', {'find-process-mode': mode});
      _clearConfigCache();
      return true;
    } catch (e) {
      Logger.error('设置查找进程模式出错：$e');
      rethrow;
    }
  }

  @override
  Future<bool> setLogLevel(String level) async {
    try {
      const validLevels = ['debug', 'info', 'warning', 'error', 'silent'];
      if (!validLevels.contains(level)) {
        throw ArgumentError('无效的日志等级：$level');
      }

      await _patch('/configs', {'log-level': level});
      _clearConfigCache();
      return true;
    } catch (e) {
      Logger.error('设置日志等级出错：$e');
      rethrow;
    }
  }

  @override
  Future<String> getMode() async {
    try {
      final config = await getConfig();
      return config['mode'] ?? 'rule';
    } catch (e) {
      Logger.error('获取出站模式出错：$e');
      return 'rule';
    }
  }

  @override
  Future<bool> setMode(String mode) async {
    try {
      const validModes = ['rule', 'global', 'direct'];
      if (!validModes.contains(mode)) {
        throw ArgumentError('无效的出站模式：$mode');
      }

      await _patch('/configs', {'mode': mode});
      Logger.info('出站模式已设置：$mode');
      return true;
    } catch (e) {
      Logger.error('设置出站模式出错：$e');
      rethrow;
    }
  }

  @override
  Future<bool> setExternalController(String? address) async {
    try {
      await _patch('/configs', {'external-controller': address ?? ''});
      _clearConfigCache();
      return true;
    } catch (e) {
      Logger.error('设置外部控制器出错：$e');
      rethrow;
    }
  }

  @override
  Future<bool> setMixedPort(int port) async {
    try {
      await _patch('/configs', {'mixed-port': port});
      _clearConfigCache();
      return true;
    } catch (e) {
      Logger.error('设置混合端口出错：$e');
      rethrow;
    }
  }

  @override
  Future<bool> setSocksPort(int port) async {
    try {
      await _patch('/configs', {'socks-port': port});
      _clearConfigCache();
      return true;
    } catch (e) {
      Logger.error('设置 SOCKS 端口出错：$e');
      rethrow;
    }
  }

  @override
  Future<bool> setHttpPort(int port) async {
    try {
      await _patch('/configs', {'port': port});
      _clearConfigCache();
      return true;
    } catch (e) {
      Logger.error('设置 HTTP 端口出错：$e');
      rethrow;
    }
  }

  @override
  Future<bool> setTunEnable(bool enable) async {
    try {
      await _patch('/configs', {
        'tun': {'enable': enable},
      });
      _clearConfigCache();
      return true;
    } catch (e) {
      Logger.error('设置虚拟网卡模式出错：$e');
      rethrow;
    }
  }

  @override
  Future<bool> setTunStack(String stack) async {
    try {
      await _patch('/configs', _buildTunPatch({'stack': stack}));
      _clearConfigCache();
      return true;
    } catch (e) {
      Logger.error('设置虚拟网卡网络栈出错：$e');
      rethrow;
    }
  }

  @override
  Future<bool> setTunDevice(String device) async {
    try {
      await _patch('/configs', _buildTunPatch({'device': device}));
      _clearConfigCache();
      return true;
    } catch (e) {
      Logger.error('设置虚拟网卡设备名称出错：$e');
      rethrow;
    }
  }

  @override
  Future<bool> setTunAutoRoute(bool enable) async {
    try {
      await _patch('/configs', _buildTunPatch({'auto-route': enable}));
      _clearConfigCache();
      return true;
    } catch (e) {
      Logger.error('设置虚拟网卡自动路由出错：$e');
      rethrow;
    }
  }

  @override
  Future<bool> setTunAutoDetectInterface(bool enable) async {
    try {
      await _patch(
        '/configs',
        _buildTunPatch({'auto-detect-interface': enable}),
      );
      _clearConfigCache();
      return true;
    } catch (e) {
      Logger.error('设置虚拟网卡自动检测接口出错：$e');
      rethrow;
    }
  }

  @override
  Future<bool> setTunDnsHijack(List<String> hijackList) async {
    try {
      await _patch('/configs', _buildTunPatch({'dns-hijack': hijackList}));
      _clearConfigCache();
      return true;
    } catch (e) {
      Logger.error('设置虚拟网卡 DNS 劫持出错：$e');
      rethrow;
    }
  }

  @override
  Future<bool> setTunMtu(int mtu) async {
    try {
      await _patch('/configs', _buildTunPatch({'mtu': mtu}));
      _clearConfigCache();
      return true;
    } catch (e) {
      Logger.error('设置虚拟网卡 MTU 出错：$e');
      rethrow;
    }
  }

  @override
  Future<bool> setTunStrictRoute(bool enable) async {
    try {
      await _patch('/configs', _buildTunPatch({'strict-route': enable}));
      _clearConfigCache();
      return true;
    } catch (e) {
      Logger.error('设置虚拟网卡严格路由出错：$e');
      rethrow;
    }
  }

  @override
  Future<bool> setTunAutoRedirect(bool enable) async {
    try {
      await _patch('/configs', _buildTunPatch({'auto-redirect': enable}));
      _clearConfigCache();
      return true;
    } catch (e) {
      Logger.error('设置虚拟网卡自动 TCP 重定向出错：$e');
      rethrow;
    }
  }

  @override
  Future<bool> setTunRouteExcludeAddress(List<String> addresses) async {
    try {
      await _patch(
        '/configs',
        _buildTunPatch({'route-exclude-address': addresses}),
      );
      _clearConfigCache();
      return true;
    } catch (e) {
      Logger.error('设置虚拟网卡排除网段列表出错：$e');
      rethrow;
    }
  }

  @override
  Future<bool> setTunDisableIcmpForwarding(bool disabled) async {
    try {
      await _patch(
        '/configs',
        _buildTunPatch({'disable-icmp-forwarding': disabled}),
      );
      _clearConfigCache();
      return true;
    } catch (e) {
      Logger.error('设置虚拟网卡 ICMP 转发出错：$e');
      rethrow;
    }
  }

  @override
  Future<List<ConnectionInfo>> getConnections() async {
    try {
      final data = await _get('/connections');
      final connections = data['connections'] as List<dynamic>? ?? [];
      return connections
          .map((conn) => ConnectionInfo.fromJson(conn as Map<String, dynamic>))
          .toList();
    } catch (e) {
      Logger.error('获取连接列表出错：$e');
      return [];
    }
  }

  @override
  Future<bool> closeConnection(String connectionId) async {
    try {
      await _delete('/connections/$connectionId');
      return true;
    } catch (e) {
      Logger.error('关闭连接出错：$e');
      return false;
    }
  }

  @override
  Future<bool> closeAllConnections() async {
    try {
      await _delete('/connections');
      return true;
    } catch (e) {
      Logger.error('关闭所有连接出错：$e');
      return false;
    }
  }

  @override
  Future<List<RuleItem>> getRules() async {
    try {
      final data = await _get('/rules');
      final rules = (data['rules'] as List?) ?? const [];
      return rules
          .whereType<Map<String, dynamic>>()
          .map(RuleItem.fromJson)
          .toList(growable: false);
    } catch (e) {
      Logger.error('获取规则列表出错：$e');
      return [];
    }
  }

  @override
  Future<Map<String, dynamic>> getProviders() async {
    try {
      final data = await _get('/providers/proxies');
      return data['providers'] ?? {};
    } catch (e) {
      Logger.error('获取 Providers 出错：$e');
      return {};
    }
  }

  @override
  Future<Map<String, dynamic>?> getProvider(String providerName) async {
    try {
      final encodedName = Uri.encodeComponent(providerName);
      final data = await _get('/providers/proxies/$encodedName');
      return data;
    } catch (e) {
      Logger.error('获取 Provider 出错：$e');
      return null;
    }
  }

  @override
  Future<bool> updateProvider(String providerName) async {
    try {
      final encodedName = Uri.encodeComponent(providerName);
      await _put('/providers/proxies/$encodedName', {});
      Logger.info('Provider 已更新：$providerName');
      return true;
    } catch (e) {
      Logger.error('更新 Provider 出错：$e');
      return false;
    }
  }

  @override
  Future<bool> healthCheckProvider(String providerName) async {
    try {
      final encodedName = Uri.encodeComponent(providerName);
      await _get('/providers/proxies/$encodedName/healthcheck');
      Logger.info('Provider 健康检查完成：$providerName');
      return true;
    } catch (e) {
      Logger.error('Provider 健康检查出错：$e');
      return false;
    }
  }

  @override
  Future<Map<String, dynamic>> getRuleProviders() async {
    try {
      final data = await _get('/providers/rules');
      return data['providers'] ?? {};
    } catch (e) {
      Logger.error('获取规则 Providers 出错：$e');
      return {};
    }
  }

  @override
  Future<Map<String, dynamic>?> getRuleProvider(String providerName) async {
    try {
      final encodedName = Uri.encodeComponent(providerName);
      final data = await _get('/providers/rules/$encodedName');
      return data;
    } catch (e) {
      Logger.error('获取规则 Provider 出错：$e');
      return null;
    }
  }

  @override
  Future<bool> updateRuleProvider(String providerName) async {
    try {
      final encodedName = Uri.encodeComponent(providerName);
      await _put('/providers/rules/$encodedName', {});
      Logger.info('规则 Provider 已更新：$providerName');
      return true;
    } catch (e) {
      Logger.error('更新规则 Provider 出错：$e');
      return false;
    }
  }
}

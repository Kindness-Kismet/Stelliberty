import 'dart:async';
import 'package:stelliberty/utils/logger.dart';
import 'package:stelliberty/clash/config/clash_defaults.dart';
import 'package:stelliberty/clash/data/connection_model.dart';
import 'package:stelliberty/clash/network/ipc_request_helper.dart';

// Clash RESTful API 客户端
//
// 使用 IPC 模式通信
class ClashApiClient {
  ClashApiClient();

  // getConfig() 缓存机制（优化并发请求）
  Map<String, dynamic>? _configCache;
  DateTime? _cacheTime;
  Future<Map<String, dynamic>>? _pendingRequest;
  static const _cacheDuration = Duration(seconds: 1);

  // 内部 GET 请求（IPC 模式）
  Future<Map<String, dynamic>> _internalGet(String path) async {
    return await IpcRequestHelper.instance.get(path);
  }

  // 内部 PATCH 请求（IPC 模式）
  Future<bool> _internalPatch(String path, Map<String, dynamic> body) async {
    await IpcRequestHelper.instance.patch(path, body: body);
    return true;
  }

  // 内部 PUT 请求（IPC 模式）
  Future<bool> _internalPut(String path, Map<String, dynamic> body) async {
    await IpcRequestHelper.instance.put(path, body: body);
    return true;
  }

  // 内部 DELETE 请求（IPC 模式）
  Future<bool> _internalDelete(String path) async {
    await IpcRequestHelper.instance.delete(path);
    return true;
  }

  // 检查 API 是否就绪（用于健康检查）
  Future<bool> checkHealth({
    Duration timeout = const Duration(
      milliseconds: ClashDefaults.apiReadyCheckTimeout,
    ),
  }) async {
    try {
      await _internalGet('/version');
      return true;
    } catch (e) {
      return false;
    }
  }

  // 获取 Clash 版本信息
  Future<String> getVersion() async {
    try {
      final data = await _internalGet('/version');
      // Mihomo 返回格式: {"meta":true,"premium":true,"version":"Mihomo 1.18.1"}
      return data['version'] ?? 'Unknown';
    } catch (e) {
      Logger.error('获取版本信息出错：$e');
      return 'Unknown';
    }
  }

  // 等待 API 就绪
  Future<void> waitForReady({
    int maxRetries = ClashDefaults.apiReadyMaxRetries,
    Duration retryInterval = const Duration(
      milliseconds: ClashDefaults.apiReadyRetryInterval,
    ),
    Duration checkTimeout = const Duration(
      milliseconds: ClashDefaults.apiReadyCheckTimeout,
    ),
  }) async {
    Object? lastError;

    for (int i = 0; i < maxRetries; i++) {
      try {
        await _internalGet('/version').timeout(checkTimeout);
        return;
      } catch (e) {
        lastError = e;
        // 简化日志：只在第 1 次、每 5 次、最后 3 次打印
        final shouldLog = i == 0 || (i + 1) % 5 == 0 || i >= maxRetries - 3;
        if (shouldLog) {
          Logger.debug('等待 Clash API 就绪…（${i + 1}/$maxRetries）- 错误: $e');
        }
      }

      await Future.delayed(retryInterval);
    }

    final totalTime =
        (maxRetries *
                (checkTimeout.inMilliseconds + retryInterval.inMilliseconds) /
                1000)
            .toStringAsFixed(1);

    Logger.error('Clash API 等待超时，最后一次错误: $lastError');

    throw TimeoutException('Clash API 在 $totalTime 秒后仍未就绪。最后错误: $lastError');
  }

  // 获取代理列表
  Future<Map<String, dynamic>> getProxies() async {
    try {
      final data = await _internalGet('/proxies');
      return data['proxies'] ?? {};
    } catch (e) {
      Logger.error('获取代理列表出错：$e');
      rethrow;
    }
  }

  // 切换代理节点
  Future<bool> changeProxy(String groupName, String proxyName) async {
    try {
      // URL 编码处理中文和特殊字符
      final encodedGroupName = Uri.encodeComponent(groupName);

      await _internalPut('/proxies/$encodedGroupName', {'name': proxyName});
      return true;
    } catch (e) {
      Logger.error('切换代理出错：$e');
      rethrow;
    }
  }

  // 测试代理延迟
  Future<int> testProxyDelay(
    String proxyName, {
    String testUrl = ClashDefaults.defaultTestUrl,
    int timeoutMs = ClashDefaults.proxyDelayTestTimeout,
  }) async {
    try {
      // URL 编码
      final encodedProxyName = Uri.encodeComponent(proxyName);

      Logger.debug('开始测试代理延迟：$proxyName');

      final data = await _internalGet(
        '/proxies/$encodedProxyName/delay?timeout=$timeoutMs&url=$testUrl',
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

  // 清除 getConfig() 缓存（在配置修改后调用）
  void _clearConfigCache() {
    _configCache = null;
    _cacheTime = null;
  }

  // 实际执行 HTTP 请求获取配置（内部方法）
  Future<Map<String, dynamic>> _fetchConfigInternal() async {
    try {
      final data = await _internalGet('/configs');
      return data;
    } catch (e) {
      Logger.error('获取配置出错：$e');
      rethrow;
    }
  }

  // 获取 Clash 配置
  Future<Map<String, dynamic>> getConfig() async {
    final now = DateTime.now();

    // 1. 短期缓存（1秒内复用，避免频繁请求）
    if (_configCache != null &&
        _cacheTime != null &&
        now.difference(_cacheTime!) < _cacheDuration) {
      return _configCache!;
    }

    // 2. 请求合并（避免并发重复请求）
    if (_pendingRequest != null) {
      return await _pendingRequest!;
    }

    // 3. 发起新请求并缓存结果
    _pendingRequest = _fetchConfigInternal();
    try {
      final result = await _pendingRequest!;
      _configCache = result;
      _cacheTime = now;
      return result;
    } finally {
      _pendingRequest = null;
    }
  }

  // 更新 Clash 配置
  Future<bool> updateConfig(Map<String, dynamic> config) async {
    try {
      await _internalPatch('/configs', config);
      // 配置已修改，清除缓存
      _clearConfigCache();
      return true;
    } catch (e) {
      Logger.error('更新配置出错：$e');
      rethrow;
    }
  }

  // 重载配置文件（不重启进程）
  // [configPath] 配置文件路径
  // [force] 是否强制重载
  Future<bool> reloadConfig({String? configPath, bool force = true}) async {
    try {
      final path = force ? '/configs?force=true' : '/configs';

      final body = configPath != null
          ? <String, dynamic>{'path': configPath}
          : <String, dynamic>{};

      await _internalPut(path, body);
      Logger.info('配置文件重载成功');
      // 配置已修改，清除缓存
      _clearConfigCache();
      return true;
    } catch (e) {
      Logger.error('配置重载出错：$e');
      rethrow;
    }
  }

  // 设置局域网代理开关
  Future<bool> setAllowLan(bool allow) async {
    try {
      await _internalPatch('/configs', {'allow-lan': allow});
      // 配置已修改，清除缓存
      _clearConfigCache();
      return true;
    } catch (e) {
      Logger.error('设置局域网代理出错：$e');
      rethrow;
    }
  }

  // 设置 IPv6 开关
  Future<bool> setIpv6(bool enable) async {
    try {
      await _internalPatch('/configs', {'ipv6': enable});
      // 配置已修改，清除缓存
      _clearConfigCache();
      return true;
    } catch (e) {
      Logger.error('设置 IPv6 出错：$e');
      rethrow;
    }
  }

  // 获取局域网代理状态
  Future<bool> getAllowLan() async {
    try {
      final config = await getConfig();
      return config['allow-lan'] ?? false;
    } catch (e) {
      Logger.error('获取局域网代理状态出错：$e');
      return false;
    }
  }

  // 获取 IPv6 状态
  Future<bool> getIpv6() async {
    try {
      final config = await getConfig();
      return config['ipv6'] ?? false;
    } catch (e) {
      Logger.error('获取 IPv6 状态出错：$e');
      return false;
    }
  }

  // 设置 TCP 并发开关
  Future<bool> setTcpConcurrent(bool enable) async {
    try {
      await _internalPatch('/configs', {'tcp-concurrent': enable});
      // 配置已修改，清除缓存
      _clearConfigCache();
      return true;
    } catch (e) {
      Logger.error('设置 TCP 并发出错：$e');
      rethrow;
    }
  }

  // 获取 TCP 并发状态
  Future<bool> getTcpConcurrent() async {
    try {
      final config = await getConfig();
      return config['tcp-concurrent'] ?? true;
    } catch (e) {
      Logger.error('获取 TCP 并发状态出错：$e');
      return true;
    }
  }

  // 设置统一延迟
  Future<bool> setUnifiedDelay(bool enable) async {
    try {
      await _internalPatch('/configs', {'unified-delay': enable});
      // 配置已修改，清除缓存
      _clearConfigCache();
      return true;
    } catch (e) {
      Logger.error('设置统一延迟出错：$e');
      rethrow;
    }
  }

  // 获取统一延迟状态
  Future<bool> getUnifiedDelay() async {
    try {
      final config = await getConfig();
      return config['unified-delay'] ?? true;
    } catch (e) {
      Logger.error('获取统一延迟状态出错：$e');
      return true;
    }
  }

  // 设置 GEO 数据加载模式
  Future<bool> setGeodataLoader(String mode) async {
    try {
      await _internalPatch('/configs', {'geodata-loader': mode});
      // 配置已修改，清除缓存
      _clearConfigCache();
      return true;
    } catch (e) {
      Logger.error('设置 GEO 数据加载模式出错：$e');
      rethrow;
    }
  }

  // 获取 GEO 数据加载模式
  Future<String> getGeodataLoader() async {
    try {
      final config = await getConfig();
      return config['geodata-loader'] ?? 'memconservative';
    } catch (e) {
      Logger.error('获取 GEO 数据加载模式出错：$e');
      return 'memconservative';
    }
  }

  // 设置查找进程模式
  Future<bool> setFindProcessMode(String mode) async {
    try {
      await _internalPatch('/configs', {'find-process-mode': mode});
      // 配置已修改，清除缓存
      _clearConfigCache();
      return true;
    } catch (e) {
      Logger.error('设置查找进程模式出错：$e');
      rethrow;
    }
  }

  // 获取查找进程模式
  Future<String> getFindProcessMode() async {
    try {
      final config = await getConfig();
      return config['find-process-mode'] ?? 'off';
    } catch (e) {
      Logger.error('获取查找进程模式出错：$e');
      return 'off';
    }
  }

  // 设置日志等级
  Future<bool> setLogLevel(String level) async {
    try {
      // 验证日志等级
      const validLevels = ['debug', 'info', 'warning', 'error', 'silent'];
      if (!validLevels.contains(level)) {
        throw ArgumentError('无效的日志等级：$level');
      }

      await _internalPatch('/configs', {'log-level': level});
      // 配置已修改，清除缓存
      _clearConfigCache();
      return true;
    } catch (e) {
      Logger.error('设置日志等级出错：$e');
      rethrow;
    }
  }

  // 获取出站模式
  Future<String> getMode() async {
    try {
      final config = await getConfig();
      return config['mode'] ?? 'rule';
    } catch (e) {
      Logger.error('获取出站模式出错：$e');
      return 'rule';
    }
  }

  // 设置出站模式
  // mode: 'rule' | 'global' | 'direct'
  Future<bool> setMode(String mode) async {
    try {
      // 验证出站模式
      const validModes = ['rule', 'global', 'direct'];
      if (!validModes.contains(mode)) {
        throw ArgumentError('无效的出站模式：$mode');
      }

      await _internalPatch('/configs', {'mode': mode});
      Logger.info('出站模式（支持配置重载）：$mode');
      return true;
    } catch (e) {
      Logger.error('设置出站模式出错：$e');
      rethrow;
    }
  }

  // 获取日志等级
  Future<String> getLogLevel() async {
    try {
      final config = await getConfig();
      return config['log-level'] ?? 'info';
    } catch (e) {
      Logger.error('获取日志等级出错：$e');
      return 'info';
    }
  }

  // 设置外部控制器
  Future<bool> setExternalController(String? address) async {
    try {
      await _internalPatch('/configs', {'external-controller': address ?? ''});
      // 配置已修改，清除缓存
      _clearConfigCache();
      return true;
    } catch (e) {
      Logger.error('设置外部控制器出错：$e');
      rethrow;
    }
  }

  // 获取外部控制器状态
  Future<String?> getExternalController() async {
    try {
      final config = await getConfig();
      final controller = config['external-controller'];
      return (controller == null || controller == '') ? null : controller;
    } catch (e) {
      Logger.error('获取外部控制器状态出错：$e');
      return null;
    }
  }

  // 设置混合端口（配置重载，无需重启）
  Future<bool> setMixedPort(int port) async {
    try {
      await _internalPatch('/configs', {'mixed-port': port});
      // 配置已修改，清除缓存
      _clearConfigCache();
      return true;
    } catch (e) {
      Logger.error('设置混合端口出错：$e');
      rethrow;
    }
  }

  // 设置 SOCKS 端口（配置重载，无需重启）
  Future<bool> setSocksPort(int port) async {
    try {
      await _internalPatch('/configs', {'socks-port': port});
      // 配置已修改，清除缓存
      _clearConfigCache();
      return true;
    } catch (e) {
      Logger.error('设置 SOCKS 端口出错：$e');
      rethrow;
    }
  }

  // 设置 HTTP 端口（配置重载，无需重启）
  Future<bool> setHttpPort(int port) async {
    try {
      await _internalPatch('/configs', {'port': port});
      // 配置已修改，清除缓存
      _clearConfigCache();
      return true;
    } catch (e) {
      Logger.error('设置 HTTP 端口出错：$e');
      rethrow;
    }
  }

  // 设置虚拟网卡模式启用状态（配置重载，无需重启）
  Future<bool> setTunEnable(bool enable) async {
    try {
      await _internalPatch('/configs', {
        'tun': {'enable': enable},
      });
      // 配置已修改，清除缓存
      _clearConfigCache();
      return true;
    } catch (e) {
      Logger.error('设置虚拟网卡模式出错：$e');
      rethrow;
    }
  }

  // 设置虚拟网卡网络栈（配置重载，无需重启）
  Future<bool> setTunStack(String stack) async {
    try {
      await _internalPatch('/configs', {
        'tun': {'stack': stack},
      });
      // 配置已修改，清除缓存
      _clearConfigCache();
      return true;
    } catch (e) {
      Logger.error('设置虚拟网卡网络栈出错：$e');
      rethrow;
    }
  }

  // 设置 TCP Keep-Alive 间隔（配置重载，无需重启）
  Future<bool> setKeepAliveInterval(int interval) async {
    try {
      await _internalPatch('/configs', {'keep-alive-interval': interval});
      // 配置已修改，清除缓存
      _clearConfigCache();
      return true;
    } catch (e) {
      Logger.error('设置 Keep-Alive 间隔出错：$e');
      rethrow;
    }
  }

  // 设置虚拟网卡设备名称（配置重载，无需重启）
  Future<bool> setTunDevice(String device) async {
    try {
      await _internalPatch('/configs', {
        'tun': {'device': device},
      });
      // 配置已修改，清除缓存
      _clearConfigCache();
      return true;
    } catch (e) {
      Logger.error('设置虚拟网卡设备名称出错：$e');
      rethrow;
    }
  }

  // 设置虚拟网卡自动路由（配置重载，无需重启）
  Future<bool> setTunAutoRoute(bool enable) async {
    try {
      await _internalPatch('/configs', {
        'tun': {'auto-route': enable},
      });
      // 配置已修改，清除缓存
      _clearConfigCache();
      return true;
    } catch (e) {
      Logger.error('设置虚拟网卡自动路由出错：$e');
      rethrow;
    }
  }

  // 设置虚拟网卡自动检测接口（配置重载，无需重启）
  Future<bool> setTunAutoDetectInterface(bool enable) async {
    try {
      await _internalPatch('/configs', {
        'tun': {'auto-detect-interface': enable},
      });
      // 配置已修改，清除缓存
      _clearConfigCache();
      return true;
    } catch (e) {
      Logger.error('设置虚拟网卡自动检测接口出错：$e');
      rethrow;
    }
  }

  // 设置虚拟网卡 DNS 劫持列表（配置重载，无需重启）
  Future<bool> setTunDnsHijack(List<String> hijackList) async {
    try {
      await _internalPatch('/configs', {
        'tun': {'dns-hijack': hijackList},
      });
      // 配置已修改，清除缓存
      _clearConfigCache();
      return true;
    } catch (e) {
      Logger.error('设置虚拟网卡 DNS 劫持出错：$e');
      rethrow;
    }
  }

  // 设置虚拟网卡 MTU（配置重载，无需重启）
  Future<bool> setTunMtu(int mtu) async {
    try {
      await _internalPatch('/configs', {
        'tun': {'mtu': mtu},
      });
      // 配置已修改，清除缓存
      _clearConfigCache();
      return true;
    } catch (e) {
      Logger.error('设置虚拟网卡 MTU 出错：$e');
      rethrow;
    }
  }

  // 设置虚拟网卡严格路由（配置重载，无需重启）
  Future<bool> setTunStrictRoute(bool enable) async {
    try {
      await _internalPatch('/configs', {
        'tun': {'strict-route': enable},
      });
      // 配置已修改，清除缓存
      _clearConfigCache();
      return true;
    } catch (e) {
      Logger.error('设置虚拟网卡严格路由出错：$e');
      rethrow;
    }
  }

  // 设置虚拟网卡自动TCP重定向（配置重载，无需重启）
  Future<bool> setTunAutoRedirect(bool enable) async {
    try {
      await _internalPatch('/configs', {
        'tun': {'auto-redirect': enable},
      });
      // 配置已修改，清除缓存
      _clearConfigCache();
      return true;
    } catch (e) {
      Logger.error('设置虚拟网卡自动 TCP 重定向出错：$e');
      rethrow;
    }
  }

  // 设置虚拟网卡排除网段列表（配置重载，无需重启）
  Future<bool> setTunRouteExcludeAddress(List<String> addresses) async {
    try {
      await _internalPatch('/configs', {
        'tun': {'route-exclude-address': addresses},
      });
      // 配置已修改，清除缓存
      _clearConfigCache();
      return true;
    } catch (e) {
      Logger.error('设置虚拟网卡排除网段列表出错：$e');
      rethrow;
    }
  }

  // 设置虚拟网卡禁用ICMP转发（配置重载，无需重启）
  Future<bool> setTunDisableIcmpForwarding(bool disabled) async {
    try {
      await _internalPatch('/configs', {
        'tun': {'disable-icmp-forwarding': disabled},
      });
      // 配置已修改，清除缓存
      _clearConfigCache();
      return true;
    } catch (e) {
      Logger.error('设置虚拟网卡 ICMP 转发出错：$e');
      rethrow;
    }
  }

  // 获取当前所有连接
  Future<List<ConnectionInfo>> getConnections() async {
    try {
      final data = await _internalGet('/connections');
      final connections = data['connections'] as List<dynamic>? ?? [];
      return connections
          .map((conn) => ConnectionInfo.fromJson(conn as Map<String, dynamic>))
          .toList();
    } catch (e) {
      Logger.error('获取连接列表出错：$e');
      return [];
    }
  }

  // 关闭指定连接
  Future<bool> closeConnection(String connectionId) async {
    try {
      await _internalDelete('/connections/$connectionId');
      return true;
    } catch (e) {
      Logger.error('关闭连接出错：$e');
      return false;
    }
  }

  // 关闭所有连接
  Future<bool> closeAllConnections() async {
    try {
      await _internalDelete('/connections');
      return true;
    } catch (e) {
      Logger.error('关闭所有连接出错：$e');
      return false;
    }
  }

  // 获取所有 Providers
  Future<Map<String, dynamic>> getProviders() async {
    try {
      final data = await _internalGet('/providers/proxies');
      return data['providers'] ?? {};
    } catch (e) {
      Logger.error('获取 Providers 出错：$e');
      return {};
    }
  }

  // 获取单个 Provider
  Future<Map<String, dynamic>?> getProvider(String providerName) async {
    try {
      final encodedName = Uri.encodeComponent(providerName);
      final data = await _internalGet('/providers/proxies/$encodedName');
      return data;
    } catch (e) {
      Logger.error('获取 Provider 出错：$e');
      return null;
    }
  }

  // 更新 Provider（从远程URL同步）
  Future<bool> updateProvider(String providerName) async {
    try {
      final encodedName = Uri.encodeComponent(providerName);
      await _internalPut('/providers/proxies/$encodedName', {});
      Logger.info('Provider 已更新：$providerName');
      return true;
    } catch (e) {
      Logger.error('更新 Provider 出错：$e');
      return false;
    }
  }

  // 健康检查 Provider
  Future<bool> healthCheckProvider(String providerName) async {
    try {
      final encodedName = Uri.encodeComponent(providerName);
      await _internalGet('/providers/proxies/$encodedName/healthcheck');
      Logger.info('Provider 健康检查完成：$providerName');
      return true;
    } catch (e) {
      Logger.error('Provider 健康检查出错：$e');
      return false;
    }
  }

  // 获取所有规则 Providers
  Future<Map<String, dynamic>> getRuleProviders() async {
    try {
      final data = await _internalGet('/providers/rules');
      return data['providers'] ?? {};
    } catch (e) {
      Logger.error('获取规则 Providers 出错：$e');
      return {};
    }
  }

  // 获取单个规则 Provider
  Future<Map<String, dynamic>?> getRuleProvider(String providerName) async {
    try {
      final encodedName = Uri.encodeComponent(providerName);
      final data = await _internalGet('/providers/rules/$encodedName');
      return data;
    } catch (e) {
      Logger.error('获取规则 Provider 出错：$e');
      return null;
    }
  }

  // 更新规则 Provider
  Future<bool> updateRuleProvider(String providerName) async {
    try {
      final encodedName = Uri.encodeComponent(providerName);
      await _internalPut('/providers/rules/$encodedName', {});
      Logger.info('规则 Provider 已更新：$providerName');
      return true;
    } catch (e) {
      Logger.error('更新规则 Provider 出错：$e');
      return false;
    }
  }
}

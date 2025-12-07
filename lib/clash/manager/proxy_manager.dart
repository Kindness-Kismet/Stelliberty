import 'package:stelliberty/clash/network/api_client.dart';

// Clash 代理管理器
// 负责代理节点的切换、延迟测试
class ProxyManager {
  final ClashApiClient _apiClient;
  final bool Function() _isCoreRunning;
  final String Function() _getTestUrl;

  ProxyManager({
    required ClashApiClient apiClient,
    required bool Function() isCoreRunning,
    required String Function() getTestUrl,
  }) : _apiClient = apiClient,
       _isCoreRunning = isCoreRunning,
       _getTestUrl = getTestUrl;

  // 获取代理列表
  Future<Map<String, dynamic>> getProxies() async {
    if (!_isCoreRunning()) {
      throw Exception('Clash 未在运行');
    }

    return await _apiClient.getProxies();
  }

  // 切换代理节点
  Future<bool> changeProxy(String groupName, String proxyName) async {
    if (!_isCoreRunning()) {
      throw Exception('Clash 未在运行');
    }

    final success = await _apiClient.changeProxy(groupName, proxyName);

    // 切换节点后关闭所有现有连接，确保立即生效
    // 避免旧连接继续使用之前的节点造成混合状态
    if (success) {
      await _apiClient.closeAllConnections();
    }

    return success;
  }

  // 测试代理延迟
  Future<int> testProxyDelay(String proxyName, {String? testUrl}) async {
    if (!_isCoreRunning()) {
      throw Exception('Clash 未在运行');
    }

    return await _apiClient.testProxyDelay(
      proxyName,
      testUrl: testUrl ?? _getTestUrl(),
    );
  }
}

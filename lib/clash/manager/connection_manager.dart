import 'package:stelliberty/clash/network/api_client.dart';
import 'package:stelliberty/clash/model/connection_model.dart';
import 'package:stelliberty/services/log_print_service.dart';

// Clash 连接管理器
// 负责连接的查询和关闭
class ConnectionManager {
  final ClashApiClient _apiClient;
  final bool Function() _isCoreRunning;

  ConnectionManager({
    required ClashApiClient apiClient,
    required bool Function() isCoreRunning,
  }) : _apiClient = apiClient,
       _isCoreRunning = isCoreRunning;

  // 获取当前所有连接
  Future<List<ConnectionInfo>> getConnections() async {
    if (!_isCoreRunning()) {
      Logger.warning('Clash 未运行，无法获取连接列表');
      return [];
    }

    try {
      return await _apiClient.getConnections();
    } catch (e) {
      Logger.error('获取连接列表失败：$e');
      return [];
    }
  }

  // 关闭指定连接
  Future<bool> closeConnection(String connectionId) async {
    if (!_isCoreRunning()) {
      Logger.warning('Clash 未运行，无法关闭连接');
      return false;
    }

    try {
      return await _apiClient.closeConnection(connectionId);
    } catch (e) {
      Logger.error('关闭连接失败：$e');
      return false;
    }
  }

  // 关闭所有连接
  Future<bool> closeAllConnections() async {
    if (!_isCoreRunning()) {
      Logger.warning('Clash 未运行，无法关闭所有连接');
      return false;
    }

    try {
      return await _apiClient.closeAllConnections();
    } catch (e) {
      Logger.error('关闭所有连接失败：$e');
      return false;
    }
  }
}

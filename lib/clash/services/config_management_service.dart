import 'package:stelliberty/clash/manager/manager.dart';
import 'package:stelliberty/clash/state/config_states.dart';
import 'package:stelliberty/services/log_print_service.dart';

// 配置管理服务
// 封装所有与 ClashManager 配置相关的方法
// 使用服务类模式替代 Mixin，提高代码可读性和可测试性
class ConfigManagementService {
  final ClashManager _clashManager;
  final ConfigState Function() _getConfigState;

  ConfigManagementService(this._clashManager, this._getConfigState);

  // 获取混合端口
  int get mixedPort => _getConfigState().mixedPort;

  // 获取局域网代理状态
  bool getAllowLan() => _getConfigState().isAllowLanEnabled;

  // 获取 IPv6 状态
  bool getIpv6() => _getConfigState().isIpv6Enabled;

  // 获取 TCP 并发状态
  bool getTcpConcurrent() => _getConfigState().isTcpConcurrentEnabled;

  // 获取统一延迟状态
  bool getUnifiedDelay() => _getConfigState().isUnifiedDelayEnabled;

  // 获取 GEO 数据加载模式
  String getGeodataLoader() => _getConfigState().geodataLoader;

  // 获取查找进程模式
  String getFindProcessMode() => _getConfigState().findProcessMode;

  // 获取日志等级
  String getClashCoreLogLevel() => _getConfigState().clashCoreLogLevel;

  // 获取外部控制器状态
  bool getExternalControllerEnabled() =>
      _getConfigState().isExternalControllerEnabled;

  // 获取外部控制器地址
  String? getExternalController() => _getConfigState().externalController;

  // 获取测速链接
  String getTestUrl() => _getConfigState().testUrl;

  // 设置局域网代理状态
  Future<bool> setAllowLan(bool enabled) async {
    try {
      final success = await _clashManager.setAllowLan(enabled);
      if (success) {
        Logger.info('局域网代理状态已更新：${enabled ? "已启用" : "已禁用"}');
      } else {
        Logger.error('更新局域网代理状态失败');
      }
      return success;
    } catch (e) {
      Logger.error('设置局域网代理状态失败：$e');
      return false;
    }
  }

  // 设置 IPv6 状态
  Future<bool> setIpv6(bool enabled) async {
    try {
      final success = await _clashManager.setIpv6(enabled);
      if (success) {
        Logger.info('IPv6 状态已更新：${enabled ? "已启用" : "已禁用"}');
      } else {
        Logger.error('更新 IPv6 状态失败');
      }
      return success;
    } catch (e) {
      Logger.error('设置 IPv6 状态失败：$e');
      return false;
    }
  }

  // 设置 TCP 并发状态
  Future<bool> setTcpConcurrent(bool enabled) async {
    try {
      final success = await _clashManager.setTcpConcurrent(enabled);
      if (success) {
        Logger.info('TCP 并发状态已更新：${enabled ? "已启用" : "已禁用"}');
      } else {
        Logger.error('更新 TCP 并发状态失败');
      }
      return success;
    } catch (e) {
      Logger.error('设置 TCP 并发状态失败：$e');
      return false;
    }
  }

  // 设置统一延迟状态
  Future<bool> setUnifiedDelay(bool enabled) async {
    try {
      final success = await _clashManager.setUnifiedDelay(enabled);
      if (success) {
        Logger.info('统一延迟状态已更新：${enabled ? "已启用（去除握手延迟）" : "已禁用（包含握手延迟）"}');
      } else {
        Logger.error('更新统一延迟状态失败');
      }
      return success;
    } catch (e) {
      Logger.error('设置统一延迟状态失败：$e');
      return false;
    }
  }

  // 设置 GEO 数据加载模式
  Future<bool> setGeodataLoader(String mode) async {
    try {
      final success = await _clashManager.setGeodataLoader(mode);
      if (success) {
        Logger.info('GEO 数据加载模式已更新：$mode');
      } else {
        Logger.error('更新 GEO 数据加载模式失败');
      }
      return success;
    } catch (e) {
      Logger.error('设置 GEO 数据加载模式失败：$e');
      return false;
    }
  }

  // 设置查找进程模式
  Future<bool> setFindProcessMode(String mode) async {
    try {
      final success = await _clashManager.setFindProcessMode(mode);
      if (success) {
        Logger.info('查找进程模式已更新：$mode');
      } else {
        Logger.error('更新查找进程模式失败');
      }
      return success;
    } catch (e) {
      Logger.error('设置查找进程模式失败：$e');
      return false;
    }
  }

  // 设置日志等级
  Future<bool> setClashCoreLogLevel(String level) async {
    try {
      final success = await _clashManager.setClashCoreLogLevel(level);
      if (success) {
        Logger.info('日志等级已更新：$level');
      } else {
        Logger.error('更新日志等级失败');
      }
      return success;
    } catch (e) {
      Logger.error('设置日志等级失败：$e');
      return false;
    }
  }

  // 设置外部控制器
  Future<bool> setExternalController(bool enabled) async {
    try {
      final success = await _clashManager.setExternalController(enabled);
      if (success) {
        Logger.info('外部控制器已${enabled ? "启用" : "禁用"}');
      } else {
        Logger.error('更新外部控制器失败');
      }
      return success;
    } catch (e) {
      Logger.error('设置外部控制器失败：$e');
      return false;
    }
  }

  // 设置 TCP 保持活动
  Future<bool> setKeepAlive(bool enabled) async {
    try {
      final success = await _clashManager.setKeepAlive(enabled);
      if (success) {
        Logger.info('TCP 保持活动已${enabled ? "启用" : "禁用"}');
      } else {
        Logger.error('更新 TCP 保持活动失败');
      }
      return success;
    } catch (e) {
      Logger.error('设置 TCP 保持活动失败：$e');
      return false;
    }
  }

  // 设置测速链接
  Future<bool> setTestUrl(String url) async {
    try {
      final success = await _clashManager.setTestUrl(url);
      if (success) {
        Logger.info('测速链接已更新：$url');
      } else {
        Logger.error('更新测速链接失败');
      }
      return success;
    } catch (e) {
      Logger.error('设置测速链接失败：$e');
      return false;
    }
  }

  // 设置混合端口
  Future<bool> setMixedPort(int port) async {
    try {
      final success = await _clashManager.setMixedPort(port);
      if (success) {
        Logger.info('混合端口已更新：$port');
      } else {
        Logger.error('更新混合端口失败');
      }
      return success;
    } catch (e) {
      Logger.error('设置混合端口失败：$e');
      return false;
    }
  }

  // 设置 SOCKS 端口
  Future<bool> setSocksPort(int? port) async {
    try {
      final success = await _clashManager.setSocksPort(port);
      if (success) {
        Logger.info('SOCKS 端口已更新：${port ?? "未设置"}');
      } else {
        Logger.error('更新 SOCKS 端口失败');
      }
      return success;
    } catch (e) {
      Logger.error('设置 SOCKS 端口失败：$e');
      return false;
    }
  }

  // 设置 HTTP 端口
  Future<bool> setHttpPort(int? port) async {
    try {
      final success = await _clashManager.setHttpPort(port);
      if (success) {
        Logger.info('HTTP 端口已更新：${port ?? "未设置"}');
      } else {
        Logger.error('更新 HTTP 端口失败');
      }
      return success;
    } catch (e) {
      Logger.error('设置 HTTP 端口失败：$e');
      return false;
    }
  }
}

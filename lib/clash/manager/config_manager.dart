import 'dart:async';
import 'package:stelliberty/clash/network/api_client.dart';
import 'package:stelliberty/clash/config/config_injector.dart';
import 'package:stelliberty/clash/config/clash_defaults.dart';
import 'package:stelliberty/storage/clash_preferences.dart';
import 'package:stelliberty/services/log_print_service.dart';
import 'package:stelliberty/src/bindings/signals/signals.dart';

// Clash 配置管理器
// 负责配置的读取、更新、重载（纯业务逻辑，无状态缓存）
class ConfigManager {
  final ClashApiClient _apiClient;
  final bool Function() _isCoreRunning;

  ConfigManager({
    required ClashApiClient apiClient,
    Function()? notifyListeners,
    required bool Function() isCoreRunning,
  }) : _apiClient = apiClient,
       _isCoreRunning = isCoreRunning;

  // 获取配置
  Future<Map<String, dynamic>> getConfig() async {
    if (!_isCoreRunning()) {
      throw Exception('Clash 未在运行');
    }
    return await _apiClient.getConfig();
  }

  // 更新配置
  Future<bool> updateConfig(Map<String, dynamic> config) async {
    if (!_isCoreRunning()) {
      throw Exception('Clash 未在运行');
    }
    return await _apiClient.updateConfig(config);
  }

  // 重载配置文件
  Future<bool> reloadConfig({
    String? configPath,
    List<OverrideConfig> overrides = const [],
  }) async {
    try {
      if (!_isCoreRunning()) {
        Logger.warning('Clash 未运行，无法重载配置');
        return false;
      }

      // 第一次尝试：使用用户配置 + 覆写
      String? runtimeConfigPath = await _generateConfig(configPath, overrides);

      // 如果配置生成失败且有覆写，尝试禁用覆写重新生成
      if (runtimeConfigPath == null && overrides.isNotEmpty) {
        Logger.error('配置生成失败（可能由覆写导致），尝试禁用覆写重新生成');
        runtimeConfigPath = await _generateConfig(configPath, const []);

        if (runtimeConfigPath != null) {
          Logger.info('禁用覆写后配置生成成功');
        }
      }

      // 如果仍然失败，直接返回 false（让上层 SubscriptionProvider 处理回退）
      if (runtimeConfigPath == null) {
        Logger.error('配置生成失败，原始配置存在错误：$configPath');
        return false;
      }

      final success = await _apiClient.reloadConfig(
        configPath: runtimeConfigPath,
        force: true,
      );

      if (!success) {
        Logger.error('配置重载失败');
      }

      return success;
    } catch (e) {
      Logger.error('重载配置文件出错：$e');
      return false;
    }
  }

  // 生成运行时配置文件（辅助方法，避免重复代码）
  Future<String?> _generateConfig(
    String? configPath,
    List<OverrideConfig> overrides,
  ) async {
    // 直接从持久化读取配置参数（ConfigManager 不再缓存状态）
    final prefs = ClashPreferences.instance;

    return await ConfigInjector.injectCustomConfigParams(
      configPath: configPath,
      overrides: overrides,
      mixedPort: prefs.getMixedPort(),
      isIpv6Enabled: prefs.getIpv6(),
      isTunEnabled: prefs.getTunEnable(),
      tunStack: prefs.getTunStack(),
      tunDevice: prefs.getTunDevice(),
      isTunAutoRouteEnabled: prefs.getTunAutoRoute(),
      isTunAutoRedirectEnabled: prefs.getTunAutoRedirect(),
      isTunAutoDetectInterfaceEnabled: prefs.getTunAutoDetectInterface(),
      tunDnsHijack: prefs.getTunDnsHijack(),
      isTunStrictRouteEnabled: prefs.getTunStrictRoute(),
      tunRouteExcludeAddress: prefs.getTunRouteExcludeAddress(),
      isTunIcmpForwardingDisabled: prefs.getTunDisableIcmpForwarding(),
      tunMtu: prefs.getTunMtu(),
      isAllowLanEnabled: prefs.getAllowLan(),
      isTcpConcurrentEnabled: prefs.getTcpConcurrent(),
      geodataLoader: prefs.getGeodataLoader(),
      findProcessMode: prefs.getFindProcessMode(),
      clashCoreLogLevel: prefs.getCoreLogLevel(),
      externalController: prefs.getExternalControllerEnabled()
          ? prefs.getExternalControllerAddress()
          : '',
      externalControllerSecret: prefs.getExternalControllerSecret(),
      isUnifiedDelayEnabled: prefs.getUnifiedDelayEnabled(),
      outboundMode: prefs.getOutboundMode(),
    );
  }

  // 设置局域网代理状态
  Future<bool> setAllowLan(bool enabled) async {
    try {
      // 先保存到持久化
      await ClashPreferences.instance.setAllowLan(enabled);

      // 如果核心正在运行，调用 API
      if (_isCoreRunning()) {
        final success = await _apiClient.setAllowLan(enabled);
        if (success) {
          Logger.info('局域网代理（支持重载）：${enabled ? "启用" : "禁用"}');
        }
        return success;
      }

      return true;
    } catch (e) {
      Logger.error('设置局域网代理状态失败：$e');
      return false;
    }
  }

  // 设置 IPv6 状态
  Future<bool> setIpv6(bool enabled) async {
    try {
      // 先保存到持久化
      await ClashPreferences.instance.setIpv6(enabled);

      // 如果核心正在运行，调用 API
      if (_isCoreRunning()) {
        final success = await _apiClient.setIpv6(enabled);
        if (success) {
          Logger.info('IPv6（支持重载）：${enabled ? "启用" : "禁用"}');
        }
        return success;
      }

      return true;
    } catch (e) {
      Logger.error('设置 IPv6 状态失败：$e');
      return false;
    }
  }

  // 设置 TCP 并发状态
  Future<bool> setTcpConcurrent(bool enabled) async {
    try {
      // 先保存到持久化
      await ClashPreferences.instance.setTcpConcurrent(enabled);

      // 如果核心正在运行，调用 API
      if (_isCoreRunning()) {
        final success = await _apiClient.setTcpConcurrent(enabled);
        if (success) {
          Logger.info('TCP 并发配置已更新：${enabled ? "启用" : "禁用"}');
        }
        return success;
      }

      return true;
    } catch (e) {
      Logger.error('设置 TCP 并发状态失败：$e');
      return false;
    }
  }

  // 设置统一延迟状态
  Future<bool> setUnifiedDelay(bool enabled) async {
    try {
      // 先保存到持久化
      await ClashPreferences.instance.setUnifiedDelayEnabled(enabled);

      // 如果核心正在运行，调用 API
      if (_isCoreRunning()) {
        final success = await _apiClient.setUnifiedDelay(enabled);
        if (success) {
          Logger.info('统一延迟配置已更新：${enabled ? "启用（去除握手延迟）" : "禁用（包含握手延迟）"}');
        }
        return success;
      }

      return true;
    } catch (e) {
      Logger.error('设置统一延迟状态失败：$e');
      return false;
    }
  }

  // 设置 GEO 数据加载模式
  Future<bool> setGeodataLoader(String mode) async {
    try {
      // 先保存到持久化
      await ClashPreferences.instance.setGeodataLoader(mode);

      // 如果核心正在运行，调用 API
      if (_isCoreRunning()) {
        final success = await _apiClient.setGeodataLoader(mode);
        if (success) {
          Logger.info('GEO 数据加载模式（支持重载）：$mode');
        }
        return success;
      }

      return true;
    } catch (e) {
      Logger.error('设置 GEO 数据加载模式失败：$e');
      return false;
    }
  }

  // 设置查找进程模式
  Future<bool> setFindProcessMode(String mode) async {
    try {
      // 先保存到持久化
      await ClashPreferences.instance.setFindProcessMode(mode);

      // 如果核心正在运行，调用 API
      if (_isCoreRunning()) {
        final success = await _apiClient.setFindProcessMode(mode);
        if (success) {
          Logger.info('查找进程模式（支持重载）：$mode');
        }
        return success;
      }

      return true;
    } catch (e) {
      Logger.error('设置查找进程模式失败：$e');
      return false;
    }
  }

  // 设置日志等级
  Future<bool> setClashCoreLogLevel(String level) async {
    try {
      // 先保存到持久化
      await ClashPreferences.instance.setCoreLogLevel(level);

      // 如果核心正在运行，调用 API
      if (_isCoreRunning()) {
        final success = await _apiClient.setLogLevel(level);
        if (success) {
          Logger.info('日志等级（支持重载）：$level');
        }
        return success;
      }

      return true;
    } catch (e) {
      Logger.error('设置日志等级失败：$e');
      return false;
    }
  }

  // 设置外部控制器
  Future<bool> setExternalController(
    bool enabled,
    String defaultAddress,
  ) async {
    try {
      // 先保存到持久化
      await ClashPreferences.instance.setExternalControllerEnabled(enabled);

      // 如果核心正在运行，调用 API
      if (_isCoreRunning()) {
        final address = enabled ? defaultAddress : '';
        final success = await _apiClient.setExternalController(address);
        if (success) {
          Logger.info('外部控制器（支持重载）：${enabled ? "启用" : "禁用"} - $address');
        }
        return success;
      }

      return true;
    } catch (e) {
      Logger.error('设置外部控制器失败：$e');
      return false;
    }
  }

  // 设置 TCP 保持活动
  Future<bool> setKeepAlive(bool enabled) async {
    try {
      // 保存到持久化
      await ClashPreferences.instance.setKeepAliveEnabled(enabled);
      Logger.info('TCP 保持活动（需要重启核心）：${enabled ? "启用" : "禁用"}');
      return true;
    } catch (e) {
      Logger.error('设置 TCP 保持活动失败：$e');
      return false;
    }
  }

  // 设置测速链接
  Future<bool> setTestUrl(String url) async {
    try {
      final uri = Uri.tryParse(url);
      if (uri == null || !uri.hasScheme) {
        Logger.error('无效的 URL 格式：$url');
        return false;
      }

      // 保存到持久化（应用层配置，不需要同步到 API）
      await ClashPreferences.instance.setTestUrl(url);
      Logger.info('测速链接（应用层配置）：$url');
      return true;
    } catch (e) {
      Logger.error('设置测速链接失败：$e');
      return false;
    }
  }

  // 设置混合端口
  Future<bool> setMixedPort(int port) async {
    try {
      if (port < ClashDefaults.minPort || port > ClashDefaults.maxPort) {
        Logger.error('无效的端口号：$port');
        return false;
      }

      // 先保存到持久化
      await ClashPreferences.instance.setMixedPort(port);

      // 如果核心正在运行，调用 API
      if (_isCoreRunning()) {
        final success = await _apiClient.setMixedPort(port);
        if (success) {
          Logger.info('混合端口（支持重载）：$port');
        }
        return success;
      }

      return true;
    } catch (e) {
      Logger.error('设置混合端口失败：$e');
      return false;
    }
  }

  // 设置 SOCKS 端口
  Future<bool> setSocksPort(int? port) async {
    try {
      if (port != null &&
          (port < ClashDefaults.minPort || port > ClashDefaults.maxPort)) {
        Logger.error('无效的端口号：$port');
        return false;
      }

      // 先保存到持久化
      await ClashPreferences.instance.setSocksPort(port);

      // 如果核心正在运行，调用 API
      if (_isCoreRunning()) {
        final success = await _apiClient.setSocksPort(port ?? 0);
        if (success) {
          Logger.info('SOCKS 端口（支持重载）：${port ?? "未设置"}');
        }
        return success;
      }

      return true;
    } catch (e) {
      Logger.error('设置 SOCKS 端口失败：$e');
      return false;
    }
  }

  // 设置 HTTP 端口
  Future<bool> setHttpPort(int? port) async {
    try {
      if (port != null &&
          (port < ClashDefaults.minPort || port > ClashDefaults.maxPort)) {
        Logger.error('无效的端口号：$port');
        return false;
      }

      // 先保存到持久化
      await ClashPreferences.instance.setHttpPort(port);

      // 如果核心正在运行，调用 API
      if (_isCoreRunning()) {
        final success = await _apiClient.setHttpPort(port ?? 0);
        if (success) {
          Logger.info('HTTP 端口（支持重载）：${port ?? "未设置"}');
        }
        return success;
      }

      return true;
    } catch (e) {
      Logger.error('设置 HTTP 端口失败：$e');
      return false;
    }
  }

  // 设置虚拟网卡模式启用状态
  Future<bool> setTunEnabled(bool enabled) async {
    try {
      // 保存到持久化（不管核心是否运行）
      await ClashPreferences.instance.setTunEnable(enabled);
      Logger.info(
        '虚拟网卡模式配置已更新：${enabled ? "启用" : "禁用"}（${_isCoreRunning() ? "将通过配置重载生效" : "将在下次启动时生效"}）',
      );
      return true;
    } catch (e) {
      Logger.error('设置虚拟网卡模式失败：$e');
      return false;
    }
  }

  // 设置虚拟网卡网络栈类型
  Future<bool> setTunStack(String stack) async {
    try {
      // 先保存到持久化
      await ClashPreferences.instance.setTunStack(stack);

      // 如果核心正在运行，调用 API
      if (_isCoreRunning()) {
        final success = await _apiClient.setTunStack(stack);
        if (success) {
          Logger.info('虚拟网卡网络栈（支持重载）：$stack');
        }
        return success;
      }

      return true;
    } catch (e) {
      Logger.error('设置虚拟网卡网络栈失败：$e');
      return false;
    }
  }

  // 设置虚拟网卡设备名称
  Future<bool> setTunDevice(String device) async {
    try {
      // 先保存到持久化
      await ClashPreferences.instance.setTunDevice(device);

      // 如果核心正在运行，调用 API
      if (_isCoreRunning()) {
        final success = await _apiClient.setTunDevice(device);
        if (success) {
          Logger.info('虚拟网卡设备名称（支持重载）：$device');
        }
        return success;
      }

      return true;
    } catch (e) {
      Logger.error('设置虚拟网卡设备名称失败：$e');
      return false;
    }
  }

  // 设置虚拟网卡自动路由
  Future<bool> setTunAutoRoute(bool enabled) async {
    try {
      // 保存到持久化
      await ClashPreferences.instance.setTunAutoRoute(enabled);
      Logger.info('虚拟网卡自动路由（支持重载）：${enabled ? "启用" : "禁用"}');
      return true;
    } catch (e) {
      Logger.error('设置虚拟网卡自动路由失败：$e');
      return false;
    }
  }

  // 设置虚拟网卡自动 TCP 重定向（Linux 专用）
  Future<bool> setTunAutoRedirect(bool enabled) async {
    try {
      // 先保存到持久化
      await ClashPreferences.instance.setTunAutoRedirect(enabled);

      // 如果核心正在运行，调用 API
      if (_isCoreRunning()) {
        final success = await _apiClient.setTunAutoRedirect(enabled);
        if (success) {
          Logger.info('虚拟网卡自动 TCP 重定向（支持重载）：${enabled ? "启用" : "禁用"}');
        }
        return success;
      }

      return true;
    } catch (e) {
      Logger.error('设置虚拟网卡自动 TCP 重定向失败：$e');
      return false;
    }
  }

  // 设置虚拟网卡自动检测接口
  Future<bool> setTunAutoDetectInterface(bool enabled) async {
    try {
      // 保存到持久化
      await ClashPreferences.instance.setTunAutoDetectInterface(enabled);
      Logger.info('虚拟网卡自动检测接口（支持重载）：${enabled ? "启用" : "禁用"}');
      return true;
    } catch (e) {
      Logger.error('设置虚拟网卡自动检测接口失败：$e');
      return false;
    }
  }

  // 设置虚拟网卡 DNS 劫持列表
  Future<bool> setTunDnsHijack(List<String> dnsHijack) async {
    try {
      // 先保存到持久化
      await ClashPreferences.instance.setTunDnsHijack(dnsHijack);

      // 如果核心正在运行，调用 API
      if (_isCoreRunning()) {
        final success = await _apiClient.setTunDnsHijack(dnsHijack);
        if (success) {
          Logger.info('虚拟网卡 DNS 劫持列表（支持重载）：$dnsHijack');
        }
        return success;
      }

      return true;
    } catch (e) {
      Logger.error('设置虚拟网卡 DNS 劫持列表失败：$e');
      return false;
    }
  }

  // 设置虚拟网卡严格路由
  Future<bool> setTunStrictRoute(bool enabled) async {
    try {
      // 保存到持久化
      await ClashPreferences.instance.setTunStrictRoute(enabled);
      Logger.info('虚拟网卡严格路由（支持重载）：${enabled ? "启用" : "禁用"}');
      return true;
    } catch (e) {
      Logger.error('设置虚拟网卡严格路由失败：$e');
      return false;
    }
  }

  // 设置虚拟网卡排除网段列表
  Future<bool> setTunRouteExcludeAddress(List<String> addresses) async {
    try {
      // 先保存到持久化
      await ClashPreferences.instance.setTunRouteExcludeAddress(addresses);

      // 如果核心正在运行，调用 API
      if (_isCoreRunning()) {
        final success = await _apiClient.setTunRouteExcludeAddress(addresses);
        if (success) {
          Logger.info('虚拟网卡排除网段列表（支持重载）：$addresses');
        }
        return success;
      }

      return true;
    } catch (e) {
      Logger.error('设置虚拟网卡排除网段列表失败：$e');
      return false;
    }
  }

  // 设置虚拟网卡禁用 ICMP 转发
  Future<bool> setTunDisableIcmpForwarding(bool disabled) async {
    try {
      // 先保存到持久化
      await ClashPreferences.instance.setTunDisableIcmpForwarding(disabled);

      // 如果核心正在运行，调用 API
      if (_isCoreRunning()) {
        final success = await _apiClient.setTunDisableIcmpForwarding(disabled);
        if (success) {
          Logger.info('虚拟网卡 ICMP 转发（支持重载）：${disabled ? "禁用" : "启用"}');
        }
        return success;
      }

      return true;
    } catch (e) {
      Logger.error('设置虚拟网卡 ICMP 转发失败：$e');
      return false;
    }
  }

  // 设置虚拟网卡 MTU 值
  Future<bool> setTunMtu(int mtu) async {
    try {
      if (mtu < ClashDefaults.minTunMtu || mtu > ClashDefaults.maxTunMtu) {
        Logger.error(
          '无效的 MTU 值：$mtu（应在 ${ClashDefaults.minTunMtu}-${ClashDefaults.maxTunMtu} 之间）',
        );
        return false;
      }

      // 先保存到持久化
      await ClashPreferences.instance.setTunMtu(mtu);

      // 如果核心正在运行，调用 API
      if (_isCoreRunning()) {
        final success = await _apiClient.setTunMtu(mtu);
        if (success) {
          Logger.info('虚拟网卡 MTU 值（支持重载）：$mtu');
        }
        return success;
      }

      return true;
    } catch (e) {
      Logger.error('设置虚拟网卡 MTU 值失败：$e');
      return false;
    }
  }

  // 设置出站模式（自动判断核心状态）
  Future<bool> setOutboundMode(String outboundMode) async {
    try {
      // 先保存到持久化
      await ClashPreferences.instance.setOutboundMode(outboundMode);

      if (_isCoreRunning()) {
        // 核心运行时，调用 API 设置
        final success = await _apiClient.setMode(outboundMode);
        if (success) {
          Logger.info('出站模式已切换：$outboundMode');
        }
        return success;
      } else {
        // 核心未运行时，只保存偏好
        Logger.info('出站模式已保存：$outboundMode（将在下次启动时应用）');
        return true;
      }
    } catch (e) {
      Logger.error('设置出站模式失败：$e');
      return false;
    }
  }
}

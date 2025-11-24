import 'dart:io';
import 'dart:async';
import 'package:tray_manager/tray_manager.dart';
import 'package:stelliberty/utils/logger.dart';
import 'package:stelliberty/tray/tray_event.dart';
import 'package:stelliberty/clash/providers/clash_provider.dart';
import 'package:stelliberty/clash/providers/service_provider.dart';
import 'package:stelliberty/clash/providers/subscription_provider.dart';
import 'package:stelliberty/clash/manager/manager.dart';
import 'package:stelliberty/i18n/i18n.dart';

// 系统托盘管理器,负责初始化、配置和生命周期管理
class AppTrayManager {
  static final AppTrayManager _instance = AppTrayManager._internal();
  factory AppTrayManager() => _instance;
  AppTrayManager._internal();

  bool _isInitialized = false;
  final TrayEventHandler _eventHandler = TrayEventHandler();
  ClashProvider? _clashProvider;
  SubscriptionProvider? _subscriptionProvider;
  bool _isListeningToClashManager = false; // 是否已监听 ClashManager
  bool? _lastProxyState; // 缓存代理状态,避免重复更新
  bool? _lastSystemProxyState; // 缓存系统代理状态
  bool? _lastTunState; // 缓存虚拟网卡状态
  bool? _lastSubscriptionState; // 缓存订阅状态
  String? _lastOutboundMode; // 缓存出站模式状态

  // 设置 ClashProvider 用于控制代理
  void setClashProvider(ClashProvider provider) {
    // 移除旧监听器(若存在),避免监听器泄漏
    if (_clashProvider != null) {
      _clashProvider!.removeListener(_updateTrayMenuOnStateChange);
    }

    _clashProvider = provider;
    _eventHandler.setClashProvider(provider);
    // 监听 ClashProvider 状态变化更新托盘
    provider.addListener(_updateTrayMenuOnStateChange);

    // 监听 ClashManager(系统代理和虚拟网卡状态)
    if (!_isListeningToClashManager) {
      ClashManager.instance.addListener(_updateTrayMenuOnStateChange);
      _isListeningToClashManager = true;
    }

    // 立即同步当前状态到托盘
    if (_isInitialized) {
      Logger.info('设置托盘 ClashProvider，当前代理状态：${provider.isRunning}');

      // 缓存 ClashManager 实例减少重复访问
      final manager = ClashManager.instance;
      _lastProxyState = provider.isRunning; // 初始化缓存
      _lastSystemProxyState = manager.isSystemProxyEnabled;
      _lastTunState = manager.tunEnable;
      _lastOutboundMode = manager.mode; // 初始化出站模式缓存

      // 获取订阅状态
      final hasSubscription =
          _subscriptionProvider?.getSubscriptionConfigPath() != null;
      _lastSubscriptionState = hasSubscription;

      _updateTrayMenu(provider.isRunning, hasSubscription);
      _updateTrayIcon(manager.isSystemProxyEnabled, manager.tunEnable);
    }
  }

  // 设置 SubscriptionProvider 获取配置文件路径
  void setSubscriptionProvider(SubscriptionProvider provider) {
    // 移除旧监听器(若存在),避免监听器泄漏
    if (_subscriptionProvider != null) {
      _subscriptionProvider!.removeListener(_updateTrayMenuOnStateChange);
    }

    _subscriptionProvider = provider;
    _eventHandler.setSubscriptionProvider(provider);

    // 监听 SubscriptionProvider 状态变化更新托盘菜单
    provider.addListener(_updateTrayMenuOnStateChange);

    // 立即同步当前订阅状态到托盘菜单
    if (_isInitialized && _clashProvider != null) {
      final hasSubscription = provider.getSubscriptionConfigPath() != null;
      _lastSubscriptionState = hasSubscription;
      _updateTrayMenu(_clashProvider!.isRunning, hasSubscription);
    }
  }

  // Clash 状态变化时更新托盘菜单和图标
  Future<void> _updateTrayMenuOnStateChange() async {
    if (_isInitialized &&
        _clashProvider != null &&
        _subscriptionProvider != null) {
      final currentProxyState = _clashProvider!.isRunning;

      // 缓存 ClashManager 实例减少重复访问
      final manager = ClashManager.instance;
      final currentSystemProxyState = manager.isSystemProxyEnabled;
      final currentTunState = manager.tunEnable;
      final currentOutboundMode = manager.mode;
      final currentSubscriptionState =
          _subscriptionProvider!.getSubscriptionConfigPath() != null;

      // 提前检查状态是否变化,避免重复调用
      final proxyStateChanged = _lastProxyState != currentProxyState;
      final systemProxyStateChanged =
          _lastSystemProxyState != currentSystemProxyState;
      final tunStateChanged = _lastTunState != currentTunState;
      final outboundModeChanged = _lastOutboundMode != currentOutboundMode;
      final subscriptionStateChanged =
          _lastSubscriptionState != currentSubscriptionState;

      // 所有状态未变化时直接返回(去重优化)
      if (!proxyStateChanged &&
          !systemProxyStateChanged &&
          !tunStateChanged &&
          !outboundModeChanged &&
          !subscriptionStateChanged) {
        return;
      }

      // 更新缓存状态(原子性,防止重复触发)
      _lastProxyState = currentProxyState;
      _lastSystemProxyState = currentSystemProxyState;
      _lastTunState = currentTunState;
      _lastOutboundMode = currentOutboundMode;
      _lastSubscriptionState = currentSubscriptionState;

      // 更新托盘菜单(系统代理、虚拟网卡、核心运行、出站模式、订阅状态变化时)
      if (proxyStateChanged ||
          subscriptionStateChanged ||
          systemProxyStateChanged ||
          tunStateChanged ||
          outboundModeChanged) {
        await _updateTrayMenu(currentProxyState, currentSubscriptionState);
      }

      // 系统代理或虚拟网卡状态变化时更新图标
      if (systemProxyStateChanged || tunStateChanged) {
        await _updateTrayIcon(currentSystemProxyState, currentTunState);
      }
    }
  }

  // 初始化托盘
  Future<void> initialize() async {
    if (_isInitialized) {
      Logger.warning('托盘已经初始化过了');
      return;
    }

    try {
      // 设置托盘图标(默认停止状态)
      final iconPath = _getTrayIconPath(false, false);
      Logger.info('尝试设置托盘图标：$iconPath');
      await trayManager.setIcon(iconPath);
      Logger.info('托盘图标设置成功');

      // 设置托盘菜单(默认代理关闭,无订阅)
      await _updateTrayMenu(false, false);

      // 设置提示文本(Linux 可能不支持)
      if (!Platform.isLinux) {
        try {
          await trayManager.setToolTip(translate.common.appName);
        } catch (e) {
          Logger.warning('设置托盘提示文本失败（平台可能不支持）：$e');
        }
      }

      // 注册事件监听器
      trayManager.addListener(_eventHandler);

      _isInitialized = true;
      Logger.info('托盘初始化成功');
    } catch (e) {
      Logger.error('初始化托盘失败：$e');
      rethrow;
    }
  }

  // 更新托盘菜单
  Future<void> _updateTrayMenu(
    bool isProxyRunning,
    bool hasSubscription,
  ) async {
    try {
      // 获取系统代理实际状态
      final manager = ClashManager.instance;
      final isSystemProxyEnabled = manager.isSystemProxyEnabled;
      final isTunEnabled = manager.tunEnable;

      // 检查虚拟网卡模式是否可用(需管理员权限或服务模式)
      final isTunAvailable = await _checkTunAvailable();

      // 获取当前出站模式
      final currentMode = manager.mode;

      final menu = Menu(
        items: [
          MenuItem(key: 'close_menu', label: translate.tray.closeMenu),
          MenuItem.separator(),
          // 出站模式子菜单
          MenuItem.submenu(
            key: 'outbound_mode',
            label: translate.tray.outboundMode,
            submenu: Menu(
              items: [
                MenuItem.checkbox(
                  key: 'outbound_mode_rule',
                  label: translate.tray.ruleMode,
                  checked: currentMode == 'rule',
                ),
                MenuItem.checkbox(
                  key: 'outbound_mode_global',
                  label: translate.tray.globalMode,
                  checked: currentMode == 'global',
                ),
                MenuItem.checkbox(
                  key: 'outbound_mode_direct',
                  label: translate.tray.directMode,
                  checked: currentMode == 'direct',
                ),
              ],
            ),
          ),
          MenuItem.separator(),
          MenuItem.checkbox(
            key: 'toggle_proxy',
            label: translate.tray.toggleProxy,
            checked: isSystemProxyEnabled, // 使用系统代理状态
            disabled: !hasSubscription && !isProxyRunning, // 无订阅且未运行时禁用
          ),
          MenuItem.checkbox(
            key: 'toggle_tun',
            label: translate.tray.toggleTun,
            checked: isTunEnabled,
            disabled: !isTunAvailable || !isProxyRunning, // 权限不足或核心未运行时禁用
          ),
          MenuItem.separator(),
          MenuItem(key: 'exit', label: translate.tray.exit),
        ],
      );

      await trayManager.setContextMenu(menu);
    } catch (e) {
      Logger.error('更新托盘菜单失败：$e');
    }
  }

  // 检查虚拟网卡模式是否可用
  // Windows: 检查管理员权限或服务安装状态
  // Linux/macOS: 检查是否为 root 用户
  Future<bool> _checkTunAvailable() async {
    if (Platform.isWindows) {
      // Windows: 使用 ServiceProvider 的缓存状态
      try {
        final serviceProvider = ServiceProvider();
        return serviceProvider.isInstalled;
      } catch (e) {
        return false;
      }
    } else {
      // Linux/macOS: 检查是否为 root 用户
      try {
        final result = await Process.run('id', ['-u']);
        final uid = int.tryParse(result.stdout.toString().trim()) ?? -1;
        return uid == 0; // root 用户 UID 为 0
      } catch (e) {
        Logger.error('检查 root 权限失败：$e');
        return false;
      }
    }
  }

  // 更新托盘图标
  Future<void> _updateTrayIcon(
    bool isSystemProxyEnabled,
    bool isTunEnabled,
  ) async {
    try {
      final iconPath = _getTrayIconPath(isSystemProxyEnabled, isTunEnabled);
      await trayManager.setIcon(iconPath);

      // 状态描述(与图标优先级一致)
      String status;
      if (isTunEnabled) {
        status = '虚拟网卡模式';
      } else if (isSystemProxyEnabled) {
        status = '运行中';
      } else {
        status = '已停止';
      }
      Logger.debug('托盘图标已更新：$status ($iconPath)');
    } catch (e) {
      Logger.error('更新托盘图标失败：$e');
    }
  }

  // 获取托盘图标路径
  String _getTrayIconPath(bool isSystemProxyEnabled, bool isTunEnabled) {
    // 根据状态返回不同图标(所有平台逻辑统一)
    if (isTunEnabled) {
      // 虚拟网卡模式(优先级最高)
      return Platform.isWindows
          ? 'assets/icons/tun_mode.ico'
          : 'assets/icons/tun_mode.png';
    } else if (isSystemProxyEnabled) {
      // 系统代理运行中
      return Platform.isWindows
          ? 'assets/icons/running.ico'
          : 'assets/icons/running.png';
    } else {
      // 停止状态
      return Platform.isWindows
          ? 'assets/icons/stopping.ico'
          : 'assets/icons/stopping.png';
    }
  }

  // 销毁托盘
  Future<void> dispose() async {
    if (_isInitialized) {
      // 移除监听器
      if (_clashProvider != null) {
        _clashProvider!.removeListener(_updateTrayMenuOnStateChange);
      }
      if (_subscriptionProvider != null) {
        _subscriptionProvider!.removeListener(_updateTrayMenuOnStateChange);
      }
      if (_isListeningToClashManager) {
        ClashManager.instance.removeListener(_updateTrayMenuOnStateChange);
        _isListeningToClashManager = false;
      }
      trayManager.removeListener(_eventHandler);
      await trayManager.destroy();
      _isInitialized = false;
      Logger.info('托盘已销毁');
    }
  }
}

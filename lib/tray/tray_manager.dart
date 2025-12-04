import 'dart:io';
import 'dart:async';
import 'package:tray_manager/tray_manager.dart';
import 'package:window_manager/window_manager.dart';
import 'package:stelliberty/utils/logger.dart';
import 'package:stelliberty/tray/tray_event.dart';
import 'package:stelliberty/clash/providers/clash_provider.dart';
import 'package:stelliberty/clash/providers/service_provider.dart';
import 'package:stelliberty/clash/providers/subscription_provider.dart';
import 'package:stelliberty/clash/manager/manager.dart';
import 'package:stelliberty/i18n/i18n.dart';
import 'package:stelliberty/services/permission_service.dart';

// 系统托盘管理器,负责初始化、配置和生命周期管理
class AppTrayManager {
  static final AppTrayManager _instance = AppTrayManager._internal();
  factory AppTrayManager() => _instance;
  AppTrayManager._internal();

  bool _isInitialized = false;
  bool _isExiting = false; // 退出标志，防止退出时托盘图标继续变化
  final TrayEventHandler _eventHandler = TrayEventHandler();
  ClashProvider? _clashProvider;
  SubscriptionProvider? _subscriptionProvider;
  bool _isListeningToClashManager = false; // 是否已监听 ClashManager
  bool? _proxyStateCache; // 缓存代理状态,避免重复更新
  bool? _systemProxyStateCache; // 缓存系统代理状态
  bool? _tunStateCache; // 缓存虚拟网卡状态
  bool? _subscriptionStateCache; // 缓存订阅状态
  String? _outboundModeCache; // 缓存出站模式状态
  bool? _tunAvailableCache; // 缓存虚拟网卡可用性状态

  // 设置 ClashProvider 用于控制代理
  void setClashProvider(ClashProvider provider) {
    _clashProvider = provider;
    _eventHandler.setClashProvider(provider);

    // 监听 ClashManager（系统代理和虚拟网卡状态）
    // 注意：不监听 ClashProvider，避免双重触发
    if (!_isListeningToClashManager) {
      ClashManager.instance.addListener(_updateTrayMenuOnStateChange);
      _isListeningToClashManager = true;
    }

    // 立即同步当前状态到托盘
    if (_isInitialized) {
      Logger.info('设置托盘 ClashProvider，当前代理状态：${provider.isRunning}');

      // 缓存 ClashManager 实例减少重复访问
      final manager = ClashManager.instance;
      _proxyStateCache = provider.isRunning; // 初始化缓存
      _systemProxyStateCache = manager.isSystemProxyEnabled;
      _tunStateCache = manager.tunEnable;
      _outboundModeCache = manager.mode; // 初始化出站模式缓存

      // 获取订阅状态
      final hasSubscription =
          _subscriptionProvider?.getSubscriptionConfigPath() != null;
      _subscriptionStateCache = hasSubscription;

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
      _subscriptionStateCache = hasSubscription;
      _updateTrayMenu(_clashProvider!.isRunning, hasSubscription);
    }
  }

  // 手动触发托盘菜单更新（供外部调用，例如窗口显示/隐藏后）
  Future<void> updateTrayMenuManually() async {
    if (_clashProvider != null && _subscriptionProvider != null) {
      final isRunning = _clashProvider!.isRunning;
      final hasSubscription =
          _subscriptionProvider!.getSubscriptionConfigPath() != null;

      // 获取系统代理和 TUN 状态
      final manager = ClashManager.instance;
      final isSystemProxyEnabled = manager.isSystemProxyEnabled;
      final isTunEnabled = manager.tunEnable;

      // 清除 TUN 可用性缓存，强制重新检查（用于服务安装/卸载后）
      _tunAvailableCache = null;

      // 强制更新托盘菜单和图标，不检查缓存
      await _updateTrayMenu(isRunning, hasSubscription);
      await _updateTrayIcon(isSystemProxyEnabled, isTunEnabled);
    }
  }

  // Clash 状态变化时更新托盘菜单和图标
  Future<void> _updateTrayMenuOnStateChange() async {
    // 退出时不再更新托盘图标，避免视觉干扰
    if (_isExiting) {
      return;
    }

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
      final proxyStateChanged = _proxyStateCache != currentProxyState;
      final systemProxyStateChanged =
          _systemProxyStateCache != currentSystemProxyState;
      final tunStateChanged = _tunStateCache != currentTunState;
      final outboundModeChanged = _outboundModeCache != currentOutboundMode;
      final subscriptionStateChanged =
          _subscriptionStateCache != currentSubscriptionState;

      // 所有状态未变化时直接返回(去重优化)
      if (!proxyStateChanged &&
          !systemProxyStateChanged &&
          !tunStateChanged &&
          !outboundModeChanged &&
          !subscriptionStateChanged) {
        return;
      }

      // 更新缓存状态(原子性,防止重复触发)
      _proxyStateCache = currentProxyState;
      _systemProxyStateCache = currentSystemProxyState;
      _tunStateCache = currentTunState;
      _outboundModeCache = currentOutboundMode;
      _subscriptionStateCache = currentSubscriptionState;

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

      // 检查窗口是否可见
      bool isWindowVisible = false;
      try {
        isWindowVisible = await windowManager.isVisible();
      } catch (e) {
        Logger.warning('检查窗口可见性失败：$e');
      }

      final menu = Menu(
        items: [
          MenuItem(key: 'close_menu', label: translate.tray.closeMenu),
          MenuItem(
            key: 'show_window',
            label: translate.tray.showWindow,
            disabled: isWindowVisible, // 窗口可见时禁用
          ),
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
            disabled: !isTunAvailable, // 仅在权限不足时禁用,服务模式下可独立工作
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
    // 使用缓存避免重复检查
    if (_tunAvailableCache != null) {
      return _tunAvailableCache!;
    }

    bool isAvailable;
    if (Platform.isWindows) {
      // Windows: 检查服务安装状态 或 管理员权限
      try {
        final serviceProvider = ServiceProvider();
        final isServiceInstalled = serviceProvider.isInstalled;

        if (isServiceInstalled) {
          // 服务已安装，可以使用 TUN
          isAvailable = true;
        } else {
          // 服务未安装，检查是否以管理员权限运行
          isAvailable = await PermissionService.isElevated();
        }
      } catch (e) {
        Logger.error('检查 TUN 可用性失败：$e');
        isAvailable = false;
      }
    } else {
      // Linux/macOS: 检查是否为 root 用户
      isAvailable = await PermissionService.isElevated();
    }

    // 缓存结果
    _tunAvailableCache = isAvailable;
    return isAvailable;
  }

  // 更新托盘图标
  Future<void> _updateTrayIcon(
    bool isSystemProxyEnabled,
    bool isTunEnabled,
  ) async {
    try {
      final iconPath = _getTrayIconPath(isSystemProxyEnabled, isTunEnabled);
      await trayManager.setIcon(iconPath);

      // 状态描述(支持四种状态组合)
      String status;
      if (isSystemProxyEnabled && isTunEnabled) {
        status = '代理和虚拟网卡模式';
      } else if (isTunEnabled) {
        status = '虚拟网卡模式';
      } else if (isSystemProxyEnabled) {
        status = '代理模式';
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
    // 根据状态返回不同图标(支持四种状态组合)
    if (isSystemProxyEnabled && isTunEnabled) {
      // 代理和虚拟网卡同时开启
      return Platform.isWindows
          ? 'assets/icons/proxy_tun_enabled.ico'
          : 'assets/icons/proxy_tun_enabled.png';
    } else if (isTunEnabled) {
      // 仅虚拟网卡开启
      return Platform.isWindows
          ? 'assets/icons/tun_enabled.ico'
          : 'assets/icons/tun_enabled.png';
    } else if (isSystemProxyEnabled) {
      // 仅系统代理开启
      return Platform.isWindows
          ? 'assets/icons/proxy_enabled.ico'
          : 'assets/icons/proxy_enabled.png';
    } else {
      // 停止状态
      return Platform.isWindows
          ? 'assets/icons/disabled.ico'
          : 'assets/icons/disabled.png';
    }
  }

  // 开始退出流程（由 TrayEventHandler 调用）
  void beginExit() {
    _isExiting = true;
    Logger.info('托盘管理器：开始退出流程，停止图标更新');
  }

  // 销毁托盘
  Future<void> dispose() async {
    if (_isInitialized) {
      _isExiting = true; // 确保退出标志已设置
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

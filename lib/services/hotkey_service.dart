import 'dart:async';
import 'dart:io';
import 'package:flutter/foundation.dart';
import 'package:flutter/services.dart';
import 'package:hotkey_manager/hotkey_manager.dart';
import 'package:stelliberty/atomic/platform_helper.dart';
import 'package:stelliberty/clash/manager/clash_manager.dart';
import 'package:stelliberty/clash/providers/clash_provider.dart';
import 'package:stelliberty/clash/providers/subscription_provider.dart';
import 'package:stelliberty/storage/preferences.dart';
import 'package:stelliberty/services/log_print_service.dart';
import 'package:stelliberty/tray/tray_manager.dart';
import 'package:stelliberty/services/window_state_service.dart';
import 'package:window_manager/window_manager.dart';

// 全局快捷键服务（使用 hotkey_manager 插件）
class HotkeyService {
  HotkeyService._();

  static final HotkeyService instance = HotkeyService._();

  static bool get isDesktopPlatform {
    if (kIsWeb) return false;
    return PlatformHelper.isDesktop;
  }

  ClashProvider? _clashProvider;
  SubscriptionProvider? _subscriptionProvider;

  bool _isSwitching = false;
  bool _isInitialized = false;

  // 存储已注册的快捷键对象
  HotKey? _toggleProxyHotkey;
  HotKey? _toggleTunHotkey;
  HotKey? _showWindowHotkey;
  HotKey? _exitAppHotkey;

  void setProviders({
    required ClashProvider clashProvider,
    required SubscriptionProvider subscriptionProvider,
  }) {
    _clashProvider = clashProvider;
    _subscriptionProvider = subscriptionProvider;
    Logger.debug('快捷键服务 Provider 已设置');
  }

  Future<void> initialize() async {
    if (_isInitialized) {
      Logger.debug('快捷键服务已初始化，跳过');
      return;
    }

    if (!isDesktopPlatform) {
      Logger.debug('非桌面平台，跳过快捷键服务初始化');
      return;
    }

    try {
      // 如果快捷键功能已启用，注册快捷键
      final enabled = AppPreferences.instance.getHotkeyEnabled();
      if (enabled) {
        await registerHotkeys();
      }

      _isInitialized = true;
      Logger.info('快捷键服务初始化完成（hotkey_manager 插件）');
    } catch (e) {
      Logger.error('快捷键服务初始化失败：$e');
    }
  }

  Future<void> registerHotkeys() async {
    try {
      // 注销快捷键
      await unregisterHotkeys();

      // 注册切换代理快捷键
      final toggleProxyStr = AppPreferences.instance.getHotkeyToggleProxy();
      if (toggleProxyStr != null && toggleProxyStr.isNotEmpty) {
        _toggleProxyHotkey = _parseHotkey(toggleProxyStr);
        if (_toggleProxyHotkey != null) {
          await hotKeyManager.register(
            _toggleProxyHotkey!,
            keyDownHandler: (_) {
              Logger.info('触发快捷键：切换系统代理');
              _handleToggleProxy();
            },
          );
          Logger.info('注册切换代理快捷键：$toggleProxyStr');
        }
      }

      // 注册切换 TUN 快捷键
      final toggleTunStr = AppPreferences.instance.getHotkeyToggleTun();
      if (toggleTunStr != null && toggleTunStr.isNotEmpty) {
        _toggleTunHotkey = _parseHotkey(toggleTunStr);
        if (_toggleTunHotkey != null) {
          await hotKeyManager.register(
            _toggleTunHotkey!,
            keyDownHandler: (_) {
              Logger.info('触发快捷键：切换虚拟网卡');
              _handleToggleTun();
            },
          );
          Logger.info('注册切换虚拟网卡快捷键：$toggleTunStr');
        }
      }

      // 注册显示/隐藏窗口快捷键
      final showWindowStr = AppPreferences.instance.getHotkeyShowWindow();
      if (showWindowStr != null && showWindowStr.isNotEmpty) {
        _showWindowHotkey = _parseHotkey(showWindowStr);
        if (_showWindowHotkey != null) {
          await hotKeyManager.register(
            _showWindowHotkey!,
            keyDownHandler: (_) {
              Logger.info('触发快捷键：显示/隐藏窗口');
              _handleShowWindow();
            },
          );
          Logger.info('注册显示/隐藏窗口快捷键：$showWindowStr');
        }
      }

      // 注册退出应用快捷键
      final exitAppStr = AppPreferences.instance.getHotkeyExitApp();
      if (exitAppStr != null && exitAppStr.isNotEmpty) {
        _exitAppHotkey = _parseHotkey(exitAppStr);
        if (_exitAppHotkey != null) {
          await hotKeyManager.register(
            _exitAppHotkey!,
            keyDownHandler: (_) {
              Logger.info('触发快捷键：退出应用');
              _handleExitApp();
            },
          );
          Logger.info('注册退出应用快捷键：$exitAppStr');
        }
      }
    } catch (e) {
      Logger.error('注册快捷键失败：$e');
    }
  }

  Future<void> unregisterHotkeys() async {
    try {
      if (_toggleProxyHotkey != null) {
        await hotKeyManager.unregister(_toggleProxyHotkey!);
        _toggleProxyHotkey = null;
      }
      if (_toggleTunHotkey != null) {
        await hotKeyManager.unregister(_toggleTunHotkey!);
        _toggleTunHotkey = null;
      }
      if (_showWindowHotkey != null) {
        await hotKeyManager.unregister(_showWindowHotkey!);
        _showWindowHotkey = null;
      }
      if (_exitAppHotkey != null) {
        await hotKeyManager.unregister(_exitAppHotkey!);
        _exitAppHotkey = null;
      }
      Logger.info('已注销所有快捷键');
    } catch (e) {
      Logger.error('注销快捷键失败：$e');
    }
  }

  Future<void> refreshFromPreferences() async {
    if (!isDesktopPlatform) {
      return;
    }

    if (!_isInitialized) {
      await initialize();
      return;
    }

    final enabled = AppPreferences.instance.getHotkeyEnabled();
    if (enabled) {
      await registerHotkeys();
    } else {
      await unregisterHotkeys();
    }
  }

  Future<bool> setEnabled(bool enabled) async {
    try {
      await AppPreferences.instance.setHotkeyEnabled(enabled);

      if (enabled) {
        await registerHotkeys();
      } else {
        await unregisterHotkeys();
      }

      Logger.info('全局快捷键已${enabled ? "启用" : "禁用"}');
      return true;
    } catch (e) {
      Logger.error('设置全局快捷键状态失败：$e');
      return false;
    }
  }

  // 通用的快捷键设置方法（DRY 原则）
  Future<bool> _setHotkeyInternal(
    String? hotkeyStr,
    Future<void> Function(String?) prefsSetter,
    String description,
  ) async {
    try {
      await prefsSetter(hotkeyStr);
      Logger.info('$description快捷键已设置：${hotkeyStr ?? "无"}');

      // 如果快捷键已启用，重新注册
      if (AppPreferences.instance.getHotkeyEnabled()) {
        await registerHotkeys();
      }

      return true;
    } catch (e) {
      Logger.error('设置$description快捷键失败：$e');
      return false;
    }
  }

  Future<bool> setToggleProxyHotkey(String? hotkeyStr) async {
    return _setHotkeyInternal(
      hotkeyStr,
      AppPreferences.instance.setHotkeyToggleProxy,
      '切换代理',
    );
  }

  Future<bool> setToggleTunHotkey(String? hotkeyStr) async {
    return _setHotkeyInternal(
      hotkeyStr,
      AppPreferences.instance.setHotkeyToggleTun,
      '切换虚拟网卡',
    );
  }

  Future<bool> setShowWindowHotkey(String? hotkeyStr) async {
    return _setHotkeyInternal(
      hotkeyStr,
      AppPreferences.instance.setHotkeyShowWindow,
      '显示/隐藏窗口',
    );
  }

  Future<bool> setExitAppHotkey(String? hotkeyStr) async {
    return _setHotkeyInternal(
      hotkeyStr,
      AppPreferences.instance.setHotkeyExitApp,
      '退出应用',
    );
  }

  // 解析快捷键字符串为 HotKey 对象
  HotKey? _parseHotkey(String hotkeyStr) {
    try {
      final parts = hotkeyStr.split('+');
      if (parts.isEmpty) return null;

      final modifiers = <HotKeyModifier>[];
      for (var i = 0; i < parts.length - 1; i++) {
        switch (parts[i].toLowerCase()) {
          case 'ctrl':
          case 'control':
            modifiers.add(HotKeyModifier.control);
            break;
          case 'alt':
            modifiers.add(HotKeyModifier.alt);
            break;
          case 'shift':
            modifiers.add(HotKeyModifier.shift);
            break;
          case 'win':
          case 'meta':
            modifiers.add(HotKeyModifier.meta);
            break;
        }
      }

      final keyStr = parts.last.toLowerCase();
      final keyCode = _parseKeyCode(keyStr);
      if (keyCode == null) {
        Logger.warning('无法解析键码: $keyStr');
        return null;
      }

      return HotKey(
        key: keyCode,
        modifiers: modifiers.isNotEmpty ? modifiers : null,
        scope: HotKeyScope.system,
      );
    } catch (e) {
      Logger.error('解析快捷键失败: $hotkeyStr, 错误: $e');
      return null;
    }
  }

  // 解析键码
  LogicalKeyboardKey? _parseKeyCode(String keyStr) {
    // F1-F12
    if (keyStr.startsWith('f') && keyStr.length <= 3) {
      final num = int.tryParse(keyStr.substring(1));
      if (num != null && num >= 1 && num <= 12) {
        switch (num) {
          case 1:
            return LogicalKeyboardKey.f1;
          case 2:
            return LogicalKeyboardKey.f2;
          case 3:
            return LogicalKeyboardKey.f3;
          case 4:
            return LogicalKeyboardKey.f4;
          case 5:
            return LogicalKeyboardKey.f5;
          case 6:
            return LogicalKeyboardKey.f6;
          case 7:
            return LogicalKeyboardKey.f7;
          case 8:
            return LogicalKeyboardKey.f8;
          case 9:
            return LogicalKeyboardKey.f9;
          case 10:
            return LogicalKeyboardKey.f10;
          case 11:
            return LogicalKeyboardKey.f11;
          case 12:
            return LogicalKeyboardKey.f12;
        }
      }
    }

    // 数字 0-9
    if (keyStr.length == 1) {
      final code = keyStr.codeUnitAt(0);
      if (code >= 48 && code <= 57) {
        switch (keyStr) {
          case '0':
            return LogicalKeyboardKey.digit0;
          case '1':
            return LogicalKeyboardKey.digit1;
          case '2':
            return LogicalKeyboardKey.digit2;
          case '3':
            return LogicalKeyboardKey.digit3;
          case '4':
            return LogicalKeyboardKey.digit4;
          case '5':
            return LogicalKeyboardKey.digit5;
          case '6':
            return LogicalKeyboardKey.digit6;
          case '7':
            return LogicalKeyboardKey.digit7;
          case '8':
            return LogicalKeyboardKey.digit8;
          case '9':
            return LogicalKeyboardKey.digit9;
        }
      }

      // 字母 A-Z
      if (code >= 97 && code <= 122) {
        switch (keyStr) {
          case 'a':
            return LogicalKeyboardKey.keyA;
          case 'b':
            return LogicalKeyboardKey.keyB;
          case 'c':
            return LogicalKeyboardKey.keyC;
          case 'd':
            return LogicalKeyboardKey.keyD;
          case 'e':
            return LogicalKeyboardKey.keyE;
          case 'f':
            return LogicalKeyboardKey.keyF;
          case 'g':
            return LogicalKeyboardKey.keyG;
          case 'h':
            return LogicalKeyboardKey.keyH;
          case 'i':
            return LogicalKeyboardKey.keyI;
          case 'j':
            return LogicalKeyboardKey.keyJ;
          case 'k':
            return LogicalKeyboardKey.keyK;
          case 'l':
            return LogicalKeyboardKey.keyL;
          case 'm':
            return LogicalKeyboardKey.keyM;
          case 'n':
            return LogicalKeyboardKey.keyN;
          case 'o':
            return LogicalKeyboardKey.keyO;
          case 'p':
            return LogicalKeyboardKey.keyP;
          case 'q':
            return LogicalKeyboardKey.keyQ;
          case 'r':
            return LogicalKeyboardKey.keyR;
          case 's':
            return LogicalKeyboardKey.keyS;
          case 't':
            return LogicalKeyboardKey.keyT;
          case 'u':
            return LogicalKeyboardKey.keyU;
          case 'v':
            return LogicalKeyboardKey.keyV;
          case 'w':
            return LogicalKeyboardKey.keyW;
          case 'x':
            return LogicalKeyboardKey.keyX;
          case 'y':
            return LogicalKeyboardKey.keyY;
          case 'z':
            return LogicalKeyboardKey.keyZ;
        }
      }
    }

    // 特殊键
    switch (keyStr) {
      case 'space':
        return LogicalKeyboardKey.space;
      case 'enter':
        return LogicalKeyboardKey.enter;
      case 'esc':
      case 'escape':
        return LogicalKeyboardKey.escape;
      case 'tab':
        return LogicalKeyboardKey.tab;
      case 'backspace':
        return LogicalKeyboardKey.backspace;
      case 'delete':
        return LogicalKeyboardKey.delete;
      case 'home':
        return LogicalKeyboardKey.home;
      case 'end':
        return LogicalKeyboardKey.end;
      case 'pageup':
        return LogicalKeyboardKey.pageUp;
      case 'pagedown':
        return LogicalKeyboardKey.pageDown;
      case 'up':
        return LogicalKeyboardKey.arrowUp;
      case 'down':
        return LogicalKeyboardKey.arrowDown;
      case 'left':
        return LogicalKeyboardKey.arrowLeft;
      case 'right':
        return LogicalKeyboardKey.arrowRight;
      default:
        return null;
    }
  }

  Future<void> _handleToggleProxy() async {
    if (_isSwitching) {
      Logger.debug('状态切换中，忽略快捷键');
      return;
    }

    if (_clashProvider == null || _subscriptionProvider == null) {
      Logger.warning('Provider 未设置，无法切换代理');
      return;
    }

    _isSwitching = true;

    final manager = ClashManager.instance;
    final isSystemProxyEnabled = manager.isSystemProxyEnabled;
    final isRunning = _clashProvider!.isCoreRunning;

    Logger.info(
      '快捷键切换代理 - 核心状态: ${isRunning ? "运行中" : "已停止"}, 系统代理: ${isSystemProxyEnabled ? "已启用" : "未启用"}',
    );

    try {
      if (isSystemProxyEnabled) {
        await manager.disableSystemProxy();
        Logger.info('系统代理已通过快捷键关闭');
      } else {
        if (!isRunning) {
          final configPath = _subscriptionProvider!.getSubscriptionConfigPath();
          if (configPath == null) {
            Logger.warning('没有可用的订阅配置文件，无法启动代理');
            _isSwitching = false;
            return;
          }
          await _clashProvider!.start(configPath: configPath);
          Logger.info('核心已通过快捷键启动');
        }

        await manager.enableSystemProxy();
        Logger.info('系统代理已通过快捷键启用');
      }

      // 更新托盘菜单状态
      AppTrayManager().updateTrayMenuManually();
    } catch (e) {
      Logger.error('快捷键切换代理失败：$e');
    } finally {
      _isSwitching = false;
    }
  }

  Future<void> _handleToggleTun() async {
    if (_isSwitching) {
      Logger.debug('状态切换中，忽略快捷键');
      return;
    }

    // 检查虚拟网卡是否可用
    final isTunAvailable = await _checkTunAvailable();
    if (!isTunAvailable) {
      Logger.warning('虚拟网卡模式不可用，忽略快捷键');
      return;
    }

    _isSwitching = true;

    final manager = ClashManager.instance;
    final isTunEnabled = _clashProvider!.configState.isTunEnabled;

    Logger.info('快捷键切换虚拟网卡 - 当前状态：${isTunEnabled ? "已启用" : "未启用"}');

    try {
      await manager.setTunEnabled(!isTunEnabled);
      Logger.info('虚拟网卡已通过快捷键${isTunEnabled ? "禁用" : "启用"}');

      // 更新托盘菜单状态
      AppTrayManager().updateTrayMenuManually();
    } catch (e) {
      Logger.error('快捷键切换虚拟网卡失败：$e');
    } finally {
      _isSwitching = false;
    }
  }

  Future<void> _handleShowWindow() async {
    try {
      final isVisible = await windowManager.isVisible();

      if (isVisible) {
        // 窗口可见，隐藏窗口
        await windowManager.hide();
        Logger.info('窗口已通过快捷键隐藏');
      } else {
        // 窗口不可见，显示窗口
        final shouldMaximize = AppPreferences.instance.getIsMaximized();

        if (shouldMaximize) {
          await windowManager.maximize();
        }

        await windowManager.show();
        await windowManager.focus();
        Logger.info('窗口已通过快捷键显示');

        // 更新托盘菜单
        AppTrayManager().updateTrayMenuManually();
      }
    } catch (e) {
      Logger.error('快捷键显示/隐藏窗口失败：$e');
    }
  }

  Future<void> _handleExitApp() async {
    try {
      Logger.info('通过快捷键退出应用');
      await WindowExitHandler.exitApp();
    } catch (e) {
      Logger.error('快捷键退出应用失败：$e');
    }
  }

  // 检查虚拟网卡是否可用
  Future<bool> _checkTunAvailable() async {
    if (Platform.isWindows) {
      // Windows: 检查服务安装状态或管理员权限
      try {
        // 简化版本：总是允许尝试
        return true;
      } catch (e) {
        Logger.error('检查 TUN 可用性失败：$e');
        return false;
      }
    } else {
      // Linux/macOS: 总是允许尝试
      return true;
    }
  }

  Future<void> dispose() async {
    await unregisterHotkeys();
    _isInitialized = false;
    Logger.info('快捷键服务已释放');
  }
}

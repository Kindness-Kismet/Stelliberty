import 'package:tray_manager/tray_manager.dart';
import 'package:window_manager/window_manager.dart';
import 'package:stelliberty/services/log_print_service.dart';
import 'package:stelliberty/services/window_state_service.dart';
import 'package:stelliberty/clash/manager/manager.dart';
import 'package:stelliberty/clash/providers/clash_provider.dart';
import 'package:stelliberty/clash/providers/subscription_provider.dart';
import 'package:stelliberty/storage/preferences.dart';
import 'package:stelliberty/tray/tray_manager.dart';

// 托盘事件处理器,处理托盘图标的各种交互事件
class TrayEventHandler with TrayListener {
  ClashProvider? _clashProvider;
  SubscriptionProvider? _subscriptionProvider;

  // 双击检测相关
  DateTime? _lastClickTime;
  static const _doubleClickThreshold = Duration(milliseconds: 300);

  // 状态切换标志（切换期间禁用托盘交互）
  bool _isSwitching = false;

  // 设置 ClashProvider 用于控制代理
  void setClashProvider(ClashProvider provider) {
    _clashProvider = provider;
  }

  // 设置 SubscriptionProvider 用于获取配置文件路径
  void setSubscriptionProvider(SubscriptionProvider provider) {
    _subscriptionProvider = provider;
  }

  // 托盘图标左键点击事件,实现双击检测(300ms 内两次点击视为双击)
  @override
  void onTrayIconMouseDown() {
    // 状态切换期间禁用托盘交互
    if (_isSwitching) {
      Logger.debug('状态切换中，忽略托盘左键点击');
      return;
    }

    final now = DateTime.now();

    if (_lastClickTime != null) {
      final timeSinceLastClick = now.difference(_lastClickTime!);

      if (timeSinceLastClick <= _doubleClickThreshold) {
        // 检测到双击
        Logger.info('托盘图标被左键双击，显示窗口');
        showWindow();
        _lastClickTime = null; // 重置,避免三击被识别为双击
        return;
      }
    }

    // 记录本次点击时间
    _lastClickTime = now;
    Logger.info('托盘图标被左键单击');
  }

  // 托盘图标右键点击事件
  @override
  void onTrayIconRightMouseDown() {
    // 状态切换期间禁用托盘交互
    if (_isSwitching) {
      Logger.debug('状态切换中，忽略托盘右键点击');
      return;
    }

    Logger.info('托盘图标被右键点击，弹出菜单');
    // 使用 bringAppToFront 参数改善菜单焦点行为
    // ignore: deprecated_member_use
    trayManager.popUpContextMenu(bringAppToFront: true);
  }

  // 托盘图标鼠标释放事件
  @override
  void onTrayIconMouseUp() {
    // 左键释放，不做任何操作
  }

  // 托盘图标右键释放事件
  @override
  void onTrayIconRightMouseUp() {
    // 右键释放，菜单已由 onTrayIconRightMouseDown 处理
  }

  // 托盘菜单项点击事件
  @override
  void onTrayMenuItemClick(MenuItem menuItem) {
    Logger.info('托盘菜单项被点击：${menuItem.key}');

    switch (menuItem.key) {
      case 'close_menu':
        // 关闭菜单，不执行任何操作
        Logger.info('关闭菜单被点击，菜单将自动关闭');
        break;
      case 'show_window':
        // Fire-and-forget，内部已有错误处理
        showWindow().catchError((e) {
          Logger.error('从托盘显示窗口失败：$e');
        });
        break;
      case 'toggle_proxy':
        // Fire-and-forget，内部已有完整的错误处理和状态管理
        toggleProxy().catchError((e) {
          Logger.error('从托盘切换代理异常：$e');
        });
        break;
      case 'toggle_tun':
        // Fire-and-forget，内部已有完整的错误处理和状态管理
        toggleTun().catchError((e) {
          Logger.error('从托盘切换虚拟网卡异常：$e');
        });
        break;
      case 'outbound_mode_rule':
        switchOutboundMode('rule').catchError((e) {
          Logger.error('从托盘切换出站模式异常：$e');
        });
        break;
      case 'outbound_mode_global':
        switchOutboundMode('global').catchError((e) {
          Logger.error('从托盘切换出站模式异常：$e');
        });
        break;
      case 'outbound_mode_direct':
        switchOutboundMode('direct').catchError((e) {
          Logger.error('从托盘切换出站模式异常：$e');
        });
        break;
      case 'exit':
        // 退出操作不需要 catchError，内部已处理
        exitApp();
        break;
    }
  }

  // 显示窗口,修复隐藏后恢复闪屏问题
  Future<void> showWindow() async {
    try {
      // 先检查窗口是否可见且不透明度不为0,避免不必要操作
      final isVisible = await windowManager.isVisible();
      final opacity = await windowManager.getOpacity();

      if (isVisible && opacity > 0) {
        // 窗口已显示且不透明,仅需聚焦
        await windowManager.focus();
        Logger.info('窗口已可见且不透明（opacity: $opacity），仅执行聚焦');
        return;
      }

      // 获取保存的窗口状态
      final shouldMaximize = AppPreferences.instance.getIsMaximized();

      if (opacity < 1.0) {
        await windowManager.setOpacity(1.0);
        Logger.info('窗口不透明度已恢复（opacity: $opacity → 1.0）');
      }

      // 根据保存的状态决定是否最大化
      if (shouldMaximize) {
        await windowManager.maximize();
      }

      await windowManager.show();
      await windowManager.focus();

      Logger.info('窗口已显示 (最大化：$shouldMaximize)');

      // 窗口显示后立即更新托盘菜单，禁用"显示窗口"选项
      AppTrayManager().updateTrayMenuManually();
    } catch (e) {
      Logger.error('显示窗口失败：$e');
    }
  }

  // 切换代理开关
  Future<void> toggleProxy() async {
    if (_clashProvider == null) {
      Logger.warning('ClashProvider 未设置，无法切换代理');
      return;
    }

    if (_subscriptionProvider == null) {
      Logger.warning('SubscriptionProvider 未设置，无法获取配置文件路径');
      return;
    }

    // 设置切换标志，禁用托盘交互
    _isSwitching = true;

    // 托盘菜单勾选状态基于系统代理,切换逻辑也基于系统代理
    final manager = ClashManager.instance;
    final isSystemProxyEnabled = manager.isSystemProxyEnabled;
    final isRunning = _clashProvider!.isCoreRunning;

    Logger.info(
      '从托盘切换代理开关 - 核心状态: ${isRunning ? "运行中" : "已停止"}, 系统代理: ${isSystemProxyEnabled ? "已启用" : "未启用"}',
    );

    try {
      if (isSystemProxyEnabled) {
        // 系统代理已开启 → 关闭系统代理(不停止核心)
        await manager.disableSystemProxy();
        Logger.info('系统代理已通过托盘关闭(核心保持运行)');
      } else {
        // 系统代理未开启 → 启动核心(若未运行) + 开启系统代理
        if (!isRunning) {
          // 核心未运行,需先启动
          final configPath = _subscriptionProvider!.getSubscriptionConfigPath();
          if (configPath == null) {
            Logger.warning('没有可用的订阅配置文件，无法启动代理');
            // 提前返回前必须恢复 _isSwitching
            _isSwitching = false;
            return;
          }
          await _clashProvider!.start(configPath: configPath);
          Logger.info('核心已通过托盘启动');
        }

        // 开启系统代理
        await manager.enableSystemProxy();
        Logger.info('系统代理已通过托盘启用');
      }
    } catch (e) {
      Logger.error('从托盘切换代理失败：$e');
      // 错误已记录,托盘菜单会在下次状态更新时恢复到正确状态
    } finally {
      _isSwitching = false;
    }
  }

  // 切换虚拟网卡模式
  Future<void> toggleTun() async {
    // 设置切换标志，禁用托盘交互
    _isSwitching = true;

    final manager = ClashManager.instance;
    final isTunEnabled = _clashProvider!.configState.isTunEnabled;

    Logger.info('从托盘切换虚拟网卡模式 - 当前状态：${isTunEnabled ? "已启用" : "未启用"}');

    try {
      // 切换虚拟网卡模式（等待结果）
      await manager.setTunEnabled(!isTunEnabled);
    } catch (e) {
      Logger.error('从托盘切换虚拟网卡模式失败：$e');
    } finally {
      _isSwitching = false;
    }
  }

  // 切换出站模式
  Future<void> switchOutboundMode(String outboundMode) async {
    // 设置切换标志，禁用托盘交互
    _isSwitching = true;

    final manager = ClashManager.instance;
    final currentOutboundMode = _clashProvider!.configState.outboundMode;

    // 如果已经是当前模式，直接返回
    if (currentOutboundMode == outboundMode) {
      Logger.debug('出站模式已经是 $outboundMode，无需切换');
      _isSwitching = false;
      // 即使无需切换，也更新托盘菜单确保状态一致
      AppTrayManager().updateTrayMenuManually();
      return;
    }

    Logger.info('从托盘切换出站模式: $currentOutboundMode → $outboundMode');

    try {
      final success = await manager.setOutboundMode(outboundMode);

      if (success) {
        Logger.info('出站模式已从托盘切换到: $outboundMode');
        // 确保状态同步：强制触发一次状态更新通知
        // 这样主页卡片和其他监听器都能收到更新
        Future.microtask(() {
          // 延迟一个微任务确保状态已完全更新
          if (_clashProvider!.configState.outboundMode == outboundMode) {
            Logger.debug('托盘出站模式切换完成，触发状态同步通知');
          }
        });
      } else {
        Logger.warning('从托盘切换出站模式失败，保持原模式: $currentOutboundMode');
      }
    } catch (e) {
      Logger.error('从托盘切换出站模式失败：$e');
    } finally {
      _isSwitching = false;
    }
  }

  // 退出应用（委托给窗口退出处理器）
  Future<void> exitApp() async {
    await WindowExitHandler.exitApp();
  }
}

import 'dart:io';
import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import 'package:stelliberty/theme/dynamic_theme.dart';
import 'package:stelliberty/providers/window_effect_provider.dart';
import 'package:stelliberty/providers/app_update_provider.dart';
import 'package:stelliberty/ui/layout/main_layout.dart';
import 'package:stelliberty/ui/layout/title_bar.dart';
import 'package:stelliberty/ui/widgets/app_update_dialog.dart';
import 'package:stelliberty/ui/widgets/modern_toast.dart';
import 'package:stelliberty/clash/manager/manager.dart';
import 'package:stelliberty/services/log_print_service.dart';

// 应用的根 Widget，负责提供动态主题和生命周期管理。
class BasicLayout extends StatefulWidget {
  const BasicLayout({super.key});

  @override
  State<BasicLayout> createState() => _BasicLayoutState();
}

class _BasicLayoutState extends State<BasicLayout> with WidgetsBindingObserver {
  VoidCallback? _updateListener;

  @override
  void initState() {
    super.initState();
    // 注册应用生命周期监听器
    WidgetsBinding.instance.addObserver(this);

    // 监听自动更新
    _setupUpdateListener();
  }

  @override
  void dispose() {
    // 移除更新监听器
    _removeUpdateListener();
    // 移除应用生命周期监听器
    WidgetsBinding.instance.removeObserver(this);
    super.dispose();
  }

  // 设置更新监听器
  void _setupUpdateListener() {
    // 延迟到下一帧，确保 MaterialApp 已构建完成
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!mounted) return;

      final updateProvider = context.read<AppUpdateProvider>();

      // 创建监听器
      _updateListener = () {
        if (!mounted) return;

        final updateInfo = updateProvider.latestUpdateInfo;
        // 检查是否有更新且对话框未显示
        if (updateInfo != null &&
            updateInfo.hasUpdate &&
            !updateProvider.isDialogShown) {
          // 标记对话框已显示（防止重复）
          updateProvider.markDialogShown();

          // 使用 GlobalKey 访问 Navigator，避免 context 问题
          final navigator = ModernToast.navigatorKey.currentState;
          if (navigator != null) {
            // 使用 unawaited 避免持有 Future 引用
            AppUpdateDialog.show(navigator.context, updateInfo)
                .then((_) {
                  // 对话框关闭后清除更新信息
                  if (mounted) {
                    updateProvider.clearUpdateInfo();
                  }
                })
                .catchError((error) {
                  // 捕获对话框显示过程中的异常，防止未处理的 Future 错误
                  Logger.warning('更新对话框异常: $error');
                });
          }
        }
      };

      // 添加监听器
      updateProvider.addListener(_updateListener!);
    });
  }

  // 移除应用更新监听器
  void _removeUpdateListener() {
    if (_updateListener != null) {
      try {
        final updateProvider = context.read<AppUpdateProvider>();
        updateProvider.removeListener(_updateListener!);
        Logger.debug('已移除应用更新监听器');
      } catch (e) {
        Logger.warning('移除应用更新监听器失败: $e');
      }
      _updateListener = null;
    }
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    super.didChangeAppLifecycleState(state);

    // 当应用即将退出时，清理 Clash 进程
    if (state == AppLifecycleState.detached) {
      Logger.info('应用即将退出，正在清理 Clash 进程...');
      _cleanupOnExit();
    }
  }

  // 应用退出时的清理工作
  void _cleanupOnExit() {
    try {
      // 同步停止 Clash（先禁用代理，再停止核心）
      ClashManager.instance.disableSystemProxy();
      ClashManager.instance.stopCore();
      Logger.info('Clash 进程清理完成');
    } catch (e) {
      Logger.error('清理 Clash 进程时出错：$e');
    }
  }

  @override
  Widget build(BuildContext context) {
    return const DynamicThemeApp(home: AppContent());
  }
}

// 应用的主要内容区，负责构建应用外壳（Scaffold）并响应窗口效果。
class AppContent extends StatelessWidget {
  const AppContent({super.key});

  @override
  Widget build(BuildContext context) {
    return Consumer<WindowEffectProvider>(
      builder: (context, windowEffectProvider, child) {
        return Scaffold(
          // 根据窗口效果提供者（WindowEffectProvider）的状态，动态设置背景色
          backgroundColor: windowEffectProvider.windowEffectBackgroundColor,
          body: const _AppBody(), // 将核心布局提取到独立的 Widget
        );
      },
    );
  }
}

// 应用的核心布局，包含标题栏和主页。
class _AppBody extends StatelessWidget {
  const _AppBody();

  bool get _isMobile => Platform.isAndroid || Platform.isIOS;

  @override
  Widget build(BuildContext context) {
    return Column(
      children: [
        // 只在桌面平台显示自定义标题栏
        if (!_isMobile) const WindowTitleBar(),
        const Expanded(child: HomePage()),
      ],
    );
  }
}

import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import 'package:stelliberty/atomic/platform_helper.dart';
import 'package:stelliberty/theme/dynamic_theme.dart';
import 'package:stelliberty/providers/window_effect_provider.dart';
import 'package:stelliberty/providers/app_update_provider.dart';
import 'package:stelliberty/ui/layout/main_layout.dart';
import 'package:stelliberty/ui/layout/title_bar.dart';
import 'package:stelliberty/ui/widgets/app_update_dialog.dart';
import 'package:stelliberty/ui/widgets/modern_toast.dart';
import 'package:stelliberty/clash/manager/clash_manager.dart';
import 'package:stelliberty/services/log_print_service.dart';

// 应用根组件
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
    WidgetsBinding.instance.addObserver(this);
    _setupUpdateListener();
  }

  @override
  void dispose() {
    _removeUpdateListener();
    WidgetsBinding.instance.removeObserver(this);
    super.dispose();
  }

  void _setupUpdateListener() {
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!mounted) return;

      final updateProvider = context.read<AppUpdateProvider>();
      _updateListener = () => _showUpdateDialogIfNeeded(updateProvider);
      updateProvider.addListener(_updateListener!);
    });
  }

  void _removeUpdateListener() {
    if (_updateListener == null) return;

    try {
      final updateProvider = context.read<AppUpdateProvider>();
      updateProvider.removeListener(_updateListener!);
      Logger.debug('已移除应用更新监听器');
    } catch (e) {
      Logger.warning('移除应用更新监听器失败: $e');
    }
    _updateListener = null;
  }

  void _showUpdateDialogIfNeeded(AppUpdateProvider updateProvider) {
    if (!mounted) return;

    final updateInfo = updateProvider.latestUpdateInfo;
    if (updateInfo == null ||
        !updateInfo.hasUpdate ||
        updateProvider.isDialogShown) {
      return;
    }

    updateProvider.markDialogShown();

    final navigator = ModernToast.navigatorKey.currentState;
    if (navigator == null) return;

    AppUpdateDialog.show(navigator.context, updateInfo)
        .then((_) {
          if (mounted) {
            updateProvider.clearUpdateInfo();
          }
        })
        .catchError((error) {
          Logger.warning('更新对话框异常: $error');
        });
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    super.didChangeAppLifecycleState(state);

    if (state == AppLifecycleState.detached) {
      Logger.info('应用即将退出，正在清理 Clash 进程...');
      _cleanupOnExit();
    }
  }

  void _cleanupOnExit() {
    try {
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

class AppContent extends StatelessWidget {
  const AppContent({super.key});

  @override
  Widget build(BuildContext context) {
    return Consumer<WindowEffectProvider>(
      builder: (context, windowEffectProvider, child) {
        return Scaffold(
          backgroundColor: windowEffectProvider.windowEffectBackgroundColor,
          body: const _AppBody(),
        );
      },
    );
  }
}

class _AppBody extends StatelessWidget {
  const _AppBody();

  @override
  Widget build(BuildContext context) {
    return Column(
      children: [
        if (!PlatformHelper.isMobile) const WindowTitleBar(),
        const Expanded(child: HomePage()),
      ],
    );
  }
}

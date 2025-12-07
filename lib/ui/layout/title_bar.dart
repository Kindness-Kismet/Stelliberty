import 'dart:async';
import 'dart:io';
import 'package:flutter/material.dart';
import 'package:rinf/rinf.dart';
import 'package:provider/provider.dart';
import 'package:window_manager/window_manager.dart';
import 'package:stelliberty/providers/window_effect_provider.dart';
import 'package:stelliberty/utils/window_state.dart';
import 'package:stelliberty/storage/preferences.dart';
import 'package:stelliberty/utils/logger.dart';
import 'package:stelliberty/clash/manager/manager.dart';
import 'package:stelliberty/tray/tray_manager.dart';

// 自定义标题栏组件
class WindowTitleBar extends StatelessWidget {
  const WindowTitleBar({super.key});

  @override
  Widget build(BuildContext context) {
    return Consumer<WindowEffectProvider>(
      builder: (context, windowEffectProvider, child) {
        return Container(
          height: 40, // 标题栏高度
          color: windowEffectProvider.windowEffectBackgroundColor,
          child: Row(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              // 窗口拖动区域 - 使用 GestureDetector 替代 MoveWindow
              Expanded(
                child: GestureDetector(
                  onPanStart: (_) {
                    windowManager.startDragging();
                  },
                  onDoubleTap: () {
                    WindowStateManager.handleMaximizeRestore();
                  },
                  child: Container(
                    color: Colors.transparent,
                    // 整个区域都可以拖拽
                  ),
                ),
              ),
              // 窗口控制按钮（最小化、最大化、关闭）
              const WindowButtons(),
            ],
          ),
        );
      },
    );
  }
}

// 自定义窗口按钮颜色配置类
class _WindowButtonColors {
  final Color iconNormal;
  final Color mouseOver;
  final Color mouseDown;

  const _WindowButtonColors({
    required this.iconNormal,
    required this.mouseOver,
    required this.mouseDown,
  });
}

// 窗口控制按钮组合（最小化、最大化、关闭）
class WindowButtons extends StatelessWidget {
  const WindowButtons({super.key});

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    // 定义通用按钮颜色
    final buttonColors = _WindowButtonColors(
      iconNormal: theme.colorScheme.onSurface,
      mouseOver: theme.colorScheme.onSurface.withAlpha(26), // 鼠标悬停时更亮
      mouseDown: theme.colorScheme.onSurface.withAlpha(51), // 鼠标按下时更亮
    );

    // 定义关闭按钮的特殊颜色
    final closeButtonColors = _WindowButtonColors(
      iconNormal: theme.colorScheme.onSurface,
      mouseOver: Colors.red, // 鼠标悬停时为红色
      mouseDown: Colors.red.shade800, // 鼠标按下时为深红色
    );

    return Row(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        // 最小化按钮
        _CustomWindowButton(
          icon: Icons.horizontal_rule_rounded,
          onPressed: () => windowManager.minimize(),
          colors: buttonColors,
        ),
        // 最大化/还原按钮
        _MaximizeRestoreButton(
          colors: buttonColors,
          onPressed: WindowStateManager.handleMaximizeRestore,
        ),
        // 关闭按钮
        _CustomWindowButton(
          icon: Icons.close_rounded,
          onPressed: _handleCloseButtonPressed,
          colors: closeButtonColors,
          isClose: true,
        ),
      ],
    );
  }

  // 处理关闭按钮点击事件
  static Future<void> _handleCloseButtonPressed() async {
    // 检查是否启用最小化到托盘
    final minimizeToTray = AppPreferences.instance.getMinimizeToTray();

    if (minimizeToTray) {
      // 最小化到托盘：先保存当前窗口状态，再隐藏窗口
      Logger.info('用户点击关闭按钮，最小化到托盘...');

      // 保存当前窗口状态（包括最大化状态和尺寸位置）
      try {
        final isMaximized = await windowManager.isMaximized();
        await AppPreferences.instance.setIsMaximized(isMaximized);

        // 非最大化时保存尺寸和位置
        if (!isMaximized) {
          final size = await windowManager.getSize();
          final position = await windowManager.getPosition();
          await Future.wait([
            AppPreferences.instance.setWindowSize(size),
            AppPreferences.instance.setWindowPosition(position),
          ]);
        }

        WindowStateManager.clearCache();
        Logger.debug('最小化到托盘前已保存窗口状态: isMaximized=$isMaximized');
      } catch (e) {
        Logger.error('保存窗口状态失败：$e');
      }

      await windowManager.setOpacity(0.99);
      await windowManager.hide();

      // 窗口隐藏后立即更新托盘菜单，恢复"显示窗口"选项
      AppTrayManager().updateTrayMenuManually();
      return;
    }

    // 完全退出应用
    Logger.info('用户点击关闭按钮，开始清理流程...');

    // 立即停止托盘图标更新，避免退出时图标闪烁
    AppTrayManager().beginExit();

    // 1. 先停止 Clash 进程（最重要）
    try {
      if (ClashManager.instance.isCoreRunning) {
        Logger.info('正在停止 Clash 进程...');
        // 先禁用系统代理，再停止核心
        await ClashManager.instance.disableSystemProxy();
        await ClashManager.instance.stopCore();
        Logger.info('Clash 进程已停止');
      }
    } catch (e) {
      Logger.error('停止 Clash 进程时出错：$e');
    }

    // 2. 保存窗口状态
    try {
      await WindowStateManager.saveStateOnClose();
      Logger.info('窗口状态已保存');
    } catch (e) {
      Logger.error('保存窗口状态失败：$e');
    }

    // 3. 关闭 Rust 异步运行时
    finalizeRust();

    // 4. 退出应用
    Logger.info('应用即将退出');
    exit(0);
  }
}

// 自定义窗口按钮
class _CustomWindowButton extends StatefulWidget {
  final IconData icon;
  final VoidCallback onPressed;
  final _WindowButtonColors colors;
  final bool isClose;

  const _CustomWindowButton({
    required this.icon,
    required this.onPressed,
    required this.colors,
    this.isClose = false,
  });

  @override
  State<_CustomWindowButton> createState() => _CustomWindowButtonState();
}

class _CustomWindowButtonState extends State<_CustomWindowButton> {
  bool _isHovered = false;
  bool _isPressed = false;

  void _setHovered(bool value) {
    if (_isHovered != value) {
      setState(() => _isHovered = value);
    }
  }

  void _setPressed(bool value) {
    if (_isPressed != value) {
      setState(() => _isPressed = value);
    }
  }

  @override
  Widget build(BuildContext context) {
    // 根据状态计算颜色
    final (backgroundColor, iconColor) = _isPressed
        ? (
            widget.isClose ? Colors.red.shade800 : widget.colors.mouseDown,
            widget.isClose ? Colors.white : widget.colors.iconNormal,
          )
        : _isHovered
        ? (
            widget.isClose ? Colors.red : widget.colors.mouseOver,
            widget.isClose ? Colors.white : widget.colors.iconNormal,
          )
        : (Colors.transparent, widget.colors.iconNormal);

    return MouseRegion(
      onEnter: (_) => _setHovered(true),
      onExit: (_) => _setHovered(false),
      child: GestureDetector(
        onTapDown: (_) => _setPressed(true),
        onTapUp: (_) => _setPressed(false),
        onTapCancel: () => _setPressed(false),
        onTap: () async {
          // 对于关闭按钮，先重置状态，等待渲染完成后再执行操作
          if (widget.isClose) {
            setState(() {
              _isHovered = false;
              _isPressed = false;
            });
            // 等待下一帧渲染完成，确保状态已经更新到 UI
            await Future.delayed(Duration.zero);
            WidgetsBinding.instance.addPostFrameCallback((_) {
              widget.onPressed();
            });
          } else {
            widget.onPressed();
          }
        },
        child: Container(
          width: 46,
          height: 40,
          decoration: BoxDecoration(color: backgroundColor),
          child: Icon(
            widget.icon,
            size: 16,
            weight: 700, // 加粗图标
            color: iconColor,
          ),
        ),
      ),
    );
  }
}

// 动态最大化/还原按钮
class _MaximizeRestoreButton extends StatefulWidget {
  final _WindowButtonColors colors;
  final VoidCallback onPressed;

  const _MaximizeRestoreButton({required this.colors, required this.onPressed});

  @override
  State<_MaximizeRestoreButton> createState() => _MaximizeRestoreButtonState();
}

class _MaximizeRestoreButtonState extends State<_MaximizeRestoreButton>
    with WindowListener {
  bool _isMaximized = false;
  Timer? _saveDebounceTimer;

  @override
  void initState() {
    super.initState();
    windowManager.addListener(this);
    _updateMaximizeState();
  }

  @override
  void dispose() {
    windowManager.removeListener(this);
    _saveDebounceTimer?.cancel();
    super.dispose();
  }

  // 窗口状态变化监听
  @override
  void onWindowMaximize() {
    _updateMaximizeState();
  }

  @override
  void onWindowUnmaximize() {
    _updateMaximizeState();
  }

  @override
  void onWindowRestore() {
    _updateMaximizeState();
  }

  // 监听窗口大小变化，自动保存
  @override
  void onWindowResize() {
    _saveWindowStateDebounced();
  }

  // 监听窗口位置变化，自动保存
  @override
  void onWindowMove() {
    _saveWindowStateDebounced();
  }

  // 防抖保存窗口状态
  void _saveWindowStateDebounced() {
    _saveDebounceTimer?.cancel();
    _saveDebounceTimer = Timer(const Duration(milliseconds: 500), () async {
      final isMaximized = await windowManager.isMaximized();

      if (!isMaximized) {
        // 只在非最大化时保存
        try {
          final size = await windowManager.getSize();
          final position = await windowManager.getPosition();
          await Future.wait([
            AppPreferences.instance.setWindowSize(size),
            AppPreferences.instance.setWindowPosition(position),
          ]);

          // 关键修复：清除缓存，确保下次读取最新状态
          WindowStateManager.clearCache();

          // Logger.debug('窗口状态已自动保存: 尺寸=$size, 位置=$position');
        } catch (e) {
          Logger.error('自动保存窗口状态失败：$e');
        }
      }
    });
  }

  void _updateMaximizeState() async {
    final isMaximized = await windowManager.isMaximized();
    if (mounted && isMaximized != _isMaximized) {
      setState(() {
        _isMaximized = isMaximized;
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    return _CustomWindowButton(
      icon: _isMaximized
          ? Icons.filter_none_rounded
          : Icons.crop_square_rounded,
      onPressed: widget.onPressed,
      colors: widget.colors,
    );
  }
}

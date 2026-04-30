import 'dart:async';

import 'package:flutter/gestures.dart';
import 'package:flutter/material.dart';

// 现代化的 Tooltip 组件
class ModernTooltip extends StatefulWidget {
  final String message;
  final Widget child;
  final bool? preferBelow;
  final EdgeInsetsGeometry? padding;
  final EdgeInsetsGeometry? margin;
  final double? verticalOffset;
  final Duration? waitDuration;
  final Duration? strictWaitDuration;
  final bool isFeedbackEnabled;

  const ModernTooltip({
    super.key,
    required this.message,
    required this.child,
    this.preferBelow,
    this.padding,
    this.margin,
    this.verticalOffset,
    this.waitDuration,
    this.strictWaitDuration,
    this.isFeedbackEnabled = true,
  });

  @override
  State<ModernTooltip> createState() => _ModernTooltipState();
}

class _ModernTooltipState extends State<ModernTooltip> {
  final _tooltipKey = GlobalKey<TooltipState>();
  Timer? _strictWaitTimer;
  bool _isStrictTooltipEnabled = false;

  bool get _hasStrictWaitDuration => widget.strictWaitDuration != null;

  void _handleStrictEnter(PointerEnterEvent event) {
    final strictWaitDuration = widget.strictWaitDuration;
    if (strictWaitDuration == null) return;

    _strictWaitTimer?.cancel();
    _strictWaitTimer = Timer(strictWaitDuration, () {
      if (!mounted) return;
      setState(() => _isStrictTooltipEnabled = true);
      WidgetsBinding.instance.addPostFrameCallback((_) {
        if (!mounted || !_isStrictTooltipEnabled) return;
        _tooltipKey.currentState?.ensureTooltipVisible();
      });
    });
  }

  void _handleStrictExit(PointerExitEvent event) {
    if (!_hasStrictWaitDuration) return;

    _strictWaitTimer?.cancel();
    _strictWaitTimer = null;
    if (_isStrictTooltipEnabled) {
      setState(() => _isStrictTooltipEnabled = false);
    }
  }

  @override
  void didUpdateWidget(ModernTooltip oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.message == widget.message &&
        oldWidget.strictWaitDuration == widget.strictWaitDuration) {
      return;
    }

    _strictWaitTimer?.cancel();
    _strictWaitTimer = null;
    _isStrictTooltipEnabled = false;
  }

  @override
  void dispose() {
    _strictWaitTimer?.cancel();
    super.dispose();
  }

  Widget _buildTooltip({
    GlobalKey<TooltipState>? key,
    Duration? waitDuration,
    TooltipTriggerMode? triggerMode,
  }) {
    final colorScheme = Theme.of(context).colorScheme;
    final isDark = Theme.of(context).brightness == Brightness.dark;

    // 使用动态主题色：surfaceContainerHighest 作为背景
    final backgroundColor = colorScheme.surfaceContainerHighest;
    final textColor = colorScheme.onSurface;

    return Tooltip(
      key: key,
      message: widget.message,
      decoration: BoxDecoration(
        color: backgroundColor,
        borderRadius: BorderRadius.circular(6),
        boxShadow: [
          BoxShadow(
            color: Colors.black.withValues(alpha: isDark ? 0.15 : 0.1),
            blurRadius: 6,
            offset: const Offset(0, 2),
          ),
        ],
      ),
      textStyle: TextStyle(
        color: textColor,
        fontSize: 12,
        fontWeight: FontWeight.w500,
        letterSpacing: 0.2,
        height: 1.2,
      ),
      padding:
          widget.padding ??
          const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
      margin: widget.margin ?? const EdgeInsets.all(8),
      preferBelow: widget.preferBelow,
      verticalOffset: widget.verticalOffset ?? 16,
      waitDuration: waitDuration,
      triggerMode: triggerMode,
      enableFeedback: widget.isFeedbackEnabled,
      child: widget.child,
    );
  }

  @override
  Widget build(BuildContext context) {
    if (!_hasStrictWaitDuration) {
      return _buildTooltip(waitDuration: widget.waitDuration);
    }

    return MouseRegion(
      onEnter: _handleStrictEnter,
      onExit: _handleStrictExit,
      child: _isStrictTooltipEnabled
          ? _buildTooltip(
              key: _tooltipKey,
              waitDuration: Duration.zero,
              triggerMode: TooltipTriggerMode.manual,
            )
          : widget.child,
    );
  }
}

// 带图标的 Tooltip 变体
class ModernIconTooltip extends StatelessWidget {
  final String message;
  final IconData icon;
  final VoidCallback? onPressed;
  final double iconSize;
  final bool isFilled;

  const ModernIconTooltip({
    super.key,
    required this.message,
    required this.icon,
    this.onPressed,
    this.iconSize = 20,
    this.isFilled = true,
  });

  @override
  Widget build(BuildContext context) {
    return ModernTooltip(
      message: message,
      child: isFilled
          ? IconButton.filledTonal(
              icon: Icon(icon),
              onPressed: onPressed,
              iconSize: iconSize,
            )
          : IconButton(
              icon: Icon(icon),
              onPressed: onPressed,
              iconSize: iconSize,
            ),
    );
  }
}

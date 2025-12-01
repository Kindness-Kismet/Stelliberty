import 'package:flutter/material.dart';

// 现代化的 Tooltip 组件
class ModernTooltip extends StatelessWidget {
  final String message;
  final Widget child;
  final bool? preferBelow;
  final EdgeInsetsGeometry? padding;
  final EdgeInsetsGeometry? margin;
  final double? verticalOffset;
  final bool enableFeedback;

  const ModernTooltip({
    super.key,
    required this.message,
    required this.child,
    this.preferBelow,
    this.padding,
    this.margin,
    this.verticalOffset,
    this.enableFeedback = true,
  });

  @override
  Widget build(BuildContext context) {
    final colorScheme = Theme.of(context).colorScheme;
    final isDark = Theme.of(context).brightness == Brightness.dark;

    // 使用动态主题色：surfaceContainerHighest 作为背景
    final backgroundColor = colorScheme.surfaceContainerHighest;
    final textColor = colorScheme.onSurface;

    return Tooltip(
      message: message,
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
          padding ?? const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
      margin: margin ?? const EdgeInsets.all(8),
      preferBelow: preferBelow,
      verticalOffset: verticalOffset ?? 16,
      enableFeedback: enableFeedback,
      child: child,
    );
  }
}

// 带图标的 Tooltip 变体
class ModernIconTooltip extends StatelessWidget {
  final String message;
  final IconData icon;
  final VoidCallback? onPressed;
  final double iconSize;
  final bool filled;

  const ModernIconTooltip({
    super.key,
    required this.message,
    required this.icon,
    this.onPressed,
    this.iconSize = 20,
    this.filled = true,
  });

  @override
  Widget build(BuildContext context) {
    return ModernTooltip(
      message: message,
      child: filled
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

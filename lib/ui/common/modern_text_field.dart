import 'package:flutter/material.dart';

// 现代化的文本输入框组件（基于原生 TextField 重构）
//
// 更简单、更稳定的实现，使用 Flutter 原生组件
class ModernTextField extends StatefulWidget {
  final TextEditingController? controller;
  final String? labelText;
  final String? hintText;
  final String? helperText;
  final String? errorText;
  final IconData? prefixIcon;
  final Widget? suffixIcon;
  final String? suffixText;
  final Widget? suffixWidget;
  final bool showDropdownIcon;
  final VoidCallback? onDropdownTap;
  final TextInputType? keyboardType;
  final ValueChanged<String>? onChanged;
  final ValueChanged<String>? onSubmitted;
  final int? maxLines;
  final bool enabled;
  final bool obscureText;
  final EdgeInsetsGeometry? contentPadding;
  final double? height;

  const ModernTextField({
    super.key,
    this.controller,
    this.labelText,
    this.hintText,
    this.helperText,
    this.errorText,
    this.prefixIcon,
    this.suffixIcon,
    this.suffixText,
    this.suffixWidget,
    this.showDropdownIcon = false,
    this.onDropdownTap,
    this.keyboardType,
    this.onChanged,
    this.onSubmitted,
    this.maxLines = 1,
    this.enabled = true,
    this.obscureText = false,
    this.contentPadding,
    this.height,
  });

  @override
  State<ModernTextField> createState() => _ModernTextFieldState();
}

class _ModernTextFieldState extends State<ModernTextField> {
  bool _isFocused = false;
  bool _isHovering = false;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;

    // 计算背景颜色
    Color backgroundColor;
    if (!widget.enabled) {
      backgroundColor = colorScheme.surface.withValues(alpha: 0.4);
    } else if (_isFocused) {
      backgroundColor = Color.alphaBlend(
        colorScheme.primary.withValues(alpha: 0.04),
        colorScheme.surface,
      );
    } else if (_isHovering) {
      backgroundColor = Color.alphaBlend(
        colorScheme.onSurface.withValues(alpha: 0.04),
        colorScheme.surface,
      );
    } else {
      backgroundColor = colorScheme.surface;
    }

    // 边框颜色
    Color borderColor;
    if (widget.errorText != null) {
      borderColor = colorScheme.error;
    } else if (!widget.enabled) {
      borderColor = colorScheme.outline.withValues(alpha: 0.2);
    } else if (_isFocused) {
      borderColor = colorScheme.primary.withValues(alpha: 0.7);
    } else if (_isHovering) {
      borderColor = colorScheme.outline.withValues(alpha: 0.6);
    } else {
      borderColor = colorScheme.outline.withValues(alpha: 0.4);
    }

    // 构建后缀 widget
    Widget? suffixWidget;
    if (widget.suffixWidget != null) {
      suffixWidget = widget.suffixWidget;
    } else if (widget.showDropdownIcon) {
      suffixWidget = GestureDetector(
        onTap: widget.onDropdownTap,
        child: Container(
          width: 40,
          alignment: Alignment.center,
          padding: const EdgeInsets.only(right: 20),
          child: Icon(
            Icons.keyboard_arrow_down,
            size: 20,
            color: colorScheme.onSurface.withValues(alpha: 0.7),
          ),
        ),
      );
    } else if (widget.suffixIcon != null) {
      suffixWidget = widget.suffixIcon;
    }

    return MouseRegion(
      onEnter: (_) => setState(() => _isHovering = true),
      onExit: (_) => setState(() => _isHovering = false),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        mainAxisSize: MainAxisSize.min,
        children: [
          // 标签
          if (widget.labelText != null) ...[
            Padding(
              padding: const EdgeInsets.only(left: 4, bottom: 8),
              child: Text(
                widget.labelText!,
                style: theme.textTheme.bodySmall?.copyWith(
                  color: _isFocused
                      ? colorScheme.primary
                      : colorScheme.onSurface.withValues(alpha: 0.7),
                  fontWeight: FontWeight.w500,
                ),
              ),
            ),
          ],
          // 输入框容器
          Focus(
            onFocusChange: (focused) => setState(() => _isFocused = focused),
            child: Container(
              height: widget.height,
              decoration: BoxDecoration(
                color: backgroundColor,
                borderRadius: BorderRadius.circular(8),
                border: Border.all(
                  color: borderColor,
                  width: 1.5,
                ),
              ),
              child: Row(
                children: [
                  Expanded(
                    child: TextField(
                      controller: widget.controller,
                      keyboardType: widget.keyboardType,
                      onChanged: widget.onChanged,
                      onSubmitted: widget.onSubmitted,
                      maxLines: widget.maxLines,
                      enabled: widget.enabled,
                      obscureText: widget.obscureText,
                      style: theme.textTheme.bodyMedium?.copyWith(
                        color: widget.enabled
                            ? colorScheme.onSurface
                            : colorScheme.onSurface.withValues(alpha: 0.4),
                      ),
                      decoration: InputDecoration(
                        hintText: widget.hintText,
                        hintStyle: theme.textTheme.bodyMedium?.copyWith(
                          color: colorScheme.onSurface.withValues(alpha: 0.4),
                        ),
                        prefixIcon: widget.prefixIcon != null
                            ? Icon(
                                widget.prefixIcon,
                                color: _isFocused
                                    ? colorScheme.primary
                                    : colorScheme.onSurface.withValues(alpha: 0.6),
                              )
                            : null,
                        suffixText: widget.suffixText,
                        suffixStyle: theme.textTheme.bodySmall?.copyWith(
                          color: colorScheme.onSurface.withValues(alpha: 0.6),
                        ),
                        border: InputBorder.none,
                        contentPadding: widget.contentPadding ??
                            EdgeInsets.symmetric(
                              horizontal: widget.prefixIcon != null ? 12 : 16,
                              vertical: 14,
                            ),
                      ),
                    ),
                  ),
                  if (suffixWidget != null) suffixWidget,
                ],
              ),
            ),
          ),
          // 错误文本或帮助文本
          if (widget.errorText != null) ...[
            Padding(
              padding: const EdgeInsets.only(left: 4, top: 6),
              child: Text(
                widget.errorText!,
                style: theme.textTheme.bodySmall?.copyWith(
                  color: colorScheme.error,
                  fontSize: 11,
                ),
              ),
            ),
          ] else if (widget.helperText != null) ...[
            Padding(
              padding: const EdgeInsets.only(left: 4, top: 6),
              child: Text(
                widget.helperText!,
                style: theme.textTheme.bodySmall?.copyWith(
                  color: colorScheme.onSurface.withValues(alpha: 0.5),
                  fontSize: 11,
                ),
              ),
            ),
          ],
        ],
      ),
    );
  }
}
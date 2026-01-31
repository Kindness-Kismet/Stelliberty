import 'package:flutter/material.dart';
import 'package:stelliberty/atomic/platform_helper.dart';

// 文本输入组件：支持图标前缀、校验与多行输入。
// 提供半透明背景并适配明暗主题。
class TextInputField extends StatelessWidget {
  // 文本控制器
  final TextEditingController controller;

  // 标签文本
  final String label;

  // 提示文本
  final String hint;

  // 前缀图标
  final IconData icon;

  // 最小行数（用于多行输入）
  final int? minLines;

  // 最大行数（null 表示自动扩展）
  final int? maxLines;

  // 验证函数
  final String? Function(String?)? validator;

  // 是否启用
  final bool enabled;

  const TextInputField({
    super.key,
    required this.controller,
    required this.label,
    required this.hint,
    required this.icon,
    this.minLines,
    this.maxLines = 1,
    this.validator,
    this.enabled = true,
  });

  @override
  Widget build(BuildContext context) {
    final isDark = Theme.of(context).brightness == Brightness.dark;
    final isMobile = PlatformHelper.isMobile;

    final iconSize = isMobile ? 18.0 : 20.0;
    final fontSize = isMobile ? 13.0 : 14.0;
    final contentPadding = isMobile
        ? const EdgeInsets.symmetric(horizontal: 12, vertical: 12)
        : const EdgeInsets.symmetric(horizontal: 16, vertical: 16);
    final iconConstraints = isMobile
        ? const BoxConstraints(minWidth: 40, minHeight: 40)
        : const BoxConstraints(minWidth: 48, minHeight: 48);

    return Material(
      color: Colors.transparent,
      child: Container(
        decoration: BoxDecoration(
          color: isDark
              ? Colors.white.withValues(alpha: 0.04)
              : Colors.white.withValues(alpha: 0.6),
          borderRadius: BorderRadius.circular(12),
          border: Border.all(
            color: Colors.white.withValues(alpha: isDark ? 0.1 : 0.2),
          ),
        ),
        child: TextFormField(
          controller: controller,
          enabled: enabled,
          minLines: minLines,
          maxLines: maxLines,
          validator: validator,
          style: TextStyle(fontSize: fontSize),
          decoration: InputDecoration(
            labelText: label,
            hintText: hint,
            prefixIcon: Icon(icon, size: iconSize),
            prefixIconConstraints: iconConstraints,
            border: InputBorder.none,
            contentPadding: contentPadding,
            labelStyle: TextStyle(
              color: Theme.of(
                context,
              ).colorScheme.onSurface.withValues(alpha: 0.7),
              fontSize: fontSize,
            ),
            hintStyle: TextStyle(
              color: Theme.of(
                context,
              ).colorScheme.onSurface.withValues(alpha: 0.5),
              fontSize: fontSize,
            ),
          ),
        ),
      ),
    );
  }
}

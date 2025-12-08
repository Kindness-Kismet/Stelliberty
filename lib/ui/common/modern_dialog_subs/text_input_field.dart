import 'package:flutter/material.dart';

// 文本输入框组件
// 特性：
// - 毛玻璃背景效果
// - 支持图标前缀
// - 支持单行/多行输入
// - 内置表单验证
// - 自适应深色/浅色主题
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
          decoration: InputDecoration(
            labelText: label,
            hintText: hint,
            prefixIcon: Icon(icon, size: 20),
            prefixIconConstraints: const BoxConstraints(
              minWidth: 48,
              minHeight: 48,
            ),
            border: InputBorder.none,
            contentPadding: const EdgeInsets.symmetric(
              horizontal: 16,
              vertical: 16,
            ),
            labelStyle: TextStyle(
              color: Theme.of(
                context,
              ).colorScheme.onSurface.withValues(alpha: 0.7),
              fontSize: 14,
            ),
            hintStyle: TextStyle(
              color: Theme.of(
                context,
              ).colorScheme.onSurface.withValues(alpha: 0.5),
              fontSize: 14,
            ),
          ),
        ),
      ),
    );
  }
}

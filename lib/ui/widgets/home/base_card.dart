import 'package:flutter/material.dart';

// 通用基础卡片组件
//
// 提供统一的卡片视觉样式，包括：
// - 圆角、背景色、边框、阴影
// - 标题栏（图标 + 标题文字 + 可选操作）
// - 内容区域
class BaseCard extends StatelessWidget {
  // 卡片标题图标
  final IconData icon;

  // 卡片标题文字
  final String title;

  // 标题右侧的操作组件（可选）
  final Widget? trailing;

  // 卡片内容区域
  final Widget child;

  // 是否显示标题栏（默认显示）
  final bool shouldShowHeader;

  const BaseCard({
    super.key,
    required this.icon,
    required this.title,
    required this.child,
    this.trailing,
    this.shouldShowHeader = true,
  });

  @override
  Widget build(BuildContext context) {
    return Container(
      decoration: BoxDecoration(
        borderRadius: BorderRadius.circular(24),
        color: Theme.of(context).colorScheme.surface.withValues(alpha: 0.4),
        border: Border.all(
          color: Theme.of(context).colorScheme.outline.withValues(alpha: 0.3),
          width: 1,
        ),
        boxShadow: [
          BoxShadow(
            color: Colors.black.withValues(alpha: 0.1),
            blurRadius: 20,
            offset: const Offset(0, 4),
          ),
        ],
      ),
      child: Padding(
        padding: const EdgeInsets.all(24.0),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          mainAxisSize: MainAxisSize.min,
          children: [
            if (shouldShowHeader) ...[
              _buildHeader(context),
              const SizedBox(height: 16),
            ],
            child,
          ],
        ),
      ),
    );
  }

  Widget _buildHeader(BuildContext context) {
    return Row(
      children: [
        Icon(icon, size: 20, color: Theme.of(context).colorScheme.primary),
        const SizedBox(width: 8),
        Expanded(
          child: Text(
            title,
            style: Theme.of(
              context,
            ).textTheme.titleMedium?.copyWith(fontWeight: FontWeight.w600),
            overflow: TextOverflow.ellipsis,
          ),
        ),
        if (trailing != null) ...[const SizedBox(width: 8), trailing!],
      ],
    );
  }
}

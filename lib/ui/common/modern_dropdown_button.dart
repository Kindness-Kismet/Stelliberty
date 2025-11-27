import 'package:flutter/material.dart';

// 统一的下拉按钮组件
//
// 提供一致的外观和交互效果，用于所有下拉菜单的触发按钮
class CustomDropdownButton extends StatelessWidget {
  final String text;
  final bool isHovering;
  final double? width;
  final double? height;

  const CustomDropdownButton({
    super.key,
    required this.text,
    required this.isHovering,
    this.width,
    this.height,
  });

  @override
  Widget build(BuildContext context) {
    // 计算背景颜色，使用渐进式颜色混合以获得更好的视觉效果
    final originalColor = Theme.of(context).colorScheme.surface.withAlpha(180);
    final hoverOverlay = Theme.of(context).colorScheme.onSurface.withAlpha(20);
    final backgroundColor = isHovering
        ? Color.alphaBlend(hoverOverlay, originalColor)
        : originalColor;

    return Container(
      width: width,
      height: height,
      padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 4),
      decoration: BoxDecoration(
        color: backgroundColor,
        borderRadius: BorderRadius.circular(10),
        border: Border.all(
          color: Theme.of(context).colorScheme.outline.withAlpha(100),
          width: 1.5,
        ),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          const SizedBox(width: 8),
          Text(
            text,
            style: Theme.of(context).textTheme.bodyMedium?.copyWith(
              color: Theme.of(context).colorScheme.onSurface,
            ),
          ),
          const SizedBox(width: 8),
          Icon(
            Icons.keyboard_arrow_down,
            color: Theme.of(context).colorScheme.onSurface,
          ),
        ],
      ),
    );
  }
}

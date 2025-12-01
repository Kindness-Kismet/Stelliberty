import 'package:flutter/material.dart';
import 'package:stelliberty/ui/common/modern_switch.dart';
import 'package:stelliberty/ui/widgets/modern_tooltip.dart';

// 信息行数据模型
//
// 用于定义 InfoContainer 中每一行的内容
class InfoRow {
  // 左侧标签文本
  final String label;

  // 标签下方的描述文本（可选）
  final String? description;

  // 左侧图标（可选）
  final IconData? icon;

  // 图标颜色（可选，默认使用主题色）
  final Color? iconColor;

  // 右侧显示的值（当 trailing 为 null 时使用）
  final String? value;

  // 右侧自定义组件（优先级高于 value）
  final Widget? trailing;

  // 右侧操作图标（可选，显示在 value/trailing 右边）
  final IconData? actionIcon;

  // 操作图标点击回调
  final VoidCallback? onActionTap;

  // 操作图标提示文本
  final String? actionTooltip;

  // 操作图标颜色
  final Color? actionIconColor;

  // 是否为开关行
  final bool isSwitch;

  // 开关状态（仅当 isSwitch 为 true 时有效）
  final bool? switchValue;

  // 开关回调（仅当 isSwitch 为 true 时有效）
  final ValueChanged<bool>? onSwitchChanged;

  // 值的文本样式（可选）
  final TextStyle? valueStyle;

  const InfoRow({
    required this.label,
    this.description,
    this.icon,
    this.iconColor,
    this.value,
    this.trailing,
    this.actionIcon,
    this.onActionTap,
    this.actionTooltip,
    this.actionIconColor,
    this.isSwitch = false,
    this.switchValue,
    this.onSwitchChanged,
    this.valueStyle,
  });

  // 创建普通文本行
  factory InfoRow.text({
    required String label,
    required String value,
    String? description,
    IconData? icon,
    Color? iconColor,
    TextStyle? valueStyle,
    IconData? actionIcon,
    VoidCallback? onActionTap,
    String? actionTooltip,
    Color? actionIconColor,
  }) {
    return InfoRow(
      label: label,
      value: value,
      description: description,
      icon: icon,
      iconColor: iconColor,
      valueStyle: valueStyle,
      actionIcon: actionIcon,
      onActionTap: onActionTap,
      actionTooltip: actionTooltip,
      actionIconColor: actionIconColor,
    );
  }

  // 创建开关行
  factory InfoRow.switchRow({
    required String label,
    required bool value,
    String? description,
    IconData? icon,
    Color? iconColor,
    ValueChanged<bool>? onChanged,
  }) {
    return InfoRow(
      label: label,
      description: description,
      icon: icon,
      iconColor: iconColor,
      isSwitch: true,
      switchValue: value,
      onSwitchChanged: onChanged,
    );
  }

  // 创建自定义尾部组件行
  factory InfoRow.custom({
    required String label,
    required Widget trailing,
    String? description,
    IconData? icon,
    Color? iconColor,
  }) {
    return InfoRow(
      label: label,
      trailing: trailing,
      description: description,
      icon: icon,
      iconColor: iconColor,
    );
  }
}

// 通用信息容器组件
//
// 提供统一的背景样式和行布局
// 支持普通文本行、开关行和自定义组件行
class InfoContainer extends StatelessWidget {
  // 行数据列表
  final List<InfoRow> rows;

  // 行间距（默认 12）
  final double rowSpacing;

  // 内边距（默认 16）
  final EdgeInsets padding;

  // 圆角半径（默认 12）
  final double borderRadius;

  const InfoContainer({
    super.key,
    required this.rows,
    this.rowSpacing = 12,
    this.padding = const EdgeInsets.all(16),
    this.borderRadius = 12,
  });

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: padding,
      decoration: BoxDecoration(
        color: Theme.of(
          context,
        ).colorScheme.surfaceContainerHighest.withValues(alpha: 0.3),
        borderRadius: BorderRadius.circular(borderRadius),
        border: Border.all(
          color: Theme.of(context).colorScheme.outline.withValues(alpha: 0.2),
        ),
      ),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: _buildRows(context),
      ),
    );
  }

  // 构建所有行
  List<Widget> _buildRows(BuildContext context) {
    final List<Widget> widgets = [];

    for (int i = 0; i < rows.length; i++) {
      widgets.add(_buildRow(context, rows[i]));

      // 添加行间距（最后一行不添加）
      if (i < rows.length - 1) {
        widgets.add(SizedBox(height: rowSpacing));
      }
    }

    return widgets;
  }

  // 构建单行
  Widget _buildRow(BuildContext context, InfoRow row) {
    // 判断是否有图标或描述
    final hasIcon = row.icon != null;
    final hasDescription = row.description != null;

    return Row(
      mainAxisAlignment: MainAxisAlignment.spaceBetween,
      children: [
        // 左侧内容（图标 + 标签 + 描述）
        Expanded(
          child: Row(
            children: [
              // 图标（可选）
              if (hasIcon) ...[
                Icon(
                  row.icon,
                  size: 20,
                  color: row.iconColor ?? Theme.of(context).colorScheme.primary,
                ),
                const SizedBox(width: 12),
              ],
              // 标签和描述
              Expanded(
                child: hasDescription
                    ? Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        mainAxisSize: MainAxisSize.min,
                        children: [
                          // 有描述时：标签使用标题样式
                          Text(
                            row.label,
                            style: Theme.of(
                              context,
                            ).textTheme.bodyMedium?.copyWith(fontSize: 14),
                          ),
                          const SizedBox(height: 2),
                          // 描述使用较小的灰色字体
                          Text(
                            row.description!,
                            style: Theme.of(context).textTheme.bodySmall
                                ?.copyWith(
                                  color: Theme.of(context).colorScheme.onSurface
                                      .withValues(alpha: 0.6),
                                  fontSize: 12,
                                ),
                          ),
                        ],
                      )
                    : Text(
                        row.label,
                        // 有图标但无描述时：使用标题样式
                        // 无图标无描述时：使用普通信息行样式
                        style: hasIcon
                            ? Theme.of(
                                context,
                              ).textTheme.bodyMedium?.copyWith(fontSize: 14)
                            : Theme.of(context).textTheme.bodySmall?.copyWith(
                                color: Theme.of(
                                  context,
                                ).colorScheme.onSurface.withValues(alpha: 0.7),
                              ),
                      ),
              ),
            ],
          ),
        ),
        // 右侧内容
        _buildTrailing(context, row),
      ],
    );
  }

  // 构建右侧内容
  Widget _buildTrailing(BuildContext context, InfoRow row) {
    Widget content;

    // 优先使用自定义组件
    if (row.trailing != null) {
      content = row.trailing!;
    } else if (row.isSwitch) {
      // 开关行
      content = ModernSwitch(
        value: row.switchValue ?? false,
        onChanged: row.onSwitchChanged,
      );
    } else {
      // 普通文本行
      content = Text(
        row.value ?? '',
        style:
            row.valueStyle ??
            Theme.of(context).textTheme.bodyMedium?.copyWith(
              fontWeight: FontWeight.w600,
              color: Theme.of(context).colorScheme.onSurface,
            ),
      );
    }

    // 如果有操作图标，添加到右侧
    if (row.actionIcon != null) {
      return Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          content,
          const SizedBox(width: 8),
          _buildActionIcon(context, row),
        ],
      );
    }

    return content;
  }

  // 构建操作图标
  Widget _buildActionIcon(BuildContext context, InfoRow row) {
    final icon = Icon(
      row.actionIcon,
      size: 18,
      color: row.actionIconColor ?? Theme.of(context).colorScheme.primary,
    );

    if (row.onActionTap != null) {
      return ModernTooltip(
        message: row.actionTooltip ?? '',
        child: InkWell(
          onTap: row.onActionTap,
          borderRadius: BorderRadius.circular(16),
          child: Padding(padding: const EdgeInsets.all(4), child: icon),
        ),
      );
    }

    return icon;
  }
}

import 'package:flutter/material.dart';
import 'package:stelliberty/atomic/platform_helper.dart';

// 横向选项间距常量（按选项数量调整）。
const double _kHorizontalSpacingTwoOptions = 6.0;
const double _kHorizontalSpacingThreeOptions = 4.0;
const double _kHorizontalSpacingTwoOptionsMobile = 4.0;
const double _kHorizontalSpacingThreeOptionsMobile = 3.0;

// 选项数据模型
class OptionItem<T> {
  final T value;
  final String title;
  final String? subtitle;
  final IconData? icon;

  const OptionItem({
    required this.value,
    required this.title,
    this.subtitle,
    this.icon,
  });
}

// 通用选项选择器：支持横向/纵向排列的单选选项卡片。
// 用于导入方式、更新模式、代理模式等选择场景。
class OptionSelectorWidget<T> extends StatelessWidget {
  // 标题
  final String title;

  // 标题图标
  final IconData titleIcon;

  // 标题颜色
  final Color? titleColor;

  // 选项列表
  final List<OptionItem<T>> options;

  // 当前选中的值
  final T selectedValue;

  // 选项变化回调
  final ValueChanged<T> onChanged;

  // 排列方向（true: 横向, false: 纵向）
  final bool isHorizontal;

  const OptionSelectorWidget({
    super.key,
    required this.title,
    required this.titleIcon,
    this.titleColor,
    required this.options,
    required this.selectedValue,
    required this.onChanged,
    this.isHorizontal = false,
  });

  @override
  Widget build(BuildContext context) {
    final isDark = Theme.of(context).brightness == Brightness.dark;
    final colorScheme = Theme.of(context).colorScheme;
    final effectiveTitleColor = titleColor ?? colorScheme.primary;
    final isMobile = PlatformHelper.isMobile;

    final containerPadding = isMobile
        ? const EdgeInsets.all(12)
        : const EdgeInsets.all(16);
    final titleIconSize = isMobile ? 14.0 : 16.0;
    final titleFontSize = isMobile ? 12.0 : 14.0;

    return Material(
      color: Colors.transparent,
      child: Container(
        padding: containerPadding,
        decoration: BoxDecoration(
          color: isDark
              ? Colors.white.withValues(alpha: 0.04)
              : Colors.white.withValues(alpha: 0.5),
          borderRadius: BorderRadius.circular(12),
          border: Border.all(
            color: Colors.white.withValues(alpha: isDark ? 0.1 : 0.2),
          ),
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          mainAxisSize: MainAxisSize.min,
          children: [
            // 标题行
            Row(
              children: [
                Icon(
                  titleIcon,
                  color: effectiveTitleColor,
                  size: titleIconSize,
                ),
                const SizedBox(width: 8),
                Text(
                  title,
                  style: TextStyle(
                    color: effectiveTitleColor,
                    fontWeight: FontWeight.w600,
                    fontSize: titleFontSize,
                  ),
                ),
              ],
            ),
            SizedBox(height: isMobile ? 10 : 12),

            // 选项列表
            if (isHorizontal)
              _buildHorizontalOptions(context, isDark, colorScheme)
            else
              _buildVerticalOptions(context, isDark, colorScheme),
          ],
        ),
      ),
    );
  }

  // 构建横向排列的选项
  Widget _buildHorizontalOptions(
    BuildContext context,
    bool isDark,
    ColorScheme colorScheme,
  ) {
    final isMobile = PlatformHelper.isMobile;
    // 根据选项数量动态设置间距
    final spacing = options.length == 2
        ? (isMobile
              ? _kHorizontalSpacingTwoOptionsMobile
              : _kHorizontalSpacingTwoOptions)
        : (isMobile
              ? _kHorizontalSpacingThreeOptionsMobile
              : _kHorizontalSpacingThreeOptions);

    return Row(
      children: List.generate(options.length, (index) {
        final option = options[index];
        final isFirst = index == 0;
        final isLast = index == options.length - 1;

        return Expanded(
          child: Padding(
            padding: EdgeInsets.only(
              left: isFirst ? 0 : spacing,
              right: isLast ? 0 : spacing,
            ),
            child: _buildOptionCard(context, option, isDark, colorScheme),
          ),
        );
      }),
    );
  }

  // 构建纵向排列的选项
  Widget _buildVerticalOptions(
    BuildContext context,
    bool isDark,
    ColorScheme colorScheme,
  ) {
    final isMobile = PlatformHelper.isMobile;
    return Column(
      children: options.map((option) {
        return Padding(
          padding: EdgeInsets.only(
            bottom: option == options.last ? 0 : (isMobile ? 6 : 8),
          ),
          child: _buildOptionCard(context, option, isDark, colorScheme),
        );
      }).toList(),
    );
  }

  // 构建单个选项卡片
  Widget _buildOptionCard(
    BuildContext context,
    OptionItem<T> option,
    bool isDark,
    ColorScheme colorScheme,
  ) {
    final isSelected = option.value == selectedValue;
    final effectiveTitleColor = titleColor ?? colorScheme.primary;
    final isMobile = PlatformHelper.isMobile;

    final cardPadding = isMobile
        ? const EdgeInsets.all(10)
        : const EdgeInsets.all(12);
    final radioIconSize = isMobile
        ? (isHorizontal ? 16.0 : 18.0)
        : (isHorizontal ? 18.0 : 20.0);
    final titleFontSize = isMobile
        ? (isHorizontal ? 11.0 : 12.0)
        : (isHorizontal ? 13.0 : 14.0);
    final subtitleFontSize = isMobile
        ? (isHorizontal ? 9.0 : 10.0)
        : (isHorizontal ? 11.0 : 12.0);

    return Material(
      color: Colors.transparent,
      borderRadius: BorderRadius.circular(10),
      child: InkWell(
        borderRadius: BorderRadius.circular(10),
        onTap: () => onChanged(option.value),
        child: Container(
          padding: cardPadding,
          decoration: BoxDecoration(
            borderRadius: BorderRadius.circular(10),
            border: isSelected
                ? Border.all(color: effectiveTitleColor, width: 2)
                : Border.all(
                    color: Colors.white.withValues(alpha: isDark ? 0.1 : 0.2),
                  ),
            color: isSelected
                ? effectiveTitleColor.withValues(alpha: 0.08)
                : (isDark
                      ? Colors.white.withValues(alpha: 0.02)
                      : Colors.white.withValues(alpha: 0.4)),
          ),
          child: Row(
            children: [
              // 单选图标
              Icon(
                isSelected
                    ? Icons.radio_button_checked
                    : Icons.radio_button_unchecked,
                color: isSelected
                    ? effectiveTitleColor
                    : colorScheme.onSurface.withValues(alpha: 0.4),
                size: radioIconSize,
              ),
              SizedBox(
                width: isMobile
                    ? (isHorizontal ? 6 : 10)
                    : (isHorizontal ? 8 : 12),
              ),

              // 内容
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    // 标题
                    Text(
                      option.title,
                      style: TextStyle(
                        fontSize: titleFontSize,
                        fontWeight: isSelected
                            ? FontWeight.w600
                            : FontWeight.w500,
                      ),
                    ),

                    // 副标题（如果有）
                    if (option.subtitle != null) ...[
                      const SizedBox(height: 2),
                      Text(
                        option.subtitle!,
                        style: TextStyle(
                          fontSize: subtitleFontSize,
                          color: colorScheme.onSurface.withValues(alpha: 0.6),
                        ),
                      ),
                    ],
                  ],
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

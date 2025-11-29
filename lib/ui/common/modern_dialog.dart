import 'dart:ui';
import 'package:flutter/material.dart';
import 'package:stelliberty/i18n/i18n.dart';

// 对话框样式常量
class DialogConstants {
  // 对话框圆角
  static const double dialogBorderRadius = 20.0;

  // 按钮圆角
  static const double buttonBorderRadius = 10.0;

  // 标题图标容器圆角
  static const double titleIconBorderRadius = 10.0;

  // 关闭按钮圆角
  static const double closeButtonBorderRadius = 10.0;

  // 加载指示器尺寸
  static const double loadingIndicatorSize = 14.0;

  // 加载指示器与文字间距
  static const double loadingIndicatorSpacing = 12.0;

  // 加载指示器线宽
  static const double loadingIndicatorStrokeWidth = 2.0;
}

// 现代对话框基础组件
// 提取所有对话框的公共视觉风格和布局结构
// 采用插槽模式，中间内容区域完全自定义
class ModernDialog extends StatefulWidget {
  // 标题文本
  final String? title;

  // 自定义标题 Widget（优先于 title 使用）
  final Widget? titleWidget;

  // 副标题（可选）
  final String? subtitle;

  // 标题下方的自定义区域（如搜索框等）
  final Widget? headerWidget;

  // 标题图标
  final IconData titleIcon;

  // 标题图标颜色（默认使用 primary 色）
  final Color? titleIconColor;

  // 是否显示"已修改"标签
  final bool? isModified;

  // "已修改"标签文本（可选，默认使用 context.translate.common.modified）
  final String? modifiedLabel;

  // 内容区域（插槽）
  final Widget content;

  // 底部左侧区域（提示文字或操作按钮列表）
  final Widget? actionsLeft;

  // 底部左侧操作按钮列表（带图标，优先于 actionsLeft 使用）
  final List<DialogActionButton>? actionsLeftButtons;

  // 底部右侧按钮列表（不带图标，仅文字+加载指示器）
  final List<DialogActionButton> actionsRight;

  // 最大宽度
  final double maxWidth;

  // 最大高度比例（相对于屏幕高度）
  final double maxHeightRatio;

  // 圆角半径
  final double borderRadius;

  // 关闭按钮回调
  final VoidCallback? onClose;

  const ModernDialog({
    super.key,
    this.title,
    this.titleWidget,
    this.subtitle,
    this.headerWidget,
    required this.titleIcon,
    this.titleIconColor,
    this.isModified,
    this.modifiedLabel,
    required this.content,
    this.actionsLeft,
    this.actionsLeftButtons,
    required this.actionsRight,
    this.maxWidth = 720,
    this.maxHeightRatio = 0.85,
    this.borderRadius = DialogConstants.dialogBorderRadius,
    this.onClose,
  }) : assert(title != null || titleWidget != null, '必须提供 title 或 titleWidget'),
       assert(
         actionsLeft == null || actionsLeftButtons == null,
         'actionsLeft 和 actionsLeftButtons 不能同时使用',
       );

  @override
  State<ModernDialog> createState() => _ModernDialogState();
}

class _ModernDialogState extends State<ModernDialog>
    with TickerProviderStateMixin {
  late final AnimationController _animationController;
  late final Animation<double> _scaleAnimation;
  late final Animation<double> _opacityAnimation;

  @override
  void initState() {
    super.initState();

    // 初始化动画控制器
    _animationController = AnimationController(
      duration: const Duration(milliseconds: 300),
      vsync: this,
    );

    // 缩放动画：从 0.8 放大到 1.0
    _scaleAnimation = Tween<double>(begin: 0.8, end: 1.0).animate(
      CurvedAnimation(parent: _animationController, curve: Curves.easeOutBack),
    );

    // 透明度动画：从 0.0 到 1.0
    _opacityAnimation = Tween<double>(begin: 0.0, end: 1.0).animate(
      CurvedAnimation(parent: _animationController, curve: Curves.easeOut),
    );

    // 延迟启动动画，确保组件完全渲染
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (mounted) {
        _animationController.forward();
      }
    });
  }

  @override
  void dispose() {
    _animationController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final isDark = Theme.of(context).brightness == Brightness.dark;

    return AnimatedBuilder(
      animation: _animationController,
      builder: (context, child) {
        return Stack(
          children: [
            // 背景遮罩
            Container(
              color: Colors.black.withValues(alpha: isDark ? 0.5 : 0.3),
            ),
            // 对话框内容
            Center(
              child: Transform.scale(
                scale: _scaleAnimation.value,
                child: Opacity(
                  opacity: _opacityAnimation.value,
                  child: _buildDialog(),
                ),
              ),
            ),
          ],
        );
      },
    );
  }

  // 构建对话框主体
  Widget _buildDialog() {
    final isDark = Theme.of(context).brightness == Brightness.dark;

    return ConstrainedBox(
      constraints: BoxConstraints(
        maxWidth: widget.maxWidth,
        maxHeight: MediaQuery.of(context).size.height * widget.maxHeightRatio,
      ),
      child: Container(
        margin: const EdgeInsets.symmetric(horizontal: 32),
        child: ClipRRect(
          borderRadius: BorderRadius.circular(widget.borderRadius),
          child: BackdropFilter(
            filter: ImageFilter.blur(sigmaX: 16, sigmaY: 16),
            child: Container(
              decoration: BoxDecoration(
                color: isDark
                    ? Colors.white.withValues(alpha: 0.08)
                    : Colors.white.withValues(alpha: 0.7),
                borderRadius: BorderRadius.circular(widget.borderRadius),
                border: Border.all(
                  color: Colors.white.withValues(alpha: isDark ? 0.1 : 0.3),
                  width: 1,
                ),
                boxShadow: [
                  BoxShadow(
                    color: Colors.black.withValues(alpha: 0.18),
                    blurRadius: 40,
                    offset: const Offset(0, 10),
                  ),
                ],
              ),
              child: Column(
                mainAxisSize: MainAxisSize.min,
                children: [
                  _buildHeader(),
                  if (widget.headerWidget != null) _buildHeaderWidget(),
                  Flexible(child: widget.content),
                  _buildActions(),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }

  // 构建 headerWidget 区域（与顶栏相同的背景样式，但无底部边框）
  Widget _buildHeaderWidget() {
    final isDark = Theme.of(context).brightness == Brightness.dark;

    return Container(
      padding: const EdgeInsets.fromLTRB(24, 0, 24, 16),
      decoration: BoxDecoration(
        color: isDark
            ? Colors.white.withValues(alpha: 0.06)
            : Colors.white.withValues(alpha: 0.3),
        border: Border(
          bottom: BorderSide(
            color: Colors.white.withValues(alpha: isDark ? 0.1 : 0.3),
            width: 1,
          ),
        ),
      ),
      child: widget.headerWidget!,
    );
  }

  // 构建顶栏
  Widget _buildHeader() {
    final isDark = Theme.of(context).brightness == Brightness.dark;

    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 24, vertical: 20),
      decoration: BoxDecoration(
        color: isDark
            ? Colors.white.withValues(alpha: 0.06)
            : Colors.white.withValues(alpha: 0.3),
        // 如果有 headerWidget，则不显示底部边框
        border: widget.headerWidget == null
            ? Border(
                bottom: BorderSide(
                  color: Colors.white.withValues(alpha: isDark ? 0.1 : 0.3),
                  width: 1,
                ),
              )
            : null,
      ),
      child: Row(
        children: [
          // 图标容器
          Container(
            padding: const EdgeInsets.all(8),
            decoration: BoxDecoration(
              color:
                  (widget.titleIconColor ??
                          Theme.of(context).colorScheme.primary)
                      .withValues(alpha: 0.9),
              borderRadius: BorderRadius.circular(
                DialogConstants.titleIconBorderRadius,
              ),
              boxShadow: [
                BoxShadow(
                  color:
                      (widget.titleIconColor ??
                              Theme.of(context).colorScheme.primary)
                          .withValues(alpha: 0.3),
                  blurRadius: 8,
                  offset: const Offset(0, 2),
                ),
              ],
            ),
            child: Icon(widget.titleIcon, color: Colors.white, size: 20),
          ),
          const SizedBox(width: 16),
          // 标题和副标题
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                // 标题行（包含标题和"已修改"标签）
                Row(
                  mainAxisSize: MainAxisSize.min,
                  crossAxisAlignment: CrossAxisAlignment.end,
                  children: [
                    // 优先使用 titleWidget，否则使用 title
                    if (widget.titleWidget != null)
                      widget.titleWidget!
                    else
                      Text(
                        widget.title!,
                        style: Theme.of(context).textTheme.titleLarge?.copyWith(
                          fontWeight: FontWeight.w600,
                          color: Theme.of(context).colorScheme.onSurface,
                        ),
                      ),
                    // "已修改"标签
                    if (widget.isModified == true) ...[
                      const SizedBox(width: 8),
                      _buildModifiedBadge(),
                    ],
                  ],
                ),
                if (widget.subtitle != null) ...[
                  const SizedBox(height: 2),
                  Text(
                    widget.subtitle!,
                    style: Theme.of(context).textTheme.bodySmall?.copyWith(
                      color: Theme.of(
                        context,
                      ).colorScheme.onSurface.withValues(alpha: 0.6),
                    ),
                  ),
                ],
              ],
            ),
          ),
          // 关闭按钮
          Material(
            color: Colors.transparent,
            child: InkWell(
              onTap: widget.onClose ?? _handleClose,
              borderRadius: BorderRadius.circular(
                DialogConstants.closeButtonBorderRadius,
              ),
              child: Container(
                padding: const EdgeInsets.all(8),
                decoration: BoxDecoration(
                  borderRadius: BorderRadius.circular(
                    DialogConstants.closeButtonBorderRadius,
                  ),
                ),
                child: Icon(
                  Icons.close,
                  color: Theme.of(
                    context,
                  ).colorScheme.onSurface.withValues(alpha: 0.7),
                  size: 20,
                ),
              ),
            ),
          ),
        ],
      ),
    );
  }

  // 构建底部操作栏
  Widget _buildActions() {
    final isDark = Theme.of(context).brightness == Brightness.dark;

    return Material(
      color: Colors.transparent,
      child: Container(
        padding: const EdgeInsets.symmetric(horizontal: 24, vertical: 16),
        decoration: BoxDecoration(
          color: isDark
              ? Colors.white.withValues(alpha: 0.06)
              : Colors.white.withValues(alpha: 0.3),
          border: Border(
            top: BorderSide(
              color: Colors.white.withValues(alpha: isDark ? 0.1 : 0.3),
              width: 1,
            ),
          ),
        ),
        child: Row(
          children: [
            // 左侧按钮：从左边开始排列
            if (widget.actionsLeftButtons != null)
              ...widget.actionsLeftButtons!.asMap().entries.map((entry) {
                final index = entry.key;
                final action = entry.value;
                return Padding(
                  padding: EdgeInsets.only(
                    right: index < widget.actionsLeftButtons!.length - 1
                        ? 8
                        : 0,
                  ),
                  child: _buildActionButtonWithIcon(action, isDark),
                );
              })
            else if (widget.actionsLeft != null)
              widget.actionsLeft!,
            // 中间空白区域
            const Spacer(),
            // 右侧按钮：从右边开始排列
            ...widget.actionsRight.asMap().entries.map((entry) {
              final index = entry.key;
              final action = entry.value;
              return Padding(
                padding: EdgeInsets.only(left: index > 0 ? 12 : 0),
                child: _buildActionButton(action, isDark),
              );
            }),
          ],
        ),
      ),
    );
  }

  // 构建左侧操作按钮（带图标）
  Widget _buildActionButtonWithIcon(DialogActionButton action, bool isDark) {
    return OutlinedButton.icon(
      onPressed: action.isLoading ? null : action.onPressed,
      icon: action.icon != null
          ? Icon(action.icon, size: 18)
          : const SizedBox.shrink(),
      label: action.isLoading
          ? Row(
              mainAxisSize: MainAxisSize.min,
              children: [
                const SizedBox(
                  width: DialogConstants.loadingIndicatorSize,
                  height: DialogConstants.loadingIndicatorSize,
                  child: CircularProgressIndicator(
                    strokeWidth: DialogConstants.loadingIndicatorStrokeWidth,
                    valueColor: AlwaysStoppedAnimation<Color>(Colors.white70),
                  ),
                ),
                const SizedBox(width: DialogConstants.loadingIndicatorSpacing),
                Text(action.label),
              ],
            )
          : Text(action.label),
      style: OutlinedButton.styleFrom(
        padding: const EdgeInsets.symmetric(vertical: 12, horizontal: 16),
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(
            DialogConstants.buttonBorderRadius,
          ),
        ),
        side: BorderSide(
          color: isDark
              ? Colors.white.withValues(alpha: 0.2)
              : Colors.white.withValues(alpha: 0.6),
        ),
        backgroundColor: isDark
            ? Colors.white.withValues(alpha: 0.04)
            : Colors.white.withValues(alpha: 0.6),
      ),
    );
  }

  // 构建操作按钮
  Widget _buildActionButton(DialogActionButton action, bool isDark) {
    if (action.isPrimary) {
      // 主要按钮 (ElevatedButton)
      return ElevatedButton(
        onPressed: action.isLoading ? null : action.onPressed,
        style: ElevatedButton.styleFrom(
          padding: const EdgeInsets.symmetric(vertical: 12, horizontal: 16),
          backgroundColor: Theme.of(context).colorScheme.primary,
          foregroundColor: Colors.white,
          shape: RoundedRectangleBorder(
            borderRadius: BorderRadius.circular(
              DialogConstants.buttonBorderRadius,
            ),
          ),
          elevation: 0,
          shadowColor: Theme.of(
            context,
          ).colorScheme.primary.withValues(alpha: 0.5),
        ),
        child: action.isLoading
            ? Row(
                mainAxisSize: MainAxisSize.min,
                children: [
                  SizedBox(
                    width: DialogConstants.loadingIndicatorSize,
                    height: DialogConstants.loadingIndicatorSize,
                    child: CircularProgressIndicator(
                      strokeWidth: DialogConstants.loadingIndicatorStrokeWidth,
                      valueColor: AlwaysStoppedAnimation<Color>(
                        Theme.of(context).colorScheme.primary,
                      ),
                    ),
                  ),
                  const SizedBox(
                    width: DialogConstants.loadingIndicatorSpacing,
                  ),
                  Text(action.label),
                ],
              )
            : Text(action.label),
      );
    } else {
      // 次要按钮 (OutlinedButton)
      return OutlinedButton(
        onPressed: action.isLoading ? null : action.onPressed,
        style: OutlinedButton.styleFrom(
          padding: const EdgeInsets.symmetric(vertical: 12, horizontal: 16),
          shape: RoundedRectangleBorder(
            borderRadius: BorderRadius.circular(
              DialogConstants.buttonBorderRadius,
            ),
          ),
          side: BorderSide(
            color: isDark
                ? Colors.white.withValues(alpha: 0.2)
                : Colors.white.withValues(alpha: 0.6),
          ),
          backgroundColor: isDark
              ? Colors.white.withValues(alpha: 0.04)
              : Colors.white.withValues(alpha: 0.6),
        ),
        child: Text(
          action.label,
          style: TextStyle(
            color: Theme.of(
              context,
            ).colorScheme.onSurface.withValues(alpha: 0.8),
          ),
        ),
      );
    }
  }

  // 构建"已修改"标签
  Widget _buildModifiedBadge() {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 2),
      decoration: BoxDecoration(
        color: Theme.of(context).colorScheme.primary.withValues(alpha: 0.15),
        borderRadius: BorderRadius.circular(4),
      ),
      child: Text(
        widget.modifiedLabel ?? context.translate.common.modified,
        style: TextStyle(
          fontSize: 10,
          color: Theme.of(context).colorScheme.primary,
          fontWeight: FontWeight.bold,
        ),
      ),
    );
  }

  // 默认关闭处理
  void _handleClose() {
    _animationController.reverse().then((_) {
      if (mounted) {
        Navigator.of(context).pop();
      }
    });
  }
}

// 对话框按钮配置类
class DialogActionButton {
  // 按钮文本
  final String label;

  // 点击回调
  final VoidCallback? onPressed;

  // 是否为主要按钮（true: ElevatedButton, false: OutlinedButton）
  final bool isPrimary;

  // 是否显示加载状态
  final bool isLoading;

  // 图标（仅用于左侧操作按钮）
  final IconData? icon;

  const DialogActionButton({
    required this.label,
    this.onPressed,
    this.isPrimary = false,
    this.isLoading = false,
    this.icon,
  });
}

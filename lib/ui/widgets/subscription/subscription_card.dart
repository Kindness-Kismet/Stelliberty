import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:provider/provider.dart';
import 'package:stelliberty/clash/data/subscription_model.dart';
import 'package:stelliberty/clash/providers/subscription_provider.dart';
import 'package:stelliberty/i18n/i18n.dart';
import 'package:stelliberty/ui/widgets/modern_toast.dart';
import 'package:stelliberty/ui/common/modern_popup_menu.dart';
import 'package:stelliberty/ui/widgets/modern_tooltip.dart';

// 订阅卡片组件
// 显示订阅的详细信息：
// - 订阅名称和图标
// - 订阅 URL（单行省略）
// - 状态标签（自动更新、更新间隔、距下次更新时间）
// - 流量统计信息（进度条 + 数值）
// - 操作菜单（使用 ModernPopupMenu）
// 性能优化：
// - 使用 Consumer 精确监听更新状态
// - 缓存 isDark 和 colorScheme 避免重复调用
class SubscriptionCard extends StatelessWidget {
  // 订阅数据
  final Subscription subscription;

  // 是否为当前选中的订阅
  final bool isSelected;

  // 点击卡片的回调
  final VoidCallback? onTap;

  // 更新订阅的回调
  final VoidCallback? onUpdate;

  // 编辑订阅配置的回调
  final VoidCallback? onEdit;

  // 编辑订阅文件的回调
  final VoidCallback? onEditFile;

  // 删除订阅的回调
  final VoidCallback? onDelete;

  // 管理规则覆写的回调
  final VoidCallback? onManageOverride;

  // 查看运行配置的回调
  final VoidCallback? onViewConfig;

  // 查看提供者的回调
  final VoidCallback? onViewProvider;

  const SubscriptionCard({
    super.key,
    required this.subscription,
    this.isSelected = false,
    this.onTap,
    this.onUpdate,
    this.onEdit,
    this.onEditFile,
    this.onDelete,
    this.onManageOverride,
    this.onViewConfig,
    this.onViewProvider,
  });

  @override
  Widget build(BuildContext context) {
    return Consumer<SubscriptionProvider>(
      builder: (context, provider, child) {
        final isUpdating = provider.isSubscriptionUpdating(subscription.id);
        final isBatchUpdating = provider.isBatchUpdating;
        final colorScheme = Theme.of(context).colorScheme;
        final isDark = Theme.of(context).brightness == Brightness.dark;

        final mixColor = isDark ? Colors.black : Colors.white;
        final mixOpacity = 0.1;

        return Stack(
          children: [
            // 整个卡片（更新时置灰）
            Opacity(
              opacity: isUpdating ? 0.5 : 1.0,
              child: Container(
                decoration: BoxDecoration(
                  borderRadius: BorderRadius.circular(16),
                  color: Color.alphaBlend(
                    mixColor.withValues(alpha: mixOpacity),
                    isSelected
                        ? colorScheme.primaryContainer.withValues(alpha: 0.2)
                        : colorScheme.surface.withValues(
                            alpha: isDark ? 0.7 : 0.85,
                          ),
                  ),
                  border: Border.all(
                    color: isSelected
                        ? colorScheme.primary.withValues(
                            alpha: isDark ? 0.7 : 0.6,
                          )
                        : colorScheme.outline.withValues(alpha: 0.4),
                    width: 2,
                  ),
                  boxShadow: [
                    BoxShadow(
                      color: Colors.black.withValues(alpha: isDark ? 0.3 : 0.1),
                      blurRadius: isSelected ? 12 : 8,
                      offset: Offset(0, isSelected ? 3 : 2),
                    ),
                  ],
                ),
                child: Material(
                  color: Colors.transparent,
                  child: InkWell(
                    borderRadius: BorderRadius.circular(16),
                    onTap: isUpdating ? null : onTap,
                    child: Padding(
                      padding: const EdgeInsets.all(10.0),
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          // 标题行
                          _buildTitleRow(context, isUpdating, isBatchUpdating),

                          const SizedBox(height: 4),

                          // URL
                          _buildUrlText(),

                          // 弹性空间，让状态标签推到底部
                          const Spacer(),

                          // 状态标签与流量进度条并排（只有真正有流量数据时才显示进度条）
                          if (subscription.info != null &&
                              subscription.info!.total > 0)
                            _buildStatusWithTraffic(context)
                          else
                            _buildStatusChips(context),
                        ],
                      ),
                    ),
                  ),
                ),
              ),
            ),
          ],
        );
      },
    );
  }

  // 构建标题行
  Widget _buildTitleRow(
    BuildContext context,
    bool isUpdating,
    bool isBatchUpdating,
  ) {
    final isDisabled = isUpdating || isBatchUpdating;
    final colorScheme = Theme.of(context).colorScheme;

    return Row(
      children: [
        Icon(
          Icons.rss_feed,
          color: isSelected ? colorScheme.primary : Colors.grey,
        ),
        const SizedBox(width: 12),
        Expanded(
          child: Text(
            subscription.name,
            style: TextStyle(
              fontSize: 18,
              fontWeight: isSelected ? FontWeight.bold : FontWeight.w500,
            ),
            overflow: TextOverflow.ellipsis,
          ),
        ),
        if (isSelected) Icon(Icons.check_circle, color: colorScheme.primary),
        const SizedBox(width: 8),
        // 独立更新按钮（更新时显示转圈指示器）
        if (!subscription.isLocalFile)
          ModernTooltip(
            message: context.translate.subscription.updateCard,
            child: IconButton(
              onPressed: isDisabled ? null : onUpdate,
              icon: isUpdating
                  ? SizedBox(
                      width: 20,
                      height: 20,
                      child: CircularProgressIndicator(
                        strokeWidth: 2,
                        valueColor: AlwaysStoppedAnimation<Color>(
                          colorScheme.primary,
                        ),
                      ),
                    )
                  : Icon(
                      Icons.sync_rounded,
                      size: 20,
                      color: isBatchUpdating
                          ? Colors.grey.withValues(alpha: 0.3)
                          : colorScheme.primary,
                    ),
              style: IconButton.styleFrom(
                padding: const EdgeInsets.all(8),
                minimumSize: const Size(32, 32),
                tapTargetSize: MaterialTapTargetSize.shrinkWrap,
              ),
            ),
          ),
        // 更多操作菜单（使用自定义弹出菜单）
        _buildModernPopupMenu(context, isDisabled),
      ],
    );
  }

  // 构建 URL 文本
  Widget _buildUrlText() {
    return Text(
      subscription.url,
      style: TextStyle(color: Colors.grey[600], fontSize: 12),
      maxLines: 1,
      overflow: TextOverflow.ellipsis,
    );
  }

  // 构建状态标签（无流量信息时使用）
  Widget _buildStatusChips(BuildContext context) {
    return _buildStatusText(context);
  }

  // 构建状态文本
  Widget _buildStatusText(BuildContext context) {
    final List<InlineSpan> children = [];

    // 自动更新状态
    final isAutoUpdateEnabled =
        subscription.autoUpdateMode != AutoUpdateMode.disabled;
    children.add(
      TextSpan(
        text: subscription.isLocalFile
            ? context.translate.subscription.localTypeLabel
            : (isAutoUpdateEnabled
                  ? context.translate.subscription.autoUpdateLabel
                  : context.translate.subscription.manualUpdateLabel),
        style: TextStyle(
          color: subscription.isLocalFile
              ? Colors.grey
              : (isAutoUpdateEnabled ? Colors.green : Colors.grey),
          fontSize: 11,
        ),
      ),
    );

    // 距下次更新时间（仅远程订阅+自动更新+有更新记录时显示）
    if (!subscription.isLocalFile &&
        isAutoUpdateEnabled &&
        subscription.lastUpdateTime != null) {
      children.add(
        const TextSpan(
          text: ' | ',
          style: TextStyle(color: Colors.grey, fontSize: 11),
        ),
      );
      children.add(
        TextSpan(
          text: _formatNextUpdate(context),
          style: const TextStyle(color: Colors.purple, fontSize: 11),
        ),
      );
    }

    return Text.rich(
      TextSpan(children: children),
      maxLines: 1,
      overflow: TextOverflow.ellipsis,
    );
  }

  // 构建现代化弹出菜单
  // 使用 ModernPopupMenu 替代标准 PopupMenuButton，
  // 提供 Windows 11 风格的交互体验
  Widget _buildModernPopupMenu(BuildContext context, bool isDisabled) {
    return ModernPopupBox(
      targetBuilder: (open) => IconButton(
        icon: const Icon(Icons.more_vert),
        onPressed: isDisabled ? null : () => open(),
        style: IconButton.styleFrom(
          padding: const EdgeInsets.all(8),
          minimumSize: const Size(32, 32),
          tapTargetSize: MaterialTapTargetSize.shrinkWrap,
        ),
      ),
      popup: ModernPopupMenu(
        items: [
          PopupMenuItemData(
            icon: Icons.edit,
            label: context.translate.subscription.menu.configEdit,
            onPressed: onEdit,
          ),
          PopupMenuItemData(
            icon: Icons.code,
            label: context.translate.subscription.menu.fileEdit,
            onPressed: onEditFile,
          ),
          // 只有当前选中的订阅才显示运行配置查看
          if (isSelected)
            PopupMenuItemData(
              icon: Icons.visibility,
              label: context.translate.subscription.menu.configView,
              onPressed: onViewConfig,
            ),
          PopupMenuItemData(
            icon: Icons.rule,
            label: context.translate.subscription.menu.overrideManage,
            onPressed: onManageOverride,
          ),
          PopupMenuItemData(
            icon: Icons.extension,
            label: context.translate.subscription.menu.providerView,
            onPressed: onViewProvider,
          ),
          // 本地文件订阅不显示复制链接选项
          if (!subscription.isLocalFile)
            PopupMenuItemData(
              icon: Icons.copy,
              label: context.translate.subscription.menu.copyLink,
              onPressed: () => _copyUrl(context),
            ),
          PopupMenuItemData(
            icon: Icons.delete,
            label: context.translate.subscription.menu.delete,
            onPressed: onDelete,
            danger: true,
          ),
        ],
      ),
    );
  }

  // 构建状态与流量并排显示
  Widget _buildStatusWithTraffic(BuildContext context) {
    final info = subscription.info!;
    final usagePercentage = info.usagePercentage;

    return Row(
      crossAxisAlignment: CrossAxisAlignment.center,
      children: [
        // 左侧：状态标签
        _buildStatusText(context),
        const SizedBox(width: 16),
        // 中间：流量进度条（垂直居中对齐）
        Expanded(
          child: Align(
            alignment: Alignment.centerLeft,
            child: ClipRRect(
              borderRadius: BorderRadius.circular(4),
              child: LinearProgressIndicator(
                value: usagePercentage / 100,
                minHeight: 6,
                backgroundColor: Colors.grey.withAlpha((255 * 0.2).round()),
                valueColor: AlwaysStoppedAnimation<Color>(
                  usagePercentage < 80 ? Colors.green : Colors.red,
                ),
              ),
            ),
          ),
        ),
        const SizedBox(width: 12),
        // 右侧：流量数值
        Text(
          '${_formatBytes(info.used)}/${_formatBytes(info.total)}',
          style: TextStyle(
            fontSize: 11,
            fontWeight: FontWeight.w600,
            color: usagePercentage < 80 ? Colors.green : Colors.red,
          ),
        ),
        // 到期时间（如果有）
        if (info.expire > 0) ...[
          const SizedBox(width: 12),
          Text(
            info.isExpired
                ? context.translate.subscription.expired
                : _formatExpireDate(info.expire, context),
            style: TextStyle(
              fontSize: 11,
              color: info.isExpired ? Colors.red : Colors.grey[600],
            ),
          ),
        ],
      ],
    );
  }

  // 复制 URL
  void _copyUrl(BuildContext context) async {
    try {
      await Clipboard.setData(ClipboardData(text: subscription.url));
      if (context.mounted) {
        ModernToast.success(context, '链接已复制到剪贴板');
      }
    } catch (e) {
      if (context.mounted) {
        ModernToast.error(context, '复制失败: $e');
      }
    }
  }

  // 格式化字节数
  String _formatBytes(int bytes) {
    if (bytes < 1024) return '${bytes}B';
    if (bytes < 1024 * 1024) return '${(bytes / 1024).toStringAsFixed(1)}KB';
    if (bytes < 1024 * 1024 * 1024) {
      return '${(bytes / 1024 / 1024).toStringAsFixed(1)}MB';
    }
    return '${(bytes / 1024 / 1024 / 1024).toStringAsFixed(2)}GB';
  }

  // 格式化距下次更新时间
  String _formatNextUpdate(BuildContext context) {
    if (subscription.lastUpdateTime == null) {
      return context.translate.subscription.pendingUpdate;
    }

    final trans = context.translate.subscription;
    final now = DateTime.now();

    // 根据更新模式计算下次更新时间
    DateTime? nextUpdateTime;
    if (subscription.autoUpdateMode == AutoUpdateMode.interval) {
      nextUpdateTime = subscription.lastUpdateTime!.add(
        Duration(minutes: subscription.intervalMinutes),
      );
    } else {
      return trans.pendingUpdate;
    }

    // 如果已经过了更新时间
    if (now.isAfter(nextUpdateTime)) {
      return trans.pendingUpdate;
    }

    final diff = nextUpdateTime.difference(now);

    if (diff.inMinutes < 1) return trans.willUpdate;
    if (diff.inMinutes < 60) {
      return trans.updateAfterMinutes.replaceAll(
        '{n}',
        diff.inMinutes.toString(),
      );
    }
    if (diff.inHours < 24) {
      return trans.updateAfterHours.replaceAll('{n}', diff.inHours.toString());
    }
    return trans.updateAfterDays.replaceAll('{n}', diff.inDays.toString());
  }

  // 格式化过期日期
  String _formatExpireDate(int timestamp, BuildContext context) {
    final date = DateTime.fromMillisecondsSinceEpoch(timestamp * 1000);
    final now = DateTime.now();
    final diff = date.difference(now);

    final trans = context.translate.subscription;

    if (diff.inDays > 30) {
      return trans.remainingMonths.replaceAll(
        '{n}',
        (diff.inDays / 30).floor().toString(),
      );
    }
    return trans.remainingDays.replaceAll('{n}', diff.inDays.toString());
  }
}

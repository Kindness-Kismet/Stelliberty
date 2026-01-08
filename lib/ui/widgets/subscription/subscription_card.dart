import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:provider/provider.dart';
import 'package:stelliberty/clash/model/subscription_model.dart';
import 'package:stelliberty/clash/providers/subscription_provider.dart';
import 'package:stelliberty/i18n/i18n.dart';
import 'package:stelliberty/atomic/external_opener.dart';
import 'package:stelliberty/services/path_service.dart';
import 'package:stelliberty/ui/widgets/modern_toast.dart';
import 'package:stelliberty/ui/common/modern_popup_menu.dart';
import 'package:stelliberty/ui/widgets/modern_tooltip.dart';
import 'package:stelliberty/services/log_print_service.dart';

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
        final isBatchUpdating = provider.isBatchUpdatingSubscriptions;
        final colorScheme = Theme.of(context).colorScheme;
        final isDark = Theme.of(context).brightness == Brightness.dark;

        final mixColor = isDark ? Colors.black : Colors.white;
        final mixOpacity = 0.1;

        return Stack(
          children: [
            // 整个卡片
            Container(
              decoration: BoxDecoration(
                borderRadius: BorderRadius.circular(16),
                color: Color.alphaBlend(
                  mixColor.withValues(alpha: mixOpacity),
                  colorScheme.surface.withValues(alpha: isDark ? 0.7 : 0.85),
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
            // 配置失败警告标记（右下角）
            if (subscription.hasConfigLoadFailed)
              Positioned(
                right: 12,
                bottom: 8,
                child: ModernTooltip(
                  message: '该配置异常，无法工作',
                  child: Container(
                    padding: const EdgeInsets.all(4),
                    decoration: BoxDecoration(
                      color: Colors.red.withValues(alpha: 0.9),
                      borderRadius: BorderRadius.circular(4),
                    ),
                    child: const Icon(
                      Icons.warning,
                      color: Colors.white,
                      size: 16,
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
    final trans = context.translate;

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
            message: trans.subscription.update_card,
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
    final trans = context.translate;

    final List<InlineSpan> children = [];

    // 自动更新状态
    final autoUpdateMode = subscription.autoUpdateMode;
    children.add(
      TextSpan(
        text: subscription.isLocalFile
            ? trans.subscription.local_type_label
            : (autoUpdateMode == AutoUpdateMode.disabled
                  ? trans.subscription.manual_update_label
                  : (autoUpdateMode == AutoUpdateMode.onStartup
                        ? trans.subscription.update_on_startup_label
                        : trans.subscription.auto_update_label)),
        style: TextStyle(
          color: subscription.isLocalFile
              ? Colors.grey
              : (autoUpdateMode == AutoUpdateMode.disabled
                    ? Colors.grey
                    : Colors.green),
          fontSize: 11,
        ),
      ),
    );

    // 距下次更新时间（仅远程订阅+间隔更新+有更新记录时显示）
    if (!subscription.isLocalFile &&
        autoUpdateMode == AutoUpdateMode.interval &&
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
  // ModernPopupMenu 会自动处理分页逻辑（超过 6 项时）
  Widget _buildModernPopupMenu(BuildContext context, bool isDisabled) {
    final trans = context.translate;
    final allMenuItems = _buildAllMenuItems(context, trans);

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
        items: allMenuItems,
        moreOptionsLabel: trans.subscription.menu.more_options,
      ),
    );
  }

  // 构建所有菜单项
  List<PopupMenuItemData> _buildAllMenuItems(
    BuildContext context,
    Translations trans,
  ) {
    final items = <PopupMenuItemData>[
      PopupMenuItemData(
        icon: Icons.edit,
        label: trans.subscription.menu.config_edit,
        onPressed: onEdit,
      ),
      PopupMenuItemData(
        icon: Icons.code,
        label: trans.subscription.menu.file_edit,
        onPressed: onEditFile,
      ),
      PopupMenuItemData(
        icon: Icons.open_in_new,
        label: trans.subscription.menu.open_external_editor,
        onPressed: () => _handleOpenInExternalEditor(context),
      ),
      PopupMenuItemData(
        icon: Icons.folder_open,
        label: trans.subscription.menu.open_in_file_manager,
        onPressed: () => _handleOpenInFileManager(context),
      ),
      // 只有当前选中的订阅才显示运行配置查看
      if (isSelected)
        PopupMenuItemData(
          icon: Icons.visibility,
          label: trans.subscription.menu.config_view,
          onPressed: onViewConfig,
        ),
      PopupMenuItemData(
        icon: Icons.rule,
        label: trans.subscription.menu.override_manage,
        onPressed: onManageOverride,
      ),
      PopupMenuItemData(
        icon: Icons.extension,
        label: trans.subscription.menu.provider_view,
        onPressed: onViewProvider,
      ),
      // 本地文件订阅不显示复制链接选项
      if (!subscription.isLocalFile)
        PopupMenuItemData(
          icon: Icons.copy,
          label: trans.subscription.menu.copy_link,
          onPressed: () => _copyUrl(context),
        ),
      PopupMenuItemData(
        icon: Icons.delete,
        label: trans.subscription.menu.delete,
        onPressed: onDelete,
        isDangerous: true,
      ),
    ];

    return items;
  }

  // 在文件管理器中显示订阅目录
  Future<void> _handleOpenInFileManager(BuildContext context) async {
    final trans = context.translate;
    final subscriptionsDir = PathService.instance.subscriptionsDir;

    Logger.info('尝试在文件管理器中打开订阅目录: $subscriptionsDir');

    final result = await ExternalOpenService.openDirectory(subscriptionsDir);

    if (!context.mounted) return;

    if (result.isSuccessful) {
      Logger.info('成功在文件管理器中打开订阅目录');
      ModernToast.success(trans.subscription.external_open.open_success);
      return;
    }

    Logger.error('在文件管理器中打开订阅目录失败: ${result.errorType}');
    ModernToast.error(
      trans.subscription.external_open.open_failed.replaceAll(
        '{error}',
        _formatExternalOpenError(trans, result),
      ),
    );
  }

  // 构建状态与流量并排显示
  Widget _buildStatusWithTraffic(BuildContext context) {
    final trans = context.translate;

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
                ? trans.subscription.expired
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

  Future<void> _handleOpenInExternalEditor(BuildContext context) async {
    final trans = context.translate;
    final configPath = PathService.instance.getSubscriptionConfigPath(
      subscription.id,
    );

    final result = await ExternalOpenService.openFile(configPath);
    if (!context.mounted) return;

    if (result.isSuccessful) {
      ModernToast.success(trans.subscription.external_open.open_success);
      return;
    }

    ModernToast.error(
      trans.subscription.external_open.open_failed.replaceAll(
        '{error}',
        _formatExternalOpenError(trans, result),
      ),
    );
  }

  String _formatExternalOpenError(
    Translations trans,
    ExternalOpenResult result,
  ) {
    switch (result.errorType) {
      case ExternalOpenErrorType.fileNotFound:
        return trans.subscription.external_open.error_file_not_found;
      case ExternalOpenErrorType.directoryNotFound:
        return trans.subscription.external_open.error_directory_not_found;
      case ExternalOpenErrorType.unsupportedPlatform:
        return trans.subscription.external_open.error_unsupported_platform;
      case ExternalOpenErrorType.processFailed:
        return result.errorDetails ??
            trans.subscription.external_open.error_unknown;
      case ExternalOpenErrorType.unknown:
      case null:
        return trans.subscription.external_open.error_unknown;
    }
  }

  // 复制 URL
  void _copyUrl(BuildContext context) async {
    final trans = context.translate;

    try {
      await Clipboard.setData(ClipboardData(text: subscription.url));
      if (!context.mounted) return;

      ModernToast.success(trans.subscription.link_copied);
    } catch (e) {
      if (!context.mounted) return;

      ModernToast.error(
        trans.subscription.copy_failed.replaceAll('{error}', e.toString()),
      );
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
    final trans = context.translate;

    if (subscription.lastUpdateTime == null) {
      return trans.subscription.pending_update;
    }

    final subTrans = trans.subscription;
    final now = DateTime.now();

    // 根据更新模式计算下次更新时间
    DateTime? nextUpdateTime;
    if (subscription.autoUpdateMode == AutoUpdateMode.interval) {
      nextUpdateTime = subscription.lastUpdateTime!.add(
        Duration(minutes: subscription.intervalMinutes),
      );
    } else {
      return subTrans.pending_update;
    }

    // 如果已经过了更新时间
    if (now.isAfter(nextUpdateTime)) {
      return subTrans.pending_update;
    }

    final diff = nextUpdateTime.difference(now);

    if (diff.inMinutes < 1) return subTrans.will_update;
    if (diff.inMinutes < 60) {
      return subTrans.update_after_minutes.replaceAll(
        '{n}',
        diff.inMinutes.toString(),
      );
    }
    if (diff.inHours < 24) {
      return subTrans.update_after_hours.replaceAll(
        '{n}',
        diff.inHours.toString(),
      );
    }
    return subTrans.update_after_days.replaceAll('{n}', diff.inDays.toString());
  }

  // 格式化过期日期
  String _formatExpireDate(int timestamp, BuildContext context) {
    final trans = context.translate;

    final date = DateTime.fromMillisecondsSinceEpoch(timestamp * 1000);
    final now = DateTime.now();
    final diff = date.difference(now);

    final subTrans = trans.subscription;

    if (diff.inDays > 30) {
      return subTrans.remaining_months.replaceAll(
        '{n}',
        (diff.inDays / 30).floor().toString(),
      );
    }
    return subTrans.remaining_days.replaceAll('{n}', diff.inDays.toString());
  }
}

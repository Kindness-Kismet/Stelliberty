import 'package:flutter/material.dart';
import 'package:stelliberty/clash/data/override_model.dart';
import 'package:stelliberty/i18n/i18n.dart';
import 'package:stelliberty/ui/common/modern_popup_menu.dart';

// 覆写卡片组件
// 显示覆写配置的信息：
// - 类型图标（远程/本地）
// - 配置名称
// - 格式标签（YAML/JS）
// - 独立更新按钮（仅远程）
// - 操作菜单（编辑配置、编辑文件、删除）
// 支持拖拽排序：
// - isDragging: 正在被拖拽
// - isDragTarget: 拖拽目标位置
class OverrideCard extends StatelessWidget {
  final OverrideConfig config;
  final bool isUpdating;
  final bool isDragging;
  final bool isDragTarget;
  final VoidCallback? onUpdate;
  final VoidCallback? onEditConfig;
  final VoidCallback? onEditFile;
  final VoidCallback? onDelete;

  const OverrideCard({
    super.key,
    required this.config,
    this.isUpdating = false,
    this.isDragging = false,
    this.isDragTarget = false,
    this.onUpdate,
    this.onEditConfig,
    this.onEditFile,
    this.onDelete,
  });

  @override
  Widget build(BuildContext context) {
    final colorScheme = Theme.of(context).colorScheme;
    final isDark = Theme.of(context).brightness == Brightness.dark;

    final mixColor = isDark ? Colors.black : Colors.white;
    final mixOpacity = 0.1;
    final trans = context.translate;

    return Container(
      decoration: BoxDecoration(
        borderRadius: BorderRadius.circular(16),
        color: Color.alphaBlend(
          mixColor.withValues(alpha: mixOpacity),
          isDragTarget
              ? colorScheme.primaryContainer.withValues(alpha: 0.2)
              : colorScheme.surface.withValues(alpha: isDark ? 0.7 : 0.85),
        ),
        border: Border.all(
          color: isDragging || isDragTarget
              ? colorScheme.primary.withValues(alpha: isDark ? 0.7 : 0.6)
              : colorScheme.outline.withValues(alpha: 0.4),
          width: 2,
        ),
        boxShadow: [
          BoxShadow(
            color: isDragging
                ? colorScheme.primary.withValues(alpha: isDark ? 0.3 : 0.15)
                : Colors.black.withValues(alpha: isDark ? 0.2 : 0.06),
            blurRadius: isDragging ? 12 : 8,
            offset: Offset(0, isDragging ? 3 : 2),
          ),
        ],
      ),
      child: Material(
        color: Colors.transparent,
        child: Padding(
          padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 10),
          child: Row(
            children: [
              Container(
                padding: const EdgeInsets.all(6),
                decoration: BoxDecoration(
                  color: config.type == OverrideType.remote
                      ? Colors.blue.withValues(alpha: 0.1)
                      : Colors.green.withValues(alpha: 0.1),
                  borderRadius: BorderRadius.circular(6),
                ),
                child: Icon(
                  config.type == OverrideType.remote
                      ? Icons.cloud
                      : Icons.insert_drive_file,
                  size: 18,
                  color: config.type == OverrideType.remote
                      ? Colors.blue
                      : Colors.green,
                ),
              ),
              const SizedBox(width: 10),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  mainAxisAlignment: MainAxisAlignment.center,
                  children: [
                    Text(
                      config.name,
                      style: const TextStyle(
                        fontSize: 13,
                        fontWeight: FontWeight.w600,
                      ),
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                    ),
                    const SizedBox(height: 3),
                    Row(
                      children: [
                        Container(
                          padding: const EdgeInsets.symmetric(
                            horizontal: 6,
                            vertical: 2,
                          ),
                          decoration: BoxDecoration(
                            color: config.format == OverrideFormat.yaml
                                ? Colors.green.withValues(alpha: 0.1)
                                : Colors.orange.withValues(alpha: 0.1),
                            borderRadius: BorderRadius.circular(4),
                          ),
                          child: Text(
                            config.format.displayName,
                            style: TextStyle(
                              fontSize: 10,
                              color: config.format == OverrideFormat.yaml
                                  ? Colors.green[700]
                                  : Colors.orange[700],
                              fontWeight: FontWeight.w500,
                            ),
                          ),
                        ),
                        // 本地类型标识
                        if (config.type == OverrideType.local) ...[
                          const SizedBox(width: 6),
                          Container(
                            padding: const EdgeInsets.symmetric(
                              horizontal: 6,
                              vertical: 2,
                            ),
                            decoration: BoxDecoration(
                              color: Colors.blue.withValues(alpha: 0.1),
                              borderRadius: BorderRadius.circular(4),
                            ),
                            child: Text(
                              trans.subscription.local_type_label,
                              style: TextStyle(
                                fontSize: 10,
                                color: Colors.blue[700],
                                fontWeight: FontWeight.w500,
                              ),
                            ),
                          ),
                        ],
                        if (isUpdating) ...[
                          const SizedBox(width: 8),
                          const SizedBox(
                            width: 12,
                            height: 12,
                            child: CircularProgressIndicator(strokeWidth: 2),
                          ),
                        ],
                      ],
                    ),
                  ],
                ),
              ),
              // 远程覆写的独立更新按钮
              if (config.type == OverrideType.remote)
                IconButton(
                  onPressed: isUpdating ? null : onUpdate,
                  icon: isUpdating
                      ? SizedBox(
                          width: 16,
                          height: 16,
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
                          color: colorScheme.primary,
                        ),
                  style: IconButton.styleFrom(
                    padding: const EdgeInsets.all(8),
                    minimumSize: const Size(32, 32),
                    tapTargetSize: MaterialTapTargetSize.shrinkWrap,
                  ),
                ),
              ModernPopupBox(
                targetBuilder: (open) => IconButton(
                  icon: const Icon(Icons.more_vert, size: 20),
                  onPressed: () => open(),
                  style: IconButton.styleFrom(
                    padding: const EdgeInsets.all(8),
                    minimumSize: const Size(32, 32),
                    tapTargetSize: MaterialTapTargetSize.shrinkWrap,
                  ),
                ),
                popup: ModernPopupMenu(
                  items: [
                    PopupMenuItemData(
                      icon: Icons.settings,
                      label: trans.kOverride.edit_config,
                      onPressed: onEditConfig,
                    ),
                    PopupMenuItemData(
                      icon: Icons.edit,
                      label: trans.kOverride.edit_file,
                      onPressed: onEditFile,
                    ),
                    PopupMenuItemData(
                      icon: Icons.delete,
                      label: trans.kOverride.delete_item,
                      onPressed: onDelete,
                      isDangerous: true,
                    ),
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

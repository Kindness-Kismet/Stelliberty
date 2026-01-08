import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import 'package:stelliberty/clash/model/override_model.dart';
import 'package:stelliberty/clash/providers/override_provider.dart';
import 'package:stelliberty/ui/common/modern_switch.dart';
import 'package:stelliberty/ui/common/modern_dialog.dart';
import 'package:stelliberty/i18n/i18n.dart';
import 'package:stelliberty/services/log_print_service.dart';

// 覆写选择对话框 - 从全局覆写列表中选择并排序
class OverrideSelectorDialog extends StatefulWidget {
  final List<String> initialSelectedIds;
  final List<String> initialSortPreference;

  const OverrideSelectorDialog({
    super.key,
    required this.initialSelectedIds,
    this.initialSortPreference = const [],
  });

  static Future<({List<String> selectedIds, List<String> sortPreference})?>
  show(
    BuildContext context, {
    required List<String> initialSelectedIds,
    List<String> initialSortPreference = const [],
  }) {
    return showDialog<
      ({List<String> selectedIds, List<String> sortPreference})
    >(
      context: context,
      barrierDismissible: false,
      builder: (context) => OverrideSelectorDialog(
        initialSelectedIds: initialSelectedIds,
        initialSortPreference: initialSortPreference,
      ),
    );
  }

  @override
  State<OverrideSelectorDialog> createState() => _OverrideSelectorDialogState();
}

class _OverrideSelectorDialogState extends State<OverrideSelectorDialog> {
  // 维护选中项的有序列表
  late List<String> _orderedSelectedIds;

  // 维护所有覆写的显示顺序（用于拖拽排序）
  List<OverrideConfig> _orderedOverrides = [];

  @override
  void initState() {
    super.initState();
    _orderedSelectedIds = List.from(widget.initialSelectedIds);
  }

  void _initializeOrder(List<OverrideConfig> allOverrides) {
    final overridesById = {for (final o in allOverrides) o.id: o};

    final List<String> sourceIds;
    final bool shouldUseSavedOrder;
    if (widget.initialSortPreference.isNotEmpty) {
      sourceIds = widget.initialSortPreference;
      shouldUseSavedOrder = true;
    } else {
      sourceIds = widget.initialSelectedIds;
      shouldUseSavedOrder = false;
    }

    final existingIds = sourceIds.toSet();
    _orderedOverrides = [
      for (final id in sourceIds)
        if (overridesById.containsKey(id)) overridesById[id]!,
      for (final override in allOverrides)
        if (!existingIds.contains(override.id)) override,
    ];

    Logger.debug(
      '初始化覆写顺序：${_orderedOverrides.length} 个，'
      '${shouldUseSavedOrder ? '使用已保存顺序' : '选中的在前'}',
    );
  }

  @override
  Widget build(BuildContext context) {
    final trans = context.translate;

    return ModernDialog(
      title: trans.override_dialog.select_overrides,
      titleIcon: Icons.checklist,
      maxWidth: 640,
      maxHeightRatio: 0.8,
      content: _buildContent(),
      actionsLeft: Text(
        '拖动卡片可调整覆写顺序',
        style: TextStyle(
          fontSize: 13,
          color: Theme.of(context).colorScheme.onSurface.withValues(alpha: 0.6),
        ),
      ),
      actionsRight: [
        DialogActionButton(
          label: trans.common.cancel,
          onPressed: () => Navigator.of(context).pop(),
        ),
        DialogActionButton(
          label: trans.common.save,
          isPrimary: true,
          onPressed: _handleConfirm,
        ),
      ],
      onClose: () => Navigator.of(context).pop(),
    );
  }

  void _handleConfirm() {
    if (_orderedOverrides.isEmpty) {
      Navigator.of(context).pop();
      return;
    }

    final newSortPreference = [for (final o in _orderedOverrides) o.id];
    final newSelectedIds = [
      for (final o in _orderedOverrides)
        if (_orderedSelectedIds.contains(o.id)) o.id,
    ];

    final hasChanges =
        !_areListsEqual(newSelectedIds, widget.initialSelectedIds) ||
        !_areListsEqual(newSortPreference, widget.initialSortPreference);

    if (!hasChanges) {
      Navigator.of(context).pop();
      return;
    }

    Logger.info(
      '保存覆写配置 - 选中: ${newSelectedIds.length} 个，'
      '排序: ${newSortPreference.length} 个',
    );
    Navigator.of(
      context,
    ).pop((selectedIds: newSelectedIds, sortPreference: newSortPreference));
  }

  bool _areListsEqual(List<String> a, List<String> b) {
    if (a.length != b.length) return false;
    for (int i = 0; i < a.length; i++) {
      if (a[i] != b[i]) return false;
    }
    return true;
  }

  Widget _buildContent() {
    return Consumer<OverrideProvider>(
      builder: (context, provider, _) {
        final overrides = provider.overrides;

        if (_orderedOverrides.length != overrides.length) {
          _initializeOrder(overrides);
        }

        if (overrides.isEmpty) {
          return _buildEmptyState(context);
        }

        return _buildReorderableList();
      },
    );
  }

  Widget _buildEmptyState(BuildContext context) {
    final trans = context.translate;

    return Center(
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(Icons.rule, size: 64, color: Colors.grey[400]),
          const SizedBox(height: 16),
          Text(
            trans.override_dialog.no_overrides_title,
            style: TextStyle(fontSize: 16, color: Colors.grey[600]),
          ),
          const SizedBox(height: 8),
          Text(
            trans.override_dialog.no_overrides_hint,
            style: TextStyle(fontSize: 12, color: Colors.grey[500]),
          ),
        ],
      ),
    );
  }

  Widget _buildReorderableList() {
    return ReorderableListView.builder(
      padding: const EdgeInsets.all(24),
      itemCount: _orderedOverrides.length,
      buildDefaultDragHandles: false,
      onReorder: (oldIndex, newIndex) {
        setState(() {
          if (oldIndex < newIndex) newIndex -= 1;
          final item = _orderedOverrides.removeAt(oldIndex);
          _orderedOverrides.insert(newIndex, item);
        });
      },
      proxyDecorator: (child, index, animation) =>
          Material(color: Colors.transparent, elevation: 0, child: child),
      itemBuilder: (context, index) =>
          _buildOverrideItem(_orderedOverrides[index], index),
    );
  }

  Widget _buildOverrideItem(OverrideConfig override, int index) {
    final colorScheme = Theme.of(context).colorScheme;
    final isDark = Theme.of(context).brightness == Brightness.dark;
    final isSelected = _orderedSelectedIds.contains(override.id);

    return ReorderableDragStartListener(
      key: ValueKey(override.id),
      index: index,
      child: Container(
        margin: const EdgeInsets.only(bottom: 16),
        padding: const EdgeInsets.all(16),
        decoration: BoxDecoration(
          color: Colors.white.withValues(alpha: isDark ? 0.04 : 0.5),
          borderRadius: BorderRadius.circular(12),
          border: Border.all(
            color: Colors.white.withValues(alpha: isDark ? 0.1 : 0.2),
          ),
        ),
        child: Row(
          children: [
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    override.name,
                    style: TextStyle(
                      fontSize: 14,
                      fontWeight: FontWeight.w500,
                      color: colorScheme.onSurface,
                    ),
                  ),
                  const SizedBox(height: 4),
                  Text(
                    override.format.displayName,
                    style: TextStyle(
                      fontSize: 12,
                      fontWeight: FontWeight.w600,
                      color: colorScheme.primary,
                    ),
                  ),
                ],
              ),
            ),
            const SizedBox(width: 12),
            ModernSwitch(
              value: isSelected,
              onChanged: (enabled) {
                setState(() {
                  if (enabled) {
                    _orderedSelectedIds.add(override.id);
                  } else {
                    _orderedSelectedIds.remove(override.id);
                  }
                });
              },
            ),
          ],
        ),
      ),
    );
  }
}

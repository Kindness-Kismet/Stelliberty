import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import 'package:reorderable_grid_view/reorderable_grid_view.dart';
import 'package:stelliberty/clash/providers/override_provider.dart';
import 'package:stelliberty/clash/data/override_model.dart';
import 'package:stelliberty/providers/content_provider.dart';
import 'package:stelliberty/i18n/i18n.dart';
import 'package:stelliberty/ui/widgets/file_editor_dialog.dart';
import 'package:stelliberty/ui/widgets/override/override_dialog.dart';
import 'package:stelliberty/ui/widgets/override/override_card.dart';
import 'package:stelliberty/ui/widgets/modern_toast.dart';
import 'package:stelliberty/utils/logger.dart';
import 'package:stelliberty/ui/widgets/modern_tooltip.dart';

class OverridePage extends StatefulWidget {
  const OverridePage({super.key});

  @override
  State<OverridePage> createState() => _OverridePageState();
}

class _OverridePageState extends State<OverridePage> {
  @override
  void initState() {
    super.initState();
    Logger.info('初始化 OverridePage');
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      body: Column(
        children: [
          _buildHeader(),
          const Divider(height: 1),
          Expanded(child: _buildContent()),
        ],
      ),
    );
  }

  Widget _buildHeader() {
    final colorScheme = Theme.of(context).colorScheme;

    return Consumer<OverrideProvider>(
      builder: (context, provider, _) {
        return Container(
          padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
          child: Row(
            children: [
              ModernIconTooltip(
                message: context.translate.common.cancel,
                icon: Icons.arrow_back,
                filled: false,
                onPressed: () {
                  context.read<ContentProvider>().switchView(
                    ContentView.subscriptions,
                  );
                },
              ),
              const SizedBox(width: 8),
              Container(
                padding: const EdgeInsets.symmetric(
                  horizontal: 10,
                  vertical: 6,
                ),
                decoration: BoxDecoration(
                  color: colorScheme.primaryContainer,
                  borderRadius: BorderRadius.circular(20),
                ),
                child: Row(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    Icon(
                      Icons.rule,
                      size: 16,
                      color: colorScheme.onPrimaryContainer,
                    ),
                    const SizedBox(width: 6),
                    Text(
                      translate.kOverride.count.replaceAll(
                        '{count}',
                        provider.overrides.length.toString(),
                      ),
                      style: TextStyle(
                        fontSize: 13,
                        fontWeight: FontWeight.w600,
                        color: colorScheme.onPrimaryContainer,
                      ),
                    ),
                  ],
                ),
              ),
              const Spacer(),
              FilledButton.icon(
                onPressed: _showAddOverrideDialog,
                icon: const Icon(Icons.add_circle, size: 18),
                label: Text(translate.kOverride.addOverride),
                style: FilledButton.styleFrom(
                  padding: const EdgeInsets.symmetric(
                    horizontal: 16,
                    vertical: 10,
                  ),
                  textStyle: const TextStyle(
                    fontSize: 13,
                    fontWeight: FontWeight.w600,
                  ),
                ),
              ),
              const SizedBox(width: 8),
              if (provider.overrides.any((o) => o.type == OverrideType.remote))
                FilledButton.tonalIcon(
                  onPressed: provider.isBatchUpdating
                      ? null
                      : () => _updateAllRemoteOverrides(context, provider),
                  icon: provider.isBatchUpdating
                      ? SizedBox(
                          width: 16,
                          height: 16,
                          child: CircularProgressIndicator(
                            strokeWidth: 2,
                            valueColor: AlwaysStoppedAnimation<Color>(
                              colorScheme.onSecondaryContainer,
                            ),
                          ),
                        )
                      : const Icon(Icons.sync_rounded, size: 18),
                  label: Text(
                    provider.isBatchUpdating
                        ? translate.kOverride.updating
                        : translate.kOverride.updateAll,
                  ),
                  style: FilledButton.styleFrom(
                    padding: const EdgeInsets.symmetric(
                      horizontal: 16,
                      vertical: 10,
                    ),
                    textStyle: const TextStyle(
                      fontSize: 13,
                      fontWeight: FontWeight.w600,
                    ),
                  ),
                ),
            ],
          ),
        );
      },
    );
  }

  Widget _buildContent() {
    return Consumer<OverrideProvider>(
      builder: (context, provider, _) {
        if (provider.isLoading) {
          return const Center(child: CircularProgressIndicator());
        }

        if (provider.overrides.isEmpty) {
          return Center(
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                Icon(Icons.rule, size: 64, color: Colors.grey[400]),
                const SizedBox(height: 16),
                Text(
                  translate.kOverride.emptyTitle,
                  style: TextStyle(fontSize: 16, color: Colors.grey[600]),
                ),
                const SizedBox(height: 8),
                Text(
                  translate.kOverride.emptyHint,
                  style: TextStyle(fontSize: 12, color: Colors.grey[500]),
                ),
              ],
            ),
          );
        }

        return ReorderableGridView.builder(
          padding: const EdgeInsets.all(16),
          gridDelegate: const SliverGridDelegateWithFixedCrossAxisCount(
            crossAxisCount: 2,
            mainAxisSpacing: 12,
            crossAxisSpacing: 12,
            mainAxisExtent: 80,
          ),
          itemCount: provider.overrides.length,
          onReorder: (oldIndex, newIndex) {
            provider.reorderOverrides(oldIndex, newIndex, autoAdjust: false);
          },
          dragWidgetBuilder: (index, child) {
            final override = provider.overrides[index];
            final isUpdating = provider.isOverrideUpdating(override.id);

            return Material(
              color: Colors.transparent,
              elevation: 0,
              child: OverrideCard(
                key: ValueKey(override.id),
                config: override,
                isUpdating: isUpdating,
                isDragging: true,
                onUpdate: () => provider.updateRemoteOverride(override.id),
                onEditConfig: () => _editOverride(override),
                onEditFile: () => _editOverrideFile(override),
                onDelete: () => _deleteOverride(override),
              ),
            );
          },
          itemBuilder: (context, index) {
            final override = provider.overrides[index];
            final isUpdating = provider.isOverrideUpdating(override.id);

            return OverrideCard(
              key: ValueKey(override.id),
              config: override,
              isUpdating: isUpdating,
              isDragging: false,
              onUpdate: () => provider.updateRemoteOverride(override.id),
              onEditConfig: () => _editOverride(override),
              onEditFile: () => _editOverrideFile(override),
              onDelete: () => _deleteOverride(override),
            );
          },
        );
      },
    );
  }

  Future<void> _showAddOverrideDialog() async {
    final provider = context.read<OverrideProvider>();

    final result = await OverrideDialog.show(
      context,
      onConfirm: (override) async {
        return await provider.addOverride(override);
      },
    );

    if (result == null || !mounted) return;

    ModernToast.success(
      context,
      translate.kOverride.addSuccess.replaceAll('{name}', result.name),
    );
  }

  Future<void> _editOverride(OverrideConfig override) async {
    final result = await OverrideDialog.show(
      context,
      editingOverride: override,
    );

    if (result == null || !mounted) return;

    await context.read<OverrideProvider>().updateOverride(override.id, result);
  }

  Future<void> _editOverrideFile(OverrideConfig override) async {
    await FileEditorDialog.show(
      context,
      fileName: override.name,
      initialContent: override.content ?? '',
      onSave: (content) async {
        final provider = context.read<OverrideProvider>();

        // 1. 先保存文件内容到实际文件
        await provider.saveOverrideFileContent(override, content);

        // 2. 更新覆写配置（更新内存中的 content 和 lastUpdate）
        final updated = override.copyWith(
          content: content,
          lastUpdate: DateTime.now(),
        );
        await provider.updateOverride(override.id, updated);

        return true;
      },
    );
  }

  Future<void> _deleteOverride(OverrideConfig override) async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: Text(translate.kOverride.confirmDelete),
        content: Text(
          translate.kOverride.confirmDeleteMessage.replaceAll(
            '{name}',
            override.name,
          ),
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context, false),
            child: Text(context.translate.common.cancel),
          ),
          ElevatedButton(
            onPressed: () => Navigator.pop(context, true),
            style: ElevatedButton.styleFrom(
              backgroundColor: Colors.red,
              foregroundColor: Colors.white,
            ),
            child: Text(context.translate.common.delete),
          ),
        ],
      ),
    );

    if (confirmed != true || !mounted) return;

    await context.read<OverrideProvider>().deleteOverride(override.id);
  }

  Future<void> _updateAllRemoteOverrides(
    BuildContext context,
    OverrideProvider provider,
  ) async {
    final errors = await provider.updateAllRemoteOverrides();

    if (!context.mounted) return;

    if (errors.isEmpty) {
      ModernToast.success(context, translate.kOverride.updateAllSuccess);
      return;
    }

    ModernToast.warning(
      context,
      translate.kOverride.updatePartialFailed.replaceAll(
        '{errors}',
        errors.join('\n'),
      ),
    );
  }
}

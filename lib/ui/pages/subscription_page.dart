import 'dart:io';
import 'dart:async';
import 'package:flutter/material.dart';
import 'package:path/path.dart' as path;
import 'package:provider/provider.dart';
import 'package:reorderable_grid_view/reorderable_grid_view.dart';
import 'package:stelliberty/clash/providers/subscription_provider.dart';
import 'package:stelliberty/clash/model/subscription_model.dart';
import 'package:stelliberty/clash/services/geo_service.dart';
import 'package:stelliberty/ui/widgets/subscription/subscription_card.dart';
import 'package:stelliberty/ui/widgets/subscription/subscription_dialog.dart';
import 'package:stelliberty/ui/widgets/override/override_selector_dialog.dart';
import 'package:stelliberty/ui/widgets/subscription/provider_viewer_dialog.dart';
import 'package:stelliberty/ui/widgets/file_editor_dialog.dart';
import 'package:stelliberty/ui/widgets/modern_toast.dart';
import 'package:stelliberty/ui/widgets/confirm_dialog.dart';
import 'package:stelliberty/providers/content_provider.dart';
import 'package:stelliberty/i18n/i18n.dart';
import 'package:stelliberty/services/log_print_service.dart';
import 'package:stelliberty/ui/common/modern_top_toolbar.dart';
import 'package:stelliberty/ui/constants/spacing.dart';

// 订阅页布局常量
class _SubscriptionGridSpacing {
  _SubscriptionGridSpacing._();

  static const gridLeftEdge = 16.0;
  static const gridTopEdge = 16.0;
  static const gridRightEdge =
      16.0 - SpacingConstants.scrollbarRightCompensation;
  static const gridBottomEdge = 10.0;
  static const cardColumnSpacing = 16.0;
  static const cardRowSpacing = 16.0;

  static const gridPadding = EdgeInsets.fromLTRB(
    gridLeftEdge,
    gridTopEdge,
    gridRightEdge,
    gridBottomEdge,
  );
}

class SubscriptionPage extends StatefulWidget {
  const SubscriptionPage({super.key});

  @override
  State<SubscriptionPage> createState() => _SubscriptionPageState();
}

class _SubscriptionPageState extends State<SubscriptionPage> {
  final ScrollController _scrollController = ScrollController();

  @override
  void initState() {
    super.initState();
    Logger.info('初始化 SubscriptionPage');
  }

  @override
  void dispose() {
    _scrollController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        // 顶部控制栏 - 使用 Selector 只监听必要字段
        Padding(
          padding: const EdgeInsets.symmetric(horizontal: 16.0),
          child: Selector<SubscriptionProvider, _SubscriptionControlBarState>(
            selector: (_, provider) => _SubscriptionControlBarState(
              subscriptionCount: provider.subscriptions.length,
              isLoading: provider.isLoading,
            ),
            builder: (context, data, child) {
              final provider = context.read<SubscriptionProvider>();
              return _buildControlBar(context, provider, data);
            },
          ),
        ),
        const Divider(height: 1),

        // 主内容区域 - 使用 Selector 监听列表变化
        Expanded(
          child: Selector<SubscriptionProvider, _SubscriptionListState>(
            selector: (_, provider) => _SubscriptionListState(
              isLoading: provider.isLoading,
              isSwitchingSubscription: provider.isSwitchingSubscription,
              errorMessage: provider.errorMessage,
              subscriptions: provider.subscriptions,
              currentSubscriptionId: provider.currentSubscriptionId,
            ),
            builder: (context, data, child) {
              final provider = context.read<SubscriptionProvider>();
              return Padding(
                padding: SpacingConstants.scrollbarPadding,
                child: _buildMainContent(context, provider, data),
              );
            },
          ),
        ),
      ],
    );
  }

  // 构建控制栏（MD3 扁平化风格）
  Widget _buildControlBar(
    BuildContext context,
    SubscriptionProvider provider,
    _SubscriptionControlBarState data,
  ) {
    final trans = context.translate;
    final colorScheme = Theme.of(context).colorScheme;

    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 12.0),
      child: Row(
        children: [
          Expanded(
            child: Align(
              alignment: Alignment.centerRight,
              child: Wrap(
                spacing: 8,
                runSpacing: 8,
                alignment: WrapAlignment.end,
                children: [
                  OutlinedButton.icon(
                    onPressed: () => _navigateToOverrideManagement(context),
                    icon: const Icon(Icons.rule, size: 18),
                    label: Text(trans.subscription.override_management),
                    style: OutlinedButton.styleFrom(
                      padding: const EdgeInsets.symmetric(
                        horizontal: 16,
                        vertical: 10,
                      ),
                      shape: modernTopToolbarButtonShape(),
                      textStyle: const TextStyle(
                        fontSize: 13,
                        fontWeight: FontWeight.w600,
                      ),
                    ),
                  ),
                  FilledButton.icon(
                    onPressed: () =>
                        _showAddSubscriptionDialog(context, provider),
                    icon: const Icon(Icons.add_rounded, size: 18),
                    label: Text(trans.subscription.add_config),
                    style: FilledButton.styleFrom(
                      padding: const EdgeInsets.symmetric(
                        horizontal: 16,
                        vertical: 10,
                      ),
                      shape: modernTopToolbarButtonShape(),
                      textStyle: const TextStyle(
                        fontSize: 13,
                        fontWeight: FontWeight.w600,
                      ),
                    ),
                  ),
                  if (data.subscriptionCount > 0)
                    FilledButton.tonalIcon(
                      onPressed: data.isLoading
                          ? null
                          : () => _updateAllSubscriptions(context, provider),
                      icon: data.isLoading
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
                        data.isLoading
                            ? trans.subscription.updating
                            : trans.subscription.update_all,
                      ),
                      style: FilledButton.styleFrom(
                        padding: const EdgeInsets.symmetric(
                          horizontal: 16,
                          vertical: 10,
                        ),
                        shape: modernTopToolbarButtonShape(),
                        textStyle: const TextStyle(
                          fontSize: 13,
                          fontWeight: FontWeight.w600,
                        ),
                      ),
                    ),
                ],
              ),
            ),
          ),
        ],
      ),
    );
  }

  // 构建主内容
  Widget _buildMainContent(
    BuildContext context,
    SubscriptionProvider provider,
    _SubscriptionListState data,
  ) {
    final trans = context.translate;

    // 如果正在加载
    if (data.isLoading) {
      return const Center(child: CircularProgressIndicator());
    }

    // 如果有错误
    if (data.errorMessage != null) {
      return Center(
        child: Column(
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            const Icon(Icons.error_outline, size: 64, color: Colors.red),
            const SizedBox(height: 16),
            Text(
              data.errorMessage!,
              style: const TextStyle(color: Colors.red),
              textAlign: TextAlign.center,
            ),
          ],
        ),
      );
    }

    // 如果没有订阅
    if (data.subscriptions.isEmpty) {
      return Center(
        child: Column(
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            const Icon(Icons.rss_feed, size: 64, color: Colors.grey),
            const SizedBox(height: 16),
            Text(
              trans.subscription.empty,
              style: const TextStyle(fontSize: 18, color: Colors.grey),
            ),
            const SizedBox(height: 8),
            Text(
              trans.subscription.empty_hint,
              style: const TextStyle(color: Colors.grey),
            ),
          ],
        ),
      );
    }

    // 显示订阅列表（支持拖动排序，响应式布局）
    return Column(
      children: [
        // 切换订阅状态提示
        if (data.isSwitchingSubscription) _buildSwitchingIndicator(context),
        // 订阅列表
        Expanded(child: _buildSubscriptionGrid(context, provider, data)),
      ],
    );
  }

  // 构建切换订阅状态提示
  Widget _buildSwitchingIndicator(BuildContext context) {
    final trans = context.translate;
    final colorScheme = Theme.of(context).colorScheme;

    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
      child: Row(
        children: [
          SizedBox(
            width: 16,
            height: 16,
            child: CircularProgressIndicator(
              strokeWidth: 2,
              valueColor: AlwaysStoppedAnimation<Color>(colorScheme.primary),
            ),
          ),
          const SizedBox(width: 12),
          Text(
            trans.subscription.switching,
            style: TextStyle(fontSize: 13, color: colorScheme.onSurfaceVariant),
          ),
        ],
      ),
    );
  }

  // 构建订阅网格列表
  Widget _buildSubscriptionGrid(
    BuildContext context,
    SubscriptionProvider provider,
    _SubscriptionListState data,
  ) {
    return LayoutBuilder(
      builder: (context, constraints) {
        // 当宽度超过 600 时显示两列，否则一列
        final crossAxisCount = constraints.maxWidth >= 800 ? 2 : 1;

        return ReorderableGridView.builder(
          controller: _scrollController,
          padding: _SubscriptionGridSpacing.gridPadding,
          itemCount: data.subscriptions.length,
          dragEnabled: true,
          onReorder: (oldIndex, newIndex) {
            provider.reorderSubscriptions(oldIndex, newIndex);
          },
          gridDelegate: SliverGridDelegateWithFixedCrossAxisCount(
            crossAxisCount: crossAxisCount,
            crossAxisSpacing: _SubscriptionGridSpacing.cardColumnSpacing,
            mainAxisSpacing: _SubscriptionGridSpacing.cardRowSpacing,
            mainAxisExtent: 110,
          ),
          dragWidgetBuilder: (index, child) {
            final subscription = data.subscriptions[index];
            final isSelected = subscription.id == data.currentSubscriptionId;

            return Material(
              color: Colors.transparent,
              elevation: 0,
              child: SubscriptionCard(
                key: ValueKey(subscription.id),
                subscription: subscription,
                isSelected: isSelected,
                onTap: null,
                onUpdate: null,
                onEdit: null,
                onEditFile: null,
                onViewConfig: null,
                onDelete: null,
                onManageOverride: null,
                onViewProvider: null,
              ),
            );
          },
          itemBuilder: (context, index) {
            final subscription = data.subscriptions[index];
            final isSelected = subscription.id == data.currentSubscriptionId;

            return RepaintBoundary(
              key: ValueKey(subscription.id),
              child: SubscriptionCard(
                subscription: subscription,
                isSelected: isSelected,
                onTap: () => provider.selectSubscription(subscription.id),
                onUpdate: () =>
                    _updateSubscription(context, provider, subscription),
                onEdit: () => _showEditSubscriptionDialog(
                  context,
                  provider,
                  subscription,
                ),
                onEditFile: () =>
                    _showFileEditorDialog(context, provider, subscription),
                onViewConfig: () =>
                    _showViewConfigDialog(context, provider, subscription),
                onDelete: () =>
                    _deleteSubscription(context, provider, subscription),
                onManageOverride: () => _showOverrideManagementDialog(
                  context,
                  provider,
                  subscription,
                ),
                onViewProvider: () => _showProviderViewerDialog(context),
              ),
            );
          },
        );
      },
    );
  }

  // 显示添加配置对话框
  Future<void> _showAddSubscriptionDialog(
    BuildContext context,
    SubscriptionProvider provider,
  ) async {
    await SubscriptionDialog.showAddDialog(
      context,
      onConfirm: (result) async {
        if (result.isLocalImport) {
          // 本地导入逻辑
          if (result.localFilePath == null) return false;

          return await _importLocalFile(context, provider, result);
        }

        // 链接导入逻辑
        if (result.url == null) return false;

        return await provider.addSubscription(
          name: result.name,
          url: result.url!,
          autoUpdateMode: result.autoUpdateMode,
          intervalMinutes: result.intervalMinutes,
          shouldUpdateOnStartup: result.shouldUpdateOnStartup,
          proxyMode: result.proxyMode,
          userAgent: result.userAgent,
        );
      },
    );
  }

  // 显示编辑订阅对话框
  Future<void> _showEditSubscriptionDialog(
    BuildContext context,
    SubscriptionProvider provider,
    Subscription subscription,
  ) async {
    await WidgetsBinding.instance.endOfFrame;
    if (!context.mounted) return;

    // 从 Provider 获取订阅数据
    final latestSubscription = provider.subscriptions.firstWhere(
      (s) => s.id == subscription.id,
      orElse: () => subscription, // 降级：如果找不到则使用传入的订阅
    );

    Logger.debug('打开编辑订阅对话框');
    Logger.debug('订阅名称：${latestSubscription.name}');
    Logger.debug('当前代理模式：${latestSubscription.proxyMode.value}');
    Logger.debug('自动更新模式：${latestSubscription.autoUpdateMode.value}');
    Logger.debug('更新间隔：${latestSubscription.intervalMinutes} 分钟');
    Logger.debug('启动时更新：${latestSubscription.shouldUpdateOnStartup}');

    final result = await SubscriptionDialog.showEditDialog(
      context,
      latestSubscription, // 使用订阅数据
    );

    if (result != null && context.mounted) {
      Logger.debug('用户确认编辑，保存新配置');
      Logger.debug('新代理模式：${result.proxyMode.value}');

      await provider.updateSubscriptionInfo(
        subscriptionId: subscription.id,
        name: result.name,
        url: result.url,
        autoUpdateMode: result.autoUpdateMode,
        intervalMinutes: result.intervalMinutes,
        shouldUpdateOnStartup: result.shouldUpdateOnStartup,
        proxyMode: result.proxyMode,
        userAgent: result.userAgent,
      );
    }
  }

  // 更新订阅
  Future<void> _updateSubscription(
    BuildContext context,
    SubscriptionProvider provider,
    Subscription subscription,
  ) async {
    final trans = context.translate;
    final isSuccess = await provider.updateSubscription(subscription.id);

    if (!context.mounted) return;

    if (isSuccess) {
      ModernToast.success(
        trans.subscription.update_success.replaceAll(
          '{name}',
          subscription.name,
        ),
      );
    } else {
      // 从订阅对象获取错误信息
      final updatedSubscription = provider.subscriptions.firstWhere(
        (s) => s.id == subscription.id,
        orElse: () => subscription,
      );
      final errorMsg = _getErrorMessage(context, updatedSubscription.lastError);

      ModernToast.error('${subscription.name}: $errorMsg');
    }
  }

  // 获取错误消息
  String _getErrorMessage(BuildContext context, String? errorTypeName) {
    final trans = context.translate;

    if (errorTypeName == null) {
      return trans.subscription.update_failed;
    }

    switch (errorTypeName) {
      case 'network':
        return trans.subscription.update_failed_network;
      case 'timeout':
        return trans.subscription.update_failed_timeout;
      case 'notFound':
        return trans.subscription.update_failed_not_found;
      case 'forbidden':
        return trans.subscription.update_failed_forbidden;
      case 'serverError':
        return trans.subscription.update_failed_server;
      case 'formatError':
        return trans.subscription.update_failed_format;
      case 'certificate':
        return trans.subscription.update_failed_certificate;
      default:
        return trans.subscription.update_failed_unknown;
    }
  }

  // 更新所有订阅
  Future<void> _updateAllSubscriptions(
    BuildContext context,
    SubscriptionProvider provider,
  ) async {
    final trans = context.translate;
    final errors = await provider.updateAllSubscriptions();

    if (!context.mounted) return;

    if (errors.isEmpty) {
      ModernToast.success(trans.subscription.update_all_success);
    } else {
      // 只显示成功/失败统计，不显示具体错误
      final successCount = provider.subscriptions.length - errors.length;
      if (successCount > 0) {
        ModernToast.warning(
          trans.subscription.update_partial_success
              .replaceAll('{success}', successCount.toString())
              .replaceAll('{failed}', errors.length.toString()),
        );
      } else {
        ModernToast.error(trans.subscription.update_all_failed);
      }
    }
  }

  // 删除订阅
  Future<void> _deleteSubscription(
    BuildContext context,
    SubscriptionProvider provider,
    Subscription subscription,
  ) async {
    final trans = context.translate;
    final isConfirmed = await showConfirmDialog(
      context: context,
      title: trans.subscription.delete_confirm,
      message: trans.subscription.delete_confirm_message.replaceAll(
        '{name}',
        subscription.name,
      ),
      confirmText: trans.common.delete,
      isDanger: true,
    );

    if (isConfirmed == true && context.mounted) {
      await provider.deleteSubscription(subscription.id);
    }
  }

  // 显示规则覆写选择对话框
  Future<void> _showOverrideManagementDialog(
    BuildContext context,
    SubscriptionProvider provider,
    Subscription subscription,
  ) async {
    await WidgetsBinding.instance.endOfFrame;
    if (!context.mounted) return;

    // 从 Provider 获取订阅数据
    final latestSubscription = provider.subscriptions.firstWhere(
      (s) => s.id == subscription.id,
      orElse: () => subscription, // 降级：如果找不到则使用传入的订阅
    );

    Logger.debug(
      '打开覆写选择对话框 - 订阅: ${latestSubscription.name}, '
      '已选: ${latestSubscription.overrideIds.length} 个',
    );

    final result = await OverrideSelectorDialog.show(
      context,
      initialSelectedIds: latestSubscription.overrideIds,
      initialSortPreference: latestSubscription.overrideSortPreferences,
    );

    if (result == null || !context.mounted) return;

    Logger.debug(
      '覆写配置已修改 - 选中: ${result.selectedIds.length} 个，'
      '排序: ${result.sortPreference.length} 个',
    );

    await provider.updateSubscriptionOverrides(
      subscription.id,
      result.selectedIds,
      result.sortPreference,
    );
  }

  // 显示提供者查看对话框
  Future<void> _showProviderViewerDialog(BuildContext context) async {
    await WidgetsBinding.instance.endOfFrame;
    if (!context.mounted) return;

    Logger.debug('打开提供者查看对话框');

    await ProviderViewerDialog.show(context);
  }

  // 显示文件编辑器对话框
  Future<void> _showFileEditorDialog(
    BuildContext context,
    SubscriptionProvider provider,
    Subscription subscription,
  ) async {
    final trans = context.translate;

    try {
      await WidgetsBinding.instance.endOfFrame;
      if (!context.mounted) return;

      // 从 Provider 获取订阅数据
      final latestSubscription = provider.subscriptions.firstWhere(
        (s) => s.id == subscription.id,
        orElse: () => subscription,
      );

      Logger.debug('打开文件编辑器对话框');
      Logger.debug('订阅名称：${latestSubscription.name}');

      // 读取订阅文件内容
      final content = await provider.readSubscriptionConfig(latestSubscription);
      if (!context.mounted) return;

      await FileEditorDialog.show(
        context,
        fileName: '${latestSubscription.name}.yaml',
        initialContent: content,
        onSave: (nextContent) async {
          // 保存文件并重载配置
          return await provider.saveSubscriptionFile(
            subscription.id,
            nextContent,
          );
        },
      );
    } catch (e) {
      if (!context.mounted) return;

      ModernToast.error(
        trans.file_editor.read_error.replaceAll('{error}', e.toString()),
      );
    }
  }

  // 显示运行配置查看器对话框（只读模式）
  Future<void> _showViewConfigDialog(
    BuildContext context,
    SubscriptionProvider provider,
    Subscription subscription,
  ) async {
    final trans = context.translate;

    try {
      await WidgetsBinding.instance.endOfFrame;
      if (!context.mounted) return;

      // 从 Provider 获取订阅数据
      final latestSubscription = provider.subscriptions.firstWhere(
        (s) => s.id == subscription.id,
        orElse: () => subscription,
      );

      Logger.debug('打开运行配置查看器');
      Logger.debug('订阅名称：${latestSubscription.name}');

      // 读取运行时配置文件（runtime_config.yaml）
      final geoDataDir = await GeoService.getGeoDataDir();
      final runtimeConfigPath = path.join(geoDataDir, 'runtime_config.yaml');
      final runtimeConfigFile = File(runtimeConfigPath);

      // 检查运行时配置文件是否存在
      if (!await runtimeConfigFile.exists()) {
        throw Exception('运行时配置文件不存在，请先启动 Clash 核心');
      }

      final content = await runtimeConfigFile.readAsString();
      if (!context.mounted) return;

      await FileEditorDialog.show(
        context,
        fileName: 'runtime_config.yaml',
        initialContent: content,
        readOnly: true, // 只读模式
        customTitle: '运行时配置', // 自定义标题
        hideSubtitle: false, // 显示文件名
        onSave: null, // 只读模式无需保存回调
      );
    } catch (e) {
      if (!context.mounted) return;

      ModernToast.error(
        trans.file_editor.read_error.replaceAll('{error}', e.toString()),
      );
    }
  }

  // 切换到覆写管理页面
  void _navigateToOverrideManagement(BuildContext context) {
    context.read<ContentProvider>().switchView(ContentView.overrides);
  }

  // 导入本地文件
  Future<bool> _importLocalFile(
    BuildContext context,
    SubscriptionProvider provider,
    SubscriptionDialogResult result,
  ) async {
    return await provider.importLocalFile(
      name: result.name,
      filePath: result.localFilePath!,
    );
  }
}

// 订阅控制栏状态 - 用于 Selector 精确控制重建
class _SubscriptionControlBarState {
  final int subscriptionCount;
  final bool isLoading;

  const _SubscriptionControlBarState({
    required this.subscriptionCount,
    required this.isLoading,
  });

  @override
  bool operator ==(Object other) =>
      identical(this, other) ||
      other is _SubscriptionControlBarState &&
          runtimeType == other.runtimeType &&
          subscriptionCount == other.subscriptionCount &&
          isLoading == other.isLoading;

  @override
  int get hashCode => Object.hash(subscriptionCount, isLoading);
}

// 订阅列表状态 - 用于 Selector 精确控制重建
class _SubscriptionListState {
  final bool isLoading;
  final bool isSwitchingSubscription;
  final String? errorMessage;
  final List<Subscription> subscriptions;
  final String? currentSubscriptionId;

  const _SubscriptionListState({
    required this.isLoading,
    required this.isSwitchingSubscription,
    required this.errorMessage,
    required this.subscriptions,
    required this.currentSubscriptionId,
  });

  @override
  bool operator ==(Object other) =>
      identical(this, other) ||
      other is _SubscriptionListState &&
          runtimeType == other.runtimeType &&
          isLoading == other.isLoading &&
          isSwitchingSubscription == other.isSwitchingSubscription &&
          errorMessage == other.errorMessage &&
          _listEquals(subscriptions, other.subscriptions) &&
          currentSubscriptionId == other.currentSubscriptionId;

  @override
  int get hashCode => Object.hash(
    isLoading,
    isSwitchingSubscription,
    errorMessage,
    subscriptions.length, // 只使用列表长度，避免与 equals 不一致
    currentSubscriptionId,
  );

  bool _listEquals(List<Subscription> a, List<Subscription> b) {
    if (a.length != b.length) return false;
    for (int i = 0; i < a.length; i++) {
      // 比较订阅的所有关键属性，而不仅仅是 ID
      if (a[i].id != b[i].id ||
          a[i].name != b[i].name ||
          a[i].url != b[i].url ||
          a[i].autoUpdateMode != b[i].autoUpdateMode ||
          a[i].intervalMinutes != b[i].intervalMinutes ||
          a[i].shouldUpdateOnStartup != b[i].shouldUpdateOnStartup ||
          a[i].proxyMode != b[i].proxyMode ||
          a[i].isUpdating != b[i].isUpdating ||
          a[i].isLocalFile != b[i].isLocalFile ||
          a[i].lastError != b[i].lastError ||
          !_listEqualsSimple(a[i].overrideIds, b[i].overrideIds)) {
        return false;
      }
    }
    return true;
  }

  // 简单列表比较（用于 overrideIds）
  bool _listEqualsSimple(List<String> a, List<String> b) {
    if (a.length != b.length) return false;
    for (int i = 0; i < a.length; i++) {
      if (a[i] != b[i]) return false;
    }
    return true;
  }
}

import 'dart:io';
import 'dart:async';
import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import 'package:reorderable_grid_view/reorderable_grid_view.dart';
import 'package:stelliberty/clash/providers/subscription_provider.dart';
import 'package:stelliberty/clash/data/subscription_model.dart';
import 'package:stelliberty/ui/widgets/subscription/subscription_card.dart';
import 'package:stelliberty/ui/widgets/subscription/subscription_dialog.dart';
import 'package:stelliberty/ui/widgets/override/override_selector_dialog.dart';
import 'package:stelliberty/ui/widgets/subscription/provider_viewer_dialog.dart';
import 'package:stelliberty/ui/widgets/file_editor_dialog.dart';
import 'package:stelliberty/ui/widgets/modern_toast.dart';
import 'package:stelliberty/ui/widgets/confirm_dialog.dart';
import 'package:stelliberty/providers/content_provider.dart';
import 'package:stelliberty/i18n/i18n.dart';
import 'package:stelliberty/utils/logger.dart';
import 'package:stelliberty/src/bindings/signals/signals.dart';
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
    final colorScheme = Theme.of(context).colorScheme;

    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 12.0),
      child: Row(
        children: [
          // 订阅数统计（使用 Badge 风格）
          Container(
            padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 6),
            decoration: BoxDecoration(
              color: colorScheme.primaryContainer,
              borderRadius: BorderRadius.circular(20),
            ),
            child: Row(
              mainAxisSize: MainAxisSize.min,
              children: [
                Icon(
                  Icons.rss_feed_rounded,
                  size: 16,
                  color: colorScheme.onPrimaryContainer,
                ),
                const SizedBox(width: 6),
                Text(
                  context.translate.subscription.configCount.replaceAll(
                    '{count}',
                    data.subscriptionCount.toString(),
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

          // 覆写管理按钮
          OutlinedButton.icon(
            onPressed: () => _navigateToOverrideManagement(context),
            icon: const Icon(Icons.rule, size: 18),
            label: Text(context.translate.subscription.overrideManagement),
            style: OutlinedButton.styleFrom(
              padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 10),
              textStyle: const TextStyle(
                fontSize: 13,
                fontWeight: FontWeight.w600,
              ),
            ),
          ),

          const SizedBox(width: 8),

          // 添加订阅按钮（FilledButton 风格）
          FilledButton.icon(
            onPressed: () => _showAddSubscriptionDialog(context, provider),
            icon: const Icon(Icons.add_rounded, size: 18),
            label: Text(context.translate.subscription.addConfig),
            style: FilledButton.styleFrom(
              padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 10),
              textStyle: const TextStyle(
                fontSize: 13,
                fontWeight: FontWeight.w600,
              ),
            ),
          ),

          const SizedBox(width: 8),

          // 更新所有按钮（FilledTonal 风格）
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
                    ? context.translate.subscription.updating
                    : context.translate.subscription.updateAll,
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
  }

  // 构建主内容
  Widget _buildMainContent(
    BuildContext context,
    SubscriptionProvider provider,
    _SubscriptionListState data,
  ) {
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
              context.translate.subscription.empty,
              style: const TextStyle(fontSize: 18, color: Colors.grey),
            ),
            const SizedBox(height: 8),
            Text(
              context.translate.subscription.emptyHint,
              style: const TextStyle(color: Colors.grey),
            ),
          ],
        ),
      );
    }

    // 显示订阅列表（支持拖动排序，响应式布局）
    return Scrollbar(
      controller: _scrollController,
      child: LayoutBuilder(
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
      ),
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
          updateOnStartup: result.updateOnStartup,
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

    // 从 Provider 获取最新的订阅数据，避免使用缓存的旧对象
    final latestSubscription = provider.subscriptions.firstWhere(
      (s) => s.id == subscription.id,
      orElse: () => subscription, // 降级：如果找不到则使用传入的订阅
    );

    Logger.debug('打开编辑订阅对话框');
    Logger.debug('订阅名称：${latestSubscription.name}');
    Logger.debug('当前代理模式：${latestSubscription.proxyMode.displayName}');
    Logger.debug('自动更新模式：${latestSubscription.autoUpdateMode.value}');
    Logger.debug('更新间隔：${latestSubscription.intervalMinutes} 分钟');
    Logger.debug('启动时更新：${latestSubscription.updateOnStartup}');

    final result = await SubscriptionDialog.showEditDialog(
      context,
      latestSubscription, // 使用最新的订阅数据
    );

    if (result != null && context.mounted) {
      Logger.debug('用户确认编辑，保存新配置');
      Logger.debug('新代理模式：${result.proxyMode.displayName}');

      await provider.updateSubscriptionInfo(
        subscriptionId: subscription.id,
        name: result.name,
        url: result.url,
        autoUpdateMode: result.autoUpdateMode,
        intervalMinutes: result.intervalMinutes,
        updateOnStartup: result.updateOnStartup,
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
    final success = await provider.updateSubscription(subscription.id);

    if (!context.mounted) return;

    if (success) {
      ModernToast.success(
        context,
        context.translate.subscription.updateSuccess.replaceAll(
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

      ModernToast.error(context, '${subscription.name}: $errorMsg');
    }
  }

  // 将错误类型转换为翻译文本
  String _getErrorMessage(BuildContext context, String? errorTypeName) {
    if (errorTypeName == null) {
      return context.translate.subscription.updateFailed;
    }

    switch (errorTypeName) {
      case 'network':
        return context.translate.subscription.updateFailedNetwork;
      case 'timeout':
        return context.translate.subscription.updateFailedTimeout;
      case 'notFound':
        return context.translate.subscription.updateFailedNotFound;
      case 'forbidden':
        return context.translate.subscription.updateFailedForbidden;
      case 'serverError':
        return context.translate.subscription.updateFailedServer;
      case 'formatError':
        return context.translate.subscription.updateFailedFormat;
      case 'certificate':
        return context.translate.subscription.updateFailedCertificate;
      default:
        return context.translate.subscription.updateFailedUnknown;
    }
  }

  // 更新所有订阅
  Future<void> _updateAllSubscriptions(
    BuildContext context,
    SubscriptionProvider provider,
  ) async {
    final errors = await provider.updateAllSubscriptions();

    if (!context.mounted) return;

    if (errors.isEmpty) {
      ModernToast.success(
        context,
        context.translate.subscription.updateAllSuccess,
      );
    } else {
      // 只显示成功/失败统计，不显示具体错误
      final successCount = provider.subscriptions.length - errors.length;
      if (successCount > 0) {
        ModernToast.warning(
          context,
          context.translate.subscription.updatePartialSuccess
              .replaceAll('{success}', successCount.toString())
              .replaceAll('{failed}', errors.length.toString()),
        );
      } else {
        ModernToast.error(
          context,
          context.translate.subscription.updateAllFailed,
        );
      }
    }
  }

  // 删除订阅
  Future<void> _deleteSubscription(
    BuildContext context,
    SubscriptionProvider provider,
    Subscription subscription,
  ) async {
    final confirmed = await showConfirmDialog(
      context: context,
      title: context.translate.subscription.deleteConfirm,
      message: context.translate.subscription.deleteConfirmMessage.replaceAll(
        '{name}',
        subscription.name,
      ),
      confirmText: context.translate.common.delete,
      isDanger: true,
    );

    if (confirmed == true && context.mounted) {
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

    // 从 Provider 获取最新的订阅数据，避免使用缓存的旧对象
    final latestSubscription = provider.subscriptions.firstWhere(
      (s) => s.id == subscription.id,
      orElse: () => subscription, // 降级：如果找不到则使用传入的订阅
    );

    Logger.debug('打开覆写选择对话框');
    Logger.debug('订阅名称：${latestSubscription.name}');
    Logger.debug('当前覆写 ID 列表：${latestSubscription.overrideIds}');

    final result = await OverrideSelectorDialog.show(
      context,
      initialSelectedIds: latestSubscription.overrideIds,
    );

    Logger.debug('用户选择结果：$result');

    if (result == null || !context.mounted) return;

    // 更新订阅的覆写 ID 列表
    await provider.updateSubscriptionOverrides(subscription.id, result);
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
    try {
      await WidgetsBinding.instance.endOfFrame;
      if (!context.mounted) return;

      // 从 Provider 获取最新的订阅数据，保持与其他对话框一致
      final latestSubscription = provider.subscriptions.firstWhere(
        (s) => s.id == subscription.id,
        orElse: () => subscription,
      );

      Logger.debug('打开文件编辑器对话框');
      Logger.debug('订阅名称：${latestSubscription.name}');

      // 读取订阅文件内容
      final content = await provider.service.readSubscriptionConfig(
        latestSubscription,
      );
      if (!context.mounted) return;

      await FileEditorDialog.show(
        context,
        fileName: '${latestSubscription.name}.yaml',
        initialContent: content,
        onSave: (newContent) async {
          // 保存文件并重载配置
          return await provider.saveSubscriptionFile(
            subscription.id,
            newContent,
          );
        },
      );
    } catch (error) {
      if (!context.mounted) return;

      ModernToast.error(
        context,
        context.translate.fileEditor.readError.replaceAll(
          '{error}',
          error.toString(),
        ),
      );
    }
  }

  // 显示运行配置查看器对话框（只读模式）
  Future<void> _showViewConfigDialog(
    BuildContext context,
    SubscriptionProvider provider,
    Subscription subscription,
  ) async {
    try {
      await WidgetsBinding.instance.endOfFrame;
      if (!context.mounted) return;

      // 从 Provider 获取最新的订阅数据
      final latestSubscription = provider.subscriptions.firstWhere(
        (s) => s.id == subscription.id,
        orElse: () => subscription,
      );

      Logger.debug('打开运行配置查看器');
      Logger.debug('订阅名称：${latestSubscription.name}');

      // 读取订阅文件内容
      final content = await provider.service.readSubscriptionConfig(
        latestSubscription,
      );
      if (!context.mounted) return;

      await FileEditorDialog.show(
        context,
        fileName: '${latestSubscription.name}.yaml',
        initialContent: content,
        readOnly: true, // 只读模式
        customTitle: '运行时配置', // 自定义标题
        hideSubtitle: true, // 隐藏副标题（文件名）
        onSave: null, // 只读模式无需保存回调
      );
    } catch (error) {
      if (!context.mounted) return;

      ModernToast.error(
        context,
        context.translate.fileEditor.readError.replaceAll(
          '{error}',
          error.toString(),
        ),
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
    StreamSubscription? streamListener;

    try {
      final file = File(result.localFilePath!);
      final trans = context.translate.subscriptionDialog;

      if (!await file.exists()) {
        throw Exception(trans.fileNotExist);
      }

      // 读取文件内容
      final content = await file.readAsString();

      // 使用 ProxyParser 解析订阅内容（支持标准 YAML、Base64 编码、纯文本代理链接）
      // 创建 Completer 等待解析结果
      final completer = Completer<String>();

      // 订阅 Rust 信号流
      streamListener = ParseSubscriptionResponse.rustSignalStream.listen((
        rustResult,
      ) {
        if (completer.isCompleted) return;

        if (rustResult.message.success) {
          completer.complete(rustResult.message.parsedConfig);
        } else {
          completer.completeError(Exception(rustResult.message.errorMessage));
        }
      });

      // 发送解析请求到 Rust
      final parseRequest = ParseSubscriptionRequest(content: content);
      parseRequest.sendSignalToRust();

      // 等待解析结果
      final parsedConfig = await completer.future.timeout(
        const Duration(seconds: 30),
        onTimeout: () {
          throw Exception('订阅解析超时');
        },
      );

      // 创建本地配置订阅（使用解析后的配置）
      final success = await provider.addLocalSubscription(
        name: result.name,
        filePath: result.localFilePath!,
        content: parsedConfig,
      );

      return success;
    } catch (error) {
      Logger.error('导入本地文件失败：$error');
      return false;
    } finally {
      // 停止监听信号流，防止内存泄漏
      await streamListener?.cancel();
    }
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
  final String? errorMessage;
  final List<Subscription> subscriptions;
  final String? currentSubscriptionId;

  const _SubscriptionListState({
    required this.isLoading,
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
          errorMessage == other.errorMessage &&
          _listEquals(subscriptions, other.subscriptions) &&
          currentSubscriptionId == other.currentSubscriptionId;

  @override
  int get hashCode => Object.hash(
    isLoading,
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
          a[i].updateOnStartup != b[i].updateOnStartup ||
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

import 'package:flutter/material.dart';
import 'package:flutter/gestures.dart';
import 'package:provider/provider.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:stelliberty/clash/providers/clash_provider.dart';
import 'package:stelliberty/clash/providers/subscription_provider.dart';
import 'package:stelliberty/ui/notifiers/proxy_notifier.dart';
import 'package:stelliberty/ui/widgets/proxy/proxy_action_bar.dart';
import 'package:stelliberty/ui/widgets/proxy/proxy_empty_state.dart';
import 'package:stelliberty/ui/widgets/proxy/proxy_node_grid.dart';
import 'package:stelliberty/utils/logger.dart';
import 'package:stelliberty/i18n/i18n.dart';
import 'package:stelliberty/clash/data/clash_model.dart';
import 'package:stelliberty/ui/widgets/modern_tooltip.dart';

// 代理页面状态数据类（用于优化 Selector）
class _ProxyPageState {
  final int proxyGroupsLength;
  final String? errorMessage;
  final bool isRunning;
  final String mode;

  const _ProxyPageState({
    required this.proxyGroupsLength,
    required this.errorMessage,
    required this.isRunning,
    required this.mode,
  });

  @override
  bool operator ==(Object other) =>
      identical(this, other) ||
      other is _ProxyPageState &&
          runtimeType == other.runtimeType &&
          proxyGroupsLength == other.proxyGroupsLength &&
          errorMessage == other.errorMessage &&
          isRunning == other.isRunning &&
          mode == other.mode;

  @override
  int get hashCode =>
      proxyGroupsLength.hashCode ^
      errorMessage.hashCode ^
      isRunning.hashCode ^
      mode.hashCode;
}

// 代理控制页面
class ProxyPage extends StatefulWidget {
  const ProxyPage({super.key});

  @override
  State<ProxyPage> createState() => _ProxyPageWidgetState();
}

class _ProxyPageWidgetState extends State<ProxyPage>
    with WidgetsBindingObserver {
  late ScrollController _nodeListScrollController;
  final ScrollController _tabScrollController = ScrollController();
  late ProxyNotifier _viewModel;
  int _currentCrossAxisCount = 2;

  // UI 状态
  int _currentGroupIndex = 0;
  double _scrollOffsetCache = 0.0;

  // 缓存 SharedPreferences 实例
  SharedPreferences? _prefs;

  // 持久化键值
  static const String _scrollOffsetKey = 'proxy_page_scroll_offset';
  static const String _subscriptionPathKey = 'proxy_page_subscription_path';

  // 保存上次的订阅路径，用于检测订阅切换
  String? _subscriptionPathCache;

  // UI 常量
  static const double _mouseScrollSpeedMultiplier = 2.0;
  static const double _tabScrollDistance = 200.0;

  @override
  void initState() {
    super.initState();

    Logger.info('初始化 ProxyPage');

    final clashProvider = context.read<ClashProvider>();

    _viewModel = ProxyNotifier(clashProvider: clashProvider);

    // 创建默认 ScrollController
    _nodeListScrollController = ScrollController();

    WidgetsBinding.instance.addObserver(this);
    _nodeListScrollController.addListener(_updateScrollOffset);

    // 在第一帧之前初始化并恢复位置
    _initializeWithScrollPosition();

    WidgetsBinding.instance.addPostFrameCallback((_) async {
      await _initializePage();

      // 延迟一帧触发 setState 以更新按钮状态
      WidgetsBinding.instance.addPostFrameCallback((_) {
        if (mounted) {
          setState(() {});
        }
      });
    });
  }

  // 初始化并立即恢复滚动位置（在首帧渲染前）
  Future<void> _initializeWithScrollPosition() async {
    try {
      _prefs = await SharedPreferences.getInstance();

      // 在 await 之后检查 mounted
      if (!mounted) return;

      final subscriptionProvider = context.read<SubscriptionProvider>();
      final currentPath = subscriptionProvider.getSubscriptionConfigPath();
      final savedPath = _prefs!.getString(_subscriptionPathKey);
      final savedOffset = _prefs!.getDouble(_scrollOffsetKey);

      // 记录当前订阅路径
      _subscriptionPathCache = currentPath;

      // 如果订阅路径匹配且有保存的偏移量，在构建完成后立即设置
      if (currentPath != null &&
          currentPath == savedPath &&
          savedOffset != null &&
          savedOffset > 0) {
        Logger.info('准备使用保存的滚动位置：$savedOffset');

        // 在第一帧渲染时设置滚动位置
        WidgetsBinding.instance.addPostFrameCallback((_) {
          if (_nodeListScrollController.hasClients) {
            // 获取当前滚动范围并限制偏移量
            final maxExtent =
                _nodeListScrollController.position.maxScrollExtent;
            final minExtent =
                _nodeListScrollController.position.minScrollExtent;
            final clampedOffset = savedOffset.clamp(minExtent, maxExtent);

            if (clampedOffset != savedOffset) {
              Logger.info('滚动位置超出范围，已限制：$savedOffset -> $clampedOffset');
            }

            // 使用 jumpTo 而不是 animateTo，避免动画效果
            _nodeListScrollController.jumpTo(clampedOffset);
            Logger.info('已设置初始滚动位置：$clampedOffset');
          }
        });
      }
    } catch (e) {
      Logger.error('初始化滚动位置失败：$e');
    }
  }

  // 初始化页面数据
  Future<void> _initializePage() async {
    final clashProvider = context.read<ClashProvider>();
    final subscriptionProvider = context.read<SubscriptionProvider>();

    // 根据 Clash 运行状态加载代理列表
    if (clashProvider.isRunning && clashProvider.proxyGroups.isEmpty) {
      Logger.info('Clash 正在运行，加载代理列表');
      await clashProvider.loadProxies();
    } else if (!clashProvider.isRunning && clashProvider.proxyGroups.isEmpty) {
      final configPath = subscriptionProvider.getSubscriptionConfigPath();
      Logger.info('Clash 未运行，尝试加载订阅配置预览：$configPath');
      if (configPath != null) {
        await clashProvider.loadProxiesFromSubscription(configPath);
      } else {
        Logger.warning('没有可用的订阅配置路径');
      }
    }
  }

  void _updateScrollOffset() {
    if (!_nodeListScrollController.hasClients) return;

    _scrollOffsetCache = _nodeListScrollController.offset;
  }

  void _saveScrollPositionSync() {
    if (_prefs == null) {
      Logger.warning('SharedPreferences 未初始化，无法保存滚动位置');
      return;
    }

    try {
      Logger.info('保存代理页面滚动位置：$_scrollOffsetCache');
      _prefs!.setDouble(_scrollOffsetKey, _scrollOffsetCache);
    } catch (e) {
      Logger.error('保存滚动位置失败：$e');
    }
  }

  @override
  void dispose() {
    // 在 dispose 前同步保存滚动位置
    _saveScrollPositionSync();

    WidgetsBinding.instance.removeObserver(this);
    _nodeListScrollController.removeListener(_updateScrollOffset);
    _nodeListScrollController.dispose();
    _tabScrollController.dispose();
    _viewModel.dispose();
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    super.didChangeAppLifecycleState(state);

    // 当应用从后台恢复时，记录日志即可
    if (state == AppLifecycleState.resumed) {
      Logger.debug('应用恢复');
    }
  }

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Expanded(
          child: Selector<ClashProvider, _ProxyPageState>(
            selector: (_, clash) => _ProxyPageState(
              proxyGroupsLength: clash.proxyGroups.length,
              errorMessage: clash.errorMessage,
              isRunning: clash.isRunning,
              mode: clash.mode,
            ),
            builder: (context, state, child) {
              final clashProvider = context.read<ClashProvider>();
              final subscriptionProvider = context.read<SubscriptionProvider>();

              return _buildMainContent(
                context,
                clashProvider,
                subscriptionProvider,
              );
            },
          ),
        ),
      ],
    );
  }

  Widget _buildMainContent(
    BuildContext context,
    ClashProvider clashProvider,
    SubscriptionProvider subscriptionProvider,
  ) {
    if (subscriptionProvider.getSubscriptionConfigPath() == null) {
      return ProxyEmptyState(
        type: ProxyEmptyStateType.noSubscription,
        message: context.translate.proxy.noSubscription,
        subtitle: context.translate.proxy.pleaseAddSubscription,
      );
    }

    if (clashProvider.errorMessage != null) {
      return ProxyEmptyState(
        type: ProxyEmptyStateType.error,
        message: clashProvider.errorMessage!,
      );
    }

    if (clashProvider.proxyGroups.isEmpty) {
      if (clashProvider.isRunning && clashProvider.mode == 'direct') {
        return ProxyEmptyState(
          type: ProxyEmptyStateType.directMode,
          message: context.translate.proxy.directModeEnabled,
          subtitle: context.translate.proxy.directModeDescription,
        );
      }

      return ProxyEmptyState(
        type: ProxyEmptyStateType.noProxyGroups,
        message: context.translate.proxy.noProxyGroups,
        subtitle: !clashProvider.isRunning
            ? context.translate.proxy.loadAfterStart
            : null,
      );
    }

    return _buildProxyNodeList(context, clashProvider);
  }

  Widget _buildProxyNodeList(
    BuildContext context,
    ClashProvider clashProvider,
  ) {
    if (clashProvider.proxyGroups.isEmpty) {
      return Center(child: Text(context.translate.proxy.noProxyGroups));
    }

    // 检测订阅是否切换
    final subscriptionProvider = context.read<SubscriptionProvider>();
    final currentPath = subscriptionProvider.getSubscriptionConfigPath();
    if (currentPath != _subscriptionPathCache) {
      // 订阅已切换，重置代理组索引
      Logger.info('检测到订阅切换：$_subscriptionPathCache -> $currentPath');
      _currentGroupIndex = 0;
      _subscriptionPathCache = currentPath;
    }

    // 确保当前选中的代理组索引有效
    if (_currentGroupIndex >= clashProvider.proxyGroups.length) {
      Logger.warning(
        '代理组索引越界，重置为 0：$_currentGroupIndex >= ${clashProvider.proxyGroups.length}',
      );
      _currentGroupIndex = 0;
    }
    final selectedGroup = clashProvider.proxyGroups[_currentGroupIndex];

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Padding(
          padding: const EdgeInsets.symmetric(horizontal: 16.0),
          child: _buildGroupSelectorHeader(
            context,
            clashProvider,
            selectedGroup,
          ),
        ),
        const Divider(height: 1),
        ListenableBuilder(
          listenable: _viewModel,
          builder: (context, _) {
            return ProxyActionBar(
              selectedGroupName: selectedGroup.name,
              onLocate: () =>
                  _locateSelectedNode(context, clashProvider, selectedGroup),
              sortMode: _viewModel.sortMode,
              onSortModeChanged: _viewModel.changeSortMode,
            );
          },
        ),
        _buildSortedProxyNodeGrid(context, clashProvider, selectedGroup),
      ],
    );
  }

  Future<void> _selectProxy(
    BuildContext context,
    String groupName,
    String proxyName,
  ) async {
    final clashProvider = context.read<ClashProvider>();
    await clashProvider.changeProxy(groupName, proxyName);

    // 移除 setState(),让 ClashProvider.notifyListeners() 触发 Selector 更新
    // 这样只会重建 ProxyNodeGrid,而不是整个页面
  }

  Future<void> _testSingleNodeDelay(
    BuildContext context,
    String proxyName,
  ) async {
    final clashProvider = context.read<ClashProvider>();
    await clashProvider.testProxyDelay(proxyName);
  }

  void _scrollTabByDistance(double distance) {
    if (!_tabScrollController.hasClients) return;

    final offset = _tabScrollController.offset + distance;
    _tabScrollController.animateTo(
      offset.clamp(0.0, _tabScrollController.position.maxScrollExtent),
      duration: const Duration(milliseconds: 200),
      curve: Curves.easeOut,
    );
  }

  Widget _buildGroupSelectorHeader(
    BuildContext context,
    ClashProvider clashProvider,
    dynamic selectedGroup,
  ) {
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 16.0),
      child: Row(
        children: [
          Expanded(
            child: Listener(
              onPointerSignal: (pointerSignal) {
                if (pointerSignal is PointerScrollEvent &&
                    _tabScrollController.hasClients) {
                  final offset =
                      _tabScrollController.offset +
                      pointerSignal.scrollDelta.dy *
                          _mouseScrollSpeedMultiplier;
                  _tabScrollController.animateTo(
                    offset.clamp(
                      0.0,
                      _tabScrollController.position.maxScrollExtent,
                    ),
                    duration: const Duration(milliseconds: 100),
                    curve: Curves.easeOut,
                  );
                }
              },
              child: SingleChildScrollView(
                controller: _tabScrollController,
                scrollDirection: Axis.horizontal,
                physics: const BouncingScrollPhysics(),
                child: Row(
                  children: List.generate(clashProvider.proxyGroups.length, (
                    index,
                  ) {
                    final group = clashProvider.proxyGroups[index];
                    final isSelected = index == _currentGroupIndex;

                    return Padding(
                      padding: const EdgeInsets.only(right: 8.0),
                      child: GestureDetector(
                        onTap: () => _switchToGroup(index),
                        child: AnimatedContainer(
                          duration: const Duration(milliseconds: 200),
                          padding: const EdgeInsets.symmetric(
                            horizontal: 12,
                            vertical: 6,
                          ),
                          decoration: BoxDecoration(
                            color: isSelected
                                ? Theme.of(context).colorScheme.primary
                                : Theme.of(
                                    context,
                                  ).colorScheme.surfaceContainerHighest,
                            borderRadius: BorderRadius.circular(16),
                            border: Border.all(
                              color: isSelected
                                  ? Theme.of(context).colorScheme.primary
                                  : Theme.of(context).colorScheme.outline
                                        .withValues(alpha: 0.3),
                              width: 1,
                            ),
                          ),
                          child: Center(
                            child: Text(
                              group.name,
                              style: TextStyle(
                                color: isSelected
                                    ? Theme.of(context).colorScheme.onPrimary
                                    : Theme.of(context).colorScheme.onSurface,
                                fontWeight: isSelected
                                    ? FontWeight.bold
                                    : FontWeight.normal,
                                fontSize: 13,
                              ),
                              overflow: TextOverflow.ellipsis,
                              maxLines: 1,
                            ),
                          ),
                        ),
                      ),
                    );
                  }),
                ),
              ),
            ),
          ),
          const SizedBox(width: 12),
          ListenableBuilder(
            listenable: _tabScrollController,
            builder: (context, _) {
              final canScrollLeft =
                  _tabScrollController.hasClients &&
                  _tabScrollController.position.hasContentDimensions &&
                  _tabScrollController.offset > 0;

              final canScrollRight =
                  _tabScrollController.hasClients &&
                  _tabScrollController.position.hasContentDimensions &&
                  _tabScrollController.offset <
                      _tabScrollController.position.maxScrollExtent;

              return Row(
                mainAxisSize: MainAxisSize.min,
                children: [
                  ModernTooltip(
                    message: context.translate.proxy.scrollLeft,
                    child: IconButton(
                      onPressed: canScrollLeft
                          ? () => _scrollTabByDistance(-_tabScrollDistance)
                          : null,
                      icon: const Icon(Icons.chevron_left),
                      iconSize: 20,
                      visualDensity: VisualDensity.compact,
                    ),
                  ),
                  ModernTooltip(
                    message: context.translate.proxy.scrollRight,
                    child: IconButton(
                      onPressed: canScrollRight
                          ? () => _scrollTabByDistance(_tabScrollDistance)
                          : null,
                      icon: const Icon(Icons.chevron_right),
                      iconSize: 20,
                      visualDensity: VisualDensity.compact,
                    ),
                  ),
                ],
              );
            },
          ),
        ],
      ),
    );
  }

  void _switchToGroup(int groupIndex) {
    final clashProvider = context.read<ClashProvider>();
    if (groupIndex < 0 || groupIndex >= clashProvider.proxyGroups.length) {
      Logger.warning(
        '代理组索引越界：$groupIndex（总数：${clashProvider.proxyGroups.length}）',
      );
      return;
    }

    setState(() {
      _currentGroupIndex = groupIndex;
    });
    _scrollToSelectedTab(groupIndex);
    Logger.debug('切换代理组：$groupIndex');
  }

  void _scrollToSelectedTab(int selectedIndex) {
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!_tabScrollController.hasClients) return;

      // 简化的滚动逻辑：根据索引计算大致位置
      // 假设每个标签平均宽度约 100-120px
      const estimatedTabWidth = 110.0;
      final viewportWidth = _tabScrollController.position.viewportDimension;

      // 计算目标位置：让选中标签尽量居中
      double targetOffset =
          (selectedIndex * estimatedTabWidth) -
          (viewportWidth / 2) +
          (estimatedTabWidth / 2);

      // 限制在有效范围内
      targetOffset = targetOffset.clamp(
        0.0,
        _tabScrollController.position.maxScrollExtent,
      );

      _tabScrollController.animateTo(
        targetOffset,
        duration: const Duration(milliseconds: 300),
        curve: Curves.easeInOut,
      );
    });
  }

  Widget _buildSortedProxyNodeGrid(
    BuildContext context,
    ClashProvider clashProvider,
    ProxyGroup selectedGroup,
  ) {
    return ProxyNodeGrid(
      clashProvider: clashProvider,
      selectedGroupName: selectedGroup.name, // 只传递组名,不传递整个对象
      viewModel: _viewModel, // 传递 viewModel 用于排序
      scrollController: _nodeListScrollController,
      onCrossAxisCountChanged: (count) => _currentCrossAxisCount = count,
      onSelectProxy: (groupName, proxyName) =>
          _selectProxy(context, groupName, proxyName),
      onTestDelay: (proxyName) => _testSingleNodeDelay(context, proxyName),
    );
  }

  void _locateSelectedNode(
    BuildContext context,
    ClashProvider clashProvider,
    dynamic selectedGroup,
  ) {
    final currentNodeName = selectedGroup.now;
    if (currentNodeName == null || currentNodeName.isEmpty) return;

    if (!_nodeListScrollController.hasClients) return;

    final targetOffset = _viewModel.calculateLocateOffset(
      nodeName: currentNodeName,
      selectedGroup: selectedGroup,
      crossAxisCount: _currentCrossAxisCount,
      maxScrollExtent: _nodeListScrollController.position.maxScrollExtent,
      viewportHeight: _nodeListScrollController.position.viewportDimension,
    );

    if (targetOffset == null) return;

    _nodeListScrollController.animateTo(
      targetOffset,
      duration: const Duration(milliseconds: 500),
      curve: Curves.easeInOut,
    );
  }
}

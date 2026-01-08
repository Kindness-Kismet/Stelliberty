import 'package:flutter/material.dart';
import 'package:stelliberty/clash/model/clash_model.dart';
import 'package:stelliberty/clash/providers/clash_provider.dart';
import 'package:stelliberty/ui/widgets/proxy/proxy_group_card_vertical.dart';
import 'package:stelliberty/ui/notifiers/proxy_notifier.dart';
import 'package:stelliberty/storage/preferences.dart';
import 'package:stelliberty/services/log_print_service.dart';

// 竖向模式的代理组列表（带高度计算优化）
class ProxyGroupListVertical extends StatefulWidget {
  final ClashProvider clashProvider;
  final ProxyNotifier viewModel;
  final ScrollController scrollController;
  final Function(String groupName, String proxyName) onSelectProxy;
  final Function(String proxyName) onTestDelay;

  const ProxyGroupListVertical({
    super.key,
    required this.clashProvider,
    required this.viewModel,
    required this.scrollController,
    required this.onSelectProxy,
    required this.onTestDelay,
  });

  @override
  State<ProxyGroupListVertical> createState() => _ProxyGroupListVerticalState();
}

class _ProxyGroupListVerticalState extends State<ProxyGroupListVertical> {
  // 记录每个代理组的展开状态
  final Map<String, bool> _expandedStates = {};
  // 记录每个节点卡片的 GlobalKey（用于定位）
  final Map<String, GlobalKey> _nodeKeys = {};
  // 当前列数
  int _columns = 2;
  // 是否正在加载（用于骨架屏显示）
  bool _isLoading = true;

  // 固定高度常量（用于预计算）
  static const double _nodeCardHeight = 88.0;
  static const double _nodeSpacing = 12.0;
  static const double _cardPadding = 16.0;
  static const double _cardMargin = 16.0;

  @override
  void initState() {
    super.initState();
    // 从本地存储加载折叠状态
    _loadExpandedStates();
    // 延迟显示真实内容，先展示骨架屏
    _delayedLoad();
  }

  // 延迟加载，让骨架屏先显示一帧
  void _delayedLoad() {
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (mounted) {
        setState(() {
          _isLoading = false;
        });
      }
    });
  }

  // 从本地存储加载折叠状态
  void _loadExpandedStates() {
    try {
      final savedStates = AppPreferences.instance.getProxyGroupExpandedStates();
      _expandedStates.addAll(savedStates);
      Logger.debug('加载折叠状态：${savedStates.length} 个代理组');
    } catch (e) {
      Logger.error('加载折叠状态失败：$e');
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    // 加载中显示骨架屏
    if (_isLoading) {
      return _buildSkeleton(theme);
    }

    return ListenableBuilder(
      // 同时监听 viewModel 和 clashProvider 的变化
      listenable: Listenable.merge([widget.viewModel, widget.clashProvider]),
      builder: (context, _) {
        // 获取过滤后的代理组
        final filteredGroups = _filterGroups();

        if (filteredGroups.isEmpty) {
          return Center(
            child: Text(
              widget.viewModel.searchQuery.isEmpty ? '没有可用的代理组' : '没有匹配的代理组',
              style: Theme.of(context).textTheme.bodyMedium?.copyWith(
                color: Theme.of(context).colorScheme.onSurfaceVariant,
              ),
            ),
          );
        }

        return LayoutBuilder(
          builder: (context, constraints) {
            // 动态计算列数
            _columns = _calculateColumns(constraints.maxWidth);

            // 计算总高度用于 cacheExtent
            double totalHeight = 8.0 + 8.0; // 顶部和底部 padding
            for (var group in filteredGroups) {
              final isExpanded = _expandedStates[group.name] ?? false;
              totalHeight += _calculateGroupHeight(group, isExpanded);
            }

            return ListView.builder(
              controller: widget.scrollController,
              padding: const EdgeInsets.symmetric(vertical: 8),
              itemCount: filteredGroups.length,
              // 设置缓存范围为总高度，确保滚动条准确
              cacheExtent: totalHeight,
              itemBuilder: (context, index) {
                final group = filteredGroups[index];
                final groupName = group.name;

                // 获取展开状态
                final isExpanded = _expandedStates[groupName] ?? false;

                return ProxyGroupCardVertical(
                  group: group,
                  isExpanded: isExpanded,
                  columns: _columns,
                  onToggle: () => _toggleGroup(groupName),
                  onSelectProxy: (proxyName) =>
                      widget.onSelectProxy(groupName, proxyName),
                  onTestDelay: widget.onTestDelay,
                  isCoreRunning: widget.clashProvider.isCoreRunning,
                  proxyNodes: widget.clashProvider.proxyNodes,
                  testingNodes: widget.clashProvider.testingNodes,
                  viewModel: widget.viewModel,
                  onLocate: () => _locateToSelectedNode(groupName),
                  nodeKeys: _nodeKeys,
                );
              },
            );
          },
        );
      },
    );
  }

  // 骨架屏占位 UI
  Widget _buildSkeleton(ThemeData theme) {
    final skeletonColor = theme.colorScheme.surfaceContainerHighest.withAlpha(
      100,
    );

    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
      child: Column(
        children: List.generate(3, (index) {
          return Padding(
            padding: const EdgeInsets.only(bottom: 16),
            child: Container(
              height: 88.0,
              decoration: BoxDecoration(
                color: skeletonColor,
                borderRadius: BorderRadius.circular(12),
              ),
            ),
          );
        }),
      ),
    );
  }

  // 切换代理组展开/折叠状态
  void _toggleGroup(String groupName) {
    setState(() {
      _expandedStates[groupName] = !(_expandedStates[groupName] ?? false);
    });

    // 保存到本地存储
    AppPreferences.instance.setProxyGroupExpanded(
      groupName,
      _expandedStates[groupName]!,
    );
    Logger.debug('保存折叠状态：$groupName = ${_expandedStates[groupName]}');
  }

  // 定位到指定代理组的选中节点
  void _locateToSelectedNode(String groupName) {
    final group = widget.clashProvider.proxyGroups.firstWhere(
      (g) => g.name == groupName,
      orElse: () => throw Exception('Group not found'),
    );

    final selectedNodeName = group.now;
    if (selectedNodeName == null || selectedNodeName.isEmpty) {
      return;
    }

    // 生成节点的唯一 key
    final nodeKey = '${groupName}_$selectedNodeName';

    // 如果 key 不存在，创建一个
    _nodeKeys.putIfAbsent(nodeKey, () => GlobalKey());

    final key = _nodeKeys[nodeKey]!;

    // 延迟执行，确保 widget 已经渲染
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (key.currentContext != null) {
        Scrollable.ensureVisible(
          key.currentContext!,
          duration: const Duration(milliseconds: 500),
          curve: Curves.easeInOut,
          alignment: 0.5, // 居中显示
        );
      }
    });
  }

  // 过滤代理组（不排序代理组本身，排序只应用于节点）
  List<ProxyGroup> _filterGroups() {
    var result = widget.clashProvider.proxyGroups;

    // 应用搜索过滤
    if (widget.viewModel.searchQuery.isNotEmpty) {
      final query = widget.viewModel.searchQuery.toLowerCase();
      result = result.where((group) {
        // 搜索代理组名称
        if (group.name.toLowerCase().contains(query)) {
          return true;
        }
        // 搜索节点名称
        final nodeNames = group.all;
        return nodeNames.any((nodeName) {
          return nodeName.toLowerCase().contains(query);
        });
      }).toList();
    }

    // 注意：不对代理组本身排序，排序只应用于节点

    return result;
  }

  // 动态计算列数（基于宽度）
  int _calculateColumns(double width) {
    const minCardWidth = 280.0;
    return (width / minCardWidth).floor().clamp(2, 999);
  }

  // 计算单个代理组的高度
  double _calculateGroupHeight(ProxyGroup group, bool isExpanded) {
    if (!isExpanded) {
      // 折叠状态：header + Card margin
      return 88.0;
    }

    final nodeCount = group.all.length;
    final rows = (nodeCount / _columns).ceil();
    final nodesHeight = rows * _nodeCardHeight + (rows - 1) * _nodeSpacing;

    // 展开状态：header + padding + nodes + padding + margin
    return 72.0 + _cardPadding + nodesHeight + _cardPadding + _cardMargin;
  }
}

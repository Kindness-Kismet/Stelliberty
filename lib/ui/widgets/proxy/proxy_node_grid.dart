import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import 'package:stelliberty/atomic/platform_helper.dart';
import 'package:stelliberty/clash/providers/clash_provider.dart';
import 'package:stelliberty/clash/model/clash_model.dart';
import 'package:stelliberty/ui/widgets/proxy/proxy_node_card.dart';
import 'package:stelliberty/ui/viewmodels/proxy_viewmodel.dart';
import 'package:stelliberty/ui/constants/spacing.dart';
import 'package:stelliberty/services/log_print_service.dart';
import 'package:stelliberty/i18n/i18n.dart';

// 代理页布局常量
class _ProxyGridSpacing {
  _ProxyGridSpacing._();

  // 桌面端间距
  static const desktopGridLeftEdge = 16.0;
  static const desktopGridTopEdge = 10.0;
  static const desktopGridRightEdge =
      16.0 - SpacingConstants.scrollbarRightCompensation;
  static const desktopGridBottomEdge = 10.0;
  static const desktopCardColumnSpacing = 16.0;
  static const desktopCardRowSpacing = 16.0;
  static const desktopCardHeight = 88.0;
  static const desktopCardMinWidth = 280.0;

  // 移动端间距（更紧凑）
  static const mobileGridLeftEdge = 10.0;
  static const mobileGridTopEdge = 8.0;
  static const mobileGridRightEdge = 10.0;
  static const mobileGridBottomEdge = 8.0;
  static const mobileCardColumnSpacing = 10.0;
  static const mobileCardRowSpacing = 10.0;
  static const mobileCardHeight = 64.0;
  static const mobileCardMinWidth = 160.0;

  static EdgeInsets get gridPadding => PlatformHelper.isMobile
      ? const EdgeInsets.fromLTRB(
          mobileGridLeftEdge,
          mobileGridTopEdge,
          mobileGridRightEdge,
          mobileGridBottomEdge,
        )
      : const EdgeInsets.fromLTRB(
          desktopGridLeftEdge,
          desktopGridTopEdge,
          desktopGridRightEdge,
          desktopGridBottomEdge,
        );

  static double get cardColumnSpacing => PlatformHelper.isMobile
      ? mobileCardColumnSpacing
      : desktopCardColumnSpacing;

  static double get cardRowSpacing =>
      PlatformHelper.isMobile ? mobileCardRowSpacing : desktopCardRowSpacing;

  static double get cardHeight =>
      PlatformHelper.isMobile ? mobileCardHeight : desktopCardHeight;

  static double get cardMinWidth =>
      PlatformHelper.isMobile ? mobileCardMinWidth : desktopCardMinWidth;
}

// 代理节点网格状态（用于 Selector）
class _ProxyNodeGridState {
  final Map<String, dynamic> proxyNodes;
  final Set<String> testingNodes;
  final bool isBatchTestingDelay;
  final int updateCount;
  final String? selectedProxyName; // 关键：选中的节点名称

  const _ProxyNodeGridState({
    required this.proxyNodes,
    required this.testingNodes,
    required this.isBatchTestingDelay,
    required this.updateCount,
    required this.selectedProxyName,
  });

  @override
  bool operator ==(Object other) =>
      identical(this, other) ||
      other is _ProxyNodeGridState &&
          runtimeType == other.runtimeType &&
          proxyNodes.length == other.proxyNodes.length &&
          testingNodes.length == other.testingNodes.length &&
          isBatchTestingDelay == other.isBatchTestingDelay &&
          updateCount == other.updateCount &&
          selectedProxyName == other.selectedProxyName;

  @override
  int get hashCode =>
      proxyNodes.length.hashCode ^
      testingNodes.length.hashCode ^
      isBatchTestingDelay.hashCode ^
      updateCount.hashCode ^
      selectedProxyName.hashCode;
}

// 代理节点网格列表组件
class ProxyNodeGrid extends StatefulWidget {
  final ClashProvider clashProvider;
  final String selectedGroupName; // 改为只传递组名
  final ProxyViewModel viewModel; // 用于排序
  final ScrollController scrollController;
  final Function(int) onCrossAxisCountChanged;
  final Function(String groupName, String proxyName) onSelectProxy;
  final Function(String proxyName) onTestDelay;

  const ProxyNodeGrid({
    super.key,
    required this.clashProvider,
    required this.selectedGroupName,
    required this.viewModel,
    required this.scrollController,
    required this.onCrossAxisCountChanged,
    required this.onSelectProxy,
    required this.onTestDelay,
  });

  @override
  State<ProxyNodeGrid> createState() => _ProxyNodeGridWidgetState();
}

class _ProxyNodeGridWidgetState extends State<ProxyNodeGrid> {
  int _crossAxisCountCache = 0;

  @override
  Widget build(BuildContext context) {
    final trans = context.translate;

    return Expanded(
      child: Padding(
        padding: SpacingConstants.scrollbarPadding,
        child: ListenableBuilder(
          listenable: widget.viewModel, // 监听排序变化
          builder: (context, _) {
            return Selector<ClashProvider, _ProxyNodeGridState>(
              selector: (_, clash) {
                // 获取 selectedGroup 以获取当前选中节点
                final selectedGroup = clash.proxyGroups.firstWhere(
                  (g) => g.name == widget.selectedGroupName,
                  orElse: () => clash.proxyGroups.isNotEmpty
                      ? clash.proxyGroups.first
                      : ProxyGroup(name: '', type: '', now: null, all: []),
                );

                return _ProxyNodeGridState(
                  proxyNodes: clash.proxyNodes,
                  testingNodes: clash.testingNodes,
                  isBatchTestingDelay: clash.isBatchTestingDelay,
                  updateCount: clash.proxyNodesUpdateCount,
                  selectedProxyName: selectedGroup.now, // 关键：传递选中节点名
                );
              },
              builder: (context, state, child) {
                // 从 clash.proxyGroups 中获取 selectedGroup
                final clashProvider = context.read<ClashProvider>();
                final selectedGroup = clashProvider.proxyGroups.firstWhere(
                  (g) => g.name == widget.selectedGroupName,
                  orElse: () => clashProvider.proxyGroups.first,
                );

                // 应用排序
                final sortedProxyNames = widget.viewModel.getSortedProxyNames(
                  selectedGroup.all,
                );
                final sortedGroup = selectedGroup.copyWith(
                  all: sortedProxyNames,
                );

                return LayoutBuilder(
                  builder: (context, constraints) {
                    final int crossAxisCount =
                        (constraints.maxWidth / _ProxyGridSpacing.cardMinWidth)
                            .floor()
                            .clamp(2, 999);

                    // 只在列数变化时调用回调
                    if (crossAxisCount != _crossAxisCountCache) {
                      _crossAxisCountCache = crossAxisCount;
                      widget.onCrossAxisCountChanged(crossAxisCount);
                    }

                    return GridView.builder(
                      controller: widget.scrollController,
                      padding: _ProxyGridSpacing.gridPadding,
                      gridDelegate: SliverGridDelegateWithFixedCrossAxisCount(
                        crossAxisCount: crossAxisCount,
                        crossAxisSpacing: _ProxyGridSpacing.cardColumnSpacing,
                        mainAxisSpacing: _ProxyGridSpacing.cardRowSpacing,
                        mainAxisExtent: _ProxyGridSpacing.cardHeight,
                      ),
                      itemCount: sortedGroup.all.length,
                      // 优化渲染性能
                      cacheExtent: 500.0,
                      addAutomaticKeepAlives: true,
                      addRepaintBoundaries: true,
                      itemBuilder: (context, index) {
                        final proxyName = sortedGroup.all[index];
                        final node = state.proxyNodes[proxyName];

                        if (node == null) {
                          // 简化日志输出，避免滚动时性能问题
                          Logger.warning('节点信息不可用: $proxyName');

                          return Card(
                            child: ListTile(
                              title: Text(
                                proxyName,
                                style: const TextStyle(fontSize: 14),
                              ),
                              subtitle: Text(
                                trans.proxy.node_info_unavailable,
                                style: const TextStyle(fontSize: 12),
                              ),
                            ),
                          );
                        }

                        final isSelected = sortedGroup.now == proxyName;
                        final isWaitingTest = state.testingNodes.contains(
                          proxyName,
                        );

                        // 使用 RepaintBoundary 隔离重绘，优化渲染性能
                        return RepaintBoundary(
                          child: ProxyNodeCard(
                            node: node,
                            isSelected: isSelected,
                            isClashRunning: widget.clashProvider.isCoreRunning,
                            isWaitingTest: isWaitingTest,
                            onTap: () => widget.onSelectProxy(
                              sortedGroup.name,
                              proxyName,
                            ),
                            onTestDelay: () => widget.onTestDelay(proxyName),
                          ),
                        );
                      },
                    );
                  },
                );
              },
            );
          },
        ),
      ),
    );
  }
}

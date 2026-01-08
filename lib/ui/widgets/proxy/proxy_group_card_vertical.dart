import 'package:flutter/material.dart';
import 'package:cached_network_image/cached_network_image.dart';
import 'package:stelliberty/clash/model/clash_model.dart';
import 'package:stelliberty/ui/widgets/proxy/proxy_node_card.dart';
import 'package:stelliberty/ui/notifiers/proxy_notifier.dart';
import 'package:stelliberty/services/image_cache_service.dart';
import 'package:stelliberty/i18n/i18n.dart';

// 竖向模式的代理组折叠卡片
class ProxyGroupCardVertical extends StatefulWidget {
  final ProxyGroup group;
  final bool isExpanded;
  final int columns;
  final VoidCallback onToggle;
  final Function(String proxyName) onSelectProxy;
  final Function(String proxyName) onTestDelay;
  final bool isCoreRunning;
  final Map<String, dynamic> proxyNodes;
  final Set<String> testingNodes; // 正在测试的节点集合
  final ProxyNotifier viewModel;
  final VoidCallback? onLocate;
  final Map<String, GlobalKey>? nodeKeys;

  const ProxyGroupCardVertical({
    super.key,
    required this.group,
    required this.isExpanded,
    required this.columns,
    required this.onToggle,
    required this.onSelectProxy,
    required this.onTestDelay,
    required this.isCoreRunning,
    required this.proxyNodes,
    required this.testingNodes,
    required this.viewModel,
    this.onLocate,
    this.nodeKeys,
  });

  @override
  State<ProxyGroupCardVertical> createState() => _ProxyGroupCardVerticalState();
}

class _ProxyGroupCardVerticalState extends State<ProxyGroupCardVertical> {
  final TextEditingController _searchController = TextEditingController();
  final FocusNode _searchFocusNode = FocusNode();

  @override
  void dispose() {
    _searchController.dispose();
    _searchFocusNode.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;

    return Card(
      margin: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
      elevation: 0,
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(12),
        side: BorderSide(
          color: colorScheme.outlineVariant.withValues(alpha: 0.5),
          width: 1,
        ),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        mainAxisSize: MainAxisSize.min,
        children: [
          _buildHeader(context, theme, colorScheme),
          if (widget.isExpanded) _buildContent(context),
        ],
      ),
    );
  }

  // 构建卡片头部
  Widget _buildHeader(
    BuildContext context,
    ThemeData theme,
    ColorScheme colorScheme,
  ) {
    final trans = context.translate;

    final currentNode = widget.group.now ?? trans.proxy.not_selected;
    final nodeCount = widget.group.all.length;
    final hasSelectedNode =
        widget.group.now != null && widget.group.now!.isNotEmpty;

    const double buttonSize = 32.0;
    const double iconSize = 20.0;
    const double backgroundAlpha = 0.3;

    return InkWell(
      onTap: widget.onToggle,
      borderRadius: BorderRadius.circular(12),
      child: Padding(
        padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
        child: Row(
          children: [
            // 代理组信息
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  // 代理组名称和图标
                  Row(
                    children: [
                      // 代理组图标
                      if (widget.group.icon != null &&
                          widget.group.icon!.isNotEmpty)
                        SizedBox(
                          width: 24,
                          height: 24,
                          child: ClipRRect(
                            borderRadius: BorderRadius.circular(3),
                            child: CachedNetworkImage(
                              imageUrl: widget.group.icon!,
                              width: 24,
                              height: 24,
                              fit: BoxFit.cover,
                              cacheManager:
                                  ImageCacheService.instance.cacheManager,
                              placeholder: (context, url) => const Icon(
                                Icons.language,
                                size: 18,
                                color: Colors.grey,
                              ),
                              errorWidget: (context, url, error) => const Icon(
                                Icons.language,
                                size: 18,
                                color: Colors.grey,
                              ),
                            ),
                          ),
                        )
                      else
                        Icon(Icons.language, size: 18, color: Colors.grey),
                      const SizedBox(width: 8),
                      Flexible(
                        child: Text(
                          widget.group.name,
                          style: theme.textTheme.titleMedium?.copyWith(
                            fontWeight: FontWeight.w600,
                          ),
                          overflow: TextOverflow.ellipsis,
                        ),
                      ),
                    ],
                  ),
                  const SizedBox(height: 4),
                  // 当前选中节点
                  Row(
                    children: [
                      Icon(
                        Icons.check_circle_outline,
                        size: 14,
                        color: colorScheme.primary,
                      ),
                      const SizedBox(width: 4),
                      Flexible(
                        child: Text(
                          currentNode,
                          style: theme.textTheme.bodySmall?.copyWith(
                            color: colorScheme.primary,
                          ),
                          overflow: TextOverflow.ellipsis,
                        ),
                      ),
                      const SizedBox(width: 8),
                      Text(
                        '($nodeCount ${trans.proxy.nodes})',
                        style: theme.textTheme.bodySmall?.copyWith(
                          color: colorScheme.onSurfaceVariant,
                        ),
                      ),
                    ],
                  ),
                ],
              ),
            ),
            const SizedBox(width: 12),
            // 定位按钮（只在有选中节点且展开时显示）
            if (hasSelectedNode &&
                widget.isExpanded &&
                widget.onLocate != null) ...[
              GestureDetector(
                onTap: widget.onLocate,
                child: Container(
                  width: buttonSize,
                  height: buttonSize,
                  decoration: BoxDecoration(
                    color: colorScheme.surfaceContainerHighest.withValues(
                      alpha: backgroundAlpha,
                    ),
                    borderRadius: BorderRadius.circular(16),
                  ),
                  child: Icon(
                    Icons.gps_fixed,
                    size: iconSize,
                    color: colorScheme.primary,
                  ),
                ),
              ),
              const SizedBox(width: 8),
            ],
            // 箭头图标
            Icon(
              widget.isExpanded
                  ? Icons.keyboard_arrow_down_rounded
                  : Icons.keyboard_arrow_right_rounded,
              size: 24,
              color: colorScheme.onSurfaceVariant,
            ),
          ],
        ),
      ),
    );
  }

  // 构建卡片内容（节点列表）
  Widget _buildContent(BuildContext context) {
    final trans = context.translate;
    final nodeNames = widget.viewModel.getSortedProxyNames(widget.group.all);

    // 根据搜索关键词过滤节点
    final filteredNodes = _searchController.text.isEmpty
        ? nodeNames
        : nodeNames
              .where(
                (name) => name.toLowerCase().contains(
                  _searchController.text.toLowerCase(),
                ),
              )
              .toList();

    return Column(
      children: [
        // 搜索框
        _buildSearchBar(context),
        // 节点列表
        if (filteredNodes.isEmpty)
          Padding(
            padding: const EdgeInsets.all(16),
            child: Center(
              child: Text(
                trans.proxy.no_nodes,
                style: Theme.of(context).textTheme.bodyMedium?.copyWith(
                  color: Theme.of(context).colorScheme.onSurfaceVariant,
                ),
              ),
            ),
          )
        else
          _buildNodeList(context, filteredNodes),
      ],
    );
  }

  // 构建搜索框（复用横向模式样式）
  Widget _buildSearchBar(BuildContext context) {
    final trans = context.translate;
    final isDark = Theme.of(context).brightness == Brightness.dark;
    final colorScheme = Theme.of(context).colorScheme;

    return Padding(
      padding: const EdgeInsets.fromLTRB(16, 8, 16, 8),
      child: Container(
        height: 34,
        decoration: BoxDecoration(
          color: isDark
              ? Colors.white.withValues(alpha: 0.05)
              : Colors.black.withValues(alpha: 0.03),
          borderRadius: BorderRadius.circular(8),
          border: Border.all(
            color: _searchController.text.isNotEmpty
                ? colorScheme.primary.withValues(alpha: 0.3)
                : colorScheme.onSurface.withValues(alpha: 0.1),
            width: 1,
          ),
        ),
        child: Row(
          children: [
            const SizedBox(width: 10),
            Icon(
              Icons.search,
              size: 16,
              color: colorScheme.onSurface.withValues(alpha: 0.5),
            ),
            const SizedBox(width: 8),
            Expanded(
              child: TextField(
                controller: _searchController,
                focusNode: _searchFocusNode,
                onChanged: (value) => setState(() {}),
                style: TextStyle(fontSize: 13, color: colorScheme.onSurface),
                decoration: InputDecoration(
                  hintText: trans.proxy.search_hint,
                  hintStyle: TextStyle(
                    fontSize: 13,
                    color: colorScheme.onSurface.withValues(alpha: 0.4),
                  ),
                  border: InputBorder.none,
                  contentPadding: EdgeInsets.zero,
                  isDense: true,
                ),
              ),
            ),
            if (_searchController.text.isNotEmpty)
              GestureDetector(
                onTap: () {
                  _searchController.clear();
                  setState(() {});
                },
                child: Padding(
                  padding: const EdgeInsets.symmetric(horizontal: 8),
                  child: Icon(
                    Icons.clear,
                    size: 16,
                    color: colorScheme.onSurface.withValues(alpha: 0.5),
                  ),
                ),
              ),
          ],
        ),
      ),
    );
  }

  // 构建节点列表
  Widget _buildNodeList(BuildContext context, List<String> nodeNames) {
    return LayoutBuilder(
      builder: (context, constraints) {
        const double spacing = 12.0;
        final double cardWidth =
            (constraints.maxWidth - (widget.columns - 1) * spacing - 32) /
            widget.columns;

        return Padding(
          padding: const EdgeInsets.fromLTRB(16, 0, 16, 16),
          child: Wrap(
            spacing: spacing,
            runSpacing: spacing,
            children: nodeNames.map((nodeName) {
              final node = widget.proxyNodes[nodeName];

              if (node == null) {
                return const SizedBox.shrink();
              }

              final isSelected = nodeName == widget.group.now;

              final nodeKey = '${widget.group.name}_$nodeName';
              GlobalKey? key;
              if (isSelected && widget.nodeKeys != null) {
                widget.nodeKeys!.putIfAbsent(nodeKey, () => GlobalKey());
                key = widget.nodeKeys![nodeKey];
              }

              Widget nodeCard = SizedBox(
                width: cardWidth,
                height: 88.0,
                child: ProxyNodeCard(
                  node: node,
                  isSelected: isSelected,
                  onTap: () => widget.onSelectProxy(nodeName),
                  onTestDelay: () async => widget.onTestDelay(nodeName),
                  isClashRunning: widget.isCoreRunning,
                  isWaitingTest: widget.testingNodes.contains(nodeName),
                ),
              );

              if (isSelected && key != null) {
                nodeCard = Container(key: key, child: nodeCard);
              }

              return nodeCard;
            }).toList(),
          ),
        );
      },
    );
  }
}

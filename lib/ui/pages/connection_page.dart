import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import 'package:stelliberty/clash/providers/connection_provider.dart';
import 'package:stelliberty/clash/data/connection_model.dart';
import 'package:stelliberty/utils/logger.dart';
import 'package:stelliberty/i18n/i18n.dart';
import 'package:stelliberty/ui/widgets/connection/connection_card.dart';
import 'package:stelliberty/ui/widgets/connection/connection_detail_dialog.dart';
import 'package:stelliberty/ui/widgets/modern_tooltip.dart';

// 连接页面 - 显示当前活跃的连接
// 使用 Material Design 3 风格，与代理和订阅页面保持一致
class ConnectionPageContent extends StatefulWidget {
  const ConnectionPageContent({super.key});

  @override
  State<ConnectionPageContent> createState() => _ConnectionPageContentState();
}

class _ConnectionPageContentState extends State<ConnectionPageContent> {
  @override
  void initState() {
    super.initState();
    Logger.info('初始化 ConnectionPage');
  }

  @override
  Widget build(BuildContext context) {
    return Consumer<ConnectionProvider>(
      builder: (context, connectionProvider, child) {
        final connections = connectionProvider.connections;
        final isLoading = connectionProvider.isLoading;

        return Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            // 过滤器和控制栏（扁平化设计）
            _buildFilterBar(context, connectionProvider),

            // 统一的分隔线（与代理和订阅页面相同高度）
            const Divider(height: 1, thickness: 1),

            // 连接列表
            Expanded(
              child: _buildConnectionList(
                context,
                connectionProvider,
                connections,
                isLoading,
              ),
            ),
          ],
        );
      },
    );
  }

  // 构建过滤器和控制栏（扁平化 MD3 风格）
  Widget _buildFilterBar(BuildContext context, ConnectionProvider provider) {
    final colorScheme = Theme.of(context).colorScheme;
    final totalCount = provider.connections.length;

    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 16.0, vertical: 12.0),
      child: Row(
        children: [
          // 连接数统计（使用 Badge 风格）
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
                  Icons.link_rounded,
                  size: 16,
                  color: colorScheme.onPrimaryContainer,
                ),
                const SizedBox(width: 6),
                Text(
                  context.translate.connection.totalConnections.replaceAll(
                    '{count}',
                    totalCount.toString(),
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

          const SizedBox(width: 12),

          // 过滤级别按钮组（使用 SegmentedButton 风格）
          Container(
            decoration: BoxDecoration(
              color: colorScheme.surfaceContainerHighest,
              borderRadius: BorderRadius.circular(20),
            ),
            padding: const EdgeInsets.all(3),
            child: Row(
              mainAxisSize: MainAxisSize.min,
              children: [
                _buildFilterChip(
                  context,
                  label: context.translate.connection.allConnections,
                  isSelected: provider.filterLevel == ConnectionFilterLevel.all,
                  onTap: () =>
                      provider.setFilterLevel(ConnectionFilterLevel.all),
                ),
                const SizedBox(width: 4),
                _buildFilterChip(
                  context,
                  label: context.translate.connection.directConnections,
                  isSelected:
                      provider.filterLevel == ConnectionFilterLevel.direct,
                  onTap: () =>
                      provider.setFilterLevel(ConnectionFilterLevel.direct),
                ),
                const SizedBox(width: 4),
                _buildFilterChip(
                  context,
                  label: context.translate.connection.proxiedConnections,
                  isSelected:
                      provider.filterLevel == ConnectionFilterLevel.proxy,
                  onTap: () =>
                      provider.setFilterLevel(ConnectionFilterLevel.proxy),
                ),
              ],
            ),
          ),

          const SizedBox(width: 12),

          // 搜索框（扁平设计）
          Expanded(
            child: SizedBox(
              height: 38,
              child: TextField(
                onChanged: (value) => provider.setSearchKeyword(value),
                decoration: InputDecoration(
                  hintText: context.translate.connection.searchPlaceholder,
                  hintStyle: TextStyle(
                    fontSize: 13,
                    color: colorScheme.onSurfaceVariant,
                  ),
                  prefixIcon: Icon(
                    Icons.search_rounded,
                    size: 20,
                    color: colorScheme.onSurfaceVariant,
                  ),
                  contentPadding: const EdgeInsets.symmetric(
                    horizontal: 12,
                    vertical: 0,
                  ),
                  border: OutlineInputBorder(
                    borderRadius: BorderRadius.circular(20),
                    borderSide: BorderSide.none,
                  ),
                  filled: true,
                  fillColor: colorScheme.surfaceContainerHighest,
                ),
                style: const TextStyle(fontSize: 13),
              ),
            ),
          ),

          const SizedBox(width: 12),

          // 控制按钮组（扁平设计，使用 IconButton）
          Row(
            mainAxisSize: MainAxisSize.min,
            children: [
              // 暂停/恢复按钮
              ModernIconTooltip(
                message: provider.isPaused
                    ? context.translate.connection.resumeBtn
                    : context.translate.connection.pauseBtn,
                icon: provider.isPaused
                    ? Icons.play_arrow_rounded
                    : Icons.pause_rounded,
                onPressed: () => provider.togglePause(),
                iconSize: 20,
              ),
              const SizedBox(width: 6),
              // 手动刷新按钮
              ModernIconTooltip(
                message: context.translate.connection.refreshBtn,
                icon: Icons.refresh_rounded,
                onPressed: () => provider.refreshConnections(),
                iconSize: 20,
              ),
              const SizedBox(width: 6),
              // 关闭所有连接按钮
              ModernIconTooltip(
                message: context.translate.connection.closeAllConnections,
                icon: Icons.clear_all_rounded,
                onPressed: totalCount > 0
                    ? () => _closeAllConnections(context, provider)
                    : null,
                iconSize: 20,
              ),
            ],
          ),
        ],
      ),
    );
  }

  // 构建过滤筛选片段（扁平设计）
  Widget _buildFilterChip(
    BuildContext context, {
    required String label,
    required bool isSelected,
    required VoidCallback onTap,
  }) {
    final colorScheme = Theme.of(context).colorScheme;

    return GestureDetector(
      onTap: onTap,
      child: AnimatedContainer(
        duration: const Duration(milliseconds: 200),
        curve: Curves.easeInOut,
        padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 6),
        decoration: BoxDecoration(
          color: isSelected ? colorScheme.primary : Colors.transparent,
          borderRadius: BorderRadius.circular(16),
        ),
        child: Text(
          label,
          style: TextStyle(
            color: isSelected
                ? colorScheme.onPrimary
                : colorScheme.onSurfaceVariant,
            fontWeight: isSelected ? FontWeight.w600 : FontWeight.w500,
            fontSize: 13,
          ),
        ),
      ),
    );
  }

  // 构建连接列表
  Widget _buildConnectionList(
    BuildContext context,
    ConnectionProvider provider,
    List<ConnectionInfo> connections,
    bool isLoading,
  ) {
    if (isLoading && connections.isEmpty) {
      return const Center(child: CircularProgressIndicator());
    }

    if (connections.isEmpty) {
      return Center(
        child: Column(
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            Icon(
              Icons.link_off_rounded,
              size: 64,
              color: Theme.of(
                context,
              ).colorScheme.outline.withValues(alpha: 0.4),
            ),
            const SizedBox(height: 16),
            Text(
              context.translate.connection.noActiveConnections,
              style: Theme.of(context).textTheme.bodyLarge?.copyWith(
                color: Theme.of(
                  context,
                ).colorScheme.onSurface.withValues(alpha: 0.5),
              ),
            ),
          ],
        ),
      );
    }

    // 固定每行两个卡片
    return GridView.builder(
      padding: const EdgeInsets.all(16.0),
      gridDelegate: const SliverGridDelegateWithFixedCrossAxisCount(
        crossAxisCount: 2, // 固定每行两个
        crossAxisSpacing: 12.0,
        mainAxisSpacing: 12.0,
        mainAxisExtent: 120.0, // 更紧凑的卡片高度
      ),
      itemCount: connections.length,
      itemBuilder: (context, index) {
        final connection = connections[index];
        return ConnectionCard(
          connection: connection,
          onTap: () => _showConnectionDetails(context, connection),
          onClose: () => _closeConnection(context, provider, connection),
        );
      },
    );
  }

  // 关闭单个连接
  void _closeConnection(
    BuildContext context,
    ConnectionProvider provider,
    ConnectionInfo connection,
  ) {
    showDialog(
      context: context,
      builder: (context) => AlertDialog(
        title: Text(context.translate.common.confirm),
        content: Text(context.translate.connection.closeConnectionConfirm),
        actions: [
          TextButton(
            onPressed: () => Navigator.of(context).pop(),
            child: Text(context.translate.common.cancel),
          ),
          FilledButton(
            onPressed: () async {
              Navigator.of(context).pop();
              await provider.closeConnection(connection.id);
            },
            child: Text(context.translate.common.ok),
          ),
        ],
      ),
    );
  }

  // 关闭所有连接
  void _closeAllConnections(BuildContext context, ConnectionProvider provider) {
    showDialog(
      context: context,
      builder: (context) => AlertDialog(
        title: Text(context.translate.common.confirm),
        content: Text(context.translate.connection.closeAllConfirm),
        actions: [
          TextButton(
            onPressed: () => Navigator.of(context).pop(),
            child: Text(context.translate.common.cancel),
          ),
          FilledButton(
            onPressed: () async {
              Navigator.of(context).pop();
              await provider.closeAllConnections();
            },
            child: Text(context.translate.common.ok),
          ),
        ],
      ),
    );
  }

  // 显示连接详情
  void _showConnectionDetails(BuildContext context, ConnectionInfo connection) {
    ConnectionDetailDialog.show(context, connection);
  }
}

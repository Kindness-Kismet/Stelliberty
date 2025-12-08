import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import 'package:stelliberty/clash/manager/manager.dart';
import 'package:stelliberty/clash/data/log_message_model.dart';
import 'package:stelliberty/clash/providers/log_provider.dart';
import 'package:stelliberty/i18n/i18n.dart';
import 'package:stelliberty/ui/widgets/core_log/core_log_card.dart';
import 'package:stelliberty/utils/logger.dart';
import 'package:stelliberty/ui/widgets/modern_tooltip.dart';
import 'package:stelliberty/ui/constants/spacing.dart';

// 日志页布局常量
class _LogListSpacing {
  _LogListSpacing._();

  static const listLeftEdge = 16.0;
  static const listTopEdge = 16.0;
  static const listRightEdge =
      16.0 - SpacingConstants.scrollbarRightCompensation;
  static const listBottomEdge = 10.0;

  static const cardHeight = 72.0; // 日志卡片高度
  static const cardSpacing = 16.0; // 日志卡片间距

  static const listPadding = EdgeInsets.fromLTRB(
    listLeftEdge,
    listTopEdge,
    listRightEdge,
    listBottomEdge,
  );
}

// 日志页面 - 显示 Clash 核心的实时日志
// 使用 Material Design 3 风格，与连接页面保持一致
// 使用 Provider 管理状态，避免切换页面时丢失日志
class LogPage extends StatefulWidget {
  const LogPage({super.key});

  @override
  State<LogPage> createState() => _LogPageState();
}

class _LogPageState extends State<LogPage> {
  final ScrollController _scrollController = ScrollController();
  bool _isFirstLoad = true; // 标记是否是首次加载

  @override
  void initState() {
    super.initState();
    Logger.info('初始化 LogPage');

    // 延迟加载日志列表（给顶栏先渲染的机会）
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (mounted) {
        setState(() {
          _isFirstLoad = false;
        });
      }
    });
  }

  @override
  void dispose() {
    _scrollController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final isRunning = ClashManager.instance.isCoreRunning;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        // 过滤器和控制栏（立即显示，不依赖 Provider 数据）
        _isFirstLoad
            ? _buildFilterBarSkeleton(context)
            : _buildFilterBar(context),

        // 统一的分隔线
        const Divider(height: 1, thickness: 1),

        // 日志列表（延迟渲染）
        Expanded(
          child: Padding(
            padding: SpacingConstants.scrollbarPadding,
            child: _isFirstLoad
                ? _buildLoadingState(context)
                : (isRunning
                      ? _buildLogList(context)
                      : _buildEmptyState(context)),
          ),
        ),
      ],
    );
  }

  // 构建过滤器栏骨架屏（立即显示，无需等待数据）
  Widget _buildFilterBarSkeleton(BuildContext context) {
    final colorScheme = Theme.of(context).colorScheme;

    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 16.0, vertical: 12.0),
      child: Row(
        children: [
          // 过滤器占位符
          Container(
            width: 400,
            height: 32,
            decoration: BoxDecoration(
              color: colorScheme.surfaceContainerHighest,
              borderRadius: BorderRadius.circular(20),
            ),
          ),

          const SizedBox(width: 12),

          // 搜索框占位符
          Expanded(
            child: Container(
              height: 38,
              decoration: BoxDecoration(
                color: colorScheme.surfaceContainerHighest,
                borderRadius: BorderRadius.circular(20),
              ),
            ),
          ),

          const SizedBox(width: 12),

          // 按钮组占位符
          Row(
            mainAxisSize: MainAxisSize.min,
            children: [
              Container(
                width: 40,
                height: 40,
                decoration: BoxDecoration(
                  color: colorScheme.surfaceContainerHighest,
                  borderRadius: BorderRadius.circular(20),
                ),
              ),
              const SizedBox(width: 6),
              Container(
                width: 40,
                height: 40,
                decoration: BoxDecoration(
                  color: colorScheme.surfaceContainerHighest,
                  borderRadius: BorderRadius.circular(20),
                ),
              ),
            ],
          ),
        ],
      ),
    );
  }

  // 构建过滤器和控制栏（扁平化 MD3 风格）
  Widget _buildFilterBar(BuildContext context) {
    final colorScheme = Theme.of(context).colorScheme;

    return Consumer<LogProvider>(
      builder: (context, provider, child) {
        return Padding(
          padding: const EdgeInsets.symmetric(horizontal: 16.0, vertical: 12.0),
          child: Row(
            children: [
              // 日志级别过滤按钮组（使用 SegmentedButton 风格）
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
                      label: context.translate.logs.allLevels,
                      isSelected: provider.filterLevel == null,
                      onTap: () => provider.setFilterLevel(null),
                    ),
                    const SizedBox(width: 4),
                    _buildFilterChip(
                      context,
                      label: ClashLogLevel.debug.getDisplayName(context),
                      isSelected: provider.filterLevel == ClashLogLevel.debug,
                      onTap: () => provider.setFilterLevel(ClashLogLevel.debug),
                    ),
                    const SizedBox(width: 4),
                    _buildFilterChip(
                      context,
                      label: ClashLogLevel.info.getDisplayName(context),
                      isSelected: provider.filterLevel == ClashLogLevel.info,
                      onTap: () => provider.setFilterLevel(ClashLogLevel.info),
                    ),
                    const SizedBox(width: 4),
                    _buildFilterChip(
                      context,
                      label: ClashLogLevel.warning.getDisplayName(context),
                      isSelected: provider.filterLevel == ClashLogLevel.warning,
                      onTap: () =>
                          provider.setFilterLevel(ClashLogLevel.warning),
                    ),
                    const SizedBox(width: 4),
                    _buildFilterChip(
                      context,
                      label: ClashLogLevel.error.getDisplayName(context),
                      isSelected: provider.filterLevel == ClashLogLevel.error,
                      onTap: () => provider.setFilterLevel(ClashLogLevel.error),
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
                      hintText: context.translate.logs.searchPlaceholder,
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
                    onPressed: provider.togglePause,
                    iconSize: 20,
                  ),
                  const SizedBox(width: 6),
                  // 清空日志按钮
                  ModernIconTooltip(
                    message: context.translate.logs.clearLogs,
                    icon: Icons.delete_outline_rounded,
                    onPressed: provider.logs.isEmpty
                        ? null
                        : provider.clearLogs,
                    iconSize: 20,
                  ),
                ],
              ),
            ],
          ),
        );
      },
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

  // 构建日志列表
  Widget _buildLogList(BuildContext context) {
    return Consumer<LogProvider>(
      builder: (context, provider, child) {
        final filteredLogs = provider.filteredLogs;

        if (filteredLogs.isEmpty) {
          return Center(
            child: Column(
              mainAxisAlignment: MainAxisAlignment.center,
              children: [
                Icon(
                  Icons.article_outlined,
                  size: 64,
                  color: Theme.of(
                    context,
                  ).colorScheme.outline.withValues(alpha: 0.4),
                ),
                const SizedBox(height: 16),
                Text(
                  provider.logs.isEmpty
                      ? context.translate.logs.emptyLogs
                      : context.translate.logs.emptyFiltered,
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

        return Scrollbar(
          controller: _scrollController,
          child: GridView.builder(
            controller: _scrollController,
            padding: _LogListSpacing.listPadding,
            gridDelegate: SliverGridDelegateWithFixedCrossAxisCount(
              crossAxisCount: 1, // 单列显示
              mainAxisSpacing: _LogListSpacing.cardSpacing,
              mainAxisExtent: _LogListSpacing.cardHeight,
            ),
            itemCount: filteredLogs.length,
            addAutomaticKeepAlives: false, // 减少内存占用
            addRepaintBoundaries: true, // 优化重绘性能
            itemBuilder: (context, index) {
              // 倒序显示日志（最新的在顶部）
              final reversedIndex = filteredLogs.length - 1 - index;
              return LogCard(log: filteredLogs[reversedIndex]);
            },
          ),
        );
      },
    );
  }

  // 构建加载状态（首次进入页面时）
  Widget _buildLoadingState(BuildContext context) {
    return Center(
      child: Column(
        mainAxisAlignment: MainAxisAlignment.center,
        children: [
          SizedBox(
            width: 32,
            height: 32,
            child: CircularProgressIndicator(
              strokeWidth: 3,
              color: Theme.of(context).colorScheme.primary,
            ),
          ),
          const SizedBox(height: 16),
          Text(
            context.translate.logs.loadingLogs,
            style: Theme.of(context).textTheme.bodyMedium?.copyWith(
              color: Theme.of(
                context,
              ).colorScheme.onSurface.withValues(alpha: 0.6),
            ),
          ),
        ],
      ),
    );
  }

  // 构建空状态（Clash 未运行）
  Widget _buildEmptyState(BuildContext context) {
    return Center(
      child: Column(
        mainAxisAlignment: MainAxisAlignment.center,
        children: [
          Icon(
            Icons.article_outlined,
            size: 64,
            color: Theme.of(context).colorScheme.outline.withValues(alpha: 0.4),
          ),
          const SizedBox(height: 16),
          Text(
            context.translate.logs.clashNotRunning,
            style: Theme.of(context).textTheme.titleMedium?.copyWith(
              color: Theme.of(
                context,
              ).colorScheme.onSurface.withValues(alpha: 0.6),
            ),
          ),
          const SizedBox(height: 8),
          Text(
            context.translate.logs.startClashToViewLogs,
            style: Theme.of(context).textTheme.bodySmall?.copyWith(
              color: Theme.of(
                context,
              ).colorScheme.onSurface.withValues(alpha: 0.4),
            ),
          ),
        ],
      ),
    );
  }
}

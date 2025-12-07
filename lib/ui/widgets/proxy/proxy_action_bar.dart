import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import 'package:stelliberty/clash/providers/clash_provider.dart';
import 'package:stelliberty/i18n/i18n.dart';
import 'package:stelliberty/ui/widgets/modern_tooltip.dart';
import 'package:stelliberty/ui/notifiers/proxy_notifier.dart';

// 代理页面操作按钮栏
class ProxyActionBar extends StatefulWidget {
  final String selectedGroupName;
  final VoidCallback onLocate;
  final int sortMode;
  final ValueChanged<int> onSortModeChanged;
  final ProxyNotifier viewModel;

  const ProxyActionBar({
    super.key,
    required this.selectedGroupName,
    required this.onLocate,
    required this.sortMode,
    required this.onSortModeChanged,
    required this.viewModel,
  });

  @override
  State<ProxyActionBar> createState() => _ProxyActionBarState();
}

class _ProxyActionBarState extends State<ProxyActionBar> {
  final TextEditingController _searchController = TextEditingController();
  final FocusNode _searchFocusNode = FocusNode();

  @override
  void initState() {
    super.initState();
    _searchController.text = widget.viewModel.searchQuery;
    // 监听输入框变化，以便实时显示/隐藏清除按钮
    _searchController.addListener(() {
      setState(() {});
    });
  }

  @override
  void dispose() {
    _searchController.dispose();
    _searchFocusNode.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return Selector<ClashProvider, _ActionBarState>(
      selector: (_, provider) => _ActionBarState(
        isLoadingProxies: provider.isLoadingProxies,
        isCoreRunning: provider.isCoreRunning,
        isBatchTestingDelay: provider.isBatchTestingDelay,
      ),
      builder: (context, state, child) {
        final clashProvider = context.read<ClashProvider>();

        return Padding(
          padding: const EdgeInsets.only(
            left: 16.0,
            right: 16.0,
            top: 8.0,
            bottom: 4.0,
          ),
          child: ListenableBuilder(
            listenable: widget.viewModel,
            builder: (context, _) {
              return Row(
                children: [
                  // 测速按钮
                  _ActionButton(
                    icon: Icons.network_check,
                    tooltip: context.translate.proxy.testAllDelays,
                    onPressed: state.canTestDelays
                        ? () => clashProvider.testGroupDelays(
                            widget.selectedGroupName,
                          )
                        : null,
                    isLoading: state.isBatchTestingDelay,
                  ),
                  const SizedBox(width: 8),
                  // 定位按钮
                  _ActionButton(
                    icon: Icons.gps_fixed,
                    tooltip: context.translate.proxy.locate,
                    onPressed: state.canLocate ? widget.onLocate : null,
                  ),
                  const SizedBox(width: 8),
                  // 排序按钮
                  _ActionButton(
                    icon: _getSortIcon(widget.sortMode),
                    tooltip: _getSortTooltip(context, widget.sortMode),
                    onPressed: _handleSortModeChange,
                  ),
                  const SizedBox(width: 8),
                  // 搜索按钮或搜索框
                  Expanded(
                    child: AnimatedCrossFade(
                      duration: const Duration(milliseconds: 300),
                      firstCurve: Curves.easeInOut,
                      secondCurve: Curves.easeInOut,
                      sizeCurve: Curves.easeInOut,
                      crossFadeState: widget.viewModel.isSearching
                          ? CrossFadeState.showSecond
                          : CrossFadeState.showFirst,
                      firstChild: Align(
                        alignment: Alignment.centerLeft,
                        child: _ActionButton(
                          icon: Icons.search,
                          tooltip: context.translate.proxy.search,
                          onPressed: () {
                            widget.viewModel.toggleSearch();
                            // 延迟聚焦，确保搜索框已渲染
                            Future.delayed(
                              const Duration(milliseconds: 350),
                              () {
                                _searchFocusNode.requestFocus();
                              },
                            );
                          },
                        ),
                      ),
                      secondChild: _buildSearchField(context),
                    ),
                  ),
                ],
              );
            },
          ),
        );
      },
    );
  }

  Widget _buildSearchField(BuildContext context) {
    final isDark = Theme.of(context).brightness == Brightness.dark;
    final colorScheme = Theme.of(context).colorScheme;

    return Container(
      height: 34,
      decoration: BoxDecoration(
        color: isDark
            ? Colors.white.withValues(alpha: 0.05)
            : Colors.black.withValues(alpha: 0.03),
        borderRadius: BorderRadius.circular(8),
        border: Border.all(
          color: colorScheme.primary.withValues(alpha: 0.3),
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
              onChanged: widget.viewModel.updateSearchQuery,
              style: TextStyle(fontSize: 13, color: colorScheme.onSurface),
              decoration: InputDecoration(
                hintText: context.translate.proxy.searchHint,
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
          // 清除按钮
          if (_searchController.text.isNotEmpty)
            GestureDetector(
              onTap: () {
                _searchController.clear();
                widget.viewModel.updateSearchQuery('');
              },
              child: Padding(
                padding: const EdgeInsets.symmetric(horizontal: 6),
                child: Icon(
                  Icons.clear,
                  size: 16,
                  color: colorScheme.onSurface.withValues(alpha: 0.5),
                ),
              ),
            ),
          // 关闭搜索按钮
          GestureDetector(
            onTap: () {
              _searchController.clear();
              widget.viewModel.closeSearch();
            },
            child: Container(
              padding: const EdgeInsets.symmetric(horizontal: 8),
              child: Icon(
                Icons.close,
                size: 16,
                color: colorScheme.onSurface.withValues(alpha: 0.7),
              ),
            ),
          ),
        ],
      ),
    );
  }

  void _handleSortModeChange() {
    const totalSortModes = 3;
    final nextMode = (widget.sortMode + 1) % totalSortModes;
    widget.onSortModeChanged(nextMode);
  }

  IconData _getSortIcon(int mode) {
    switch (mode) {
      case 0:
        return Icons.sort;
      case 1:
        return Icons.sort_by_alpha;
      case 2:
        return Icons.speed;
      default:
        return Icons.sort;
    }
  }

  String _getSortTooltip(BuildContext context, int mode) {
    switch (mode) {
      case 0:
        return context.translate.proxy.defaultSort;
      case 1:
        return context.translate.proxy.nameSort;
      case 2:
        return context.translate.proxy.delaySort;
      default:
        return context.translate.proxy.defaultSort;
    }
  }
}

// 美化的操作按钮组件
class _ActionButton extends StatefulWidget {
  final IconData icon;
  final String tooltip;
  final VoidCallback? onPressed;
  final bool isLoading;

  const _ActionButton({
    required this.icon,
    required this.tooltip,
    this.onPressed,
    this.isLoading = false,
  });

  @override
  State<_ActionButton> createState() => _ActionButtonState();
}

class _ActionButtonState extends State<_ActionButton> {
  bool _isHovering = false;

  @override
  Widget build(BuildContext context) {
    final isDark = Theme.of(context).brightness == Brightness.dark;
    final isDisabled = widget.onPressed == null;
    final colorScheme = Theme.of(context).colorScheme;

    // 背景颜色
    Color backgroundColor;
    if (isDisabled) {
      backgroundColor = Colors.transparent;
    } else if (_isHovering) {
      backgroundColor = isDark
          ? colorScheme.primary.withValues(alpha: 0.15)
          : colorScheme.primary.withValues(alpha: 0.1);
    } else {
      backgroundColor = isDark
          ? Colors.white.withValues(alpha: 0.05)
          : Colors.black.withValues(alpha: 0.03);
    }

    // 图标颜色
    Color iconColor;
    if (widget.isLoading) {
      iconColor = colorScheme.primary;
    } else if (isDisabled) {
      iconColor = colorScheme.onSurface.withValues(alpha: 0.3);
    } else if (_isHovering) {
      iconColor = colorScheme.primary;
    } else {
      iconColor = colorScheme.onSurface.withValues(alpha: 0.7);
    }

    return ModernTooltip(
      message: widget.tooltip,
      child: MouseRegion(
        onEnter: (_) => setState(() => _isHovering = true),
        onExit: (_) => setState(() => _isHovering = false),
        cursor: isDisabled
            ? SystemMouseCursors.basic
            : SystemMouseCursors.click,
        child: GestureDetector(
          onTap: widget.isLoading ? null : widget.onPressed,
          child: AnimatedContainer(
            duration: const Duration(milliseconds: 150),
            padding: const EdgeInsets.all(8),
            decoration: BoxDecoration(
              color: backgroundColor,
              borderRadius: BorderRadius.circular(8),
              border: Border.all(
                color: _isHovering && !isDisabled
                    ? colorScheme.primary.withValues(alpha: 0.3)
                    : Colors.transparent,
                width: 1,
              ),
            ),
            child: widget.isLoading
                ? SizedBox(
                    width: 18,
                    height: 18,
                    child: CircularProgressIndicator(
                      strokeWidth: 2,
                      valueColor: AlwaysStoppedAnimation<Color>(iconColor),
                    ),
                  )
                : Icon(widget.icon, size: 18, color: iconColor),
          ),
        ),
      ),
    );
  }
}

// 操作栏状态数据类
class _ActionBarState {
  final bool isLoadingProxies;
  final bool isCoreRunning;
  final bool isBatchTestingDelay;

  _ActionBarState({
    required this.isLoadingProxies,
    required this.isCoreRunning,
    required this.isBatchTestingDelay,
  });

  bool get canTestDelays =>
      !isLoadingProxies && isCoreRunning && !isBatchTestingDelay;
  bool get canLocate => !isLoadingProxies;

  @override
  bool operator ==(Object other) =>
      identical(this, other) ||
      other is _ActionBarState &&
          runtimeType == other.runtimeType &&
          isLoadingProxies == other.isLoadingProxies &&
          isCoreRunning == other.isCoreRunning &&
          isBatchTestingDelay == other.isBatchTestingDelay;

  @override
  int get hashCode =>
      isLoadingProxies.hashCode ^
      isCoreRunning.hashCode ^
      isBatchTestingDelay.hashCode;
}

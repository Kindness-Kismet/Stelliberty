import 'package:flutter/material.dart';
import 'package:flutter/gestures.dart';
import 'package:stelliberty/clash/providers/clash_provider.dart';
import 'package:stelliberty/i18n/i18n.dart';
import 'package:stelliberty/ui/widgets/modern_tooltip.dart';

// 代理组选择器组件
class ProxyGroupSelector extends StatelessWidget {
  final ClashProvider clashProvider;
  final int currentGroupIndex;
  final ScrollController scrollController;
  final Function(int) onGroupChanged;

  const ProxyGroupSelector({
    super.key,
    required this.clashProvider,
    required this.currentGroupIndex,
    required this.scrollController,
    required this.onGroupChanged,
  });

  void _scrollByDistance(double distance) {
    if (!scrollController.hasClients) return;

    final offset = scrollController.offset + distance;
    scrollController.animateTo(
      offset.clamp(0.0, scrollController.position.maxScrollExtent),
      duration: const Duration(milliseconds: 200),
      curve: Curves.easeOut,
    );
  }

  bool get _canScrollLeft {
    return scrollController.hasClients &&
        scrollController.position.hasContentDimensions &&
        scrollController.offset > 0;
  }

  bool get _canScrollRight {
    return scrollController.hasClients &&
        scrollController.position.hasContentDimensions &&
        scrollController.offset < scrollController.position.maxScrollExtent;
  }

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 16.0),
      child: Row(
        children: [
          // 代理组标签列表（可滑动）
          Expanded(
            child: Listener(
              onPointerSignal: (pointerSignal) {
                // 支持鼠标滚轮滚动（提高滚动速度）
                if (pointerSignal is PointerScrollEvent &&
                    scrollController.hasClients) {
                  final offset =
                      scrollController.offset +
                      pointerSignal.scrollDelta.dy * 2.0;
                  scrollController.animateTo(
                    offset.clamp(
                      0.0,
                      scrollController.position.maxScrollExtent,
                    ),
                    duration: const Duration(milliseconds: 100),
                    curve: Curves.easeOut,
                  );
                }
              },
              child: SingleChildScrollView(
                controller: scrollController,
                scrollDirection: Axis.horizontal,
                physics: const BouncingScrollPhysics(),
                child: Row(
                  children: List.generate(clashProvider.proxyGroups.length, (
                    index,
                  ) {
                    final group = clashProvider.proxyGroups[index];
                    final isSelected = index == currentGroupIndex;

                    return Padding(
                      padding: const EdgeInsets.only(right: 8.0),
                      child: GestureDetector(
                        onTap: () => onGroupChanged(index),
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

          // 右侧控制按钮组
          Row(
            mainAxisSize: MainAxisSize.min,
            children: [
              // 左移按钮
              ModernTooltip(
                message: context.translate.proxy.scrollLeft,
                child: IconButton(
                  onPressed: _canScrollLeft
                      ? () => _scrollByDistance(-300)
                      : null,
                  icon: const Icon(Icons.chevron_left),
                  iconSize: 20,
                  visualDensity: VisualDensity.compact,
                ),
              ),
              // 右移按钮
              ModernTooltip(
                message: context.translate.proxy.scrollRight,
                child: IconButton(
                  onPressed: _canScrollRight
                      ? () => _scrollByDistance(300)
                      : null,
                  icon: const Icon(Icons.chevron_right),
                  iconSize: 20,
                  visualDensity: VisualDensity.compact,
                ),
              ),
            ],
          ),
        ],
      ),
    );
  }
}

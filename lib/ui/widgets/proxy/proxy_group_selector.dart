import 'package:flutter/material.dart';
import 'package:flutter/gestures.dart';
import 'package:stelliberty/clash/providers/clash_provider.dart';
import 'package:stelliberty/i18n/i18n.dart';
import 'package:stelliberty/ui/widgets/modern_tooltip.dart';

// 代理组选择器组件
class ProxyGroupSelector extends StatefulWidget {
  final ClashProvider clashProvider;
  final int currentGroupIndex;
  final ScrollController scrollController;
  final Function(int) onGroupChanged;
  final double mouseScrollSpeedMultiplier;
  final double tabScrollDistance;

  const ProxyGroupSelector({
    super.key,
    required this.clashProvider,
    required this.currentGroupIndex,
    required this.scrollController,
    required this.onGroupChanged,
    this.mouseScrollSpeedMultiplier = 2.0,
    this.tabScrollDistance = 300.0,
  });

  @override
  State<ProxyGroupSelector> createState() => _ProxyGroupSelectorState();
}

class _ProxyGroupSelectorState extends State<ProxyGroupSelector> {
  int? _hoveredIndex;

  void _scrollByDistance(double distance) {
    if (!widget.scrollController.hasClients) return;

    final offset = widget.scrollController.offset + distance;
    widget.scrollController.animateTo(
      offset.clamp(0.0, widget.scrollController.position.maxScrollExtent),
      duration: const Duration(milliseconds: 200),
      curve: Curves.easeOut,
    );
  }

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.only(top: 4.0, bottom: 12.0),
      child: Row(
        children: [
          Expanded(
            child: Listener(
              onPointerSignal: (pointerSignal) {
                if (pointerSignal is PointerScrollEvent &&
                    widget.scrollController.hasClients) {
                  final offset =
                      widget.scrollController.offset +
                      pointerSignal.scrollDelta.dy *
                          widget.mouseScrollSpeedMultiplier;
                  widget.scrollController.animateTo(
                    offset.clamp(
                      0.0,
                      widget.scrollController.position.maxScrollExtent,
                    ),
                    duration: const Duration(milliseconds: 100),
                    curve: Curves.easeOut,
                  );
                }
              },
              child: SingleChildScrollView(
                controller: widget.scrollController,
                scrollDirection: Axis.horizontal,
                physics: const BouncingScrollPhysics(),
                child: Row(
                  children: List.generate(
                    widget.clashProvider.proxyGroups.length,
                    (index) {
                      final group = widget.clashProvider.proxyGroups[index];
                      final isSelected = index == widget.currentGroupIndex;
                      final isHovered = _hoveredIndex == index;

                      return Padding(
                        padding: const EdgeInsets.only(right: 24.0),
                        child: MouseRegion(
                          onEnter: (_) => setState(() => _hoveredIndex = index),
                          onExit: (_) => setState(() => _hoveredIndex = null),
                          cursor: SystemMouseCursors.click,
                          child: GestureDetector(
                            onTap: () => widget.onGroupChanged(index),
                            child: Container(
                              padding: const EdgeInsets.symmetric(
                                horizontal: 8,
                                vertical: 2,
                              ),
                              decoration: BoxDecoration(
                                color: isHovered && !isSelected
                                    ? Theme.of(context)
                                          .colorScheme
                                          .surfaceContainerHighest
                                          .withValues(
                                            alpha:
                                                Theme.of(context).brightness ==
                                                    Brightness.light
                                                ? 0.5
                                                : 0.3,
                                          )
                                    : Colors.transparent,
                                borderRadius: BorderRadius.circular(8),
                              ),
                              child: Column(
                                mainAxisSize: MainAxisSize.min,
                                children: [
                                  // 代理组名称
                                  Padding(
                                    padding: const EdgeInsets.symmetric(
                                      horizontal: 4,
                                      vertical: 4,
                                    ),
                                    child: Text(
                                      group.name,
                                      style: TextStyle(
                                        color: isSelected
                                            ? Theme.of(
                                                context,
                                              ).colorScheme.primary
                                            : Theme.of(context)
                                                  .colorScheme
                                                  .onSurface
                                                  .withValues(alpha: 0.7),
                                        fontWeight: isSelected
                                            ? FontWeight.w600
                                            : FontWeight.normal,
                                        fontSize: 14,
                                      ),
                                      overflow: TextOverflow.ellipsis,
                                      maxLines: 1,
                                    ),
                                  ),
                                  // 底部下划线
                                  AnimatedContainer(
                                    duration: const Duration(milliseconds: 200),
                                    height: 2,
                                    width: isSelected ? 40 : 0,
                                    decoration: BoxDecoration(
                                      color: Theme.of(
                                        context,
                                      ).colorScheme.primary,
                                      borderRadius: BorderRadius.circular(1),
                                    ),
                                  ),
                                ],
                              ),
                            ),
                          ),
                        ),
                      );
                    },
                  ),
                ),
              ),
            ),
          ),
          const SizedBox(width: 12),
          ListenableBuilder(
            listenable: widget.scrollController,
            builder: (context, _) {
              final canScrollLeft =
                  widget.scrollController.hasClients &&
                  widget.scrollController.position.hasContentDimensions &&
                  widget.scrollController.offset > 0;

              final canScrollRight =
                  widget.scrollController.hasClients &&
                  widget.scrollController.position.hasContentDimensions &&
                  widget.scrollController.offset <
                      widget.scrollController.position.maxScrollExtent;

              return Row(
                mainAxisSize: MainAxisSize.min,
                children: [
                  ModernTooltip(
                    message: context.translate.proxy.scrollLeft,
                    child: IconButton(
                      onPressed: canScrollLeft
                          ? () => _scrollByDistance(-widget.tabScrollDistance)
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
                          ? () => _scrollByDistance(widget.tabScrollDistance)
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
}

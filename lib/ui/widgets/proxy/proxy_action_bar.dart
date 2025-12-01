import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import 'package:stelliberty/clash/providers/clash_provider.dart';
import 'package:stelliberty/i18n/i18n.dart';
import 'package:stelliberty/ui/widgets/modern_tooltip.dart';

// 代理页面操作按钮栏
class ProxyActionBar extends StatelessWidget {
  final String selectedGroupName;
  final VoidCallback onLocate;
  final int sortMode;
  final ValueChanged<int> onSortModeChanged;

  const ProxyActionBar({
    super.key,
    required this.selectedGroupName,
    required this.onLocate,
    required this.sortMode,
    required this.onSortModeChanged,
  });

  @override
  Widget build(BuildContext context) {
    return Selector<ClashProvider, _ActionBarState>(
      selector: (_, provider) => _ActionBarState(
        isLoading: provider.isLoading,
        isRunning: provider.isRunning,
        isBatchTesting: provider.isBatchTesting,
      ),
      builder: (context, state, child) {
        final clashProvider = context.read<ClashProvider>();

        return Padding(
          padding: const EdgeInsets.only(
            left: 16.0,
            right: 16.0,
            top: 4.0,
            bottom: 0.0,
          ),
          child: Row(
            children: [
              ModernTooltip(
                message: context.translate.proxy.testAllDelays,
                child: IconButton(
                  onPressed: state.canTestDelays
                      ? () => clashProvider.testGroupDelays(selectedGroupName)
                      : null,
                  icon: Icon(
                    Icons.network_check,
                    size: 18,
                    color: state.isBatchTesting ? Colors.grey : null,
                  ),
                  visualDensity: VisualDensity.compact,
                  padding: const EdgeInsets.symmetric(
                    horizontal: 8,
                    vertical: 10,
                  ),
                ),
              ),
              ModernTooltip(
                message: context.translate.proxy.locate,
                child: IconButton(
                  onPressed: state.canLocate ? onLocate : null,
                  icon: const Icon(Icons.gps_fixed, size: 18),
                  visualDensity: VisualDensity.compact,
                  padding: const EdgeInsets.symmetric(
                    horizontal: 8,
                    vertical: 10,
                  ),
                ),
              ),
              ModernTooltip(
                message: _getSortTooltip(context, sortMode),
                child: IconButton(
                  onPressed: _handleSortModeChange,
                  icon: Icon(_getSortIcon(sortMode), size: 18),
                  visualDensity: VisualDensity.compact,
                  padding: const EdgeInsets.symmetric(
                    horizontal: 8,
                    vertical: 10,
                  ),
                ),
              ),
            ],
          ),
        );
      },
    );
  }

  void _handleSortModeChange() {
    const totalSortModes = 3;
    final nextMode = (sortMode + 1) % totalSortModes;
    onSortModeChanged(nextMode);
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

// 操作栏状态数据类
class _ActionBarState {
  final bool isLoading;
  final bool isRunning;
  final bool isBatchTesting;

  _ActionBarState({
    required this.isLoading,
    required this.isRunning,
    required this.isBatchTesting,
  });

  bool get canTestDelays => !isLoading && isRunning && !isBatchTesting;
  bool get canLocate => !isLoading;

  @override
  bool operator ==(Object other) =>
      identical(this, other) ||
      other is _ActionBarState &&
          runtimeType == other.runtimeType &&
          isLoading == other.isLoading &&
          isRunning == other.isRunning &&
          isBatchTesting == other.isBatchTesting;

  @override
  int get hashCode =>
      isLoading.hashCode ^ isRunning.hashCode ^ isBatchTesting.hashCode;
}

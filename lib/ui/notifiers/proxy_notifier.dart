import 'package:flutter/foundation.dart';
import 'package:stelliberty/clash/providers/clash_provider.dart';
import 'package:stelliberty/storage/clash_preferences.dart';
import 'package:stelliberty/clash/model/clash_model.dart';
import 'package:stelliberty/services/log_print_service.dart';

// ProxyPage 的业务逻辑
// 负责代理节点排序、搜索和定位计算
class ProxyNotifier extends ChangeNotifier {
  final ClashProvider _clashProvider;

  int _sortMode = 0;
  String _searchQuery = '';
  bool _isSearching = false;

  // UI 常量（用于定位计算）
  static const double proxyCardHeight = 88.0;
  static const double proxyCardSpacing = 16.0;
  static const double proxyCardTotalHeight = proxyCardHeight + proxyCardSpacing;

  ProxyNotifier({required ClashProvider clashProvider})
    : _clashProvider = clashProvider {
    _loadSortMode();
  }

  // ========== Getters ==========

  int get sortMode => _sortMode;
  String get searchQuery => _searchQuery;
  bool get isSearching => _isSearching;

  // ========== 搜索功能 ==========

  // 切换搜索状态
  void toggleSearch() {
    _isSearching = !_isSearching;
    if (!_isSearching) {
      _searchQuery = '';
    }
    notifyListeners();
  }

  // 关闭搜索
  void closeSearch() {
    _isSearching = false;
    _searchQuery = '';
    notifyListeners();
  }

  // 更新搜索关键字
  void updateSearchQuery(String query) {
    _searchQuery = query;
    notifyListeners();
  }

  // ========== 排序功能 ==========

  // 加载排序模式
  void _loadSortMode() {
    _sortMode = ClashPreferences.instance.getProxyNodeSortMode();
    Logger.debug('加载排序模式：$_sortMode');
  }

  // 切换排序模式
  void changeSortMode(int mode) {
    _sortMode = mode;
    ClashPreferences.instance.setProxyNodeSortMode(mode);
    notifyListeners();
    Logger.info('排序模式已更改：$_sortMode');
  }

  // 根据当前排序模式对代理名称列表进行排序（并应用搜索过滤）
  List<String> getSortedProxyNames(List<String> proxyNames) {
    // 先应用搜索过滤
    List<String> filteredNames = proxyNames;
    if (_searchQuery.isNotEmpty) {
      final query = _searchQuery.toLowerCase();
      filteredNames = proxyNames.where((name) {
        return name.toLowerCase().contains(query);
      }).toList();
    }

    if (_sortMode == 0) {
      return filteredNames; // 默认排序（配置文件顺序）
    }

    final sortedNames = List<String>.from(filteredNames);

    if (_sortMode == 1) {
      // 按名称排序
      sortedNames.sort((a, b) => a.toLowerCase().compareTo(b.toLowerCase()));
    } else if (_sortMode == 2) {
      // 按延迟排序
      sortedNames.sort((a, b) {
        final nodeA = _clashProvider.proxyNodes[a];
        final nodeB = _clashProvider.proxyNodes[b];

        final delayA = nodeA?.delay;
        final delayB = nodeB?.delay;

        if (delayA == null && delayB == null) return 0;
        if (delayA == null) return 1; // null 排后面
        if (delayB == null) return -1;

        if (delayA < 0 && delayB < 0) return 0; // 都未测试
        if (delayA < 0) return 1; // 未测试排后面
        if (delayB < 0) return -1;

        return delayA.compareTo(delayB); // 延迟从低到高
      });
    }

    return sortedNames;
  }

  // ========== 节点定位 ==========

  // 计算定位到指定节点的滚动偏移量
  double? calculateLocateOffset({
    required String nodeName,
    required ProxyGroup selectedGroup,
    required int crossAxisCount,
    required double maxScrollExtent,
    required double viewportHeight,
  }) {
    if (nodeName.isEmpty) {
      Logger.warning('节点名称为空，无法定位');
      return null;
    }

    final sortedProxyNames = getSortedProxyNames(selectedGroup.all);

    if (sortedProxyNames.isEmpty) {
      Logger.warning('代理组为空，无法定位');
      return null;
    }

    final nodeIndex = sortedProxyNames.indexOf(nodeName);

    if (nodeIndex == -1) {
      Logger.warning('节点 $nodeName 在代理组中不存在');
      return null;
    }

    // 计算节点所在行
    final int rowIndex = nodeIndex ~/ crossAxisCount;

    // 计算目标偏移量（让节点居中显示）
    double targetOffset =
        (rowIndex * proxyCardTotalHeight) -
        (viewportHeight / 2) +
        (proxyCardTotalHeight / 2);

    // 限制在有效范围内
    targetOffset = targetOffset.clamp(0.0, maxScrollExtent);

    Logger.debug(
      '计算定位偏移量：节点=$nodeName，索引=$nodeIndex，行=$rowIndex，偏移量=$targetOffset',
    );

    return targetOffset;
  }
}

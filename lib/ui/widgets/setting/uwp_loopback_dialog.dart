import 'dart:async';
import 'dart:ui';
import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import 'package:stelliberty/i18n/i18n.dart';
import 'package:stelliberty/ui/common/modern_switch.dart';
import 'package:stelliberty/ui/widgets/modern_toast.dart';
import 'package:stelliberty/src/bindings/signals/signals.dart';

// UWP 应用数据模型
class UwpApp {
  final String appContainerName;
  final String displayName;
  final String packageFamilyName;
  final List<int> sid;
  final String sidString;
  bool isLoopbackEnabled;

  UwpApp({
    required this.appContainerName,
    required this.displayName,
    required this.packageFamilyName,
    required this.sid,
    required this.sidString,
    required this.isLoopbackEnabled,
  });

  // 从 Rust 消息创建
  factory UwpApp.fromRust(AppContainerInfo info) {
    return UwpApp(
      appContainerName: info.containerName,
      displayName: info.displayName,
      packageFamilyName: info.packageFamilyName,
      sid: info.sid,
      sidString: info.sidString,
      isLoopbackEnabled: info.loopbackEnabled,
    );
  }
}

// UWP 回环对话框状态管理器
class UwpLoopbackState extends ChangeNotifier {
  List<UwpApp> _apps = [];
  bool _isLoading = true;
  String? _errorMessage;
  String _searchQuery = '';

  // Getters
  List<UwpApp> get apps => _apps;
  bool get isLoading => _isLoading;
  String? get errorMessage => _errorMessage;
  String get searchQuery => _searchQuery;

  // 获取过滤后的应用列表
  List<UwpApp> get filteredApps {
    if (_searchQuery.isEmpty) {
      return _apps;
    }
    return _apps.where((app) {
      return app.displayName.toLowerCase().contains(
            _searchQuery.toLowerCase(),
          ) ||
          app.packageFamilyName.toLowerCase().contains(
            _searchQuery.toLowerCase(),
          );
    }).toList();
  }

  // 设置搜索查询
  void setSearchQuery(String query) {
    _searchQuery = query;
    notifyListeners();
  }

  // 从 Rust 后端加载 UWP 应用列表
  Future<void> loadApps() async {
    _isLoading = true;
    _errorMessage = null;
    notifyListeners();

    try {
      // 收集应用信息，使用 Completer 确保所有消息都已处理
      final apps = <UwpApp>[];
      final completer = Completer<void>();

      // **关键修复**：先建立所有订阅，再发送请求，消除竞态窗口
      // 监听应用信息流
      final appStreamListener = AppContainerInfo.rustSignalStream.listen((
        signal,
      ) {
        apps.add(UwpApp.fromRust(signal.message));
      });

      // 监听完成信号
      final completeListener = AppContainersComplete.rustSignalStream.listen((
        _,
      ) {
        // 等待一小段时间确保所有消息都已入队并处理
        Future.delayed(const Duration(milliseconds: 50)).then((_) {
          if (!completer.isCompleted) {
            completer.complete();
          }
        });
      });

      // 等待列表初始化信号的订阅
      final listListener = AppContainersList.rustSignalStream.listen((_) {
        // 初始化信号，不需要处理
      });

      try {
        // **所有订阅已就绪，现在安全发送请求**
        const GetAppContainers().sendSignalToRust();

        // 等待完成信号或超时
        await completer.future.timeout(const Duration(seconds: 10));
      } finally {
        await appStreamListener.cancel();
        await completeListener.cancel();
        await listListener.cancel();
      }

      _apps = apps;
      _isLoading = false;
      notifyListeners();
    } catch (e) {
      _errorMessage = '加载失败: ${e.toString()}';
      _isLoading = false;
      notifyListeners();
    }
  }

  // 全选
  void selectAll() {
    for (var app in _apps) {
      app.isLoopbackEnabled = true;
    }
    notifyListeners();
  }

  // 反选
  void invertSelection() {
    for (var app in _apps) {
      app.isLoopbackEnabled = !app.isLoopbackEnabled;
    }
    notifyListeners();
  }

  // 切换单个应用的回环状态
  void toggleApp(UwpApp app, bool value) {
    app.isLoopbackEnabled = value;
    notifyListeners();
  }

  // 获取启用回环的应用 SID 列表
  List<String> getEnabledSids() {
    return _apps
        .where((app) => app.isLoopbackEnabled)
        .map((app) => app.sidString)
        .toList();
  }
}

// UWP 回环管理对话框
class UwpLoopbackDialog extends StatefulWidget {
  const UwpLoopbackDialog({super.key});

  static Future<void> show(BuildContext context) {
    return showDialog<void>(
      context: context,
      barrierDismissible: false,
      builder: (context) => ChangeNotifierProvider(
        create: (_) => UwpLoopbackState()..loadApps(),
        child: const UwpLoopbackDialog(),
      ),
    );
  }

  @override
  State<UwpLoopbackDialog> createState() => _UwpLoopbackDialogState();
}

class _UwpLoopbackDialogState extends State<UwpLoopbackDialog>
    with TickerProviderStateMixin {
  late final AnimationController _animationController;
  late final Animation<double> _scaleAnimation;
  late final Animation<double> _opacityAnimation;
  final TextEditingController _searchController = TextEditingController();

  @override
  void initState() {
    super.initState();

    _animationController = AnimationController(
      duration: const Duration(milliseconds: 300),
      vsync: this,
    );

    _scaleAnimation = Tween<double>(begin: 0.8, end: 1.0).animate(
      CurvedAnimation(parent: _animationController, curve: Curves.easeOutBack),
    );

    _opacityAnimation = Tween<double>(begin: 0.0, end: 1.0).animate(
      CurvedAnimation(parent: _animationController, curve: Curves.easeOut),
    );

    _animationController.forward();
  }

  @override
  void dispose() {
    _animationController.dispose();
    _searchController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final isDark = Theme.of(context).brightness == Brightness.dark;

    return Material(
      type: MaterialType.transparency,
      child: AnimatedBuilder(
        animation: _animationController,
        builder: (context, child) {
          return Stack(
            children: [
              // 背景遮罩
              Container(
                color: Colors.black.withValues(alpha: isDark ? 0.5 : 0.3),
              ),
              // 对话框内容
              Center(
                child: Transform.scale(
                  scale: _scaleAnimation.value,
                  child: Opacity(
                    opacity: _opacityAnimation.value,
                    child: _buildDialog(),
                  ),
                ),
              ),
            ],
          );
        },
      ),
    );
  }

  Widget _buildDialog() {
    final isDark = Theme.of(context).brightness == Brightness.dark;
    final screenSize = MediaQuery.of(context).size;

    return SizedBox(
      width: screenSize.width - 400, // 左右各200px间距
      height: screenSize.height - 100, // 上下各50px间距
      child: ClipRRect(
        borderRadius: BorderRadius.circular(20),
        child: BackdropFilter(
          filter: ImageFilter.blur(sigmaX: 16, sigmaY: 16),
          child: Container(
            decoration: BoxDecoration(
              color: isDark
                  ? Colors.white.withValues(alpha: 0.08)
                  : Colors.white.withValues(alpha: 0.7),
              borderRadius: BorderRadius.circular(20),
              border: Border.all(
                color: Colors.white.withValues(alpha: isDark ? 0.1 : 0.3),
                width: 1,
              ),
              boxShadow: [
                BoxShadow(
                  color: Colors.black.withValues(alpha: 0.18),
                  blurRadius: 40,
                  offset: const Offset(0, 10),
                ),
              ],
            ),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                _buildHeader(),
                Flexible(fit: FlexFit.loose, child: _buildContent()),
                _buildActions(),
              ],
            ),
          ),
        ),
      ),
    );
  }

  Widget _buildHeader() {
    final isDark = Theme.of(context).brightness == Brightness.dark;

    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 24, vertical: 20),
      decoration: BoxDecoration(
        color: isDark
            ? Colors.white.withValues(alpha: 0.06)
            : Colors.white.withValues(alpha: 0.3),
        border: Border(
          bottom: BorderSide(
            color: Colors.white.withValues(alpha: isDark ? 0.1 : 0.3),
            width: 1,
          ),
        ),
      ),
      child: Column(
        children: [
          Row(
            children: [
              Container(
                padding: const EdgeInsets.all(8),
                decoration: BoxDecoration(
                  color: Theme.of(
                    context,
                  ).colorScheme.primary.withValues(alpha: 0.9),
                  borderRadius: BorderRadius.circular(10),
                  boxShadow: [
                    BoxShadow(
                      color: Theme.of(
                        context,
                      ).colorScheme.primary.withValues(alpha: 0.3),
                      blurRadius: 8,
                      offset: const Offset(0, 2),
                    ),
                  ],
                ),
                child: const Icon(Icons.apps, color: Colors.white, size: 20),
              ),
              const SizedBox(width: 16),
              Expanded(
                child: Builder(
                  builder: (context) {
                    return Text(
                      context.translate.uwpLoopback.dialogTitle,
                      style: Theme.of(context).textTheme.titleLarge?.copyWith(
                        fontWeight: FontWeight.w600,
                        color: Theme.of(context).colorScheme.onSurface,
                      ),
                    );
                  },
                ),
              ),
              Material(
                color: Colors.transparent,
                child: InkWell(
                  onTap: _handleClose,
                  borderRadius: BorderRadius.circular(10),
                  child: Container(
                    padding: const EdgeInsets.all(8),
                    decoration: BoxDecoration(
                      borderRadius: BorderRadius.circular(10),
                    ),
                    child: Icon(
                      Icons.close,
                      color: Theme.of(
                        context,
                      ).colorScheme.onSurface.withValues(alpha: 0.7),
                      size: 20,
                    ),
                  ),
                ),
              ),
            ],
          ),
          const SizedBox(height: 16),
          // 搜索框
          _buildSearchField(),
        ],
      ),
    );
  }

  Widget _buildSearchField() {
    final isDark = Theme.of(context).brightness == Brightness.dark;

    return Consumer<UwpLoopbackState>(
      builder: (context, state, _) {
        return Container(
          decoration: BoxDecoration(
            color: isDark
                ? Colors.white.withValues(alpha: 0.05)
                : Colors.white.withValues(alpha: 0.5),
            borderRadius: BorderRadius.circular(12),
            border: Border.all(
              color: Colors.white.withValues(alpha: isDark ? 0.1 : 0.2),
            ),
          ),
          child: TextField(
            controller: _searchController,
            onChanged: (value) => state.setSearchQuery(value),
            style: TextStyle(
              color: Theme.of(context).colorScheme.onSurface,
              fontSize: 14,
            ),
            decoration: InputDecoration(
              hintText: context.translate.uwpLoopback.searchPlaceholder,
              hintStyle: TextStyle(
                color: Theme.of(
                  context,
                ).colorScheme.onSurface.withValues(alpha: 0.5),
                fontSize: 14,
              ),
              prefixIcon: Icon(
                Icons.search,
                color: Theme.of(
                  context,
                ).colorScheme.onSurface.withValues(alpha: 0.5),
                size: 20,
              ),
              suffixIcon: state.searchQuery.isNotEmpty
                  ? IconButton(
                      icon: Icon(
                        Icons.clear,
                        color: Theme.of(
                          context,
                        ).colorScheme.onSurface.withValues(alpha: 0.5),
                        size: 20,
                      ),
                      onPressed: () {
                        _searchController.clear();
                        state.setSearchQuery('');
                      },
                    )
                  : null,
              border: InputBorder.none,
              contentPadding: const EdgeInsets.symmetric(
                horizontal: 16,
                vertical: 12,
              ),
            ),
          ),
        );
      },
    );
  }

  Widget _buildContent() {
    return Consumer<UwpLoopbackState>(
      builder: (context, state, _) {
        if (state.isLoading) {
          return Center(
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                const CircularProgressIndicator(),
                const SizedBox(height: 16),
                Text(context.translate.uwpLoopback.loading),
              ],
            ),
          );
        }

        if (state.errorMessage != null) {
          return Center(
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                Icon(Icons.error_outline, size: 64, color: Colors.red[400]),
                const SizedBox(height: 16),
                Text(
                  state.errorMessage!,
                  style: TextStyle(fontSize: 16, color: Colors.red[600]),
                  textAlign: TextAlign.center,
                ),
                const SizedBox(height: 16),
                ElevatedButton.icon(
                  onPressed: state.loadApps,
                  icon: const Icon(Icons.refresh),
                  label: Text(context.translate.common.refresh),
                ),
              ],
            ),
          );
        }

        final filteredApps = state.filteredApps;

        if (filteredApps.isEmpty) {
          return Center(
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                Icon(Icons.search_off, size: 64, color: Colors.grey[400]),
                const SizedBox(height: 16),
                Text(
                  context.translate.uwpLoopback.noApps,
                  style: TextStyle(fontSize: 16, color: Colors.grey[600]),
                ),
              ],
            ),
          );
        }

        return ListView.builder(
          padding: const EdgeInsets.all(24),
          itemCount: filteredApps.length,
          itemBuilder: (context, index) {
            return _buildAppItem(filteredApps[index]);
          },
        );
      },
    );
  }

  Widget _buildAppItem(UwpApp app) {
    final isDark = Theme.of(context).brightness == Brightness.dark;

    return Container(
      margin: const EdgeInsets.only(bottom: 12),
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        color: isDark
            ? Colors.white.withValues(alpha: 0.04)
            : Colors.white.withValues(alpha: 0.5),
        borderRadius: BorderRadius.circular(12),
        border: Border.all(
          color: Colors.white.withValues(alpha: isDark ? 0.1 : 0.2),
        ),
      ),
      child: Row(
        children: [
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                // 应用名称
                Text(
                  app.displayName,
                  style: TextStyle(
                    fontSize: 14,
                    fontWeight: FontWeight.w600,
                    color: Theme.of(context).colorScheme.onSurface,
                  ),
                ),
                const SizedBox(height: 6),
                // 包家族名称
                Text(
                  app.packageFamilyName.isEmpty
                      ? 'None'
                      : app.packageFamilyName,
                  style: TextStyle(
                    fontSize: 11,
                    color: Theme.of(
                      context,
                    ).colorScheme.onSurface.withValues(alpha: 0.6),
                  ),
                ),
                const SizedBox(height: 4),
                // AC Name
                Row(
                  children: [
                    Text(
                      'AC Name: ',
                      style: TextStyle(
                        fontSize: 10,
                        fontWeight: FontWeight.w500,
                        color: Theme.of(
                          context,
                        ).colorScheme.onSurface.withValues(alpha: 0.5),
                      ),
                    ),
                    Expanded(
                      child: Text(
                        app.appContainerName.isEmpty
                            ? 'None'
                            : app.appContainerName,
                        style: TextStyle(
                          fontSize: 10,
                          color: Theme.of(
                            context,
                          ).colorScheme.onSurface.withValues(alpha: 0.5),
                        ),
                        overflow: TextOverflow.ellipsis,
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: 2),
                // AC SID
                Row(
                  children: [
                    Text(
                      'AC SID: ',
                      style: TextStyle(
                        fontSize: 10,
                        fontWeight: FontWeight.w500,
                        color: Theme.of(
                          context,
                        ).colorScheme.onSurface.withValues(alpha: 0.5),
                      ),
                    ),
                    Expanded(
                      child: Text(
                        app.sidString,
                        style: TextStyle(
                          fontSize: 10,
                          fontFamily: 'monospace',
                          color: Theme.of(
                            context,
                          ).colorScheme.onSurface.withValues(alpha: 0.5),
                        ),
                        overflow: TextOverflow.ellipsis,
                      ),
                    ),
                  ],
                ),
              ],
            ),
          ),
          const SizedBox(width: 12),
          Consumer<UwpLoopbackState>(
            builder: (context, state, _) {
              return ModernSwitch(
                value: app.isLoopbackEnabled,
                onChanged: (value) => state.toggleApp(app, value),
              );
            },
          ),
        ],
      ),
    );
  }

  Widget _buildActions() {
    final isDark = Theme.of(context).brightness == Brightness.dark;

    return Material(
      color: Colors.transparent,
      child: Container(
        padding: const EdgeInsets.symmetric(horizontal: 24, vertical: 16),
        decoration: BoxDecoration(
          color: isDark
              ? Colors.white.withValues(alpha: 0.06)
              : Colors.white.withValues(alpha: 0.3),
          border: Border(
            top: BorderSide(
              color: Colors.white.withValues(alpha: isDark ? 0.1 : 0.3),
              width: 1,
            ),
          ),
        ),
        child: Row(
          children: [
            // 左侧：全选/反选按钮
            Consumer<UwpLoopbackState>(
              builder: (context, state, _) {
                return OutlinedButton.icon(
                  onPressed: state.selectAll,
                  icon: const Icon(Icons.check_box, size: 18),
                  label: Text(context.translate.uwpLoopback.enableAll),
                  style: OutlinedButton.styleFrom(
                    padding: const EdgeInsets.symmetric(
                      vertical: 12,
                      horizontal: 16,
                    ),
                    shape: RoundedRectangleBorder(
                      borderRadius: BorderRadius.circular(10),
                    ),
                    side: BorderSide(
                      color: isDark
                          ? Colors.white.withValues(alpha: 0.2)
                          : Colors.white.withValues(alpha: 0.6),
                    ),
                    backgroundColor: isDark
                        ? Colors.white.withValues(alpha: 0.04)
                        : Colors.white.withValues(alpha: 0.6),
                  ),
                );
              },
            ),
            const SizedBox(width: 8),
            Consumer<UwpLoopbackState>(
              builder: (context, state, _) {
                return OutlinedButton.icon(
                  onPressed: state.invertSelection,
                  icon: const Icon(Icons.swap_horiz, size: 18),
                  label: Text(context.translate.uwpLoopback.invertSelection),
                  style: OutlinedButton.styleFrom(
                    padding: const EdgeInsets.symmetric(
                      vertical: 12,
                      horizontal: 16,
                    ),
                    shape: RoundedRectangleBorder(
                      borderRadius: BorderRadius.circular(10),
                    ),
                    side: BorderSide(
                      color: isDark
                          ? Colors.white.withValues(alpha: 0.2)
                          : Colors.white.withValues(alpha: 0.6),
                    ),
                    backgroundColor: isDark
                        ? Colors.white.withValues(alpha: 0.04)
                        : Colors.white.withValues(alpha: 0.6),
                  ),
                );
              },
            ),
            const Spacer(),
            // 右侧：取消/保存按钮
            OutlinedButton(
              onPressed: _handleClose,
              style: OutlinedButton.styleFrom(
                padding: const EdgeInsets.symmetric(
                  vertical: 12,
                  horizontal: 16,
                ),
                shape: RoundedRectangleBorder(
                  borderRadius: BorderRadius.circular(10),
                ),
                side: BorderSide(
                  color: isDark
                      ? Colors.white.withValues(alpha: 0.2)
                      : Colors.white.withValues(alpha: 0.6),
                ),
                backgroundColor: isDark
                    ? Colors.white.withValues(alpha: 0.04)
                    : Colors.white.withValues(alpha: 0.6),
              ),
              child: Text(
                context.translate.common.cancel,
                style: TextStyle(
                  color: Theme.of(
                    context,
                  ).colorScheme.onSurface.withValues(alpha: 0.8),
                ),
              ),
            ),
            const SizedBox(width: 12),
            ElevatedButton(
              onPressed: _handleSave,
              style: ElevatedButton.styleFrom(
                padding: const EdgeInsets.symmetric(
                  vertical: 12,
                  horizontal: 16,
                ),
                backgroundColor: Theme.of(context).colorScheme.primary,
                foregroundColor: Colors.white,
                shape: RoundedRectangleBorder(
                  borderRadius: BorderRadius.circular(10),
                ),
                elevation: 0,
                shadowColor: Theme.of(
                  context,
                ).colorScheme.primary.withValues(alpha: 0.5),
              ),
              child: Text(context.translate.common.save),
            ),
          ],
        ),
      ),
    );
  }

  void _handleClose() {
    _animationController.reverse().then((_) {
      if (mounted) {
        Navigator.of(context).pop();
      }
    });
  }

  Future<void> _handleSave() async {
    final state = context.read<UwpLoopbackState>();

    // 收集启用回环的应用的 SID
    final enabledSids = state.getEnabledSids();

    // 发送保存请求到 Rust
    SaveLoopbackConfiguration(sidStrings: enabledSids).sendSignalToRust();

    // 监听保存结果
    try {
      final result = await SaveLoopbackConfigurationResult
          .rustSignalStream
          .first
          .timeout(const Duration(seconds: 10));

      if (mounted) {
        if (result.message.success) {
          // 成功，显示提示并关闭对话框
          ModernToast.success(
            context,
            context.translate.uwpLoopback.saveSuccess,
          );
          _animationController.reverse().then((_) {
            if (mounted) {
              Navigator.of(context).pop();
            }
          });
        } else {
          // 失败，根据错误类型显示友好提示
          final errorMsg = result.message.errorMessage ?? '';
          final t = context.translate.uwpLoopback;
          String userFriendlyMsg;

          if (errorMsg.contains('权限不足') ||
              errorMsg.contains('ERROR_ACCESS_DENIED')) {
            userFriendlyMsg = t.errorPermissionDenied;
          } else if (errorMsg.contains('参数无效') ||
              errorMsg.contains('ERROR_INVALID_PARAMETER')) {
            userFriendlyMsg = t.errorInvalidParameter;
          } else if (errorMsg.contains('系统限制') || errorMsg.contains('E_FAIL')) {
            userFriendlyMsg = t.errorSystemRestriction;
          } else {
            userFriendlyMsg = t.saveFailed;
          }

          ModernToast.error(context, userFriendlyMsg);
        }
      }
    } catch (e) {
      if (mounted) {
        ModernToast.error(
          context,
          context.translate.uwpLoopback.applyFailed.replaceAll(
            '{error}',
            e.toString(),
          ),
        );
      }
    }
  }
}

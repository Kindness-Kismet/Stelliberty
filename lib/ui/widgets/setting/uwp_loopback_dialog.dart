import 'dart:async';
import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import 'package:stelliberty/i18n/i18n.dart';
import 'package:stelliberty/ui/common/modern_switch.dart';
import 'package:stelliberty/ui/common/modern_dialog.dart';
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
      isLoopbackEnabled: info.isLoopbackEnabled,
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
      final appInfoSubscription = AppContainerInfo.rustSignalStream.listen((
        signal,
      ) {
        apps.add(UwpApp.fromRust(signal.message));
      });

      // 监听完成信号
      final completeSubscription = AppContainersComplete.rustSignalStream
          .listen((_) {
            // 等待一小段时间确保所有消息都已入队并处理
            Future.delayed(const Duration(milliseconds: 50)).then((_) {
              if (!completer.isCompleted) {
                completer.complete();
              }
            });
          });

      // 等待列表初始化信号的订阅
      final listSubscription = AppContainersList.rustSignalStream.listen((_) {
        // 初始化信号，不需要处理
      });

      try {
        // **所有订阅已就绪，现在安全发送请求**
        const GetAppContainers().sendSignalToRust();

        // 等待完成信号或超时
        await completer.future.timeout(const Duration(seconds: 10));
      } finally {
        await appInfoSubscription.cancel();
        await completeSubscription.cancel();
        await listSubscription.cancel();
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

class _UwpLoopbackDialogState extends State<UwpLoopbackDialog> {
  final TextEditingController _searchController = TextEditingController();
  bool _isSaving = false;

  @override
  void dispose() {
    _searchController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final screenSize = MediaQuery.of(context).size;
    final trans = context.translate;

    return ModernDialog(
      title: trans.uwp_loopback.dialog_title,
      titleIcon: Icons.apps,
      maxWidth: screenSize.width - 400, // 特殊尺寸：左右各200px间距
      maxHeightRatio:
          (screenSize.height - 100) / screenSize.height, // 上下各50px间距
      searchController: _searchController,
      searchHint: trans.uwp_loopback.search_placeholder,
      onSearchChanged: (value) {
        setState(() {
          // 搜索状态由 ModernDialog 管理，这里只需要触发重建
        });
      },
      content: _buildContent(),
      actionsLeftButtons: [
        DialogActionButton(
          label: trans.uwp_loopback.enable_all,
          icon: Icons.check_box,
          onPressed: () => context.read<UwpLoopbackState>().selectAll(),
        ),
        DialogActionButton(
          label: trans.uwp_loopback.invert_selection,
          icon: Icons.swap_horiz,
          onPressed: () => context.read<UwpLoopbackState>().invertSelection(),
        ),
      ],
      actionsRight: [
        DialogActionButton(
          label: trans.common.cancel,
          isPrimary: false,
          onPressed: _isSaving ? null : () => Navigator.of(context).pop(),
        ),
        DialogActionButton(
          label: trans.common.save,
          isPrimary: true,
          isLoading: _isSaving,
          onPressed: _handleSave,
        ),
      ],
      onClose: _isSaving ? null : () => Navigator.of(context).pop(),
    );
  }

  Widget _buildContent() {
    final trans = context.translate;
    return Consumer<UwpLoopbackState>(
      builder: (context, state, _) {
        if (state.isLoading) {
          return const Center(
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                CircularProgressIndicator(),
                SizedBox(height: 16),
                Text('Loading...'),
              ],
            ),
          );
        }

        if (state.errorMessage != null) {
          return Center(
            child: Padding(
              padding: const EdgeInsets.all(24),
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
                    label: Text(trans.common.refresh),
                  ),
                ],
              ),
            ),
          );
        }

        // 使用 _searchController.text 进行过滤
        final searchQuery = _searchController.text.toLowerCase();
        final filteredApps = searchQuery.isEmpty
            ? state.apps
            : state.apps.where((app) {
                return app.displayName.toLowerCase().contains(searchQuery) ||
                    app.packageFamilyName.toLowerCase().contains(searchQuery);
              }).toList();

        if (filteredApps.isEmpty) {
          return Center(
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                Icon(Icons.search_off, size: 64, color: Colors.grey[400]),
                const SizedBox(height: 16),
                Text(
                  trans.uwp_loopback.no_apps,
                  style: TextStyle(fontSize: 16, color: Colors.grey[600]),
                ),
              ],
            ),
          );
        }

        return Material(
          color: Colors.transparent,
          child: ListView.builder(
            padding: const EdgeInsets.symmetric(horizontal: 24, vertical: 12),
            itemCount: filteredApps.length,
            itemBuilder: (context, index) {
              final isLast = index == filteredApps.length - 1;
              return _buildAppItem(filteredApps[index], isLast: isLast);
            },
          ),
        );
      },
    );
  }

  Widget _buildAppItem(UwpApp app, {bool isLast = false}) {
    final isDark = Theme.of(context).brightness == Brightness.dark;

    return Container(
      margin: EdgeInsets.only(bottom: isLast ? 0 : 12),
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

  Future<void> _handleSave() async {
    final trans = context.translate;
    final state = context.read<UwpLoopbackState>();

    setState(() {
      _isSaving = true;
    });

    try {
      // 收集启用回环的应用的 SID
      final enabledSids = state.getEnabledSids();

      // 发送保存请求到 Rust
      SaveLoopbackConfiguration(sidStrings: enabledSids).sendSignalToRust();

      // 监听保存结果
      final result = await SaveLoopbackConfigurationResult
          .rustSignalStream
          .first
          .timeout(const Duration(seconds: 10));

      if (result.message.isSuccessful) {
        // 成功，显示提示并关闭对话框
        if (mounted) {
          ModernToast.success(context, trans.uwp_loopback.save_success);
          Navigator.of(context).pop();
        }
      } else {
        // 失败，根据错误类型显示友好提示
        if (mounted) {
          setState(() {
            _isSaving = false;
          });

          final errorMsg = result.message.errorMessage ?? '';
          final t = trans.uwp_loopback;
          String userFriendlyMsg;

          if (errorMsg.contains('权限不足') ||
              errorMsg.contains('ERROR_ACCESS_DENIED')) {
            userFriendlyMsg = t.error_permission_denied;
          } else if (errorMsg.contains('参数无效') ||
              errorMsg.contains('ERROR_INVALID_PARAMETER')) {
            userFriendlyMsg = t.error_invalid_parameter;
          } else if (errorMsg.contains('系统限制') || errorMsg.contains('E_FAIL')) {
            userFriendlyMsg = t.error_system_restriction;
          } else {
            userFriendlyMsg = t.save_failed;
          }

          ModernToast.error(context, userFriendlyMsg);
        }
      }
    } catch (e) {
      if (mounted) {
        setState(() {
          _isSaving = false;
        });
        ModernToast.error(
          context,
          trans.uwp_loopback.apply_failed.replaceAll('{error}', e.toString()),
        );
      }
    }
  }
}

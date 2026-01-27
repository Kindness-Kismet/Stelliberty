import 'dart:io';
import 'package:flutter/material.dart';
import 'package:stelliberty/clash/model/provider_model.dart';
import 'package:stelliberty/clash/manager/clash_manager.dart';
import 'package:stelliberty/clash/services/geo_service.dart';
import 'package:stelliberty/i18n/i18n.dart';
import 'package:stelliberty/services/log_print_service.dart';
import 'package:stelliberty/ui/widgets/modern_toast.dart';
import 'package:stelliberty/ui/widgets/subscription/subscription_info_widget.dart';
import 'package:stelliberty/ui/common/modern_dialog.dart';
import 'package:intl/intl.dart';
import 'package:file_picker/file_picker.dart';
import 'package:path/path.dart' as p;

// 提供者查看对话框
class ProviderViewerDialog extends StatefulWidget {
  const ProviderViewerDialog({super.key});

  static Future<void> show(BuildContext context) {
    return showDialog<void>(
      context: context,
      barrierDismissible: false,
      builder: (context) => const ProviderViewerDialog(),
    );
  }

  @override
  State<ProviderViewerDialog> createState() => _ProviderViewerDialogState();
}

class _ProviderViewerDialogState extends State<ProviderViewerDialog> {
  // 同步相关常量
  static const int _syncBatchSize = 6; // 每批同步的提供者数量
  static const Duration _batchDelay = Duration(milliseconds: 300); // 批次间延迟
  static const Duration _syncDelay = Duration(milliseconds: 500); // 同步后延迟

  List<Provider> _providers = [];
  bool _isLoading = true;
  String? _errorMessage;
  bool _isSyncingAll = false;
  bool _hasLoadedProviders = false;

  // 搜索相关
  final TextEditingController _searchController = TextEditingController();
  String _searchQuery = '';

  @override
  void initState() {
    super.initState();
    _searchController.addListener(_onSearchChanged);
  }

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    // 只在第一次调用时加载，避免重复加载
    if (!_hasLoadedProviders) {
      _hasLoadedProviders = true;
      _loadProviders();
    }
  }

  @override
  void dispose() {
    _searchController.removeListener(_onSearchChanged);
    _searchController.dispose();
    super.dispose();
  }

  // 搜索变化回调
  void _onSearchChanged() {
    setState(() {
      _searchQuery = _searchController.text;
    });
  }

  // 获取过滤后的提供者列表
  List<Provider> get _filteredProviders {
    if (_searchQuery.isEmpty) {
      return _providers;
    }
    return _providers.where((provider) {
      return provider.name.toLowerCase().contains(_searchQuery.toLowerCase()) ||
          (provider.path?.toLowerCase().contains(_searchQuery.toLowerCase()) ??
              false);
    }).toList();
  }

  // 解析并过滤 Provider 数据
  void _addFilteredProviders(
    Map<String, dynamic> providersData,
    List<Provider> providers,
    ProviderType providerType,
  ) {
    final isProxy = providerType == ProviderType.proxy;
    final providerTypeLabel = isProxy ? '代理提供者' : '规则提供者';
    providersData.forEach((name, data) {
      if (data is Map<String, dynamic>) {
        final vehicleType = data['vehicleType'];
        if (vehicleType == 'HTTP' || vehicleType == 'File') {
          final count = isProxy
              ? ((data['proxies'] is List)
                    ? (data['proxies'] as List).length
                    : 0)
              : ((data['ruleCount'] as num?)?.toInt() ?? 0);
          final countLabel = isProxy ? '节点' : '规则';
          Logger.debug(
            '✓ $providerTypeLabel：$name ($vehicleType，$count $countLabel)',
          );
          providers.add(
            Provider.fromClashApi(name, data, providerType: providerType),
          );
        } else {
          final skipLabel = isProxy ? '代理组' : '规则项';
          Logger.debug('✗ 跳过$skipLabel：$name ($vehicleType)');
        }
      }
    });
  }

  // 从 API 加载 Providers
  Future<void> _loadProviders() async {
    final trans = context.translate;
    setState(() {
      _isLoading = true;
      _errorMessage = null;
    });

    try {
      final clashManager = ClashManager.instance;
      final apiClient = clashManager.apiClient;

      if (apiClient == null) {
        setState(() {
          _errorMessage = trans.provider.clash_not_running;
          _isLoading = false;
        });
        return;
      }

      // 获取代理 providers 和规则 providers
      final proxyProvidersData = await apiClient.getProviders();
      final ruleProvidersData = await apiClient.getRuleProviders();

      Logger.debug(
        '加载提供者：代理提供者 ${proxyProvidersData.length} 个，规则提供者 ${ruleProvidersData.length} 个',
      );

      final providers = <Provider>[];

      // 解析代理 providers
      _addFilteredProviders(proxyProvidersData, providers, ProviderType.proxy);

      // 解析规则 providers
      _addFilteredProviders(ruleProvidersData, providers, ProviderType.rule);

      Logger.debug(
        '过滤完成：共 ${providers.length} 个提供者 (代理 ${providers.where((p) => p.type == ProviderType.proxy).length}，规则 ${providers.where((p) => p.type == ProviderType.rule).length})',
      );

      setState(() {
        _providers = providers;
        _isLoading = false;
      });
    } catch (e) {
      setState(() {
        _errorMessage = trans.provider.loading_failed.replaceAll(
          '{error}',
          e.toString(),
        );
        _isLoading = false;
      });
    }
  }

  // 同步所有 Providers
  Future<void> _syncAll() async {
    final trans = context.translate;
    // 防止重复触发
    if (_isSyncingAll) {
      Logger.warning('同步全部操作正在进行中，忽略重复请求');
      return;
    }

    final clashManager = ClashManager.instance;
    final apiClient = clashManager.apiClient;

    if (apiClient == null) {
      if (mounted) {
        ModernToast.error(trans.provider.clash_not_running);
      }
      return;
    }

    // 筛选出需要同步的 HTTP providers，排除已经在同步中的
    final httpProviders = _providers
        .where((p) => p.vehicleType == 'HTTP' && !p.isUpdating)
        .toList();

    if (httpProviders.isEmpty) {
      if (mounted) {
        final updatingCount = _providers.where((p) => p.isUpdating).length;
        if (updatingCount > 0) {
          ModernToast.info(
            trans.provider.all_sync_in_progress.replaceAll(
              '{count}',
              updatingCount.toString(),
            ),
          );
        } else {
          ModernToast.info(trans.provider.no_providers_to_sync);
        }
      }
      return;
    }

    Logger.info('开始同步 ${httpProviders.length} 个提供者（分批处理）');

    // 构建需要同步的名称集合（O(1) 查找）
    final httpProviderNames = httpProviders.map((p) => p.name).toSet();

    // 设置"同步全部"状态
    setState(() {
      _isSyncingAll = true;
      for (var i = 0; i < _providers.length; i++) {
        final provider = _providers[i];
        if (httpProviderNames.contains(provider.name)) {
          _providers[i] = provider.copyWith(isUpdating: true);
        }
      }
    });

    // 分批并行同步，避免 Clash API 超载
    final allResults = <(String, bool, String?)>[];

    for (var i = 0; i < httpProviders.length; i += _syncBatchSize) {
      final batch = httpProviders.skip(i).take(_syncBatchSize).toList();
      Logger.debug('同步第 ${i ~/ _syncBatchSize + 1} 批（${batch.length} 个提供者）');

      final batchResults = await Future.wait(
        batch.map((provider) async {
          try {
            if (provider.type == ProviderType.proxy) {
              await apiClient.updateProvider(provider.name);
            } else {
              await apiClient.updateRuleProvider(provider.name);
            }
            Logger.info('同步成功: ${provider.name}');
            return (provider.name, true, null);
          } catch (e) {
            Logger.error('同步失败: ${provider.name} - $e');
            return (provider.name, false, e.toString());
          }
        }),
      );

      allResults.addAll(batchResults);

      // 批次之间延迟，让 Clash 有时间处理
      if (i + _syncBatchSize < httpProviders.length) {
        await Future.delayed(_batchDelay);
      }
    }

    // 统计结果
    final successCount = allResults.where((r) => r.$2).length;
    final failedResults = allResults.where((r) => !r.$2).toList();

    // 如果有失败的，等待更长时间让 Clash 完成处理
    if (failedResults.isNotEmpty) {
      Logger.warning('检测到 ${failedResults.length} 个提供者同步失败，等待 2 秒后重新加载数据');
      await Future.delayed(const Duration(seconds: 2));
    }

    // 重新加载数据并重置状态
    await _loadProviders();

    setState(() {
      _isSyncingAll = false;
    });

    if (mounted) {
      if (failedResults.isEmpty) {
        ModernToast.success(
          trans.provider.all_sync_complete
              .replaceAll('{success}', successCount.toString())
              .replaceAll('{total}', httpProviders.length.toString()),
        );
      } else {
        final failedNames = failedResults.map((r) => r.$1).join('，');
        ModernToast.error(
          trans.provider.partial_sync_failed
              .replaceAll('{names}', failedNames)
              .replaceAll('{success}', successCount.toString())
              .replaceAll('{failed}', failedResults.length.toString()),
        );
      }
    }
  }

  // 上传文件更新 Provider
  Future<void> _handleUpload(Provider provider) async {
    final trans = context.translate;
    try {
      // 选择文件
      final result = await FilePicker.pickFiles(
        type: FileType.custom,
        allowedExtensions: ['yaml', 'yml'],
      );

      if (result == null || result.files.isEmpty) {
        return;
      }

      final platformFile = result.files.first;
      if (platformFile.path == null || provider.path == null) {
        if (mounted) {
          ModernToast.error(trans.provider.path_not_available);
        }
        return;
      }

      // 读取选择的文件内容
      final sourceFile = File(platformFile.path!);
      final bytes = await sourceFile.readAsBytes();

      // 将相对路径转换为绝对路径
      final clashDataDir = await GeoService.getGeoDataDir();
      final absolutePath = provider.path!.startsWith('./')
          ? p.join(clashDataDir, provider.path!.substring(2))
          : p.join(clashDataDir, provider.path!);

      // 写入到 provider 的路径
      final targetFile = File(absolutePath);
      await targetFile.parent.create(recursive: true);
      await targetFile.writeAsBytes(bytes);

      if (mounted) {
        ModernToast.success(
          trans.provider.upload_success.replaceAll('{name}', provider.name),
        );
      }

      // 重新加载 providers 数据
      await Future.delayed(const Duration(milliseconds: 500));
      await _loadProviders();
    } catch (e) {
      Logger.error('上传提供者失败: $e');
      if (mounted) {
        ModernToast.error(
          trans.provider.upload_failed.replaceAll('{error}', e.toString()),
        );
      }
    }
  }

  // 同步单个 Provider
  Future<void> _handleSync(Provider provider) async {
    final trans = context.translate;
    if (provider.vehicleType != 'HTTP') {
      return;
    }

    // 如果正在执行"同步全部"，阻止单个同步
    if (_isSyncingAll) {
      Logger.warning('同步全部操作正在进行中，忽略单个同步请求：${provider.name}');
      if (mounted) {
        ModernToast.info(trans.provider.sync_all_in_progress);
      }
      return;
    }

    // 如果该 provider 已经在同步中，阻止重复同步
    if (provider.isUpdating) {
      Logger.warning('提供者 ${provider.name} 已在同步中，忽略重复请求');
      return;
    }

    final providerName = provider.name;

    // 辅助函数：通过名称查找并更新 provider
    void updateProviderByName(Provider updatedProvider) {
      final idx = _providers.indexWhere((p) => p.name == providerName);
      if (idx != -1) {
        _providers[idx] = updatedProvider;
      }
    }

    // 开始同步：设置 isUpdating
    setState(() {
      updateProviderByName(provider.copyWith(isUpdating: true));
    });

    // 执行同步
    final syncResult = await _executeSyncOperation(provider);

    // 结束同步：更新最终状态
    setState(() {
      updateProviderByName(syncResult.updatedProvider);
    });

    // 显示结果反馈
    if (mounted) {
      if (syncResult.isSuccessful) {
        ModernToast.success(
          trans.provider.sync_success.replaceAll('{name}', provider.name),
        );
      } else {
        ModernToast.error(
          trans.provider.sync_failed
              .replaceAll('{name}', provider.name)
              .replaceAll(
                '{error}',
                syncResult.errorMessage ?? trans.provider.unknown_error,
              ),
        );
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final trans = context.translate;

    return ModernDialog(
      title: trans.provider.title,
      titleIcon: Icons.cloud_sync,
      maxWidth: 720,
      maxHeightRatio: 0.8,
      searchController: _searchController,
      searchHint: '搜索提供者名称或路径',
      onSearchChanged: (value) {
        setState(() {
          _searchQuery = value;
        });
      },
      content: _buildContent(),
      actionsLeftButtons: [
        DialogActionButton(
          label: trans.provider.sync_all,
          icon: Icons.sync,
          onPressed: _isSyncingAll ? null : _syncAll,
          isLoading: _isSyncingAll,
        ),
      ],
      actionsRight: [
        DialogActionButton(
          label: trans.common.close,
          onPressed: () => Navigator.of(context).pop(),
        ),
      ],
      onClose: () => Navigator.of(context).pop(),
    );
  }

  Widget _buildContent() {
    final trans = context.translate;
    if (_isLoading) {
      return Center(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            const CircularProgressIndicator(),
            const SizedBox(height: 16),
            Text(trans.provider.loading),
          ],
        ),
      );
    }

    if (_errorMessage != null) {
      return Center(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(Icons.error_outline, size: 64, color: Colors.red[400]),
            const SizedBox(height: 16),
            Text(
              _errorMessage!,
              style: TextStyle(fontSize: 16, color: Colors.red[600]),
              textAlign: TextAlign.center,
            ),
            const SizedBox(height: 16),
            ElevatedButton.icon(
              onPressed: _loadProviders,
              icon: const Icon(Icons.refresh),
              label: Text(trans.provider.retry),
            ),
          ],
        ),
      );
    }

    if (_providers.isEmpty) {
      return Center(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            const Icon(Icons.cloud_off, size: 64, color: Colors.grey),
            const SizedBox(height: 16),
            Text(
              trans.provider.empty_title,
              style: const TextStyle(fontSize: 16, color: Colors.grey),
            ),
          ],
        ),
      );
    }

    // 按类型分组（使用过滤后的列表）
    final proxyProviders = _filteredProviders
        .where((p) => p.type == ProviderType.proxy)
        .toList();
    final ruleProviders = _filteredProviders
        .where((p) => p.type == ProviderType.rule)
        .toList();

    return ListView(
      padding: const EdgeInsets.all(24),
      children: [
        if (proxyProviders.isNotEmpty) ...[
          Text(
            trans.provider.proxy_providers,
            style: Theme.of(
              context,
            ).textTheme.titleMedium?.copyWith(fontWeight: FontWeight.w600),
          ),
          const SizedBox(height: 12),
          ...proxyProviders.map((provider) => _buildProviderItem(provider)),
          const SizedBox(height: 24),
        ],
        if (ruleProviders.isNotEmpty) ...[
          Text(
            trans.provider.rule_providers,
            style: Theme.of(
              context,
            ).textTheme.titleMedium?.copyWith(fontWeight: FontWeight.w600),
          ),
          const SizedBox(height: 12),
          ...ruleProviders.map((provider) => _buildProviderItem(provider)),
        ],
      ],
    );
  }

  Widget _buildProviderItem(Provider provider) {
    final trans = context.translate;
    final isDark = Theme.of(context).brightness == Brightness.dark;
    final dateFormat = DateFormat('yyyy-MM-dd HH:mm');

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
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          // 名称和类型
          Row(
            children: [
              Expanded(
                child: Text(
                  provider.name,
                  style: TextStyle(
                    fontSize: 14,
                    fontWeight: FontWeight.w600,
                    color: Theme.of(context).colorScheme.onSurface,
                  ),
                ),
              ),
              Container(
                padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 4),
                decoration: BoxDecoration(
                  color: provider.vehicleType == 'HTTP'
                      ? Colors.blue.withValues(alpha: 0.1)
                      : Colors.orange.withValues(alpha: 0.1),
                  borderRadius: BorderRadius.circular(6),
                ),
                child: Text(
                  provider.vehicleType,
                  style: TextStyle(
                    fontSize: 10,
                    fontWeight: FontWeight.w600,
                    color: provider.vehicleType == 'HTTP'
                        ? Colors.blue
                        : Colors.orange,
                  ),
                ),
              ),
            ],
          ),
          const SizedBox(height: 8),
          // 路径
          if (provider.path != null)
            Text(
              provider.path!,
              style: TextStyle(
                fontSize: 11,
                color: Theme.of(
                  context,
                ).colorScheme.onSurface.withValues(alpha: 0.6),
              ),
            ),
          const SizedBox(height: 8),
          // 统计信息
          Row(
            children: [
              Icon(
                Icons.layers,
                size: 12,
                color: Theme.of(
                  context,
                ).colorScheme.onSurface.withValues(alpha: 0.5),
              ),
              const SizedBox(width: 4),
              Text(
                '${provider.count} ${provider.type == ProviderType.proxy ? trans.provider.nodes : trans.provider.rules}',
                style: TextStyle(
                  fontSize: 11,
                  color: Theme.of(
                    context,
                  ).colorScheme.onSurface.withValues(alpha: 0.6),
                ),
              ),
              const SizedBox(width: 16),
              Icon(
                Icons.access_time,
                size: 12,
                color: Theme.of(
                  context,
                ).colorScheme.onSurface.withValues(alpha: 0.5),
              ),
              const SizedBox(width: 4),
              Text(
                dateFormat.format(provider.updateAt),
                style: TextStyle(
                  fontSize: 11,
                  color: Theme.of(
                    context,
                  ).colorScheme.onSurface.withValues(alpha: 0.6),
                ),
              ),
            ],
          ),
          // 流量信息
          if (provider.subscriptionInfo != null)
            SubscriptionInfoWidget(
              subscriptionInfo: provider.subscriptionInfo!,
            ),
          const SizedBox(height: 12),
          // 操作按钮
          Row(
            children: [
              // 上传按钮
              OutlinedButton.icon(
                onPressed: () => _handleUpload(provider),
                icon: const Icon(Icons.upload, size: 16),
                label: Text(trans.provider.upload),
                style: OutlinedButton.styleFrom(
                  padding: const EdgeInsets.symmetric(
                    horizontal: 12,
                    vertical: 12,
                  ),
                  minimumSize: Size.zero,
                  tapTargetSize: MaterialTapTargetSize.shrinkWrap,
                  textStyle: const TextStyle(fontSize: 12),
                  shape: RoundedRectangleBorder(
                    borderRadius: BorderRadius.circular(8),
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
              ),
              const SizedBox(width: 8),
              // 同步按钮（仅 HTTP 类型）
              if (provider.vehicleType == 'HTTP')
                provider.isUpdating
                    ? const SizedBox(
                        width: 24,
                        height: 24,
                        child: CircularProgressIndicator(strokeWidth: 2),
                      )
                    : OutlinedButton.icon(
                        onPressed: () => _handleSync(provider),
                        icon: const Icon(Icons.sync, size: 16),
                        label: Text(trans.provider.sync),
                        style: OutlinedButton.styleFrom(
                          padding: const EdgeInsets.symmetric(
                            horizontal: 12,
                            vertical: 12,
                          ),
                          minimumSize: Size.zero,
                          tapTargetSize: MaterialTapTargetSize.shrinkWrap,
                          textStyle: const TextStyle(fontSize: 12),
                          shape: RoundedRectangleBorder(
                            borderRadius: BorderRadius.circular(8),
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
                      ),
            ],
          ),
        ],
      ),
    );
  }

  // 执行同步操作
  Future<_SyncResult> _executeSyncOperation(Provider provider) async {
    try {
      final clashManager = ClashManager.instance;
      final apiClient = clashManager.apiClient;

      if (apiClient == null) {
        return _SyncResult(
          updatedProvider: provider.copyWith(isUpdating: false),
          isSuccessful: false,
          errorMessage: 'Clash 未运行',
        );
      }

      // 执行同步
      if (provider.type == ProviderType.proxy) {
        await apiClient.updateProvider(provider.name);
      } else {
        await apiClient.updateRuleProvider(provider.name);
      }

      await Future.delayed(_syncDelay);

      // 获取更新数据
      final updatedData = await (provider.type == ProviderType.proxy
          ? apiClient.getProvider(provider.name)
          : apiClient.getRuleProvider(provider.name));

      if (updatedData != null) {
        return _SyncResult(
          updatedProvider: Provider.fromClashApi(
            provider.name,
            updatedData,
            providerType: provider.type,
          ),
          isSuccessful: true,
        );
      } else {
        return _SyncResult(
          updatedProvider: provider.copyWith(isUpdating: false),
          isSuccessful: false,
          errorMessage: '获取更新数据失败',
        );
      }
    } catch (e) {
      Logger.error('同步提供者失败 ${provider.name}: $e');
      return _SyncResult(
        updatedProvider: provider.copyWith(isUpdating: false),
        isSuccessful: false,
        errorMessage: e.toString(),
      );
    }
  }
}

// 同步结果数据类
class _SyncResult {
  final Provider updatedProvider;
  final bool isSuccessful;
  final String? errorMessage;

  _SyncResult({
    required this.updatedProvider,
    required this.isSuccessful,
    this.errorMessage,
  });
}

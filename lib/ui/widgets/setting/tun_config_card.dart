// ignore_for_file: use_build_context_synchronously
import 'dart:async';
import 'dart:io';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:provider/provider.dart';
import 'package:stelliberty/ui/common/modern_feature_card.dart';
import 'package:stelliberty/ui/common/modern_dropdown_menu.dart';
import 'package:stelliberty/ui/common/modern_dropdown_button.dart';
import 'package:stelliberty/ui/common/modern_text_field.dart';
import 'package:stelliberty/ui/common/modern_switch.dart';
import 'package:stelliberty/ui/widgets/modern_toast.dart';
import 'package:stelliberty/clash/manager/manager.dart';
import 'package:stelliberty/clash/providers/clash_provider.dart';
import 'package:stelliberty/clash/providers/service_provider.dart';
import 'package:stelliberty/clash/state/service_states.dart';
import 'package:stelliberty/i18n/i18n.dart';
import 'package:stelliberty/services/log_print_service.dart';
import 'package:stelliberty/src/bindings/signals/signals.dart';
import 'package:rinf/rinf.dart';

// 虚拟网卡网络栈类型枚举
enum TunStack {
  mixed('mixed', 'Mixed'),
  gvisor('gvisor', 'gVisor'),
  system('system', 'System');

  const TunStack(this.value, this.displayName);

  final String value;
  final String displayName;

  static TunStack fromString(String value) {
    for (final stack in TunStack.values) {
      if (stack.value == value) return stack;
    }
    return TunStack.mixed;
  }
}

// 虚拟网卡模式配置卡片组件
class TunConfigCard extends StatefulWidget {
  const TunConfigCard({super.key});

  @override
  State<TunConfigCard> createState() => _TunConfigCardState();
}

class _TunConfigCardState extends State<TunConfigCard> {
  // 加载状态
  bool _isLoading = true;

  // 服务版本号状态
  String? _installedServiceVersion; // 已安装服务的版本号
  String? _bundledServiceVersion; // 应用内置服务的版本号

  // Stream 订阅（需要在 dispose 时取消）
  StreamSubscription<RustSignalPack<ServiceVersionResponse>>?
  _versionResponseSubscription;

  // 虚拟网卡模式配置
  TunStack _tunStack = TunStack.mixed;
  final TextEditingController _tunDeviceController = TextEditingController();
  bool _tunAutoRoute = true;
  bool _tunAutoDetectInterface = true;
  bool _tunStrictRoute = true;
  final TextEditingController _tunMtuController = TextEditingController();
  final TextEditingController _tunDnsHijackController = TextEditingController();
  bool _tunAutoRedirect = false;
  final TextEditingController _tunRouteExcludeAddressController =
      TextEditingController();
  bool _tunDisableIcmpForwarding = false;

  // 错误状态
  String? _tunMtuError;

  // 保存状态
  bool _isSaving = false;

  bool _isHoveringOnTunStackMenu = false;

  @override
  void initState() {
    super.initState();
    // 延迟加载配置，先显示骨架屏
    Future.delayed(Duration.zero, () {
      _loadConfig();
      // 检查服务版本号（仅在服务已安装时）
      final serviceProvider = context.read<ServiceProvider>();
      if (serviceProvider.serviceState.isServiceModeInstalled) {
        _checkServiceVersion();
      }
      // 100ms 后隐藏骨架屏
      Future.delayed(const Duration(milliseconds: 100), () {
        if (mounted) {
          setState(() => _isLoading = false);
        }
      });
    });

    // 监听服务版本号响应
    _versionResponseSubscription = ServiceVersionResponse.rustSignalStream
        .listen((signal) {
          if (mounted) {
            setState(() {
              _installedServiceVersion = signal.message.installedVersion;
              _bundledServiceVersion = signal.message.bundledVersion;
            });
          }
        });
  }

  @override
  void dispose() {
    _versionResponseSubscription?.cancel();
    _tunDeviceController.dispose();
    _tunMtuController.dispose();
    _tunDnsHijackController.dispose();
    _tunRouteExcludeAddressController.dispose();
    super.dispose();
  }

  // 加载虚拟网卡配置
  void _loadConfig() {
    if (!mounted) return;

    final configState = context.read<ClashProvider>().configState;

    setState(() {
      _tunStack = TunStack.fromString(configState.tunStack);
      _tunDeviceController.text = configState.tunDevice;
      _tunAutoRoute = configState.isTunAutoRouteEnabled;
      _tunAutoDetectInterface = configState.isTunAutoDetectInterfaceEnabled;
      _tunStrictRoute = configState.isTunStrictRouteEnabled;
      _tunMtuController.text = configState.tunMtu.toString();
      _tunDnsHijackController.text = configState.tunDnsHijack.join('，');
      _tunAutoRedirect = configState.isTunAutoRedirectEnabled;
      _tunRouteExcludeAddressController.text = configState
          .tunRouteExcludeAddress
          .join('，');
      _tunDisableIcmpForwarding = configState.isTunIcmpForwardingDisabled;
    });
  }

  // 检查服务版本号
  void _checkServiceVersion() {
    GetServiceVersion().sendSignalToRust();
  }

  // 更新服务
  Future<void> _updateService(ServiceProvider serviceProvider) async {
    final trans = context.translate;
    final clashProvider = context.read<ClashProvider>();

    // 记录当前状态
    final wasRunning = ClashManager.instance.isCoreRunning;
    final currentConfig = clashProvider.currentConfigPath;
    final overrides = ClashManager.instance.getOverrides();

    try {
      // 显示更新中提示
      if (mounted) {
        ModernToast.info(trans.tun_config.updating);
      }

      // 1. 停止核心（如果正在运行）
      if (wasRunning) {
        Logger.info('更新服务前停止核心');
        await ClashManager.instance.stopCore();
      }

      // 2. 调用 install 命令（Rust 端会自动检测并原地更新，只需 1 次 UAC）
      Logger.info('开始更新服务（原地更新）');
      final installSuccess = await serviceProvider.installService();
      if (!installSuccess) {
        throw Exception(serviceProvider.lastOperationError ?? '更新服务失败');
      }

      // 3. 恢复核心运行状态
      if (wasRunning && currentConfig != null) {
        Logger.info('恢复核心运行状态');
        await ClashManager.instance.startCore(
          configPath: currentConfig,
          overrides: overrides,
        );
      }

      // 4. 刷新版本号
      _checkServiceVersion();

      if (mounted) {
        ModernToast.success(trans.tun_config.update_success);
      }
    } catch (e) {
      Logger.error('更新服务失败：$e');

      // 尝试恢复核心
      if (wasRunning && currentConfig != null) {
        try {
          await ClashManager.instance.startCore(
            configPath: currentConfig,
            overrides: overrides,
          );
        } catch (e2) {
          Logger.error('恢复核心失败：$e2');
        }
      }

      if (mounted) {
        ModernToast.error(
          trans.tun_config.update_failed.replaceAll('{error}', e.toString()),
        );
      }
    } finally {
      if (mounted) {
        serviceProvider.clearLastOperationResult();
      }
    }
  }

  // 验证 MTU 值
  String? _validateMtu(String value) {
    final trans = context.translate;
    if (value.isEmpty) {
      return trans.tun_config.mtu_error;
    }

    final mtu = int.tryParse(value);
    if (mtu == null) {
      return trans.tun_config.mtu_invalid;
    }

    if (mtu < 1280 || mtu > 9000) {
      return trans.tun_config.mtu_range;
    }

    return null;
  }

  // 统一保存文本输入配置
  Future<void> _saveConfig() async {
    final trans = context.translate;
    if (_isSaving) return;

    // 验证 MTU 值
    final mtuError = _validateMtu(_tunMtuController.text);
    if (mtuError != null) {
      setState(() => _tunMtuError = mtuError);
      return;
    }
    setState(() => _tunMtuError = null);

    setState(() => _isSaving = true);

    try {
      // 保存设备名称
      final deviceName = _tunDeviceController.text.trim();
      if (deviceName.isNotEmpty) {
        ClashManager.instance.setTunDevice(deviceName);
      }

      // 保存 MTU
      final mtu = int.parse(_tunMtuController.text);
      ClashManager.instance.setTunMtu(mtu);

      // 保存 DNS 劫持列表
      final hijacks = _tunDnsHijackController.text
          .split('，')
          .map((s) => s.trim())
          .where((s) => s.isNotEmpty)
          .toList();
      ClashManager.instance.setTunDnsHijack(hijacks);

      // 保存排除路由地址列表
      final addresses = _tunRouteExcludeAddressController.text
          .split('，')
          .map((s) => s.trim())
          .where((s) => s.isNotEmpty)
          .toList();
      ClashManager.instance.setTunRouteExcludeAddress(addresses);

      if (mounted) {
        ModernToast.success(trans.tun_config.save_success);
      }
    } catch (e) {
      Logger.error('保存 TUN 配置失败: $e');
      if (mounted) {
        ModernToast.error(
          trans.tun_config.save_failed.replaceAll('{error}', e.toString()),
        );
      }
    } finally {
      if (mounted) {
        setState(() => _isSaving = false);
      }
    }
  }

  // 骨架屏占位 UI
  Widget _buildSkeleton(ThemeData theme) {
    final skeletonColor = theme.colorScheme.surfaceContainerHighest.withAlpha(
      100,
    );

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        // 服务模式安装
        Container(
          height: 72,
          decoration: BoxDecoration(
            color: skeletonColor,
            borderRadius: BorderRadius.circular(8),
          ),
        ),
        const SizedBox(height: 16),
        // 网络栈 + 设备名称 + MTU
        Container(
          height: 140,
          decoration: BoxDecoration(
            color: skeletonColor,
            borderRadius: BorderRadius.circular(8),
          ),
        ),
        const SizedBox(height: 16),
        // 开关选项（自动路由、自动检测、严格路由）
        Container(
          height: 120,
          decoration: BoxDecoration(
            color: skeletonColor,
            borderRadius: BorderRadius.circular(8),
          ),
        ),
        const SizedBox(height: 16),
        // DNS 劫持 + 排除路由 + 禁用 ICMP
        Container(
          height: 200,
          decoration: BoxDecoration(
            color: skeletonColor,
            borderRadius: BorderRadius.circular(8),
          ),
        ),
      ],
    );
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final trans = context.translate;

    if (Platform.isAndroid) {
      return const SizedBox.shrink();
    }

    return ModernFeatureCard(
      isSelected: false,
      onTap: () {},
      isHoverEnabled: false,
      isTapEnabled: false,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          // 标题行
          Row(
            children: [
              const Icon(Icons.vpn_lock),
              const SizedBox(
                width: ModernFeatureCardSpacing.featureIconToTextSpacing,
              ),
              Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    trans.clash_features.tun_mode.title,
                    style: theme.textTheme.titleMedium,
                  ),
                  Text(
                    trans.clash_features.tun_mode.subtitle,
                    style: theme.textTheme.bodySmall,
                  ),
                ],
              ),
            ],
          ),

          const SizedBox(height: 16),
          const Divider(height: 1),
          const SizedBox(height: 16),

          // 加载中显示骨架屏
          if (_isLoading)
            _buildSkeleton(theme)
          else
            ..._buildRealContent(context, theme),
        ],
      ),
    );
  }

  // 真实内容
  List<Widget> _buildRealContent(BuildContext context, ThemeData theme) {
    final trans = context.translate;
    return [
      // ========== 服务模式安装（第一行） ==========
      Consumer<ServiceProvider>(
        builder: (context, serviceProvider, _) {
          final isServiceModeInstalled =
              serviceProvider.serviceState.isServiceModeInstalled;
          final isServiceModeProcessing =
              serviceProvider.serviceState.isServiceModeProcessing;

          // 检查是否有可用更新
          final hasUpdate =
              _installedServiceVersion != null &&
              _bundledServiceVersion != null &&
              _installedServiceVersion != _bundledServiceVersion;

          return Row(
            mainAxisAlignment: MainAxisAlignment.spaceBetween,
            children: [
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      trans.tun_config.service_mode,
                      style: theme.textTheme.titleSmall,
                    ),
                    const SizedBox(height: 4),
                    Text(
                      isServiceModeInstalled
                          ? trans.tun_config.service_installed
                          : trans.tun_config.service_not_installed,
                      style: theme.textTheme.bodySmall?.copyWith(
                        color: theme.colorScheme.outline,
                      ),
                    ),
                  ],
                ),
              ),
              Row(
                mainAxisSize: MainAxisSize.min,
                children: [
                  if (isServiceModeInstalled) ...[
                    IconButton(
                      icon: const Icon(Icons.refresh, size: 20),
                      tooltip: hasUpdate
                          ? trans.tun_config.update_available
                          : trans.tun_config.up_to_date,
                      onPressed: hasUpdate && !isServiceModeProcessing
                          ? () => _updateService(serviceProvider)
                          : null,
                      color: hasUpdate
                          ? theme.colorScheme.primary
                          : theme.colorScheme.outline.withAlpha(100),
                    ),
                    const SizedBox(width: 8),
                  ],
                  ModernSwitch(
                    value: isServiceModeInstalled,
                    onChanged: isServiceModeProcessing
                        ? null
                        : (value) async {
                            if (value) {
                              // 安装服务
                              final success = await serviceProvider
                                  .installService();
                              if (mounted) {
                                if (success) {
                                  ModernToast.success(
                                    trans.tun_config.service_install_success,
                                  );
                                  _checkServiceVersion(); // 安装后重新检查版本
                                } else {
                                  final errorMsg =
                                      serviceProvider.lastOperationError ??
                                      trans.tun_config.service_install_failed;
                                  ModernToast.error(errorMsg);
                                }
                                serviceProvider.clearLastOperationResult();
                              }
                            } else {
                              // 卸载服务
                              final success = await serviceProvider
                                  .uninstallService();
                              if (mounted) {
                                if (success) {
                                  ModernToast.success(
                                    trans.tun_config.service_uninstall_success,
                                  );
                                  _checkServiceVersion(); // 卸载后重新检查版本
                                } else {
                                  final errorMsg =
                                      serviceProvider.lastOperationError ??
                                      trans.tun_config.service_uninstall_failed;
                                  ModernToast.error(errorMsg);
                                }
                                serviceProvider.clearLastOperationResult();
                              }
                            }
                          },
                  ),
                ],
              ),
            ],
          );
        },
      ),

      const SizedBox(height: 16),
      Divider(color: Colors.grey.withValues(alpha: 0.2)),
      const SizedBox(height: 16),

      // 网络栈选择
      Row(
        mainAxisAlignment: MainAxisAlignment.spaceBetween,
        children: [
          Text(
            trans.clash_features.tun_mode.network_stack,
            style: Theme.of(context).textTheme.titleSmall,
          ),
          MouseRegion(
            onEnter: (_) => setState(() => _isHoveringOnTunStackMenu = true),
            onExit: (_) => setState(() => _isHoveringOnTunStackMenu = false),
            child: ModernDropdownMenu<TunStack>(
              items: TunStack.values,
              selectedItem: _tunStack,
              onSelected: (stack) {
                setState(() => _tunStack = stack);
                ClashManager.instance.setTunStack(stack.value);
              },
              itemToString: (stack) => stack.displayName,
              child: CustomDropdownButton(
                text: _tunStack.displayName,
                isHovering: _isHoveringOnTunStackMenu,
              ),
            ),
          ),
        ],
      ),

      const SizedBox(height: 16),

      // 虚拟网卡名称
      Row(
        children: [
          Text(
            trans.clash_features.tun_mode.device_name,
            style: Theme.of(context).textTheme.titleSmall,
          ),
          const Spacer(),
          SizedBox(
            width: 150,
            child: ModernTextField(
              controller: _tunDeviceController,
              hintText: 'Mihomo',
              height: 36,
            ),
          ),
        ],
      ),

      const SizedBox(height: 16),

      // MTU 值
      Row(
        children: [
          Text(
            trans.clash_features.tun_mode.mtu,
            style: Theme.of(context).textTheme.titleSmall,
          ),
          const Spacer(),
          SizedBox(
            width: 100,
            child: ModernTextField(
              controller: _tunMtuController,
              keyboardType: TextInputType.number,
              hintText: '1500',
              height: 36,
              errorText: _tunMtuError,
              inputFormatters: [
                FilteringTextInputFormatter.digitsOnly,
                LengthLimitingTextInputFormatter(5),
              ],
            ),
          ),
        ],
      ),

      const SizedBox(height: 16),

      // 自动路由
      Row(
        mainAxisAlignment: MainAxisAlignment.spaceBetween,
        children: [
          Text(
            trans.clash_features.tun_mode.auto_route,
            style: Theme.of(context).textTheme.titleSmall,
          ),
          ModernSwitch(
            value: _tunAutoRoute,
            onChanged: (value) {
              setState(() => _tunAutoRoute = value);
              ClashManager.instance.setTunAutoRoute(value);
            },
          ),
        ],
      ),

      const SizedBox(height: 12),

      // 自动检测接口
      Row(
        mainAxisAlignment: MainAxisAlignment.spaceBetween,
        children: [
          Text(
            trans.clash_features.tun_mode.auto_detect_interface,
            style: Theme.of(context).textTheme.titleSmall,
          ),
          ModernSwitch(
            value: _tunAutoDetectInterface,
            onChanged: (value) {
              setState(() => _tunAutoDetectInterface = value);
              ClashManager.instance.setTunAutoDetectInterface(value);
            },
          ),
        ],
      ),

      const SizedBox(height: 12),

      // 严格路由
      Row(
        mainAxisAlignment: MainAxisAlignment.spaceBetween,
        children: [
          Text(
            trans.clash_features.tun_mode.strict_route,
            style: Theme.of(context).textTheme.titleSmall,
          ),
          ModernSwitch(
            value: _tunStrictRoute,
            onChanged: (value) {
              setState(() => _tunStrictRoute = value);
              ClashManager.instance.setTunStrictRoute(value);
            },
          ),
        ],
      ),

      const SizedBox(height: 16),

      // DNS 劫持
      Row(
        children: [
          Text(
            trans.clash_features.tun_mode.dns_hijack,
            style: Theme.of(context).textTheme.titleSmall,
          ),
          const Spacer(),
          SizedBox(
            width: 200,
            child: ModernTextField(
              controller: _tunDnsHijackController,
              hintText: 'any:53, tcp://any:53',
              height: 36,
            ),
          ),
        ],
      ),

      const SizedBox(height: 16),

      // 自动重定向 (Linux only)
      if (Platform.isLinux) ...[
        Row(
          mainAxisAlignment: MainAxisAlignment.spaceBetween,
          children: [
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    trans.clash_features.tun_mode.auto_redirect,
                    style: Theme.of(context).textTheme.titleSmall,
                  ),
                  const SizedBox(height: 4),
                  Text(
                    trans.clash_features.tun_mode.auto_redirect_desc,
                    style: Theme.of(
                      context,
                    ).textTheme.bodySmall?.copyWith(color: Colors.grey),
                  ),
                ],
              ),
            ),
            const SizedBox(width: 16),
            ModernSwitch(
              value: _tunAutoRedirect,
              onChanged: (value) {
                setState(() => _tunAutoRedirect = value);
                ClashManager.instance.setTunAutoRedirect(value);
              },
            ),
          ],
        ),
        const SizedBox(height: 16),
      ],

      // 排除路由地址
      Row(
        children: [
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  trans.clash_features.tun_mode.route_exclude_address,
                  style: Theme.of(context).textTheme.titleSmall,
                ),
                const SizedBox(height: 4),
                Text(
                  context
                      .translate
                      .clash_features
                      .tun_mode
                      .route_exclude_address_desc,
                  style: Theme.of(
                    context,
                  ).textTheme.bodySmall?.copyWith(color: Colors.grey),
                ),
              ],
            ),
          ),
          const SizedBox(width: 16),
          SizedBox(
            width: 200,
            child: ModernTextField(
              controller: _tunRouteExcludeAddressController,
              hintText: '172.20.0.0/16',
              height: 36,
            ),
          ),
        ],
      ),

      const SizedBox(height: 16),

      // ICMP 转发（正向表述，开关打开=启用，关闭=禁用）
      Row(
        mainAxisAlignment: MainAxisAlignment.spaceBetween,
        children: [
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  trans.clash_features.tun_mode.icmp_forwarding,
                  style: Theme.of(context).textTheme.titleSmall,
                ),
                const SizedBox(height: 4),
                Text(
                  trans.clash_features.tun_mode.icmp_forwarding_desc,
                  style: Theme.of(
                    context,
                  ).textTheme.bodySmall?.copyWith(color: Colors.grey),
                ),
              ],
            ),
          ),
          const SizedBox(width: 16),
          ModernSwitch(
            // 反转显示：内部存储的是 disable，UI 显示的是 enable
            value: !_tunDisableIcmpForwarding,
            onChanged: (value) {
              // 反转存储：UI 的 enable 转换为内部的 disable
              setState(() => _tunDisableIcmpForwarding = !value);
              ClashManager.instance.setTunDisableIcmpForwarding(!value);
            },
          ),
        ],
      ),

      const SizedBox(height: 16),

      // 保存按钮
      Row(
        mainAxisAlignment: MainAxisAlignment.end,
        children: [
          FilledButton.icon(
            onPressed: _isSaving ? null : _saveConfig,
            icon: _isSaving
                ? const SizedBox(
                    width: 18,
                    height: 18,
                    child: CircularProgressIndicator(strokeWidth: 2),
                  )
                : const Icon(Icons.save, size: 18),
            label: Text(
              _isSaving ? trans.tun_config.saving : trans.common.save,
            ),
          ),
        ],
      ),
    ];
  }
}

import 'package:flutter/material.dart';
import 'package:stelliberty/i18n/i18n.dart';
import 'package:stelliberty/clash/data/dns_config_model.dart';
import 'package:stelliberty/clash/services/dns_service.dart';
import 'package:stelliberty/clash/storage/preferences.dart';
import 'package:stelliberty/clash/manager/manager.dart';
import 'package:stelliberty/utils/logger.dart';
import 'package:stelliberty/ui/common/modern_feature_card.dart';
import 'package:stelliberty/ui/common/modern_dropdown_menu.dart';
import 'package:stelliberty/ui/common/modern_dropdown_button.dart';
import 'package:stelliberty/ui/common/modern_text_field.dart';
import 'package:stelliberty/ui/common/modern_switch.dart';

// DNS 配置卡片组件
class DnsConfigCard extends StatefulWidget {
  const DnsConfigCard({super.key});

  @override
  State<DnsConfigCard> createState() => _DnsConfigCardState();
}

class _DnsConfigCardState extends State<DnsConfigCard> {
  // 是否启用 DNS 覆写（固定展开，不需要 _isExpanded）
  bool _enableDns = false;

  // 基础配置状态
  String _enhancedMode = 'fake-ip'; // fake-ip 或 redir-host
  String _fakeIpFilterMode = 'blacklist'; // blacklist 或 whitelist
  bool _ipv6 = true;

  // 高级配置状态
  bool _preferH3 = false;
  bool _respectRules = false;
  bool _useHosts = false;
  bool _useSystemHosts = false;
  bool _directNameserverFollowPolicy = false;

  // Fallback 过滤器状态
  bool _fallbackGeoip = true;

  // 表单控制器 - 使用 late 延迟初始化
  late TextEditingController _nameserverPolicyController;
  late TextEditingController _hostsController;
  late TextEditingController _nameserverController;
  late TextEditingController _defaultNameserverController;
  late TextEditingController _fallbackController;
  late TextEditingController _listenController;
  late TextEditingController _fakeIpRangeController;
  late TextEditingController _proxyServerNameserverController;
  late TextEditingController _directNameserverController;
  late TextEditingController _fakeIpFilterController;
  late TextEditingController _fallbackGeoipCodeController;
  late TextEditingController _fallbackIpcidrController;
  late TextEditingController _fallbackDomainController;

  // 当前配置
  DnsConfig? _currentConfig;

  // 加载状态
  bool _isLoading = true;

  // 悬停状态
  bool _isHoveringOnEnhancedModeMenu = false;
  bool _isHoveringOnFakeIpFilterModeMenu = false;

  @override
  void initState() {
    super.initState();
    // 加载启用状态
    _enableDns = ClashPreferences.instance.getDnsOverrideEnabled();
    // 初始化控制器
    _initializeControllers();
    // 加载配置
    _loadConfig().then((_) {
      if (mounted) {
        setState(() {
          _isLoading = false;
        });
      }
    });
  }

  // 初始化所有 TextEditingController
  void _initializeControllers() {
    _nameserverPolicyController = TextEditingController();
    _hostsController = TextEditingController();
    _nameserverController = TextEditingController();
    _defaultNameserverController = TextEditingController();
    _fallbackController = TextEditingController();
    _listenController = TextEditingController();
    _fakeIpRangeController = TextEditingController();
    _proxyServerNameserverController = TextEditingController();
    _directNameserverController = TextEditingController();
    _fakeIpFilterController = TextEditingController();
    _fallbackGeoipCodeController = TextEditingController();
    _fallbackIpcidrController = TextEditingController();
    _fallbackDomainController = TextEditingController();
  }

  @override
  void dispose() {
    _nameserverPolicyController.dispose();
    _hostsController.dispose();
    _nameserverController.dispose();
    _defaultNameserverController.dispose();
    _fallbackController.dispose();
    _listenController.dispose();
    _fakeIpRangeController.dispose();
    _proxyServerNameserverController.dispose();
    _directNameserverController.dispose();
    _fakeIpFilterController.dispose();
    _fallbackGeoipCodeController.dispose();
    _fallbackIpcidrController.dispose();
    _fallbackDomainController.dispose();
    super.dispose();
  }

  // 加载配置
  Future<void> _loadConfig() async {
    try {
      final config = await DnsService.instance.loadDnsConfig();

      if (config != null && mounted) {
        setState(() {
          _currentConfig = config;
          // 注意：不要重新加载 _enableDns，保持用户当前设置的值
          // _enableDns 只在 initState 中初始化一次，后续由用户交互控制

          // 加载基础配置
          _enhancedMode = config.enhancedMode;
          _fakeIpFilterMode = config.fakeIpFilterMode;
          _ipv6 = config.ipv6;

          // 加载高级配置
          _preferH3 = config.preferH3;
          _respectRules = config.respectRules;
          _useHosts = config.useHosts;
          _useSystemHosts = config.useSystemHosts;
          _directNameserverFollowPolicy = config.directNameserverFollowPolicy;

          // 加载 Fallback 过滤器配置
          _fallbackGeoip = config.fallbackGeoip;
          _fallbackGeoipCodeController.text = config.fallbackGeoipCode;
          _fallbackIpcidrController.text = config.fallbackIpcidr.join('，');
          _fallbackDomainController.text = config.fallbackDomain.join('，');

          // 填充表单控制器
          _listenController.text = config.listen;
          _fakeIpRangeController.text = config.fakeIpRange;
          _nameserverPolicyController.text = DnsConfig.formatNameserverPolicy(
            config.nameserverPolicy,
          );
          _hostsController.text = DnsConfig.formatHosts(config.hosts);
          _nameserverController.text = config.nameserver.join('，');
          _defaultNameserverController.text = config.defaultNameserver.join(
            '，',
          );
          _fallbackController.text = config.fallback.join('，');
          _proxyServerNameserverController.text = config.proxyServerNameserver
              .join('，');
          _directNameserverController.text = config.directNameserver.join('，');
          _fakeIpFilterController.text = config.fakeIpFilter.join('，');
        });
      }
    } catch (e, stackTrace) {
      Logger.error('DNS 配置卡片: 加载配置失败 - $e\n堆栈跟踪: $stackTrace');
    }
  }

  // 保存配置
  Future<void> _saveConfig() async {
    try {
      // 解析表单数据
      final nameserverPolicy = DnsConfig.parseNameserverPolicy(
        _nameserverPolicyController.text,
      );
      final hosts = DnsConfig.parseHosts(_hostsController.text);
      final nameserver = _parseList(_nameserverController.text);
      final defaultNameserver = _parseList(_defaultNameserverController.text);
      final fallback = _parseList(_fallbackController.text);
      final proxyServerNameserver = _parseList(
        _proxyServerNameserverController.text,
      );
      final directNameserver = _parseList(_directNameserverController.text);
      final fakeIpFilter = _parseList(_fakeIpFilterController.text);
      final fallbackIpcidr = _parseList(_fallbackIpcidrController.text);
      final fallbackDomain = _parseList(_fallbackDomainController.text);

      // 获取默认配置
      final defaultConfig = DnsConfig.defaultConfig();

      // 创建新配置
      // 当用户字段为空时，使用默认值而非 null，确保配置完整性
      final config = (_currentConfig ?? defaultConfig).copyWith(
        enable: _enableDns,
        listen: _listenController.text.trim().isNotEmpty
            ? _listenController.text.trim()
            : ':53',
        enhancedMode: _enhancedMode,
        fakeIpRange: _fakeIpRangeController.text.trim().isNotEmpty
            ? _fakeIpRangeController.text.trim()
            : '198.18.0.1/16',
        fakeIpFilterMode: _fakeIpFilterMode,
        ipv6: _ipv6,
        preferH3: _preferH3,
        respectRules: _respectRules,
        useHosts: _useHosts,
        useSystemHosts: _useSystemHosts,
        directNameserverFollowPolicy: _directNameserverFollowPolicy,
        nameserverPolicy: nameserverPolicy,
        hosts: hosts,
        // 当用户清空字段时，使用默认值
        nameserver: nameserver.isNotEmpty
            ? nameserver
            : defaultConfig.nameserver,
        defaultNameserver: defaultNameserver.isNotEmpty
            ? defaultNameserver
            : defaultConfig.defaultNameserver,
        fallback: fallback, // fallback 默认就是空的
        proxyServerNameserver: proxyServerNameserver.isNotEmpty
            ? proxyServerNameserver
            : defaultConfig.proxyServerNameserver,
        directNameserver: directNameserver, // direct-nameserver 默认就是空的
        fakeIpFilter: fakeIpFilter.isNotEmpty
            ? fakeIpFilter
            : defaultConfig.fakeIpFilter,
        fallbackGeoip: _fallbackGeoip,
        fallbackGeoipCode: _fallbackGeoipCodeController.text.trim().isNotEmpty
            ? _fallbackGeoipCodeController.text.trim()
            : 'CN',
        fallbackIpcidr: fallbackIpcidr.isNotEmpty
            ? fallbackIpcidr
            : defaultConfig.fallbackIpcidr,
        fallbackDomain: fallbackDomain.isNotEmpty
            ? fallbackDomain
            : defaultConfig.fallbackDomain,
      );

      // 保存配置
      await DnsService.instance.saveDnsConfig(config);

      // 更新当前配置缓存
      _currentConfig = config;

      // 保存 DNS 启用状态到 Preferences
      await ClashPreferences.instance.setDnsOverrideEnabled(_enableDns);

      // 如果 Clash 正在运行，热重载配置文件
      if (ClashManager.instance.isCoreRunning) {
        final currentConfigPath = ClashManager.instance.currentConfigPath;
        if (currentConfigPath != null) {
          await ClashManager.instance.reloadConfig(
            configPath: currentConfigPath,
            overrides: ClashManager.instance.getOverrides(),
          );
        } else {
          Logger.warning('当前配置路径为空，无法热重载 DNS 配置');
        }
      }
    } catch (e, stackTrace) {
      Logger.error('DNS 配置卡片: 保存 DNS 配置失败 - $e\n堆栈跟踪: $stackTrace');
    }
  }

  // 重置为默认配置
  Future<void> _resetToDefault() async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: Text(context.translate.dnsSettings.reset),
        content: Text(context.translate.dnsSettings.resetConfirm),
        actions: [
          TextButton(
            onPressed: () => Navigator.of(context).pop(false),
            child: Text(context.translate.dnsSettings.cancel),
          ),
          TextButton(
            onPressed: () => Navigator.of(context).pop(true),
            child: Text(context.translate.dnsSettings.reset),
          ),
        ],
      ),
    );

    if (confirmed == true && mounted) {
      final defaultConfig = DnsConfig.defaultConfig();
      setState(() {
        _currentConfig = defaultConfig;

        // 重置基础配置
        _enhancedMode = defaultConfig.enhancedMode;
        _fakeIpFilterMode = defaultConfig.fakeIpFilterMode;
        _ipv6 = defaultConfig.ipv6;

        // 重置高级配置
        _preferH3 = defaultConfig.preferH3;
        _respectRules = defaultConfig.respectRules;
        _useHosts = defaultConfig.useHosts;
        _useSystemHosts = defaultConfig.useSystemHosts;
        _directNameserverFollowPolicy =
            defaultConfig.directNameserverFollowPolicy;

        // 重置 Fallback 过滤器配置
        _fallbackGeoip = defaultConfig.fallbackGeoip;
        _fallbackGeoipCodeController.text = defaultConfig.fallbackGeoipCode;
        _fallbackIpcidrController.text = defaultConfig.fallbackIpcidr.join('，');
        _fallbackDomainController.text = defaultConfig.fallbackDomain.join('，');

        // 重置表单控制器
        _listenController.text = defaultConfig.listen;
        _fakeIpRangeController.text = defaultConfig.fakeIpRange;
        _nameserverPolicyController.text = '';
        _hostsController.text = '';
        _nameserverController.text = defaultConfig.nameserver.join('，');
        _defaultNameserverController.text = defaultConfig.defaultNameserver
            .join('，');
        _fallbackController.text = defaultConfig.fallback.join('，');
        _proxyServerNameserverController.text = defaultConfig
            .proxyServerNameserver
            .join('，');
        _directNameserverController.text = defaultConfig.directNameserver.join(
          '，',
        );
        _fakeIpFilterController.text = defaultConfig.fakeIpFilter.join('，');
      });
    }
  }

  // 解析逗号分隔的列表
  List<String> _parseList(String input) {
    if (input.trim().isEmpty) return [];
    return input
        .split('，')
        .map((s) => s.trim())
        .where((s) => s.isNotEmpty)
        .toList();
  }

  // 骨架屏占位 UI
  Widget _buildSkeleton(ThemeData theme) {
    final skeletonColor = theme.colorScheme.surfaceContainerHighest.withAlpha(
      100,
    );

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        // 基础配置骨架
        Container(
          height: 200,
          decoration: BoxDecoration(
            color: skeletonColor,
            borderRadius: BorderRadius.circular(8),
          ),
        ),
        const SizedBox(height: 24),
        // 高级配置骨架
        Container(
          height: 180,
          decoration: BoxDecoration(
            color: skeletonColor,
            borderRadius: BorderRadius.circular(8),
          ),
        ),
        const SizedBox(height: 24),
        // DNS 服务器配置骨架
        Container(
          height: 300,
          decoration: BoxDecoration(
            color: skeletonColor,
            borderRadius: BorderRadius.circular(8),
          ),
        ),
        const SizedBox(height: 24),
        // Fallback 过滤器配置骨架
        Container(
          height: 150,
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
    return ModernFeatureCard(
      isSelected: false,
      onTap: () {},
      enableHover: false,
      enableTap: false,
      child: Padding(
        padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 20),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            // 标题行（包含 DNS 开关）
            Row(
              mainAxisAlignment: MainAxisAlignment.spaceBetween,
              children: [
                Row(
                  children: [
                    const Icon(Icons.dns_outlined),
                    const SizedBox(width: 12),
                    Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          context.translate.dnsSettings.title,
                          style: Theme.of(context).textTheme.titleMedium,
                        ),
                        Text(
                          context.translate.dnsSettings.description,
                          style: Theme.of(context).textTheme.bodySmall,
                        ),
                      ],
                    ),
                  ],
                ),
                // DNS 开关直接显示在标题行
                ModernSwitch(
                  value: _enableDns,
                  onChanged: (value) {
                    setState(() => _enableDns = value);
                    _saveConfig();
                  },
                ),
              ],
            ),

            // 配置内容（固定展开）
            const SizedBox(height: 16),
            const Divider(height: 1),
            const SizedBox(height: 16),

            // 如果正在加载，显示骨架屏
            if (_isLoading)
              _buildSkeleton(Theme.of(context))
            else
              ..._buildConfigContent(),

            const SizedBox(height: 16),
            const Divider(height: 1),
            const SizedBox(height: 16),

            // 操作按钮
            Row(
              children: [
                TextButton.icon(
                  icon: const Icon(Icons.restart_alt, size: 18),
                  label: Text(context.translate.dnsSettings.reset),
                  onPressed: _resetToDefault,
                ),
                const Spacer(),
                ElevatedButton.icon(
                  icon: const Icon(Icons.save, size: 18),
                  label: Text(context.translate.dnsSettings.save),
                  onPressed: _saveConfig,
                  style: ElevatedButton.styleFrom(
                    backgroundColor: Theme.of(context).colorScheme.primary,
                    foregroundColor: Theme.of(context).colorScheme.onPrimary,
                  ),
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }

  // 构建配置内容
  List<Widget> _buildConfigContent() {
    return [
      // 基础配置
      Text(
        context.translate.dnsSettings.basicConfig,
        style: Theme.of(context).textTheme.titleSmall?.copyWith(
          fontWeight: FontWeight.bold,
          color: Theme.of(context).colorScheme.primary,
        ),
      ),
      const SizedBox(height: 12),

      // DNS 监听地址
      _buildTextField(
        controller: _listenController,
        label: context.translate.dnsSettings.listen,
        hint: ':53',
      ),

      const SizedBox(height: 12),

      // 增强模式
      _buildDropdown<String>(
        label: context.translate.dnsSettings.enhancedMode,
        value: _enhancedMode,
        isHovering: _isHoveringOnEnhancedModeMenu,
        onHoverChanged: (hovering) =>
            setState(() => _isHoveringOnEnhancedModeMenu = hovering),
        items: ['normal', 'fake-ip', 'redir-host', 'hosts'],
        itemToString: (mode) {
          switch (mode) {
            case 'normal':
              return context.translate.dnsSettings.enhancedModeNormal;
            case 'fake-ip':
              return context.translate.dnsSettings.enhancedModeFakeIp;
            case 'redir-host':
              return context.translate.dnsSettings.enhancedModeRedirHost;
            case 'hosts':
              return context.translate.dnsSettings.enhancedModeHosts;
            default:
              return mode;
          }
        },
        onChanged: (value) {
          setState(() => _enhancedMode = value);
          _saveConfig(); // 立即保存
        },
      ),

      const SizedBox(height: 12),

      // Fake IP 范围
      _buildTextField(
        controller: _fakeIpRangeController,
        label: context.translate.dnsSettings.fakeIpRange,
        hint: '198.18.0.1/16',
      ),

      const SizedBox(height: 12),

      // Fake IP 过滤模式
      _buildDropdown<String>(
        label: context.translate.dnsSettings.fakeIpFilterMode,
        value: _fakeIpFilterMode,
        isHovering: _isHoveringOnFakeIpFilterModeMenu,
        onHoverChanged: (hovering) =>
            setState(() => _isHoveringOnFakeIpFilterModeMenu = hovering),
        items: ['blacklist', 'whitelist'],
        itemToString: (mode) => mode == 'blacklist'
            ? context.translate.dnsSettings.fakeIpFilterModeBlacklist
            : context.translate.dnsSettings.fakeIpFilterModeWhitelist,
        onChanged: (value) {
          setState(() => _fakeIpFilterMode = value);
          _saveConfig(); // 立即保存
        },
      ),

      const SizedBox(height: 12),

      // IPv6 开关
      _buildSwitch(
        label: context.translate.dnsSettings.ipv6Support,
        value: _ipv6,
        onChanged: (value) {
          setState(() => _ipv6 = value);
          _saveConfig(); // 立即保存
        },
      ),

      const SizedBox(height: 24),

      // 高级配置
      Text(
        context.translate.dnsSettings.advancedConfig,
        style: Theme.of(context).textTheme.titleSmall?.copyWith(
          fontWeight: FontWeight.bold,
          color: Theme.of(context).colorScheme.primary,
        ),
      ),
      const SizedBox(height: 12),

      _buildSwitch(
        label: context.translate.dnsSettings.preferH3,
        value: _preferH3,
        onChanged: (value) {
          setState(() => _preferH3 = value);
          _saveConfig(); // 立即保存
        },
      ),

      const SizedBox(height: 12),

      _buildSwitch(
        label: context.translate.dnsSettings.respectRules,
        value: _respectRules,
        onChanged: (value) {
          setState(() => _respectRules = value);
          _saveConfig(); // 立即保存
        },
      ),

      const SizedBox(height: 12),

      _buildSwitch(
        label: context.translate.dnsSettings.useHosts,
        value: _useHosts,
        onChanged: (value) {
          setState(() => _useHosts = value);
          _saveConfig(); // 立即保存
        },
      ),

      const SizedBox(height: 12),

      _buildSwitch(
        label: context.translate.dnsSettings.useSystemHosts,
        value: _useSystemHosts,
        onChanged: (value) {
          setState(() => _useSystemHosts = value);
          _saveConfig(); // 立即保存
        },
      ),

      const SizedBox(height: 12),

      _buildSwitch(
        label: context.translate.dnsSettings.directNameserverFollowPolicy,
        value: _directNameserverFollowPolicy,
        onChanged: (value) {
          setState(() => _directNameserverFollowPolicy = value);
          _saveConfig(); // 立即保存
        },
      ),

      const SizedBox(height: 24),

      // DNS 服务器配置
      Text(
        context.translate.dnsSettings.domainDnsOverride,
        style: Theme.of(context).textTheme.titleSmall?.copyWith(
          fontWeight: FontWeight.bold,
          color: Theme.of(context).colorScheme.primary,
        ),
      ),
      const SizedBox(height: 12),

      _buildMultilineTextField(
        controller: _nameserverPolicyController,
        label: context.translate.dnsSettings.nameserverPolicy,
        hint: context.translate.dnsSettings.nameserverPolicyPlaceholder,
        maxLines: 3,
      ),

      const SizedBox(height: 12),

      _buildMultilineTextField(
        controller: _hostsController,
        label: context.translate.dnsSettings.hosts,
        hint: context.translate.dnsSettings.hostsPlaceholder,
        maxLines: 3,
      ),

      const SizedBox(height: 12),

      _buildMultilineTextField(
        controller: _nameserverController,
        label: context.translate.dnsSettings.nameserver,
        hint: context.translate.dnsSettings.nameserverPlaceholder,
        maxLines: 2,
      ),

      const SizedBox(height: 12),

      _buildMultilineTextField(
        controller: _defaultNameserverController,
        label: context.translate.dnsSettings.defaultNameserver,
        hint: context.translate.dnsSettings.nameserverPlaceholder,
        maxLines: 2,
      ),

      const SizedBox(height: 12),

      _buildMultilineTextField(
        controller: _fallbackController,
        label: context.translate.dnsSettings.fallback,
        hint: context.translate.dnsSettings.nameserverPlaceholder,
        maxLines: 2,
      ),

      const SizedBox(height: 12),

      _buildMultilineTextField(
        controller: _proxyServerNameserverController,
        label: context.translate.dnsSettings.proxyServerNameserver,
        hint: 'https://doh.pub/dns-query, https://dns.alidns.com/dns-query',
        maxLines: 2,
      ),

      const SizedBox(height: 12),

      _buildMultilineTextField(
        controller: _directNameserverController,
        label: context.translate.dnsSettings.directNameserver,
        hint: 'system, 223.6.6.6',
        maxLines: 2,
      ),

      const SizedBox(height: 12),

      _buildMultilineTextField(
        controller: _fakeIpFilterController,
        label: context.translate.dnsSettings.fakeIpFilter,
        hint: '*.lan, *.local, localhost.ptlogin2.qq.com',
        maxLines: 3,
      ),

      const SizedBox(height: 24),

      // Fallback 过滤器配置
      Text(
        context.translate.dnsSettings.fallbackFilter,
        style: Theme.of(context).textTheme.titleSmall?.copyWith(
          fontWeight: FontWeight.bold,
          color: Theme.of(context).colorScheme.primary,
        ),
      ),
      const SizedBox(height: 12),

      _buildSwitch(
        label: context.translate.dnsSettings.fallbackGeoip,
        value: _fallbackGeoip,
        onChanged: (value) {
          setState(() => _fallbackGeoip = value);
          _saveConfig(); // 立即保存
        },
      ),

      const SizedBox(height: 12),

      _buildTextField(
        controller: _fallbackGeoipCodeController,
        label: context.translate.dnsSettings.fallbackGeoipCode,
        hint: 'CN',
      ),

      const SizedBox(height: 12),

      _buildMultilineTextField(
        controller: _fallbackIpcidrController,
        label: context.translate.dnsSettings.fallbackIpcidr,
        hint: '240.0.0.0/4, 0.0.0.0/32',
        maxLines: 2,
      ),

      const SizedBox(height: 12),

      _buildMultilineTextField(
        controller: _fallbackDomainController,
        label: context.translate.dnsSettings.fallbackDomain,
        hint: '+.google.com, +.facebook.com, +.youtube.com',
        maxLines: 2,
      ),
    ];
  }

  // 构建文本输入框
  Widget _buildTextField({
    required TextEditingController controller,
    required String label,
    required String hint,
  }) {
    return Row(
      children: [
        Expanded(
          flex: 2,
          child: Text(label, style: Theme.of(context).textTheme.titleSmall),
        ),
        const SizedBox(width: 16),
        Expanded(
          flex: 3,
          child: ModernTextField(
            controller: controller,
            hintText: hint,
            // TextField 不立即保存，等待用户点击保存按钮
          ),
        ),
      ],
    );
  }

  // 构建多行文本输入框
  Widget _buildMultilineTextField({
    required TextEditingController controller,
    required String label,
    required String hint,
    int maxLines = 1,
  }) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(label, style: Theme.of(context).textTheme.titleSmall),
        const SizedBox(height: 8),
        ModernTextField(
          controller: controller,
          maxLines: maxLines,
          hintText: hint,
          // TextField 不立即保存，等待用户点击保存按钮
        ),
      ],
    );
  }

  // 构建开关
  Widget _buildSwitch({
    required String label,
    required bool value,
    required ValueChanged<bool> onChanged,
  }) {
    return Row(
      mainAxisAlignment: MainAxisAlignment.spaceBetween,
      children: [
        Row(
          children: [
            const Icon(Icons.check_circle_outline, size: 20),
            const SizedBox(width: 12),
            Text(label, style: Theme.of(context).textTheme.titleSmall),
          ],
        ),
        ModernSwitch(value: value, onChanged: onChanged),
      ],
    );
  }

  // 构建下拉菜单
  Widget _buildDropdown<T>({
    required String label,
    required T value,
    required bool isHovering,
    required ValueChanged<bool> onHoverChanged,
    required List<T> items,
    required String Function(T) itemToString,
    required ValueChanged<T> onChanged,
  }) {
    return Row(
      mainAxisAlignment: MainAxisAlignment.spaceBetween,
      children: [
        Text(label, style: Theme.of(context).textTheme.titleSmall),
        const SizedBox(width: 16),
        MouseRegion(
          onEnter: (_) => onHoverChanged(true),
          onExit: (_) => onHoverChanged(false),
          child: ModernDropdownMenu<T>(
            items: items,
            selectedItem: value,
            onSelected: onChanged,
            itemToString: itemToString,
            child: CustomDropdownButton(
              text: itemToString(value),
              isHovering: isHovering,
            ),
          ),
        ),
      ],
    );
  }
}

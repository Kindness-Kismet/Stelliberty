// DNS 配置数据模型
//
// 该配置独立于订阅配置文件，在 Clash 启动时合并到运行时配置中
class DnsConfig {
  // ==================== 基础配置 ====================

  // 是否启用 DNS
  bool enable;

  // DNS 监听地址（如 :53）
  String listen;

  // 增强模式：fake-ip 或 redir-host
  String enhancedMode;

  // Fake IP 地址范围
  String fakeIpRange;

  // Fake IP 过滤模式：blacklist 或 whitelist
  String fakeIpFilterMode;

  // IPv6 支持
  bool ipv6;

  // ==================== 高级配置 ====================

  // DoH 使用 HTTP/3
  bool preferH3;

  // DNS 连接遵循路由规则
  bool respectRules;

  // 使用 hosts 文件
  bool useHosts;

  // 使用系统 hosts 文件
  bool useSystemHosts;

  // 直连 DNS 遵循 nameserver-policy
  bool directNameserverFollowPolicy;

  // ==================== DNS 服务器列表 ====================

  // 默认 DNS 服务器（用于解析其他 DNS 服务器）
  List<String> defaultNameserver;

  // 主 DNS 服务器列表
  List<String> nameserver;

  // 备用 DNS 服务器列表
  List<String> fallback;

  // 代理节点 DNS 服务器
  List<String> proxyServerNameserver;

  // 直连 DNS 服务器
  List<String> directNameserver;

  // Fake IP 过滤列表（跳过 fake-ip 解析的域名）
  List<String> fakeIpFilter;

  // ==================== Fallback 过滤器 ====================

  // 启用 GeoIP 过滤
  bool fallbackGeoip;

  // GeoIP 国家代码（如 CN）
  String fallbackGeoipCode;

  // IP CIDR 过滤列表
  List<String> fallbackIpcidr;

  // 使用 fallback 的域名列表
  List<String> fallbackDomain;

  // ==================== 核心功能 ====================

  // 域名特定 DNS 覆写规则
  //
  // 格式：域名 -> DNS 服务器列表
  // 示例：
  // {
  //   "*.google.com": ["8.8.8.8", "8.8.4.4"],
  //   "+.cn": ["223.5.5.5"],
  //   "rule-set:cn": ["https://doh.pub/dns-query"]
  // }
  Map<String, dynamic> nameserverPolicy;

  // Hosts 映射（域名 -> IP 地址或域名）
  //
  // 示例：
  // {
  //   "localhost": "127.0.0.1",
  //   "*.test.com": ["1.2.3.4", "5.6.7.8"]
  // }
  Map<String, dynamic> hosts;

  DnsConfig({
    // 基础配置
    this.enable = true,
    this.listen = ':53',
    this.enhancedMode = 'fake-ip',
    this.fakeIpRange = '198.18.0.1/16',
    this.fakeIpFilterMode = 'blacklist',
    this.ipv6 = true,

    // 高级配置
    this.preferH3 = false,
    this.respectRules = false,
    this.useHosts = false,
    this.useSystemHosts = false,
    this.directNameserverFollowPolicy = false,

    // DNS 服务器列表
    List<String>? defaultNameserver,
    List<String>? nameserver,
    List<String>? fallback,
    List<String>? proxyServerNameserver,
    List<String>? directNameserver,
    List<String>? fakeIpFilter,

    // Fallback 过滤器
    this.fallbackGeoip = true,
    this.fallbackGeoipCode = 'CN',
    List<String>? fallbackIpcidr,
    List<String>? fallbackDomain,

    // 核心功能
    Map<String, dynamic>? nameserverPolicy,
    Map<String, dynamic>? hosts,
  }) : defaultNameserver =
           defaultNameserver ??
           [
             'system',
             '223.6.6.6',
             '8.8.8.8',
             '2400:3200::1',
             '2001:4860:4860::8888',
           ],
       nameserver =
           nameserver ??
           [
             '8.8.8.8',
             'https://doh.pub/dns-query',
             'https://dns.alidns.com/dns-query',
           ],
       fallback = fallback ?? [],
       proxyServerNameserver =
           proxyServerNameserver ??
           [
             'https://doh.pub/dns-query',
             'https://dns.alidns.com/dns-query',
             'tls://223.5.5.5',
           ],
       directNameserver = directNameserver ?? [],
       fakeIpFilter =
           fakeIpFilter ??
           [
             '*.lan',
             '*.local',
             '*.arpa',
             'time.*.com',
             'ntp.*.com',
             '+.market.xiaomi.com',
             'localhost.ptlogin2.qq.com',
             '*.msftncsi.com',
             'www.msftconnecttest.com',
           ],
       fallbackIpcidr = fallbackIpcidr ?? ['240.0.0.0/4', '0.0.0.0/32'],
       fallbackDomain =
           fallbackDomain ??
           ['+.google.com', '+.facebook.com', '+.youtube.com'],
       nameserverPolicy = nameserverPolicy ?? {},
       hosts = hosts ?? {};

  // 从 Map 创建 DnsConfig
  factory DnsConfig.fromMap(Map<String, dynamic> map) {
    final dnsMap = map['dns'] as Map<String, dynamic>? ?? {};
    final hosts = map['hosts'] as Map<String, dynamic>? ?? {};
    final fallbackFilter =
        dnsMap['fallback-filter'] as Map<String, dynamic>? ?? {};

    return DnsConfig(
      // 基础配置
      enable: dnsMap['enable'] as bool? ?? true,
      listen: dnsMap['listen'] as String? ?? ':53',
      enhancedMode: dnsMap['enhanced-mode'] as String? ?? 'fake-ip',
      fakeIpRange: dnsMap['fake-ip-range'] as String? ?? '198.18.0.1/16',
      fakeIpFilterMode: dnsMap['fake-ip-filter-mode'] as String? ?? 'blacklist',
      ipv6: dnsMap['ipv6'] as bool? ?? true,

      // 高级配置
      preferH3: dnsMap['prefer-h3'] as bool? ?? false,
      respectRules: dnsMap['respect-rules'] as bool? ?? false,
      useHosts: dnsMap['use-hosts'] as bool? ?? false,
      useSystemHosts: dnsMap['use-system-hosts'] as bool? ?? false,
      directNameserverFollowPolicy:
          dnsMap['direct-nameserver-follow-policy'] as bool? ?? false,

      // DNS 服务器列表
      defaultNameserver: _parseStringList(dnsMap['default-nameserver']),
      nameserver: _parseStringList(dnsMap['nameserver']),
      fallback: _parseStringList(dnsMap['fallback']),
      proxyServerNameserver: _parseStringList(
        dnsMap['proxy-server-nameserver'],
      ),
      directNameserver: _parseStringList(dnsMap['direct-nameserver']),
      fakeIpFilter: _parseStringList(dnsMap['fake-ip-filter']),

      // Fallback 过滤器
      fallbackGeoip: fallbackFilter['geoip'] as bool? ?? true,
      fallbackGeoipCode: fallbackFilter['geoip-code'] as String? ?? 'CN',
      fallbackIpcidr: _parseStringList(fallbackFilter['ipcidr']),
      fallbackDomain: _parseStringList(fallbackFilter['domain']),

      // 核心功能
      nameserverPolicy:
          dnsMap['nameserver-policy'] as Map<String, dynamic>? ?? {},
      hosts: hosts,
    );
  }

  // 转换为 Map（用于保存到 YAML）
  Map<String, dynamic> toMap() {
    final result = <String, dynamic>{};

    // DNS 配置部分
    final dnsConfig = <String, dynamic>{
      'enable': enable,
      'listen': listen,
      'enhanced-mode': enhancedMode,
      'fake-ip-range': fakeIpRange,
      'fake-ip-filter-mode': fakeIpFilterMode,
      'ipv6': ipv6,
      'prefer-h3': preferH3,
      'respect-rules': respectRules,
      'use-hosts': useHosts,
      'use-system-hosts': useSystemHosts,
      'direct-nameserver-follow-policy': directNameserverFollowPolicy,
      'default-nameserver': defaultNameserver,
      'nameserver': nameserver,
      'fake-ip-filter': fakeIpFilter,
    };

    // 只有非空时才添加
    if (fallback.isNotEmpty) {
      dnsConfig['fallback'] = fallback;
    }

    // 当 respect-rules 为 true 时，proxy-server-nameserver 是必需的
    // 即使用户清空了这个字段，也应该写入默认值以避免 Mihomo 报错
    if (respectRules || proxyServerNameserver.isNotEmpty) {
      final servers = proxyServerNameserver.isNotEmpty
          ? proxyServerNameserver
          : [
              'https://doh.pub/dns-query',
              'https://dns.alidns.com/dns-query',
              'tls://223.5.5.5',
            ];
      dnsConfig['proxy-server-nameserver'] = servers;
    }

    if (directNameserver.isNotEmpty) {
      dnsConfig['direct-nameserver'] = directNameserver;
    }

    if (nameserverPolicy.isNotEmpty) {
      dnsConfig['nameserver-policy'] = nameserverPolicy;
    }

    // Fallback 过滤器
    dnsConfig['fallback-filter'] = {
      'geoip': fallbackGeoip,
      'geoip-code': fallbackGeoipCode,
      'ipcidr': fallbackIpcidr,
      'domain': fallbackDomain,
    };

    result['dns'] = dnsConfig;

    // Hosts 配置部分
    if (hosts.isNotEmpty) {
      result['hosts'] = hosts;
    }

    return result;
  }

  // 解析字符串列表（兼容 List 和其他格式）
  static List<String> _parseStringList(dynamic value) {
    if (value == null) return [];
    if (value is List) {
      return value.map((e) => e.toString()).toList();
    }
    return [];
  }

  // 创建默认配置
  factory DnsConfig.defaultConfig() {
    return DnsConfig();
  }

  // 从可视化格式的字符串解析 nameserver-policy
  //
  // 输入格式：domain1=dns1;dns2, domain2=dns3
  // 示例：*.google.com=8.8.8.8;8.8.4.4, +.cn=223.5.5.5
  static Map<String, dynamic> parseNameserverPolicy(String input) {
    final result = <String, dynamic>{};
    if (input.trim().isEmpty) return result;

    // 按逗号分隔不同的规则
    final rules = input.split(',');

    for (final rule in rules) {
      final trimmedRule = rule.trim();
      if (trimmedRule.isEmpty) continue;

      // 按等号分隔域名和 DNS 服务器
      final parts = trimmedRule.split('=');
      if (parts.length != 2) continue;

      final domain = parts[0].trim();
      final serversStr = parts[1].trim();

      // 按分号分隔多个 DNS 服务器
      if (serversStr.contains(';')) {
        final servers = serversStr
            .split(';')
            .map((s) => s.trim())
            .where((s) => s.isNotEmpty)
            .toList();
        result[domain] = servers;
      } else {
        result[domain] = serversStr;
      }
    }

    return result;
  }

  // 将 nameserver-policy 格式化为可视化字符串
  //
  // 输出格式：domain1=dns1;dns2, domain2=dns3
  static String formatNameserverPolicy(Map<String, dynamic> policy) {
    if (policy.isEmpty) return '';

    final parts = <String>[];

    for (final entry in policy.entries) {
      final domain = entry.key;
      final value = entry.value;

      if (value is List) {
        final servers = value.join(';');
        parts.add('$domain=$servers');
      } else {
        parts.add('$domain=$value');
      }
    }

    return parts.join(', ');
  }

  // 从可视化格式的字符串解析 hosts
  //
  // 输入格式：domain1=ip1, domain2=ip2;ip3
  // 示例：localhost=127.0.0.1, *.test.com=1.2.3.4;5.6.7.8
  static Map<String, dynamic> parseHosts(String input) {
    final result = <String, dynamic>{};
    if (input.trim().isEmpty) return result;

    // 按逗号分隔不同的规则
    final rules = input.split(',');

    for (final rule in rules) {
      final trimmedRule = rule.trim();
      if (trimmedRule.isEmpty) continue;

      // 按等号分隔域名和 IP
      final parts = trimmedRule.split('=');
      if (parts.length != 2) continue;

      final domain = parts[0].trim();
      final valueStr = parts[1].trim();

      // 按分号分隔多个 IP
      if (valueStr.contains(';')) {
        final ips = valueStr
            .split(';')
            .map((s) => s.trim())
            .where((s) => s.isNotEmpty)
            .toList();
        result[domain] = ips;
      } else {
        result[domain] = valueStr;
      }
    }

    return result;
  }

  // 将 hosts 格式化为可视化字符串
  //
  // 输出格式：domain1=ip1, domain2=ip2;ip3
  static String formatHosts(Map<String, dynamic> hosts) {
    if (hosts.isEmpty) return '';

    final parts = <String>[];

    for (final entry in hosts.entries) {
      final domain = entry.key;
      final value = entry.value;

      if (value is List) {
        final ips = value.join(';');
        parts.add('$domain=$ips');
      } else {
        parts.add('$domain=$value');
      }
    }

    return parts.join(', ');
  }

  // 复制并修改配置
  DnsConfig copyWith({
    // 基础配置
    bool? enable,
    String? listen,
    String? enhancedMode,
    String? fakeIpRange,
    String? fakeIpFilterMode,
    bool? ipv6,

    // 高级配置
    bool? preferH3,
    bool? respectRules,
    bool? useHosts,
    bool? useSystemHosts,
    bool? directNameserverFollowPolicy,

    // DNS 服务器列表
    List<String>? defaultNameserver,
    List<String>? nameserver,
    List<String>? fallback,
    List<String>? proxyServerNameserver,
    List<String>? directNameserver,
    List<String>? fakeIpFilter,

    // Fallback 过滤器
    bool? fallbackGeoip,
    String? fallbackGeoipCode,
    List<String>? fallbackIpcidr,
    List<String>? fallbackDomain,

    // 核心功能
    Map<String, dynamic>? nameserverPolicy,
    Map<String, dynamic>? hosts,
  }) {
    return DnsConfig(
      enable: enable ?? this.enable,
      listen: listen ?? this.listen,
      enhancedMode: enhancedMode ?? this.enhancedMode,
      fakeIpRange: fakeIpRange ?? this.fakeIpRange,
      fakeIpFilterMode: fakeIpFilterMode ?? this.fakeIpFilterMode,
      ipv6: ipv6 ?? this.ipv6,
      preferH3: preferH3 ?? this.preferH3,
      respectRules: respectRules ?? this.respectRules,
      useHosts: useHosts ?? this.useHosts,
      useSystemHosts: useSystemHosts ?? this.useSystemHosts,
      directNameserverFollowPolicy:
          directNameserverFollowPolicy ?? this.directNameserverFollowPolicy,
      defaultNameserver: defaultNameserver ?? List.from(this.defaultNameserver),
      nameserver: nameserver ?? List.from(this.nameserver),
      fallback: fallback ?? List.from(this.fallback),
      proxyServerNameserver:
          proxyServerNameserver ?? List.from(this.proxyServerNameserver),
      directNameserver: directNameserver ?? List.from(this.directNameserver),
      fakeIpFilter: fakeIpFilter ?? List.from(this.fakeIpFilter),
      fallbackGeoip: fallbackGeoip ?? this.fallbackGeoip,
      fallbackGeoipCode: fallbackGeoipCode ?? this.fallbackGeoipCode,
      fallbackIpcidr: fallbackIpcidr ?? List.from(this.fallbackIpcidr),
      fallbackDomain: fallbackDomain ?? List.from(this.fallbackDomain),
      nameserverPolicy: nameserverPolicy ?? Map.from(this.nameserverPolicy),
      hosts: hosts ?? Map.from(this.hosts),
    );
  }
}

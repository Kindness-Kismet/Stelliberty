// 代理组信息
class ProxyGroup {
  final String name;
  final String type; // Selector、URLTest、Fallback、LoadBalance
  final String? now; // 当前选中的节点
  final List<String> all; // 所有可用节点
  final bool isHidden; // 是否隐藏
  final String? icon; // 代理组图标 URL

  ProxyGroup({
    required this.name,
    required this.type,
    this.now,
    required this.all,
    this.isHidden = false,
    this.icon,
  });

  factory ProxyGroup.fromJson(String name, Map<String, dynamic> json) {
    return ProxyGroup(
      name: name,
      type: json['type'] ?? '',
      now: json['now'],
      all: List<String>.from(json['all'] ?? []),
      isHidden: json['hidden'] ?? false,
      icon: json['icon'],
    );
  }

  Map<String, dynamic> toJson() {
    return {
      'name': name,
      'type': type,
      'now': now,
      'all': all,
      'hidden': isHidden,
      'icon': icon,
    };
  }

  ProxyGroup copyWith({
    String? name,
    String? type,
    String? now,
    List<String>? all,
    bool? isHidden,
    String? icon,
  }) {
    return ProxyGroup(
      name: name ?? this.name,
      type: type ?? this.type,
      now: now ?? this.now,
      all: all ?? this.all,
      isHidden: isHidden ?? this.isHidden,
      icon: icon ?? this.icon,
    );
  }
}

// 代理节点信息
class ProxyNode {
  final String name;
  final String type; // Shadowsocks、VMess、Trojan 等
  final int? delay; // 延迟（ms）
  final String? server;
  final int? port;

  ProxyNode({
    required this.name,
    required this.type,
    this.delay,
    this.server,
    this.port,
  });

  factory ProxyNode.fromJson(String name, Map<String, dynamic> json) {
    return ProxyNode(
      name: name,
      type: json['type'] ?? '',
      delay: json['delay'],
      server: json['server'],
      port: json['port'],
    );
  }

  Map<String, dynamic> toJson() {
    return {
      'name': name,
      'type': type,
      'delay': delay,
      'server': server,
      'port': port,
    };
  }

  ProxyNode copyWith({
    String? name,
    String? type,
    int? delay,
    String? server,
    int? port,
  }) {
    return ProxyNode(
      name: name ?? this.name,
      type: type ?? this.type,
      delay: delay ?? this.delay,
      server: server ?? this.server,
      port: port ?? this.port,
    );
  }

  // 获取延迟显示文本
  String get delayText {
    if (delay == null) {
      return '-';
    }
    return delay.toString();
  }

  // 获取延迟颜色（用于 UI 展示）
  String get delayColor {
    if (delay == null || delay! < 0) {
      return 'grey';
    } else if (delay! < 100) {
      return 'green';
    } else if (delay! < 300) {
      return 'orange';
    } else {
      return 'red';
    }
  }
}

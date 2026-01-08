import 'package:stelliberty/services/log_print_service.dart';

// 订阅流量信息
class SubscriptionInfo {
  final int upload; // 已上传字节数
  final int download; // 已下载字节数
  final int total; // 总流量字节数
  final int expire; // 过期时间戳（秒）

  const SubscriptionInfo({
    this.upload = 0,
    this.download = 0,
    this.total = 0,
    this.expire = 0,
  });

  factory SubscriptionInfo.fromJson(Map<String, dynamic>? json) {
    if (json == null) {
      return const SubscriptionInfo();
    }
    return SubscriptionInfo(
      upload: (json['Upload'] as num?)?.toInt() ?? 0,
      download: (json['Download'] as num?)?.toInt() ?? 0,
      total: (json['Total'] as num?)?.toInt() ?? 0,
      expire: (json['Expire'] as num?)?.toInt() ?? 0,
    );
  }

  Map<String, dynamic> toJson() => {
    'Upload': upload,
    'Download': download,
    'Total': total,
    'Expire': expire,
  };
}

// 提供者类型
enum ProviderType {
  // 代理提供者
  proxy('Proxy', '代理提供者'),

  // 规则提供者
  rule('Rule', '规则提供者');

  const ProviderType(this.value, this.displayName);

  final String value;
  final String displayName;
}

// 提供者配置
class Provider {
  final String name; // 提供者名称
  final ProviderType type; // 提供者类型
  final String? url; // 订阅链接（HTTP 类型）
  final String? path; // 本地文件路径
  final int count; // 项目数量
  final DateTime updateAt; // 最后更新时间
  final bool isUpdating; // 是否正在更新
  final String vehicleType; // 传输方式：HTTP/File
  final SubscriptionInfo? subscriptionInfo; // 订阅流量信息

  const Provider({
    required this.name,
    required this.type,
    this.url,
    this.path,
    this.count = 0,
    required this.updateAt,
    this.isUpdating = false,
    this.vehicleType = 'HTTP',
    this.subscriptionInfo,
  });

  // 从 Clash API 响应构建（providerType 需由调用方根据 API 端点指定）
  factory Provider.fromClashApi(
    String name,
    Map<String, dynamic> json, {
    required ProviderType providerType,
  }) {
    // 解析更新时间
    DateTime updateAt = DateTime.now();
    final updateAtStr = json['updatedAt'];
    if (updateAtStr != null &&
        updateAtStr is String &&
        updateAtStr.isNotEmpty) {
      try {
        updateAt = DateTime.parse(updateAtStr);
      } catch (e) {
        // 如果解析失败，使用当前时间
        Logger.warning('提供者 $name 的时间解析失败 (原始值：$updateAtStr)：$e');
        updateAt = DateTime.now();
      }
    }

    // 计算数量
    int count = 0;
    if (json['proxies'] != null && json['proxies'] is List) {
      count = (json['proxies'] as List).length;
    } else if (json['ruleCount'] != null) {
      count = (json['ruleCount'] as num).toInt();
    }

    // 解析订阅信息
    final subscriptionInfo = SubscriptionInfo.fromJson(
      json['subscription-info'] as Map<String, dynamic>?,
    );

    return Provider(
      name: name,
      type: providerType,
      path: json['path'],
      count: count,
      updateAt: updateAt,
      vehicleType: json['vehicleType'] ?? 'HTTP',
      subscriptionInfo: subscriptionInfo.total > 0 ? subscriptionInfo : null,
    );
  }

  // 复制并修改属性
  Provider copyWith({
    String? name,
    ProviderType? type,
    String? url,
    String? path,
    int? count,
    DateTime? updateAt,
    bool? isUpdating,
    String? vehicleType,
    SubscriptionInfo? subscriptionInfo,
  }) {
    return Provider(
      name: name ?? this.name,
      type: type ?? this.type,
      url: url ?? this.url,
      path: path ?? this.path,
      count: count ?? this.count,
      updateAt: updateAt ?? this.updateAt,
      isUpdating: isUpdating ?? this.isUpdating,
      vehicleType: vehicleType ?? this.vehicleType,
      subscriptionInfo: subscriptionInfo ?? this.subscriptionInfo,
    );
  }

  Map<String, dynamic> toJson() => {
    'name': name,
    'type': type.value,
    'url': url,
    'path': path,
    'count': count,
    'updatedAt': updateAt.toIso8601String(),
    'isUpdating': isUpdating,
    'vehicleType': vehicleType,
    'subscription-info': subscriptionInfo?.toJson(),
  };

  @override
  String toString() => 'Provider(name: $name, type: ${type.value})';
}

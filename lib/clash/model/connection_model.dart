// Clash 连接数据模型
library;

// 元数据信息
class Metadata {
  // 连接类型（HTTP/HTTPS/SOCKS5/Inner等）
  final String type;

  // 网络协议类型（tcp/udp）
  final String network;

  // 源 IP 地址
  final String sourceIP;

  // 源端口
  final String sourcePort;

  // 源 GeoIP（国家/地区列表）
  final List<String> sourceGeoIP;

  // 源 IP ASN 信息
  final String sourceIPASN;

  // 目标 IP 地址
  final String destinationIP;

  // 目标端口
  final String destinationPort;

  // 目标 GeoIP（国家/地区列表）
  final List<String> destinationGeoIP;

  // 目标 IP ASN 信息
  final String destinationIPASN;

  // 主机名
  final String host;

  // 嗅探到的主机名
  final String sniffHost;

  // 进程名称
  final String process;

  // 进程路径
  final String processPath;

  // 进程所属用户 ID（Linux/Android）
  final int? uid;

  // 入站 IP
  final String inboundIP;

  // 入站端口
  final String inboundPort;

  // 入站名称
  final String inboundName;

  // 入站用户
  final String inboundUser;

  // DSCP 值（Differentiated Services Code Point）
  final int dscp;

  // 远程目标
  final String remoteDestination;

  // DNS 模式
  final String dnsMode;

  // 特殊代理
  final String specialProxy;

  // 特殊规则
  final String specialRules;

  const Metadata({
    this.type = '',
    this.network = '',
    this.sourceIP = '',
    this.sourcePort = '',
    this.sourceGeoIP = const [],
    this.sourceIPASN = '',
    this.destinationIP = '',
    this.destinationPort = '',
    this.destinationGeoIP = const [],
    this.destinationIPASN = '',
    this.host = '',
    this.sniffHost = '',
    this.process = '',
    this.processPath = '',
    this.uid,
    this.inboundIP = '',
    this.inboundPort = '',
    this.inboundName = '',
    this.inboundUser = '',
    this.dscp = 0,
    this.remoteDestination = '',
    this.dnsMode = '',
    this.specialProxy = '',
    this.specialRules = '',
  });

  // 从 JSON 创建元数据
  factory Metadata.fromJson(Map<String, dynamic> json) {
    return Metadata(
      type: json['type'] as String? ?? '',
      network: json['network'] as String? ?? '',
      sourceIP: json['sourceIP'] as String? ?? '',
      sourcePort: json['sourcePort'] as String? ?? '',
      sourceGeoIP:
          (json['sourceGeoIP'] as List<dynamic>?)
              ?.map((e) => e.toString())
              .toList() ??
          const [],
      sourceIPASN: json['sourceIPASN'] as String? ?? '',
      destinationIP: json['destinationIP'] as String? ?? '',
      destinationPort: json['destinationPort'] as String? ?? '',
      destinationGeoIP:
          (json['destinationGeoIP'] as List<dynamic>?)
              ?.map((e) => e.toString())
              .toList() ??
          const [],
      destinationIPASN: json['destinationIPASN'] as String? ?? '',
      host: json['host'] as String? ?? '',
      sniffHost: json['sniffHost'] as String? ?? '',
      process: json['process'] as String? ?? '',
      processPath: json['processPath'] as String? ?? '',
      uid: json['uid'] as int?,
      inboundIP: json['inboundIP'] as String? ?? '',
      inboundPort: json['inboundPort'] as String? ?? '',
      inboundName: json['inboundName'] as String? ?? '',
      inboundUser: json['inboundUser'] as String? ?? '',
      dscp: json['dscp'] as int? ?? 0,
      remoteDestination: json['remoteDestination'] as String? ?? '',
      dnsMode: json['dnsMode'] as String? ?? '',
      specialProxy: json['specialProxy'] as String? ?? '',
      specialRules: json['specialRules'] as String? ?? '',
    );
  }

  // 转换为 JSON
  Map<String, dynamic> toJson() {
    return {
      'type': type,
      'network': network,
      'sourceIP': sourceIP,
      'sourcePort': sourcePort,
      'sourceGeoIP': sourceGeoIP,
      'sourceIPASN': sourceIPASN,
      'destinationIP': destinationIP,
      'destinationPort': destinationPort,
      'destinationGeoIP': destinationGeoIP,
      'destinationIPASN': destinationIPASN,
      'host': host,
      'sniffHost': sniffHost,
      'process': process,
      'processPath': processPath,
      if (uid != null) 'uid': uid,
      'inboundIP': inboundIP,
      'inboundPort': inboundPort,
      'inboundName': inboundName,
      'inboundUser': inboundUser,
      'dscp': dscp,
      'remoteDestination': remoteDestination,
      'dnsMode': dnsMode,
      'specialProxy': specialProxy,
      'specialRules': specialRules,
    };
  }

  // 获取描述文本（协议://主机:端口）
  String get description {
    var text = '$network://';
    if (host.isNotEmpty) {
      text += host;
    } else if (destinationIP.isNotEmpty) {
      text += destinationIP;
    }
    text += ':$destinationPort';
    return text;
  }

  // 获取显示用的主机名（优先使用 sniffHost）
  String get displayHost {
    if (sniffHost.isNotEmpty) return sniffHost;
    if (host.isNotEmpty) return host;
    if (destinationIP.isNotEmpty) return destinationIP;
    if (remoteDestination.isNotEmpty) return remoteDestination;
    return '';
  }
}

// 连接信息
class ConnectionInfo {
  // 连接 ID
  final String id;

  // 上传流量（字节）
  final int upload;

  // 下载流量（字节）
  final int download;

  // 上传速度（字节/秒）
  final int uploadSpeed;

  // 下载速度（字节/秒）
  final int downloadSpeed;

  // 连接开始时间
  final DateTime start;

  // 元数据
  final Metadata metadata;

  // 代理链（节点路径）
  final List<String> chains;

  // 匹配的规则
  final String rule;

  // 规则负载
  final String rulePayload;

  const ConnectionInfo({
    required this.id,
    this.upload = 0,
    this.download = 0,
    this.uploadSpeed = 0,
    this.downloadSpeed = 0,
    required this.start,
    required this.metadata,
    this.chains = const [],
    this.rule = '',
    this.rulePayload = '',
  });

  // 从 JSON 创建连接信息
  factory ConnectionInfo.fromJson(Map<String, dynamic> json) {
    return ConnectionInfo(
      id: json['id'] as String? ?? '',
      upload: json['upload'] as int? ?? 0,
      download: json['download'] as int? ?? 0,
      uploadSpeed: json['uploadSpeed'] as int? ?? 0,
      downloadSpeed: json['downloadSpeed'] as int? ?? 0,
      start: json['start'] != null
          ? DateTime.parse(json['start'] as String)
          : DateTime.now(),
      metadata: json['metadata'] != null
          ? Metadata.fromJson(json['metadata'] as Map<String, dynamic>)
          : const Metadata(),
      chains:
          (json['chains'] as List<dynamic>?)
              ?.map((e) => e.toString())
              .toList() ??
          [],
      rule: json['rule'] as String? ?? '',
      rulePayload: json['rulePayload'] as String? ?? '',
    );
  }

  // 转换为 JSON
  Map<String, dynamic> toJson() {
    return {
      'id': id,
      'upload': upload,
      'download': download,
      'uploadSpeed': uploadSpeed,
      'downloadSpeed': downloadSpeed,
      'start': start.toIso8601String(),
      'metadata': metadata.toJson(),
      'chains': chains,
      'rule': rule,
      'rulePayload': rulePayload,
    };
  }

  // 获取代理节点名称（实际的代理服务器）
  String get proxyNode {
    if (chains.isNotEmpty) {
      return chains.last;
    }
    return 'DIRECT';
  }

  // 获取代理组名称（用户选择的代理组）
  String get proxyGroup {
    if (chains.length > 1) {
      return chains.first;
    } else if (chains.length == 1) {
      // 只有一个元素，可能是 DIRECT/REJECT 或代理组
      return chains.first;
    }
    return 'DIRECT';
  }

  // 获取连接持续时间
  Duration get duration {
    return DateTime.now().difference(start);
  }

  // 格式化持续时间
  String get formattedDuration {
    final duration = this.duration;
    final hours = duration.inHours;
    final minutes = duration.inMinutes.remainder(60);
    final seconds = duration.inSeconds.remainder(60);

    if (hours > 0) {
      return '$hours:${minutes.toString().padLeft(2, '0')}:${seconds.toString().padLeft(2, '0')}';
    } else if (minutes > 0) {
      return '$minutes:${seconds.toString().padLeft(2, '0')}';
    } else {
      return '${seconds}s';
    }
  }
}

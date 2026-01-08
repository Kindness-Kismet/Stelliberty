// 流量数据模型
class TrafficData {
  // 上传速度（字节/秒）
  final int upload;

  // 下载速度（字节/秒）
  final int download;

  // 时间戳
  final DateTime timestamp;

  // 累计上传流量（字节）
  final int totalUpload;

  // 累计下载流量（字节）
  final int totalDownload;

  TrafficData({
    required this.upload,
    required this.download,
    required this.timestamp,
    this.totalUpload = 0,
    this.totalDownload = 0,
  });

  factory TrafficData.fromJson(Map<String, dynamic> json) {
    return TrafficData(
      upload: json['up'] as int? ?? 0,
      download: json['down'] as int? ?? 0,
      timestamp: DateTime.now(),
    );
  }

  // 创建带累计流量的数据
  TrafficData copyWithTotal({
    required int totalUpload,
    required int totalDownload,
  }) {
    return TrafficData(
      upload: upload,
      download: download,
      timestamp: timestamp,
      totalUpload: totalUpload,
      totalDownload: totalDownload,
    );
  }

  // 空数据（无流量）
  static final TrafficData zero = TrafficData(
    upload: 0,
    download: 0,
    timestamp: DateTime.now(),
    totalUpload: 0,
    totalDownload: 0,
  );

  // 格式化速度（自动选择单位）
  static String formatSpeed(int bytesPerSecond) {
    if (bytesPerSecond < 1024) {
      return '${bytesPerSecond.toStringAsFixed(0)} B/s';
    } else if (bytesPerSecond < 1024 * 1024) {
      final kbps = bytesPerSecond / 1024;
      return '${kbps.toStringAsFixed(1)} KB/s';
    } else if (bytesPerSecond < 1024 * 1024 * 1024) {
      final mbps = bytesPerSecond / (1024 * 1024);
      return '${mbps.toStringAsFixed(2)} MB/s';
    } else {
      final gbps = bytesPerSecond / (1024 * 1024 * 1024);
      return '${gbps.toStringAsFixed(2)} GB/s';
    }
  }

  // 格式化为简短显示（KB/s）
  String get uploadSpeedKB => (upload / 1024).toStringAsFixed(1);
  String get downloadSpeedKB => (download / 1024).toStringAsFixed(1);

  // 格式化为自动单位
  String get uploadFormatted => formatSpeed(upload);
  String get downloadFormatted => formatSpeed(download);

  // 格式化累计流量（自动选择单位）
  static String formatBytes(int bytes) {
    if (bytes < 1024) {
      return '$bytes B';
    } else if (bytes < 1024 * 1024) {
      final kb = bytes / 1024;
      return '${kb.toStringAsFixed(1)} KB';
    } else if (bytes < 1024 * 1024 * 1024) {
      final mb = bytes / (1024 * 1024);
      return '${mb.toStringAsFixed(2)} MB';
    } else {
      final gb = bytes / (1024 * 1024 * 1024);
      return '${gb.toStringAsFixed(2)} GB';
    }
  }

  // 格式化累计上传流量
  String get totalUploadFormatted => formatBytes(totalUpload);

  // 格式化累计下载流量
  String get totalDownloadFormatted => formatBytes(totalDownload);

  @override
  String toString() {
    return 'TrafficData(↑ $uploadFormatted, ↓ $downloadFormatted, Total: ↑ $totalUploadFormatted, ↓ $totalDownloadFormatted)';
  }
}

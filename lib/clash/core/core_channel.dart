/// 内核渠道
/// - stable: 正式版
/// - beta: 测试版（预发布）
/// - custom: 自定义路径
enum CoreChannel { stable, beta, custom }

extension CoreChannelX on CoreChannel {
  /// 用于持久化的字符串值
  String get storageValue => name;

  bool get isCustom => this == CoreChannel.custom;

  static CoreChannel fromStorage(String? value) {
    switch (value) {
      case 'beta':
        return CoreChannel.beta;
      case 'custom':
        return CoreChannel.custom;
      case 'stable':
      default:
        return CoreChannel.stable;
    }
  }
}

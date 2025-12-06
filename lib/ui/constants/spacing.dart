import 'package:flutter/material.dart';

// 全局间距常量定义
class SpacingConstants {
  SpacingConstants._(); // 私有构造函数，防止实例化

  // 滚动条边距
  static const scrollbarPaddingTop = 1.0;
  static const scrollbarPaddingRight = 1.0;
  static const scrollbarPaddingBottom = 5.0;
  static const scrollbarPaddingLeft = 0.0;

  // 滚动条 EdgeInsets
  static const scrollbarPadding = EdgeInsets.fromLTRB(
    scrollbarPaddingLeft,
    scrollbarPaddingTop,
    scrollbarPaddingRight,
    scrollbarPaddingBottom,
  );

  // 滚动条右侧占用的空间（用于内容 padding 补偿）
  static const scrollbarRightCompensation = scrollbarPaddingRight;
}

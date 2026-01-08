import 'package:flutter/material.dart';
import 'package:stelliberty/ui/widgets/home/clash_info_card.dart';
import 'package:stelliberty/ui/widgets/home/outbound_mode_card.dart';
import 'package:stelliberty/ui/widgets/home/proxy_switch_card.dart';
import 'package:stelliberty/ui/widgets/home/traffic_stats_card.dart';
import 'package:stelliberty/ui/widgets/home/tun_mode_card.dart';
import 'package:stelliberty/services/log_print_service.dart';
import 'package:stelliberty/ui/constants/spacing.dart';

// 主页 - 代理控制中心
class HomePageContent extends StatefulWidget {
  const HomePageContent({super.key});

  @override
  State<HomePageContent> createState() => _HomePageContentState();
}

class _HomePageContentState extends State<HomePageContent> {
  @override
  void initState() {
    super.initState();
    Logger.info('初始化 HomePageContent');
  }

  @override
  Widget build(BuildContext context) {
    final scrollController = ScrollController();

    return LayoutBuilder(
      builder: (context, constraints) {
        return Padding(
          padding: SpacingConstants.scrollbarPadding,
          child: SingleChildScrollView(
            controller: scrollController,
            child: ConstrainedBox(
              constraints: BoxConstraints(
                minHeight:
                    constraints.maxHeight -
                    SpacingConstants.scrollbarPaddingTop -
                    SpacingConstants.scrollbarPaddingBottom,
              ),
              child: Padding(
                padding: EdgeInsets.fromLTRB(
                  25.0,
                  24.0,
                  25.0 - SpacingConstants.scrollbarRightCompensation,
                  2.0, // 距底2px
                ),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  mainAxisAlignment: MainAxisAlignment.start,
                  children: [
                    // 第一行：代理控制卡片 + TUN 模式卡片
                    IntrinsicHeight(
                      child: Row(
                        crossAxisAlignment: CrossAxisAlignment.stretch,
                        children: [
                          Expanded(child: ProxySwitchCard()),
                          const SizedBox(width: 25),
                          Expanded(child: TunModeCard()),
                        ],
                      ),
                    ),
                    const SizedBox(height: 24),
                    // 第二行：流量统计卡片（占据整行）
                    TrafficStatsCard(),
                    const SizedBox(height: 24),
                    // 第三行：Clash 信息卡片 + 出站模式卡片
                    IntrinsicHeight(
                      child: Row(
                        crossAxisAlignment: CrossAxisAlignment.stretch,
                        children: [
                          Expanded(child: ClashInfoCard()),
                          const SizedBox(width: 25),
                          Expanded(child: OutboundModeCard()),
                        ],
                      ),
                    ),
                  ],
                ),
              ),
            ),
          ),
        );
      },
    );
  }
}

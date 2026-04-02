import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:stelliberty/ui/widgets/home/outbound_mode_card.dart';
import 'package:stelliberty/ui/widgets/home/proxy_switch_card.dart';
import 'package:provider/provider.dart';
import 'package:stelliberty/clash/model/traffic_data_model.dart';
import 'package:stelliberty/clash/providers/clash_provider.dart';
import 'package:stelliberty/clash/providers/traffic_provider.dart';
import 'package:stelliberty/ui/widgets/home/running_status_card.dart';
import 'package:stelliberty/ui/widgets/home/traffic_speed_card.dart';
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
        final isMobilePlatform =
            !kIsWeb &&
            (defaultTargetPlatform == TargetPlatform.android ||
                defaultTargetPlatform == TargetPlatform.iOS);
        final shouldShowTunCard = !isMobilePlatform;

        final isCompactLayout = constraints.maxWidth < 600;
        final horizontalPadding = isCompactLayout ? 16.0 : 25.0;
        final sectionSpacing = isCompactLayout ? 16.0 : 24.0;
        final cardSpacing = isCompactLayout ? 16.0 : 25.0;

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
                  horizontalPadding,
                  // 抵消外层滚动条上边距，避免内容刚好溢出触发滚动抖动
                  (isCompactLayout ? 16.0 : 24.0) -
                      SpacingConstants.scrollbarPaddingTop,
                  horizontalPadding -
                      SpacingConstants.scrollbarRightCompensation,
                  2.0, // 距底2px
                ),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  mainAxisAlignment: MainAxisAlignment.start,
                  children: [
                    // 第一行：代理控制卡片 + TUN 模式卡片
                    if (isCompactLayout) ...[
                      ProxySwitchCard(key: const ValueKey('home_proxy_switch')),
                      if (shouldShowTunCard) ...[
                        SizedBox(height: sectionSpacing),
                        TunModeCard(key: const ValueKey('home_tun_mode')),
                      ],
                    ] else ...[
                      if (shouldShowTunCard)
                        IntrinsicHeight(
                          child: Row(
                            crossAxisAlignment: CrossAxisAlignment.stretch,
                            children: [
                              Expanded(
                                child: ProxySwitchCard(
                                  key: const ValueKey('home_proxy_switch'),
                                ),
                              ),
                              SizedBox(width: cardSpacing),
                              Expanded(
                                child: TunModeCard(
                                  key: const ValueKey('home_tun_mode'),
                                ),
                              ),
                            ],
                          ),
                        )
                      else
                        ProxySwitchCard(
                          key: const ValueKey('home_proxy_switch'),
                        ),
                    ],
                    SizedBox(height: sectionSpacing),
                    // 第二行：运行状态卡片 + 出站模式卡片
                    if (isCompactLayout) ...[
                      const RunningStatusCard(
                        key: ValueKey('home_running_status'),
                      ),
                      SizedBox(height: sectionSpacing),
                      const OutboundModeCard(
                        key: ValueKey('home_outbound_mode'),
                      ),
                    ] else ...[
                      IntrinsicHeight(
                        child: Row(
                          crossAxisAlignment: CrossAxisAlignment.stretch,
                          children: [
                            const Expanded(
                              child: RunningStatusCard(
                                key: ValueKey('home_running_status'),
                              ),
                            ),
                            SizedBox(width: cardSpacing),
                            const Expanded(
                              child: OutboundModeCard(
                                key: ValueKey('home_outbound_mode'),
                              ),
                            ),
                          ],
                        ),
                      ),
                    ],
                    SizedBox(height: sectionSpacing),
                    // 第三行：网速显示卡片
                    _buildTrafficSection(context),
                  ],
                ),
              ),
            ),
          ),
        );
      },
    );
  }

  Widget _buildTrafficSection(BuildContext context) {
    final trafficProvider = context.read<TrafficProvider>();
    final isCoreRunning = context.select<ClashProvider, bool>(
      (m) => m.isCoreRunning,
    );

    return isCoreRunning
        ? StreamBuilder<TrafficData>(
            stream: context.read<ClashProvider>().trafficStream,
            builder: (context, snapshot) {
              final traffic = snapshot.data ?? TrafficData.zero;
              final trafficWithTotal = traffic.copyWithTotal(
                totalUpload: trafficProvider.totalUpload,
                totalDownload: trafficProvider.totalDownload,
              );
              return TrafficSpeedCard(
                traffic: trafficWithTotal,
                isCoreRunning: isCoreRunning,
                onReset: trafficProvider.resetTotalTraffic,
              );
            },
          )
        : TrafficSpeedCard(
            traffic: trafficProvider.lastTrafficData ?? TrafficData.zero,
            isCoreRunning: isCoreRunning,
            onReset: trafficProvider.resetTotalTraffic,
          );
  }
}

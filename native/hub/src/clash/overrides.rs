// Clash 配置覆写处理
//
// 目的：提供 YAML 和 JavaScript 格式的配置覆写功能

pub mod downloader;
pub mod js_executor;
pub mod processor;
pub mod yaml_merger;

pub use processor::{
    ApplyOverridesRequest, DownloadOverrideRequest, ParseSubscriptionRequest,
};

use rinf::DartSignal;
use tokio::spawn;

// 初始化覆写处理消息监听器
//
// 目的：建立覆写处理请求的响应通道
pub fn init_message_listeners() {
    // 覆写应用请求监听器
    spawn(async {
        let receiver = ApplyOverridesRequest::get_dart_signal_receiver();
        while let Some(dart_signal) = receiver.recv().await {
            dart_signal.message.handle();
        }
        log::info!("覆写处理消息通道已关闭，退出监听器");
    });

    // 订阅解析请求监听器
    spawn(async {
        let receiver = ParseSubscriptionRequest::get_dart_signal_receiver();
        while let Some(dart_signal) = receiver.recv().await {
            dart_signal.message.handle();
        }
        log::info!("订阅解析消息通道已关闭，退出监听器");
    });

    // 覆写文件下载请求监听器
    spawn(async {
        let receiver = DownloadOverrideRequest::get_dart_signal_receiver();
        while let Some(dart_signal) = receiver.recv().await {
            spawn(async move {
                dart_signal.message.handle().await;
            });
        }
        log::info!("覆写文件下载消息通道已关闭，退出监听器");
    });
}

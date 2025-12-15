// 订阅管理模块
//
// 处理订阅源的解析、转换和配置生成

pub mod downloader;
pub mod parser;

pub use parser::ProxyParser;
pub use downloader::DownloadSubscriptionRequest;

use rinf::DartSignal;
use tokio::spawn;

// 初始化订阅管理消息监听器
//
// 目的：建立订阅下载请求的响应通道
pub fn init_message_listeners() {
    // 订阅下载请求监听器
    spawn(async {
        let receiver = DownloadSubscriptionRequest::get_dart_signal_receiver();
        while let Some(dart_signal) = receiver.recv().await {
            let message = dart_signal.message;
            tokio::spawn(async move {
                message.handle().await;
            });
        }
        log::info!("订阅下载消息通道已关闭，退出监听器");
    });
}

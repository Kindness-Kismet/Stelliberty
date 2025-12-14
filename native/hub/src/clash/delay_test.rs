// Clash 延迟测试模块
//
// 目的：提供批量延迟测试功能

pub mod batch_tester;
pub mod signals;

pub use signals::BatchDelayTestRequest;

use rinf::DartSignal;
use tokio::spawn;

// 初始化延迟测试消息监听器
//
// 目的：建立延迟测试请求的响应通道
pub fn init_message_listeners() {
    // 批量延迟测试请求监听器
    spawn(async {
        let receiver = BatchDelayTestRequest::get_dart_signal_receiver();
        while let Some(dart_signal) = receiver.recv().await {
            spawn(async move {
                dart_signal.message.handle().await;
            });
        }
        log::info!("批量延迟测试消息通道已关闭，退出监听器");
    });
}

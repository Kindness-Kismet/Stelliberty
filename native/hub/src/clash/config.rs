// Clash 配置生成模块
//
// 负责统一生成 Clash 运行时配置

pub mod generator;
pub mod injector;
pub mod runtime_params;

use rinf::{DartSignal, RustSignal};
use generator::GenerateRuntimeConfigRequest;
use tokio::spawn;

// 初始化配置生成消息监听器
pub fn init_message_listeners() {
    log::info!("初始化配置生成消息监听器");

    spawn(async move {
        let receiver = GenerateRuntimeConfigRequest::get_dart_signal_receiver();
        while let Some(dart_signal) = receiver.recv().await {
            let request = dart_signal.message;
            let response = request.handle();
            response.send_signal_to_dart();
        }
    });
}

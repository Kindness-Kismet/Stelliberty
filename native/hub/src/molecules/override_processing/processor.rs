// 覆写处理器
// 处理配置覆写（YAML 合并 + JavaScript 执行）

use crate::atoms::override_processor::OverrideProcessor;
use crate::atoms::ProxyParser;
use crate::molecules::OverrideConfig;
use rinf::{DartSignal, RustSignal};
use serde::{Deserialize, Serialize};

// Dart → Rust：应用覆写请求
#[derive(Deserialize, DartSignal)]
pub struct ApplyOverridesRequest {
    pub base_config_content: String,
    pub overrides: Vec<OverrideConfig>,
}

// Rust → Dart：应用覆写响应
#[derive(Serialize, RustSignal)]
pub struct ApplyOverridesResponse {
    pub is_successful: bool,
    pub result_config: String,
    pub error_message: String,
    pub logs: Vec<String>,
}

// Dart → Rust：解析订阅请求
#[derive(Deserialize, DartSignal)]
pub struct ParseSubscriptionRequest {
    pub request_id: String, // 请求标识符，用于响应匹配
    pub content: String,
}

// Rust → Dart：解析订阅响应
#[derive(Serialize, RustSignal)]
pub struct ParseSubscriptionResponse {
    pub request_id: String, // 请求标识符，用于请求匹配
    pub is_successful: bool,
    pub parsed_config: String,
    pub error_message: String,
}

impl ApplyOverridesRequest {
    pub fn handle(self) {
        log::info!("收到应用覆写请求，覆写数量：{}", self.overrides.len());

        let mut processor = match OverrideProcessor::new() {
            Ok(p) => p,
            Err(e) => {
                log::error!("初始化覆写处理器失败：{}", e);
                let response = ApplyOverridesResponse {
                    is_successful: false,
                    result_config: String::new(),
                    error_message: format!("初始化处理器失败：{}", e),
                    logs: vec![],
                };
                response.send_signal_to_dart();
                return;
            }
        };

        // 先解析订阅内容为标准 Clash 配置
        let parsed_config = match ProxyParser::parse_subscription(&self.base_config_content) {
            Ok(config) => config,
            Err(e) => {
                log::error!("订阅解析失败：{}", e);
                let response = ApplyOverridesResponse {
                    is_successful: false,
                    result_config: String::new(),
                    error_message: format!("订阅解析失败：{}", e),
                    logs: vec![],
                };
                response.send_signal_to_dart();
                return;
            }
        };

        log::info!("订阅解析成功，配置长度：{}字节", parsed_config.len());

        match processor.apply_overrides(&parsed_config, self.overrides) {
            Ok(result) => {
                log::info!("覆写处理成功");
                let response = ApplyOverridesResponse {
                    is_successful: true,
                    result_config: result,
                    error_message: String::new(),
                    logs: vec!["处理成功".to_string()],
                };
                response.send_signal_to_dart();
            }
            Err(e) => {
                log::error!("覆写处理失败：{}", e);
                let response = ApplyOverridesResponse {
                    is_successful: false,
                    result_config: String::new(),
                    error_message: e,
                    logs: vec![],
                };
                response.send_signal_to_dart();
            }
        }
    }
}

impl ParseSubscriptionRequest {
    // 处理订阅解析请求
    pub fn handle(self) {
        log::info!(
            "收到订阅解析请求 [{}]，内容长度：{}字节",
            self.request_id,
            self.content.len()
        );

        match ProxyParser::parse_subscription(&self.content) {
            Ok(parsed_config) => {
                log::info!(
                    "订阅解析成功 [{}]，配置长度：{}字节",
                    self.request_id,
                    parsed_config.len()
                );
                let response = ParseSubscriptionResponse {
                    request_id: self.request_id,
                    is_successful: true,
                    parsed_config,
                    error_message: String::new(),
                };
                response.send_signal_to_dart();
            }
            Err(e) => {
                log::error!("订阅解析失败 [{}]：{}", self.request_id, e);
                let response = ParseSubscriptionResponse {
                    request_id: self.request_id,
                    is_successful: false,
                    parsed_config: String::new(),
                    error_message: e,
                };
                response.send_signal_to_dart();
            }
        }
    }
}

// 初始化 Dart 信号监听器
pub fn init_dart_signal_listeners() {
    use tokio::spawn;

    // 应用覆写请求监听器
    spawn(async {
        let receiver = ApplyOverridesRequest::get_dart_signal_receiver();
        while let Some(dart_signal) = receiver.recv().await {
            dart_signal.message.handle();
        }
    });

    // 订阅解析请求监听器
    spawn(async {
        let receiver = ParseSubscriptionRequest::get_dart_signal_receiver();
        while let Some(dart_signal) = receiver.recv().await {
            dart_signal.message.handle();
        }
    });
}

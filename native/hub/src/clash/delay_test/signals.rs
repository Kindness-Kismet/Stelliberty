// Clash 延迟测试信号定义
//
// 目的：定义批量延迟测试的请求和响应信号

use rinf::{DartSignal, RustSignal};
use serde::{Deserialize, Serialize};

// Dart → Rust：批量延迟测试请求
#[derive(Deserialize, DartSignal)]
pub struct BatchDelayTestRequest {
    pub node_names: Vec<String>, // 要测试的节点名称列表
    pub test_url: String,         // 测试 URL
    pub timeout_ms: u32,          // 超时时间（毫秒）
    pub concurrency: u32,         // 并发数
}

// Rust → Dart：单个节点测试完成（流式进度更新）
#[derive(Serialize, RustSignal)]
pub struct DelayTestProgress {
    pub node_name: String,
    pub delay_ms: i32, // -1 表示失败
}

// Rust → Dart：批量测试完成
#[derive(Serialize, RustSignal)]
pub struct BatchDelayTestComplete {
    pub success: bool,
    pub total_count: u32,
    pub success_count: u32,
    pub error_message: Option<String>,
}

impl BatchDelayTestRequest {
    pub async fn handle(self) {
        log::info!(
            "收到批量延迟测试请求，节点数：{}，并发数：{}",
            self.node_names.len(),
            self.concurrency
        );

        let total_count = self.node_names.len() as u32;
        let node_names = self.node_names;
        let test_url = self.test_url;
        let timeout_ms = self.timeout_ms;
        let concurrency = self.concurrency as usize;

        // 进度回调：每个节点测试完成后发送进度信号
        let progress_callback = std::sync::Arc::new(move |node_name: String, delay_ms: i32| {
            DelayTestProgress {
                node_name,
                delay_ms,
            }
            .send_signal_to_dart();
        });

        // 执行批量测试
        let results = super::batch_tester::batch_test_delays(
            node_names,
            test_url,
            timeout_ms,
            concurrency,
            progress_callback,
        )
        .await;

        // 统计成功数量
        let success_count = results.iter().filter(|r| r.delay_ms > 0).count() as u32;

        // 发送完成信号
        BatchDelayTestComplete {
            success: true,
            total_count,
            success_count,
            error_message: None,
        }
        .send_signal_to_dart();

        log::info!(
            "批量延迟测试完成，成功：{}/{}",
            success_count,
            total_count
        );
    }
}

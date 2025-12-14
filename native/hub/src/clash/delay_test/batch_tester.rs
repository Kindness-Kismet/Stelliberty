// Clash 延迟批量测试器
//
// 目的：通过 IPC 批量测试节点延迟，支持滑动窗口并发

use futures_util::stream::{self, StreamExt};
use std::sync::Arc;
use tokio::sync::Semaphore;

use crate::clash::network::internal_ipc_get;

// 批量测试结果
#[derive(Debug, Clone)]
#[allow(dead_code)] // node_name 字段供 Dart 层使用
pub struct BatchTestResult {
    pub node_name: String,
    pub delay_ms: i32, // -1 表示测试失败
}

// 批量测试延迟
//
// [node_names] 要测试的节点名称列表
// [test_url] 测试 URL
// [timeout_ms] 超时时间（毫秒）
// [concurrency] 并发数（滑动窗口大小）
// [progress_callback] 进度回调（每个节点测试完成后调用）
//
// 返回：所有节点的测试结果 Vec
pub async fn batch_test_delays(
    node_names: Vec<String>,
    test_url: String,
    timeout_ms: u32,
    concurrency: usize,
    progress_callback: Arc<dyn Fn(String, i32) + Send + Sync>,
) -> Vec<BatchTestResult> {
    if node_names.is_empty() {
        log::warn!("批量延迟测试：节点列表为空");
        return Vec::new();
    }

    let total = node_names.len();
    log::info!(
        "开始批量延迟测试，节点数：{}，并发数：{}",
        total,
        concurrency
    );

    // 信号量控制并发数（滑动窗口）
    let semaphore = Arc::new(Semaphore::new(concurrency));
    let test_url = Arc::new(test_url);

    // 创建测试任务流
    let tasks = stream::iter(node_names.into_iter().enumerate())
        .map(|(index, node_name)| {
            let semaphore = Arc::clone(&semaphore);
            let test_url = Arc::clone(&test_url);
            let progress_callback = Arc::clone(&progress_callback);

            async move {
                // 获取信号量许可（阻塞，直到有空闲槽位）
                let _permit = semaphore.acquire().await.ok()?;

                log::debug!(
                    "开始测试节点 ({}/{}): {}",
                    index + 1,
                    total,
                    node_name
                );

                // 执行单个节点的延迟测试
                let delay_ms = test_single_node(&node_name, &test_url, timeout_ms).await;

                // 触发进度回调
                progress_callback(node_name.clone(), delay_ms);

                Some(BatchTestResult {
                    node_name,
                    delay_ms,
                })
            }
        })
        .buffer_unordered(concurrency) // 滑动窗口并发执行
        .filter_map(|x| async { x }); // 过滤掉 None

    // 收集所有结果
    let results: Vec<BatchTestResult> = tasks.collect().await;

    let success_count = results.iter().filter(|r| r.delay_ms > 0).count();
    log::info!(
        "批量延迟测试完成，成功：{}/{}",
        success_count,
        total
    );

    results
}

// 测试单个节点的延迟
//
// 通过 IPC 调用 Clash API: GET /proxies/{proxyName}/delay?timeout={timeout}&url={testUrl}
async fn test_single_node(node_name: &str, test_url: &str, timeout_ms: u32) -> i32 {
    // 构建 Clash API 路径
    let encoded_name = urlencoding::encode(node_name);
    let path = format!(
        "/proxies/{}/delay?timeout={}&url={}",
        encoded_name, timeout_ms, test_url
    );

    log::debug!("测试节点延迟：{}", node_name);

    // 发送 IPC GET 请求
    match internal_ipc_get(&path).await {
        Ok(body) => {
            // 解析 JSON 响应：{"delay": 123}
            match serde_json::from_str::<serde_json::Value>(&body) {
                Ok(json) => {
                    if let Some(delay) = json.get("delay").and_then(|v| v.as_i64()) {
                        let delay_i32 = delay as i32;
                        if delay_i32 > 0 {
                            log::info!("节点延迟测试成功：{} - {}ms", node_name, delay_i32);
                        } else {
                            log::warn!("节点延迟测试失败：{} - 超时", node_name);
                        }
                        delay_i32
                    } else {
                        log::error!("节点延迟测试响应格式错误：{}", node_name);
                        -1
                    }
                }
                Err(e) => {
                    log::error!("节点延迟测试 JSON 解析失败：{} - {}", node_name, e);
                    -1
                }
            }
        }
        Err(e) => {
            log::debug!("节点延迟测试失败：{} - {}", node_name, e);
            -1
        }
    }
}

// Clash 延迟测试模块

use once_cell::sync::Lazy;
use rinf::{DartSignal, RustSignal};
use serde::{Deserialize, Serialize};
use std::collections::{HashMap, VecDeque};
use std::sync::{Arc, Mutex, MutexGuard};
use std::time::{Duration, Instant};
use tokio::spawn;
use tokio::sync::watch;
use tokio::task::JoinSet;

use crate::atoms::IpcClient;

// Dart → Rust：取消测速请求
#[derive(Deserialize, DartSignal)]
pub struct CancelDelayTestsRequest {
    pub request_id: i64,
}

// Dart → Rust：单节点延迟测试请求
#[derive(Deserialize, DartSignal)]
pub struct SingleDelayTestRequest {
    pub request_id: i64,
    pub node_name: String,
    pub test_url: String,
    pub timeout_ms: u32,
}

// Rust → Dart：单节点延迟测试结果
#[derive(Serialize, RustSignal)]
pub struct SingleDelayTestResult {
    pub request_id: i64,
    pub node_name: String,
    pub delay_ms: i32, // -1 表示失败
    pub is_cancelled: bool,
}

// Dart → Rust：批量延迟测试请求
#[derive(Deserialize, DartSignal)]
pub struct BatchDelayTestRequest {
    pub request_id: i64,
    pub node_names: Vec<String>,
    pub test_url: String,
    pub timeout_ms: u32,
    pub concurrency: u32,
}

// Rust → Dart：单个节点测试完成（流式进度更新）
#[derive(Serialize, RustSignal)]
pub struct DelayTestProgress {
    pub request_id: i64,
    pub node_name: String,
    pub delay_ms: i32, // -1 表示失败
}

// Rust → Dart：批量测试完成
#[derive(Serialize, RustSignal)]
pub struct BatchDelayTestComplete {
    pub request_id: i64,
    pub is_successful: bool,
    pub is_cancelled: bool,
    pub total_count: u32,
    pub success_count: u32,
    pub error_message: Option<String>,
}

// 批量测试结果
#[derive(Debug, Clone)]
#[allow(dead_code)]
pub struct BatchTestResult {
    pub node_name: String,
    pub delay_ms: i32,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
enum DelayTestSessionKind {
    Single,
    Batch,
}

impl DelayTestSessionKind {
    fn label(self) -> &'static str {
        match self {
            Self::Single => "单节点",
            Self::Batch => "批量",
        }
    }
}

#[derive(Debug)]
struct DelayTestSessionState {
    kind: DelayTestSessionKind,
    is_cancelled: bool,
    cancel_tx: watch::Sender<bool>,
}

impl DelayTestSessionState {
    fn new(kind: DelayTestSessionKind, cancel_tx: watch::Sender<bool>) -> Self {
        Self {
            kind,
            is_cancelled: false,
            cancel_tx,
        }
    }

    fn cancel(&mut self) {
        if self.is_cancelled {
            return;
        }

        self.is_cancelled = true;
        let _ = self.cancel_tx.send(true);
    }
}

#[derive(Clone)]
struct DelayTestSessionHandle {
    request_id: i64,
    cancel_rx: watch::Receiver<bool>,
}

impl DelayTestSessionHandle {
    fn is_cancelled(&self) -> bool {
        *self.cancel_rx.borrow()
    }

    fn subscribe(&self) -> watch::Receiver<bool> {
        self.cancel_rx.clone()
    }
}

enum NodeDelayTestOutcome {
    Completed(i32),
    Cancelled,
}

enum BatchNodeTestOutcome {
    Completed(BatchTestResult),
    Cancelled { node_name: String },
}

static DELAY_TEST_SESSIONS: Lazy<Mutex<HashMap<i64, DelayTestSessionState>>> =
    Lazy::new(|| Mutex::new(HashMap::new()));

pub fn init() {
    // 取消测速请求监听器
    spawn(async {
        let receiver = CancelDelayTestsRequest::get_dart_signal_receiver();
        while let Some(dart_signal) = receiver.recv().await {
            spawn(async move {
                handle_cancel_delay_tests_request(dart_signal.message).await;
            });
        }
        log::info!("取消测速消息通道已关闭，退出监听器");
    });

    // 单节点延迟测试请求监听器
    spawn(async {
        let receiver = SingleDelayTestRequest::get_dart_signal_receiver();
        while let Some(dart_signal) = receiver.recv().await {
            spawn(async move {
                handle_single_delay_test_request(dart_signal.message).await;
            });
        }
        log::info!("单节点延迟测试消息通道已关闭，退出监听器");
    });

    // 批量延迟测试请求监听器
    spawn(async {
        let receiver = BatchDelayTestRequest::get_dart_signal_receiver();
        while let Some(dart_signal) = receiver.recv().await {
            spawn(async move {
                handle_batch_delay_test_request(dart_signal.message).await;
            });
        }
        log::info!("批量延迟测试消息通道已关闭，退出监听器");
    });
}

fn lock_delay_test_sessions() -> MutexGuard<'static, HashMap<i64, DelayTestSessionState>> {
    match DELAY_TEST_SESSIONS.lock() {
        Ok(guard) => guard,
        Err(e) => {
            log::error!("延迟测试活动状态锁已中毒，继续使用恢复后的状态");
            e.into_inner()
        }
    }
}

fn register_delay_test_session(
    request_id: i64,
    kind: DelayTestSessionKind,
) -> DelayTestSessionHandle {
    let (cancel_tx, cancel_rx) = watch::channel(false);

    let session = DelayTestSessionState::new(kind, cancel_tx);

    {
        let mut sessions = lock_delay_test_sessions();
        sessions.insert(request_id, session);
    }

    DelayTestSessionHandle {
        request_id,
        cancel_rx,
    }
}

fn cancel_delay_test_session(request_id: i64) {
    let cancelled_kind = {
        let mut sessions = lock_delay_test_sessions();
        if let Some(session) = sessions.get_mut(&request_id) {
            session.cancel();
            Some(session.kind.label())
        } else {
            None
        }
    };

    if let Some(session_kind) = cancelled_kind {
        log::info!(
            "已取消测速会话：request_id={}，类型={}",
            request_id,
            session_kind
        );
    }
}

fn finish_delay_test_session(session: &DelayTestSessionHandle) -> bool {
    let mut sessions = lock_delay_test_sessions();
    let Some(active_session) = sessions.remove(&session.request_id) else {
        return session.is_cancelled();
    };

    active_session.is_cancelled
}

async fn wait_for_delay_test_cancel(mut cancel_rx: watch::Receiver<bool>) {
    if *cancel_rx.borrow() {
        return;
    }

    while cancel_rx.changed().await.is_ok() {
        if *cancel_rx.borrow() {
            return;
        }
    }
}

async fn test_single_node_with_cancel(
    request_id: i64,
    node_name: &str,
    test_url: &str,
    timeout_ms: u32,
    cancel_rx: watch::Receiver<bool>,
) -> NodeDelayTestOutcome {
    tokio::select! {
        biased;
        _ = wait_for_delay_test_cancel(cancel_rx) => {
            log::info!("节点延迟测试已取消：request_id={}，{}", request_id, node_name);
            NodeDelayTestOutcome::Cancelled
        }
        delay_ms = test_single_node(node_name, test_url, timeout_ms) => {
            NodeDelayTestOutcome::Completed(delay_ms)
        }
    }
}

async fn handle_cancel_delay_tests_request(request: CancelDelayTestsRequest) {
    log::info!("收到取消测速请求：request_id={}", request.request_id);
    cancel_delay_test_session(request.request_id);
}

// 处理单节点延迟测试请求
async fn handle_single_delay_test_request(request: SingleDelayTestRequest) {
    let SingleDelayTestRequest {
        request_id,
        node_name,
        test_url,
        timeout_ms,
    } = request;

    log::info!(
        "收到单节点延迟测试请求：request_id={}，{}（timeout {}ms，url={}）",
        request_id,
        node_name,
        timeout_ms,
        test_url
    );

    let session = register_delay_test_session(request_id, DelayTestSessionKind::Single);

    let outcome = test_single_node_with_cancel(
        request_id,
        &node_name,
        &test_url,
        timeout_ms,
        session.subscribe(),
    )
    .await;
    let delay_ms = match outcome {
        NodeDelayTestOutcome::Completed(delay_ms) => delay_ms,
        NodeDelayTestOutcome::Cancelled => -1,
    };
    let is_cancelled =
        matches!(outcome, NodeDelayTestOutcome::Cancelled) || finish_delay_test_session(&session);

    SingleDelayTestResult {
        request_id,
        node_name,
        delay_ms,
        is_cancelled,
    }
    .send_signal_to_dart();
}

// 处理批量延迟测试请求
async fn handle_batch_delay_test_request(request: BatchDelayTestRequest) {
    let BatchDelayTestRequest {
        request_id,
        node_names,
        test_url,
        timeout_ms,
        concurrency,
    } = request;

    let total_count = node_names.len() as u32;
    let requested_concurrency = concurrency.max(1) as usize;
    let actual_concurrency = requested_concurrency.min(node_names.len().max(1));

    log::info!(
        "收到批量延迟测试请求：request_id={}，节点数：{}，并发数：{}（请求 {}），timeout {}ms，url={}",
        request_id,
        total_count,
        actual_concurrency,
        requested_concurrency,
        timeout_ms,
        test_url
    );

    let session = register_delay_test_session(request_id, DelayTestSessionKind::Batch);

    // 进度回调：每个节点测试完成后发送进度信号。
    let progress_session = session.clone();
    let on_progress = Arc::new(move |node_name: String, delay_ms: i32| {
        if progress_session.is_cancelled() {
            log::debug!(
                "批量延迟测试已取消，跳过进度信号：request_id={}，{}",
                request_id,
                node_name
            );
            return;
        }

        DelayTestProgress {
            request_id,
            node_name,
            delay_ms,
        }
        .send_signal_to_dart();
    });

    // 执行批量测试
    let results = batch_test_delays(
        session.clone(),
        node_names,
        test_url,
        timeout_ms,
        actual_concurrency,
        on_progress,
    )
    .await;

    // 统计成功数量
    let success_count = results.iter().filter(|result| result.delay_ms > 0).count() as u32;
    let is_cancelled = session.is_cancelled() || finish_delay_test_session(&session);

    // 发送完成信号
    BatchDelayTestComplete {
        request_id,
        is_successful: !is_cancelled,
        is_cancelled,
        total_count,
        success_count,
        error_message: None,
    }
    .send_signal_to_dart();

    log::info!(
        "批量延迟测试完成：request_id={}，成功：{}/{}，is_cancelled={}",
        request_id,
        success_count,
        total_count,
        is_cancelled
    );
}

// 批量延迟测试：并发受限的滑动窗口。
// 返回所有节点的测试结果列表。
async fn batch_test_delays(
    session: DelayTestSessionHandle,
    node_names: Vec<String>,
    test_url: String,
    timeout_ms: u32,
    concurrency: usize,
    on_progress: Arc<dyn Fn(String, i32) + Send + Sync>,
) -> Vec<BatchTestResult> {
    if node_names.is_empty() {
        log::warn!("批量延迟测试：节点列表为空");
        return Vec::new();
    }

    let total = node_names.len();
    let test_url = Arc::new(test_url);
    let mut pending_tasks = JoinSet::new();
    let mut remaining_nodes: VecDeque<(usize, String)> =
        node_names.into_iter().enumerate().collect();
    let mut results = Vec::new();

    loop {
        while pending_tasks.len() < concurrency && !session.is_cancelled() {
            let Some((index, node_name)) = remaining_nodes.pop_front() else {
                break;
            };

            let node_session = session.clone();
            let test_url = Arc::clone(&test_url);
            pending_tasks.spawn(async move {
                log::debug!("开始测试节点 ({}/{}): {}", index + 1, total, node_name);

                match test_single_node_with_cancel(
                    node_session.request_id,
                    &node_name,
                    test_url.as_str(),
                    timeout_ms,
                    node_session.subscribe(),
                )
                .await
                {
                    NodeDelayTestOutcome::Completed(delay_ms) => {
                        BatchNodeTestOutcome::Completed(BatchTestResult {
                            node_name,
                            delay_ms,
                        })
                    }
                    NodeDelayTestOutcome::Cancelled => {
                        BatchNodeTestOutcome::Cancelled { node_name }
                    }
                }
            });
        }

        let Some(join_result) = pending_tasks.join_next().await else {
            break;
        };

        match join_result {
            Ok(BatchNodeTestOutcome::Completed(result)) => {
                on_progress(result.node_name.clone(), result.delay_ms);
                results.push(result);
            }
            Ok(BatchNodeTestOutcome::Cancelled { node_name }) => {
                log::debug!(
                    "批量延迟测试节点已取消：request_id={}，{}",
                    session.request_id,
                    node_name
                );
            }
            Err(e) => {
                log::error!(
                    "批量延迟测试节点任务异常结束：request_id={}，{}",
                    session.request_id,
                    e
                );
            }
        }
    }

    results
}

fn timeout_result(node_name: &str, timeout_ms: u32, elapsed_ms: u128, retry_count: u32) -> i32 {
    log::warn!(
        "节点延迟测试超时：{} - 超过 {}ms（耗时 {}ms，重试 {} 次）",
        node_name,
        timeout_ms,
        elapsed_ms,
        retry_count
    );
    -1
}

// 测试单个节点延迟：通过 IPC 调用 Clash API。
// GET /proxies/{proxyName}/delay?timeout={timeout}&url={testUrl}
async fn test_single_node(node_name: &str, test_url: &str, timeout_ms: u32) -> i32 {
    // 构建 Clash API 路径
    let encoded_name = urlencoding::encode(node_name);
    let path = format!(
        "/proxies/{}/delay?timeout={}&url={}",
        encoded_name, timeout_ms, test_url
    );

    let start_time = Instant::now();
    let timeout = Duration::from_millis(timeout_ms as u64);
    let response = tokio::time::timeout(timeout, IpcClient::get_with_pool(&path)).await;

    match response {
        Ok(result) => match result {
            Ok(body) => match serde_json::from_str::<serde_json::Value>(&body) {
                Ok(json) => {
                    if let Some(delay) = json.get("delay").and_then(|value| value.as_i64()) {
                        let delay_i32 = delay as i32;
                        let elapsed_ms = start_time.elapsed().as_millis();
                        if delay_i32 > 0 {
                            log::info!(
                                "节点延迟测试成功：{} - {}ms（耗时 {}ms，重试 0 次）",
                                node_name,
                                delay_i32,
                                elapsed_ms
                            );
                        } else {
                            log::warn!(
                                "节点延迟测试失败：{} - 超时（耗时 {}ms，重试 0 次）",
                                node_name,
                                elapsed_ms
                            );
                        }
                        return delay_i32;
                    }
                    log::error!("节点延迟测试响应格式错误：{}", node_name);
                    -1
                }
                Err(e) => {
                    log::error!("节点延迟测试 JSON 解析失败：{} - {}", node_name, e);
                    -1
                }
            },
            Err(e) => {
                if e.contains("HTTP 503") || e.contains("HTTP 504") {
                    return timeout_result(
                        node_name,
                        timeout_ms,
                        start_time.elapsed().as_millis(),
                        0,
                    );
                }

                log::warn!("节点延迟测试 IPC 请求失败：{} - {}", node_name, e);
                -1
            }
        },
        Err(_) => timeout_result(node_name, timeout_ms, start_time.elapsed().as_millis(), 0),
    }
}

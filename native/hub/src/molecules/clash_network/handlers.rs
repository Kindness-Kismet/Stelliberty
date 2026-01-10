// IPC 请求处理器
//
// 处理 Dart 层发送的 IPC 请求，通过 IpcClient 转发给 Clash 核心

use super::ipc_client::IpcClient;
use super::ws_client::WebSocketClient;
use once_cell::sync::Lazy;
use rinf::{DartSignal, RustSignal};
use serde::{Deserialize, Serialize};
use std::collections::VecDeque;
use std::sync::Arc;
use std::time::{Duration, Instant};
use tokio::sync::{RwLock, Semaphore};

#[cfg(unix)]
use tokio::net::UnixStream;

#[cfg(windows)]
use tokio::net::windows::named_pipe::NamedPipeClient;

// Dart → Rust：通过 IPC 发送 GET 请求
#[derive(Deserialize, DartSignal)]
pub struct IpcGetRequest {
    pub request_id: i64,
    pub path: String,
}

// Dart → Rust：通过 IPC 发送 POST 请求
#[derive(Deserialize, DartSignal)]
pub struct IpcPostRequest {
    pub request_id: i64,
    pub path: String,
    pub body: Option<String>,
}

// Dart → Rust：通过 IPC 发送 PUT 请求
#[derive(Deserialize, DartSignal)]
pub struct IpcPutRequest {
    pub request_id: i64,
    pub path: String,
    pub body: Option<String>,
}

// Dart → Rust：通过 IPC 发送 PATCH 请求
#[derive(Deserialize, DartSignal)]
pub struct IpcPatchRequest {
    pub request_id: i64,
    pub path: String,
    pub body: Option<String>,
}

// Dart → Rust：通过 IPC 发送 DELETE 请求
#[derive(Deserialize, DartSignal)]
pub struct IpcDeleteRequest {
    pub request_id: i64,
    pub path: String,
}

// Rust → Dart：IPC 请求响应
#[derive(Serialize, RustSignal)]
pub struct IpcResponse {
    // 请求 ID（用于匹配请求和响应）
    pub request_id: i64,
    // HTTP 状态码
    pub status_code: u16,
    // 响应体（JSON 字符串）
    pub body: String,
    // 是否成功
    pub is_successful: bool,
    // 错误消息（如果有）
    pub error_message: Option<String>,
}

// WebSocket 流式数据

// Dart → Rust：开始监听 Clash 日志
#[derive(Deserialize, DartSignal)]
pub struct StartLogStream;

// Dart → Rust：停止监听 Clash 日志
#[derive(Deserialize, DartSignal)]
pub struct StopLogStream;

// Rust → Dart：Clash 日志数据
#[derive(Serialize, RustSignal)]
pub struct IpcLogData {
    pub log_type: String,
    pub payload: String,
}

// Dart → Rust：开始监听流量数据
#[derive(Deserialize, DartSignal)]
pub struct StartTrafficStream;

// Dart → Rust：停止监听流量数据
#[derive(Deserialize, DartSignal)]
pub struct StopTrafficStream;

// Rust → Dart：流量数据
#[derive(Serialize, RustSignal)]
pub struct IpcTrafficData {
    pub upload: u64,
    pub download: u64,
}

// Rust → Dart：流操作结果
#[derive(Serialize, RustSignal)]
pub struct StreamResult {
    pub is_successful: bool,
    pub error_message: Option<String>,
}

// 检查错误是否为 IPC 尚未就绪（启动时的正常情况）
fn is_ipc_not_ready_error(error_msg: &str) -> bool {
    // Windows: os error 2 (系统找不到指定的文件)
    // Linux: os error 111 (ECONNREFUSED，拒绝连接)
    // macOS: os error 61 (ECONNREFUSED，拒绝连接)
    error_msg.contains("系统找不到指定的文件")
        || error_msg.contains("os error 2")
        || error_msg.contains("拒绝连接")
        || error_msg.contains("os error 111")
        || error_msg.contains("os error 61")
        || error_msg.contains("Connection refused")
}

// 检查错误是否需要重试（连接失效但非 IPC 未就绪）
fn should_retry_on_error(error_msg: &str, attempt: usize, max_retries: usize) -> bool {
    attempt < max_retries
        && !is_ipc_not_ready_error(error_msg)
        && (error_msg.contains("os error")
            || error_msg.contains("系统找不到指定的文件")
            || error_msg.contains("Connection refused")
            || error_msg.contains("Broken pipe"))
}

// 公共函数：处理 IPC 请求的核心逻辑（带自动重试）
//
// 参数：
// - method: HTTP 方法名（"GET"/"POST"/"PUT"/"PATCH"/"DELETE"）
// - path: 请求路径
// - body: 请求体（Option<&str>）
// - request_id: 请求 ID
// - log_response: 是否记录响应体（仅 GET 请求）
async fn handle_ipc_request_with_retry(
    method: &str,
    path: &str,
    body: Option<&str>,
    request_id: i64,
    should_log_response: bool,
) {
    const MAX_RETRIES: usize = 2;

    for attempt in 0..=MAX_RETRIES {
        // 从连接池获取连接
        let ipc_conn = match acquire_connection().await {
            Ok(c) => c,
            Err(e) => {
                let error_msg = e.to_string();
                if is_ipc_not_ready_error(&error_msg) {
                    log::trace!("IPC {} 请求等待中：{}，原因：IPC 尚未就绪", method, path);
                } else {
                    log::error!("IPC {} 获取连接失败：{}，error：{}", method, path, e);
                }

                IpcResponse {
                    request_id,
                    status_code: 0,
                    body: String::new(),
                    is_successful: false,
                    error_message: Some(format!("获取连接失败：{}", e)),
                }
                .send_signal_to_dart();
                return;
            }
        };

        // 使用连接发送请求
        match IpcClient::request_with_connection(method, path, body, ipc_conn).await {
            Ok((response, ipc_conn)) => {
                // 归还连接
                release_connection(ipc_conn).await;

                // 特殊日志处理（仅 GET 请求）
                if should_log_response {
                    if response.body.len() > 200 {
                        let preview = response.body.chars().take(100).collect::<String>();
                        log::trace!(
                            "响应体内容（截断）：{}…[总长度：{}字节]",
                            preview,
                            response.body.len()
                        );
                    } else {
                        log::trace!("响应体内容：{}", response.body);
                    }
                }

                IpcResponse {
                    request_id,
                    status_code: response.status_code,
                    body: response.body,
                    is_successful: true,
                    error_message: None,
                }
                .send_signal_to_dart();
                return;
            }
            Err(e) => {
                // 连接已失效，不归还
                let error_msg = e.to_string();

                // 检查是否需要重试
                if should_retry_on_error(&error_msg, attempt, MAX_RETRIES) {
                    log::warn!(
                        "IPC {} 请求失败（第 {} 次尝试），清空连接池后重试：{}，error：{}",
                        method,
                        attempt + 1,
                        path,
                        e
                    );

                    // 清空连接池（连接可能在系统休眠后失效）
                    cleanup_ipc_connection_pool().await;

                    // 等待 200ms 后重试
                    tokio::time::sleep(tokio::time::Duration::from_millis(200)).await;
                    continue;
                }

                // 不重试，返回错误
                if is_ipc_not_ready_error(&error_msg) {
                    log::trace!("IPC {} 请求等待中：{}，原因：IPC 尚未就绪", method, path);
                } else {
                    log::error!("IPC {} 请求失败：{}，error：{}", method, path, e);
                }

                IpcResponse {
                    request_id,
                    status_code: 0,
                    body: String::new(),
                    is_successful: false,
                    error_message: Some(format!("IPC 请求失败：{}", e)),
                }
                .send_signal_to_dart();
                return;
            }
        }
    }
}

// 连接池配置
const MAX_POOL_SIZE: usize = 30; // 连接池上限
const IDLE_TIMEOUT_MS: u64 = 35000; // 35 秒空闲超时（大于健康检查周期 30 秒，避免批量延迟测试期间连接被误删）
const MAX_CONCURRENT_CONNECTIONS: usize = 20; // IPC 最大并发连接创建数（限制新连接创建速度，避免冲击 IPC 服务器）

// 连接包装器
struct PooledConnection {
    #[cfg(windows)]
    conn: NamedPipeClient,
    #[cfg(unix)]
    conn: UnixStream,
    last_used: Instant,
}

impl PooledConnection {
    // 检查连接是否有效（主动探测）
    fn is_valid(&self) -> bool {
        use std::io::ErrorKind;

        let mut buf = [0u8; 1];
        match self.conn.try_read(&mut buf) {
            Ok(0) => false,                                      // 连接已关闭
            Ok(_) => true, // 有数据可读（不应发生，但连接有效）
            Err(e) if e.kind() == ErrorKind::WouldBlock => true, // 无数据但连接正常
            Err(_) => false, // 其他错误表示连接失效
        }
    }
}

// 全局 IPC 连接池（使用 VecDeque 实现 FIFO）
static IPC_CONNECTION_POOL: Lazy<Arc<RwLock<VecDeque<PooledConnection>>>> =
    Lazy::new(|| Arc::new(RwLock::new(VecDeque::new())));

// 连接创建信号量（限制并发连接数，避免 Named Pipe 服务器过载）
static CONNECTION_SEMAPHORE: Lazy<Arc<Semaphore>> =
    Lazy::new(|| Arc::new(Semaphore::new(MAX_CONCURRENT_CONNECTIONS)));

// 配置更新信号量（限制并发为 1，防止竞态条件）
static CONFIG_UPDATE_SEMAPHORE: Lazy<Arc<Semaphore>> = Lazy::new(|| Arc::new(Semaphore::new(1)));

// 启动连接池健康检查（30 秒间隔）
pub fn start_connection_pool_health_check() {
    tokio::spawn(async {
        let mut interval = tokio::time::interval(Duration::from_secs(30));
        interval.tick().await; // 跳过首次立即触发

        loop {
            interval.tick().await;

            // 健康检查（使用 try_write 避免阻塞）
            let mut pool = match IPC_CONNECTION_POOL.try_write() {
                Ok(pool) => pool,
                Err(_) => {
                    log::trace!("健康检查：连接池繁忙，跳过本轮");
                    continue;
                }
            };

            let initial_count = pool.len();
            if initial_count == 0 {
                continue; // 连接池为空，跳过
            }

            log::trace!("开始连接池健康检查（当前 {} 个连接）", initial_count);

            // 检查并移除失效连接（时间过期 + 连接状态检查）
            pool.retain(|pooled_conn| {
                pooled_conn.last_used.elapsed() < Duration::from_millis(IDLE_TIMEOUT_MS)
                    && pooled_conn.is_valid()
            });

            let removed = initial_count - pool.len();
            if removed > 0 {
                log::info!(
                    "健康检查：移除{}个过期连接（剩余{}个）",
                    removed,
                    pool.len()
                );
            } else {
                log::trace!("健康检查完成：所有连接正常（{}个）", pool.len());
            }
        }
    });

    log::info!("连接池健康检查已启动（30秒间隔）");
}

// 连接获取通用逻辑宏（消除 Windows 和 Unix 平台的重复代码）
macro_rules! acquire_connection_with_retry {
    ($connect_fn:expr, $conn_type:literal) => {{
        // 1. 尝试从池中获取（FIFO + 有效性检查）
        loop {
            let mut pool = IPC_CONNECTION_POOL.write().await;

            if let Some(pooled) = pool.pop_front() {
                // 检查连接是否过期或失效
                if pooled.last_used.elapsed() < Duration::from_millis(IDLE_TIMEOUT_MS)
                    && pooled.is_valid()
                {
                    log::trace!("从连接池获取连接（剩余{}）", pool.len());
                    return Ok(pooled.conn);
                }
                // 连接已过期或失效，丢弃并继续尝试下一个
                log::trace!("连接失效，丢弃并尝试下一个");
                continue;
            }

            // 连接池为空，释放锁后创建新连接
            drop(pool);
            break;
        }

        // 2. 获取连接创建信号量（限制并发连接数）
        let _permit = CONNECTION_SEMAPHORE
            .acquire()
            .await
            .map_err(|e| format!("获取连接信号量失败：{}", e))?;

        log::trace!("连接池为空，创建新连接（信号量已获取）");

        // 3. 带重试的连接创建（最多 3 次尝试，每次间隔 50ms）
        const MAX_CONNECT_RETRIES: usize = 3;
        const RETRY_DELAY_MS: u64 = 50;

        for attempt in 0..MAX_CONNECT_RETRIES {
            match $connect_fn.await {
                Ok(conn) => {
                    if attempt > 0 {
                        log::debug!("{} 连接成功（第 {} 次尝试）", $conn_type, attempt + 1);
                    }
                    return Ok(conn);
                }
                Err(e) if attempt < MAX_CONNECT_RETRIES - 1 => {
                    log::trace!(
                        "{} 连接失败（第 {} 次），{}ms 后重试：{}",
                        $conn_type,
                        attempt + 1,
                        RETRY_DELAY_MS,
                        e
                    );
                    tokio::time::sleep(Duration::from_millis(RETRY_DELAY_MS)).await;
                    continue;
                }
                Err(e) => {
                    return Err(format!(
                        "{} 连接失败（已重试 {} 次）：{}",
                        $conn_type, MAX_CONNECT_RETRIES, e
                    ));
                }
            }
        }

        unreachable!()
    }};
}

// 从连接池获取连接（如果没有则创建新的）
#[cfg(windows)]
async fn acquire_connection() -> Result<NamedPipeClient, String> {
    acquire_connection_with_retry!(
        super::connection::connect_named_pipe(&IpcClient::default_ipc_path()),
        "Named Pipe"
    )
}

#[cfg(unix)]
async fn acquire_connection() -> Result<UnixStream, String> {
    acquire_connection_with_retry!(
        super::connection::connect_unix_socket(&IpcClient::default_ipc_path()),
        "Unix Socket"
    )
}

// 归还连接到池中通用逻辑宏
macro_rules! release_connection_impl {
    ($conn:expr) => {{
        let mut pool = IPC_CONNECTION_POOL.write().await;

        if pool.len() < MAX_POOL_SIZE {
            pool.push_back(PooledConnection {
                conn: $conn,
                last_used: Instant::now(),
            });
            log::trace!("归还连接到池（当前{}）", pool.len());
        } else {
            log::trace!("连接池已满，丢弃连接");
        }
    }};
}

// 归还连接到池中（FIFO：从尾部加入）
#[cfg(windows)]
async fn release_connection(conn: NamedPipeClient) {
    release_connection_impl!(conn);
}

#[cfg(unix)]
async fn release_connection(conn: UnixStream) {
    release_connection_impl!(conn);
}

// 全局 WebSocket 客户端实例
static WS_CLIENT: Lazy<Arc<RwLock<Option<WebSocketClient>>>> =
    Lazy::new(|| Arc::new(RwLock::new(None)));

// 存储当前的流量监控连接 ID
static TRAFFIC_CONNECTION_ID: Lazy<Arc<RwLock<Option<u32>>>> =
    Lazy::new(|| Arc::new(RwLock::new(None)));

// 存储当前的日志监控连接 ID
static LOG_CONNECTION_ID: Lazy<Arc<RwLock<Option<u32>>>> =
    Lazy::new(|| Arc::new(RwLock::new(None)));

// 确保 WebSocket 客户端已初始化（统一入口）
async fn ensure_ws_client_initialized() {
    let mut client_guard = WS_CLIENT.write().await;
    if client_guard.is_none() {
        let ipc_path = IpcClient::default_ipc_path();
        *client_guard = Some(WebSocketClient::new(ipc_path));
        log::debug!("WebSocket 客户端已初始化");
    }
}

// 清理 IPC 连接池（在 Clash 停止时调用）
pub async fn cleanup_ipc_connection_pool() -> usize {
    let mut pool = IPC_CONNECTION_POOL.write().await;
    let count = pool.len();
    pool.clear();
    count
}

// 清理 WebSocket 客户端（在 Clash 停止时调用）
pub async fn cleanup_ws_client() -> bool {
    let mut client_guard = WS_CLIENT.write().await;
    if let Some(ws_client) = client_guard.take() {
        ws_client.disconnect_all().await;
        true
    } else {
        false
    }
}

// 清理所有网络资源（在 Clash 停止时调用的统一入口）
pub async fn cleanup_all_network_resources() {
    // 1. 清理 WebSocket 连接
    let ws_cleaned = cleanup_ws_client().await;

    // 2. 清理 IPC 连接池
    let ipc_count = cleanup_ipc_connection_pool().await;

    log::info!(
        "网络资源已清理（WebSocket={}, IPC连接池={}个）",
        if ws_cleaned { "是" } else { "否" },
        ipc_count
    );
}

// GET 请求处理器
impl IpcGetRequest {
    pub fn handle(self) {
        tokio::spawn(async move {
            handle_ipc_request_with_retry("GET", &self.path, None, self.request_id, true).await;
        });
    }
}

// POST 请求处理器
impl IpcPostRequest {
    pub fn handle(self) {
        tokio::spawn(async move {
            handle_ipc_request_with_retry(
                "POST",
                &self.path,
                self.body.as_deref(),
                self.request_id,
                false,
            )
            .await;
        });
    }
}

// PUT 请求处理器（需要获取配置更新信号量）
impl IpcPutRequest {
    pub fn handle(self) {
        tokio::spawn(async move {
            // 获取配置更新信号量，防止并发配置修改
            let _permit = match CONFIG_UPDATE_SEMAPHORE.acquire().await {
                Ok(permit) => permit,
                Err(e) => {
                    log::error!("获取配置更新信号量失败：{}", e);
                    IpcResponse {
                        request_id: self.request_id,
                        status_code: 0,
                        body: String::new(),
                        is_successful: false,
                        error_message: Some(format!("获取配置更新信号量失败：{}", e)),
                    }
                    .send_signal_to_dart();
                    return;
                }
            };

            handle_ipc_request_with_retry(
                "PUT",
                &self.path,
                self.body.as_deref(),
                self.request_id,
                false,
            )
            .await;
        });
    }
}

// PATCH 请求处理器
impl IpcPatchRequest {
    pub fn handle(self) {
        tokio::spawn(async move {
            handle_ipc_request_with_retry(
                "PATCH",
                &self.path,
                self.body.as_deref(),
                self.request_id,
                false,
            )
            .await;
        });
    }
}

// DELETE 请求处理器
impl IpcDeleteRequest {
    pub fn handle(self) {
        tokio::spawn(async move {
            handle_ipc_request_with_retry("DELETE", &self.path, None, self.request_id, false).await;
        });
    }
}

// 初始化 IPC REST API 消息监听器
pub fn init_rest_api_listeners() {
    log::info!("初始化 IPC REST API 监听器");

    // 启动连接池健康检查
    start_connection_pool_health_check();

    tokio::spawn(async {
        let receiver = IpcGetRequest::get_dart_signal_receiver();
        while let Some(dart_signal) = receiver.recv().await {
            dart_signal.message.handle();
        }
    });

    tokio::spawn(async {
        let receiver = IpcPostRequest::get_dart_signal_receiver();
        while let Some(dart_signal) = receiver.recv().await {
            dart_signal.message.handle();
        }
    });

    tokio::spawn(async {
        let receiver = IpcPutRequest::get_dart_signal_receiver();
        while let Some(dart_signal) = receiver.recv().await {
            dart_signal.message.handle();
        }
    });

    tokio::spawn(async {
        let receiver = IpcPatchRequest::get_dart_signal_receiver();
        while let Some(dart_signal) = receiver.recv().await {
            dart_signal.message.handle();
        }
    });

    tokio::spawn(async {
        let receiver = IpcDeleteRequest::get_dart_signal_receiver();
        while let Some(dart_signal) = receiver.recv().await {
            dart_signal.message.handle();
        }
    });

    // WebSocket 流式数据监听器
    tokio::spawn(async {
        let receiver = StartTrafficStream::get_dart_signal_receiver();
        while let Some(_dart_signal) = receiver.recv().await {
            StartTrafficStream::handle_start().await;
        }
    });

    tokio::spawn(async {
        let receiver = StopTrafficStream::get_dart_signal_receiver();
        while let Some(_dart_signal) = receiver.recv().await {
            StopTrafficStream::handle_stop().await;
        }
    });

    tokio::spawn(async {
        let receiver = StartLogStream::get_dart_signal_receiver();
        while let Some(_dart_signal) = receiver.recv().await {
            StartLogStream::handle_start().await;
        }
    });

    tokio::spawn(async {
        let receiver = StopLogStream::get_dart_signal_receiver();
        while let Some(_dart_signal) = receiver.recv().await {
            StopLogStream::handle_stop().await;
        }
    });
}

// WebSocket 流式数据处理器

impl StartTrafficStream {
    async fn handle_start() {
        log::info!("开始监听流量数据");

        // 确保 WebSocket 客户端已初始化
        ensure_ws_client_initialized().await;

        // 建立 WebSocket 连接
        let client = WS_CLIENT.read().await;
        if let Some(ws_client) = client.as_ref() {
            match ws_client
                .connect("/traffic", |json_value| {
                    // 解析流量数据
                    if let Some(obj) = json_value.as_object() {
                        let upload = obj.get("up").and_then(|v| v.as_u64()).unwrap_or(0);
                        let download = obj.get("down").and_then(|v| v.as_u64()).unwrap_or(0);

                        // 发送到 Dart 层
                        IpcTrafficData { upload, download }.send_signal_to_dart();
                    }
                })
                .await
            {
                Ok(connection_id) => {
                    log::info!("流量监控 WebSocket 连接已建立：{}", connection_id);

                    // 保存连接 ID
                    let mut id_guard = TRAFFIC_CONNECTION_ID.write().await;
                    *id_guard = Some(connection_id);

                    StreamResult {
                        is_successful: true,
                        error_message: None,
                    }
                    .send_signal_to_dart();
                }
                Err(e) => {
                    log::error!("流量监控 WebSocket 连接失败：{}", e);
                    StreamResult {
                        is_successful: false,
                        error_message: Some(e),
                    }
                    .send_signal_to_dart();
                }
            }
        }
    }
}

impl StopTrafficStream {
    async fn handle_stop() {
        log::info!("停止监听流量数据");

        // 获取并清除连接 ID
        let connection_id = {
            let mut id_guard = TRAFFIC_CONNECTION_ID.write().await;
            id_guard.take()
        };

        if let Some(id) = connection_id {
            let client = WS_CLIENT.read().await;
            if let Some(ws_client) = client.as_ref() {
                ws_client.disconnect(id).await;
            }
        }

        StreamResult {
            is_successful: true,
            error_message: None,
        }
        .send_signal_to_dart();
    }
}

impl StartLogStream {
    async fn handle_start() {
        log::info!("开始监听日志数据");

        // 确保 WebSocket 客户端已初始化
        ensure_ws_client_initialized().await;

        // 建立 WebSocket 连接
        let client = WS_CLIENT.read().await;
        if let Some(ws_client) = client.as_ref() {
            match ws_client
                .connect("/logs?level=info", |json_value| {
                    // 解析日志数据
                    if let Some(obj) = json_value.as_object() {
                        let log_type = obj
                            .get("type")
                            .and_then(|v| v.as_str())
                            .unwrap_or("info")
                            .to_string();
                        let payload = obj
                            .get("payload")
                            .and_then(|v| v.as_str())
                            .unwrap_or("")
                            .to_string();

                        // 发送到 Dart 层
                        IpcLogData { log_type, payload }.send_signal_to_dart();
                    }
                })
                .await
            {
                Ok(connection_id) => {
                    log::info!("日志监控 WebSocket 连接已建立：{}", connection_id);

                    // 保存连接 ID
                    let mut id_guard = LOG_CONNECTION_ID.write().await;
                    *id_guard = Some(connection_id);

                    StreamResult {
                        is_successful: true,
                        error_message: None,
                    }
                    .send_signal_to_dart();
                }
                Err(e) => {
                    log::error!("日志监控 WebSocket 连接失败：{}", e);
                    StreamResult {
                        is_successful: false,
                        error_message: Some(e),
                    }
                    .send_signal_to_dart();
                }
            }
        }
    }
}

impl StopLogStream {
    async fn handle_stop() {
        log::info!("停止监听日志数据");

        // 获取并清除连接 ID
        let connection_id = {
            let mut id_guard = LOG_CONNECTION_ID.write().await;
            id_guard.take()
        };

        if let Some(id) = connection_id {
            let client = WS_CLIENT.read().await;
            if let Some(ws_client) = client.as_ref() {
                ws_client.disconnect(id).await;
            }
        }

        StreamResult {
            is_successful: true,
            error_message: None,
        }
        .send_signal_to_dart();
    }
}

// 公开的 IPC GET 请求接口（供 Rust 内部模块使用）
//
// 用于批量延迟测试等场景，直接使用连接池发送 IPC GET 请求
pub async fn internal_ipc_get(path: &str) -> Result<String, String> {
    // 从连接池获取连接
    let ipc_conn = acquire_connection().await?;

    // 使用连接发送请求
    match IpcClient::request_with_connection("GET", path, None, ipc_conn).await {
        Ok((response, ipc_conn)) => {
            // 归还连接
            release_connection(ipc_conn).await;

            if response.status_code >= 200 && response.status_code < 300 {
                Ok(response.body)
            } else {
                Err(format!("HTTP {}", response.status_code))
            }
        }
        Err(e) => Err(e),
    }
}

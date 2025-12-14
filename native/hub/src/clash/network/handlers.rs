// IPC 请求处理器
//
// 处理 Dart 层发送的 IPC 请求，通过 IpcClient 转发给 Clash 核心

use super::ipc_client::IpcClient;

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
use super::signals::{
    IpcDeleteRequest, IpcGetRequest, IpcLogData, IpcPatchRequest, IpcPostRequest, IpcPutRequest,
    IpcResponse, IpcTrafficData, StartLogStream, StartTrafficStream, StopLogStream,
    StopTrafficStream, StreamResult,
};
use super::ws_client::WebSocketClient;
use once_cell::sync::Lazy;
use rinf::{DartSignal, RustSignal};
use std::collections::VecDeque;
use std::sync::Arc;
use std::time::{Duration, Instant};
use tokio::sync::{RwLock, Semaphore};

#[cfg(unix)]
use tokio::net::UnixStream;

#[cfg(windows)]
use tokio::net::windows::named_pipe::NamedPipeClient;

// 连接池配置
const MAX_POOL_SIZE: usize = 300; // 匹配 Dart 层最大并发（CPU核心数*15，最高300）
const IDLE_TIMEOUT_MS: u64 = 500;

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

        #[cfg(windows)]
        {
            let mut buf = [0u8; 1];
            match self.conn.try_read(&mut buf) {
                Ok(0) => false,                                      // 连接已关闭
                Ok(_) => true, // 有数据可读（不应发生，但连接有效）
                Err(e) if e.kind() == ErrorKind::WouldBlock => true, // 无数据但连接正常
                Err(_) => false, // 其他错误表示连接失效
            }
        }

        #[cfg(unix)]
        {
            let mut buf = [0u8; 1];
            match self.conn.try_read(&mut buf) {
                Ok(0) => false,                                      // 连接已关闭
                Ok(_) => true, // 有数据可读（不应发生，但连接有效）
                Err(e) if e.kind() == ErrorKind::WouldBlock => true, // 无数据但连接正常
                Err(_) => false, // 其他错误表示连接失效
            }
        }
    }
}

// 全局 IPC 连接池（使用 VecDeque 实现 FIFO）
static IPC_CONNECTION_POOL: Lazy<Arc<RwLock<VecDeque<PooledConnection>>>> =
    Lazy::new(|| Arc::new(RwLock::new(VecDeque::new())));

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
            if let Ok(mut pool) = IPC_CONNECTION_POOL.try_write() {
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
            } else {
                log::trace!("健康检查：连接池繁忙，跳过本轮");
            }
        }
    });

    log::info!("连接池健康检查已启动（30秒间隔）");
}

// 从连接池获取连接（如果没有则创建新的）
#[cfg(windows)]
async fn acquire_connection() -> Result<NamedPipeClient, String> {
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

    // 2. 创建新连接
    log::trace!("连接池为空，创建新连接");
    super::connection::connect_named_pipe(&IpcClient::default_ipc_path()).await
}

#[cfg(unix)]
async fn acquire_connection() -> Result<UnixStream, String> {
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

    // 2. 创建新连接
    log::trace!("连接池为空，创建新连接");
    super::connection::connect_unix_socket(&IpcClient::default_ipc_path()).await
}

// 归还连接到池中（FIFO：从尾部加入）
#[cfg(windows)]
async fn release_connection(conn: NamedPipeClient) {
    let mut pool = IPC_CONNECTION_POOL.write().await;

    if pool.len() < MAX_POOL_SIZE {
        pool.push_back(PooledConnection {
            conn,
            last_used: Instant::now(),
        });
        log::trace!("归还连接到池（当前{}）", pool.len());
    } else {
        log::trace!("连接池已满，丢弃连接");
    }
}

#[cfg(unix)]
async fn release_connection(conn: UnixStream) {
    let mut pool = IPC_CONNECTION_POOL.write().await;

    if pool.len() < MAX_POOL_SIZE {
        pool.push_back(PooledConnection {
            conn,
            last_used: Instant::now(),
        });
        log::trace!("归还连接到池（当前{}）", pool.len());
    } else {
        log::trace!("连接池已满，丢弃连接");
    }
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
pub async fn cleanup_ipc_connection_pool() {
    let mut pool = IPC_CONNECTION_POOL.write().await;
    let count = pool.len();
    pool.clear();
    if count > 0 {
        log::info!("已清理 IPC 连接池（{}个连接）", count);
    }
}

// 清理 WebSocket 客户端（在 Clash 停止时调用）
pub async fn cleanup_ws_client() {
    let mut client_guard = WS_CLIENT.write().await;
    if let Some(ws_client) = client_guard.take() {
        ws_client.disconnect_all().await;
        log::info!("WebSocket 客户端已清理");
    }
}

// 清理所有网络资源（在 Clash 停止时调用的统一入口）
pub async fn cleanup_all_network_resources() {
    log::info!("开始清理所有网络资源");

    // 1. 清理 WebSocket 连接
    cleanup_ws_client().await;

    // 2. 清理 IPC 连接池
    cleanup_ipc_connection_pool().await;

    log::info!("所有网络资源已清理");
}

impl IpcGetRequest {
    pub fn handle(self) {
        let request_id = self.request_id;
        tokio::spawn(async move {
            // 从连接池获取连接
            let ipc_conn = match acquire_connection().await {
                Ok(c) => c,
                Err(e) => {
                    let error_msg = e.to_string();
                    if is_ipc_not_ready_error(&error_msg) {
                        log::trace!("IPC GET 请求等待中：{}，原因：IPC 尚未就绪", self.path);
                    } else {
                        log::error!("IPC GET 获取连接失败：{}，error：{}", self.path, e);
                    }

                    IpcResponse {
                        request_id,
                        status_code: 0,
                        body: String::new(),
                        success: false,
                        error_message: Some(format!("获取连接失败：{}", e)),
                    }
                    .send_signal_to_dart();
                    return;
                }
            };

            // 使用连接发送请求
            match IpcClient::request_with_connection("GET", &self.path, None, ipc_conn).await {
                Ok((response, ipc_conn)) => {
                    // 归还连接
                    release_connection(ipc_conn).await;

                    // 日志处理（成功）
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

                    IpcResponse {
                        request_id,
                        status_code: response.status_code,
                        body: response.body,
                        success: true,
                        error_message: None,
                    }
                    .send_signal_to_dart();
                }
                Err(e) => {
                    // 连接已失效，不归还
                    let error_msg = e.to_string();
                    if is_ipc_not_ready_error(&error_msg) {
                        log::trace!("IPC GET 请求等待中：{}，原因：IPC 尚未就绪", self.path);
                    } else {
                        log::error!("IPC GET 请求失败：{}，error：{}", self.path, e);
                    }

                    IpcResponse {
                        request_id,
                        status_code: 0,
                        body: String::new(),
                        success: false,
                        error_message: Some(format!("IPC 请求失败：{}", e)),
                    }
                    .send_signal_to_dart();
                }
            }
        });
    }
}

impl IpcPostRequest {
    pub fn handle(self) {
        let request_id = self.request_id;
        tokio::spawn(async move {
            let ipc_conn = match acquire_connection().await {
                Ok(c) => c,
                Err(e) => {
                    let error_msg = e.to_string();
                    if is_ipc_not_ready_error(&error_msg) {
                        log::trace!("IPC POST 请求等待中：{}，原因：IPC 尚未就绪", self.path);
                    } else {
                        log::error!("IPC POST 获取连接失败：{}，error：{}", self.path, e);
                    }

                    IpcResponse {
                        request_id,
                        status_code: 0,
                        body: String::new(),
                        success: false,
                        error_message: Some(format!("获取连接失败：{}", e)),
                    }
                    .send_signal_to_dart();
                    return;
                }
            };

            match IpcClient::request_with_connection(
                "POST",
                &self.path,
                self.body.as_deref(),
                ipc_conn,
            )
            .await
            {
                Ok((response, ipc_conn)) => {
                    release_connection(ipc_conn).await;

                    IpcResponse {
                        request_id,
                        status_code: response.status_code,
                        body: response.body,
                        success: true,
                        error_message: None,
                    }
                    .send_signal_to_dart();
                }
                Err(e) => {
                    let error_msg = e.to_string();
                    if is_ipc_not_ready_error(&error_msg) {
                        log::trace!("IPC POST 请求等待中：{}，原因：IPC 尚未就绪", self.path);
                    } else {
                        log::error!("IPC POST 请求失败：{}，error：{}", self.path, e);
                    }

                    IpcResponse {
                        request_id,
                        status_code: 0,
                        body: String::new(),
                        success: false,
                        error_message: Some(format!("IPC 请求失败：{}", e)),
                    }
                    .send_signal_to_dart();
                }
            }
        });
    }
}

impl IpcPutRequest {
    pub fn handle(self) {
        let request_id = self.request_id;
        tokio::spawn(async move {
            // 获取配置更新锁（确保串行执行）
            let _permit = match CONFIG_UPDATE_SEMAPHORE.acquire().await {
                Ok(permit) => permit,
                Err(e) => {
                    log::error!("获取配置更新锁失败：{}", e);
                    IpcResponse {
                        request_id,
                        status_code: 0,
                        body: String::new(),
                        success: false,
                        error_message: Some(format!("获取配置锁失败：{}", e)),
                    }
                    .send_signal_to_dart();
                    return;
                }
            };
            log::trace!("获取配置更新锁，开始处理 PUT 请求：{}", self.path);

            let ipc_conn = match acquire_connection().await {
                Ok(c) => c,
                Err(e) => {
                    let error_msg = e.to_string();
                    if is_ipc_not_ready_error(&error_msg) {
                        log::trace!("IPC PUT 请求等待中：{}，原因：IPC 尚未就绪", self.path);
                    } else {
                        log::error!("IPC PUT 获取连接失败：{}，error：{}", self.path, e);
                    }

                    IpcResponse {
                        request_id,
                        status_code: 0,
                        body: String::new(),
                        success: false,
                        error_message: Some(format!("获取连接失败：{}", e)),
                    }
                    .send_signal_to_dart();
                    return;
                }
            };

            match IpcClient::request_with_connection(
                "PUT",
                &self.path,
                self.body.as_deref(),
                ipc_conn,
            )
            .await
            {
                Ok((response, ipc_conn)) => {
                    release_connection(ipc_conn).await;

                    IpcResponse {
                        request_id,
                        status_code: response.status_code,
                        body: response.body,
                        success: true,
                        error_message: None,
                    }
                    .send_signal_to_dart();

                    log::trace!("PUT 请求完成，释放配置更新锁：{}", self.path);
                }
                Err(e) => {
                    let error_msg = e.to_string();
                    if is_ipc_not_ready_error(&error_msg) {
                        log::trace!("IPC PUT 请求等待中：{}，原因：IPC 尚未就绪", self.path);
                    } else {
                        log::error!("IPC PUT 请求失败：{}，error：{}", self.path, e);
                    }

                    IpcResponse {
                        request_id,
                        status_code: 0,
                        body: String::new(),
                        success: false,
                        error_message: Some(format!("IPC 请求失败：{}", e)),
                    }
                    .send_signal_to_dart();
                }
            }
            // _permit 在此处 drop，自动释放锁
        });
    }
}

impl IpcPatchRequest {
    pub fn handle(self) {
        let request_id = self.request_id;
        tokio::spawn(async move {
            let ipc_conn = match acquire_connection().await {
                Ok(c) => c,
                Err(e) => {
                    let error_msg = e.to_string();
                    if is_ipc_not_ready_error(&error_msg) {
                        log::trace!("IPC PATCH 请求等待中：{}，原因：IPC 尚未就绪", self.path);
                    } else {
                        log::error!("IPC PATCH 获取连接失败：{}，error：{}", self.path, e);
                    }

                    IpcResponse {
                        request_id,
                        status_code: 0,
                        body: String::new(),
                        success: false,
                        error_message: Some(format!("获取连接失败：{}", e)),
                    }
                    .send_signal_to_dart();
                    return;
                }
            };

            match IpcClient::request_with_connection(
                "PATCH",
                &self.path,
                self.body.as_deref(),
                ipc_conn,
            )
            .await
            {
                Ok((response, ipc_conn)) => {
                    release_connection(ipc_conn).await;

                    IpcResponse {
                        request_id,
                        status_code: response.status_code,
                        body: response.body,
                        success: true,
                        error_message: None,
                    }
                    .send_signal_to_dart();
                }
                Err(e) => {
                    let error_msg = e.to_string();
                    if is_ipc_not_ready_error(&error_msg) {
                        log::trace!("IPC PATCH 请求等待中：{}，原因：IPC 尚未就绪", self.path);
                    } else {
                        log::error!("IPC PATCH 请求失败：{}，error：{}", self.path, e);
                    }

                    IpcResponse {
                        request_id,
                        status_code: 0,
                        body: String::new(),
                        success: false,
                        error_message: Some(format!("IPC 请求失败：{}", e)),
                    }
                    .send_signal_to_dart();
                }
            }
        });
    }
}

impl IpcDeleteRequest {
    pub fn handle(self) {
        let request_id = self.request_id;
        tokio::spawn(async move {
            let ipc_conn = match acquire_connection().await {
                Ok(c) => c,
                Err(e) => {
                    let error_msg = e.to_string();
                    if is_ipc_not_ready_error(&error_msg) {
                        log::trace!("IPC DELETE 请求等待中：{}，原因：IPC 尚未就绪", self.path);
                    } else {
                        log::error!("IPC DELETE 获取连接失败：{}，error：{}", self.path, e);
                    }

                    IpcResponse {
                        request_id,
                        status_code: 0,
                        body: String::new(),
                        success: false,
                        error_message: Some(format!("获取连接失败：{}", e)),
                    }
                    .send_signal_to_dart();
                    return;
                }
            };

            match IpcClient::request_with_connection("DELETE", &self.path, None, ipc_conn).await {
                Ok((response, ipc_conn)) => {
                    release_connection(ipc_conn).await;

                    IpcResponse {
                        request_id,
                        status_code: response.status_code,
                        body: response.body,
                        success: true,
                        error_message: None,
                    }
                    .send_signal_to_dart();
                }
                Err(e) => {
                    let error_msg = e.to_string();
                    if is_ipc_not_ready_error(&error_msg) {
                        log::trace!("IPC DELETE 请求等待中：{}，原因：IPC 尚未就绪", self.path);
                    } else {
                        log::error!("IPC DELETE 请求失败：{}，error：{}", self.path, e);
                    }

                    IpcResponse {
                        request_id,
                        status_code: 0,
                        body: String::new(),
                        success: false,
                        error_message: Some(format!("IPC 请求失败：{}", e)),
                    }
                    .send_signal_to_dart();
                }
            }
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
                        success: true,
                        error_message: None,
                    }
                    .send_signal_to_dart();
                }
                Err(e) => {
                    log::error!("流量监控 WebSocket 连接失败：{}", e);
                    StreamResult {
                        success: false,
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
            success: true,
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
                        success: true,
                        error_message: None,
                    }
                    .send_signal_to_dart();
                }
                Err(e) => {
                    log::error!("日志监控 WebSocket 连接失败：{}", e);
                    StreamResult {
                        success: false,
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
            success: true,
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

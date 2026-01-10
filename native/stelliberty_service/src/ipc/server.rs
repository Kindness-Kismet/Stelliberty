// IPC 服务端实现

use super::error::{IpcError, Result};
use super::protocol::{IPC_PATH, IpcCommand, IpcResponse};
use std::future::Future;
use std::pin::Pin;
use std::sync::Arc;
use tokio::io::{AsyncReadExt, AsyncWriteExt};
use tokio::sync::mpsc;

#[cfg(windows)]
use windows::Win32::{
    Foundation::{HLOCAL, LocalFree},
    Security::Authorization::{
        ConvertStringSecurityDescriptorToSecurityDescriptorW, SDDL_REVISION_1,
    },
};

// 命令处理器类型（异步）
pub type CommandHandler =
    Arc<dyn Fn(IpcCommand) -> Pin<Box<dyn Future<Output = IpcResponse> + Send>> + Send + Sync>;

// IPC 服务端
pub struct IpcServer {
    handler: CommandHandler,
    shutdown_tx: Option<mpsc::Sender<()>>,
}

impl IpcServer {
    // 创建新的 IPC 服务端
    pub fn new<F, Fut>(handler: F) -> Self
    where
        F: Fn(IpcCommand) -> Fut + Send + Sync + 'static,
        Fut: Future<Output = IpcResponse> + Send + 'static,
    {
        Self {
            handler: Arc::new(move |cmd| {
                Box::pin(handler(cmd)) as Pin<Box<dyn Future<Output = IpcResponse> + Send>>
            }),
            shutdown_tx: None,
        }
    }

    // 启动服务端（阻塞直到关闭）
    pub async fn run(&mut self) -> Result<()> {
        // 删除旧的 IPC 文件
        #[cfg(not(windows))]
        {
            let _ = std::fs::remove_file(IPC_PATH);
        }

        // 创建关闭通道
        let (shutdown_tx, shutdown_rx) = mpsc::channel::<()>(1);
        self.shutdown_tx = Some(shutdown_tx);

        log::info!("IPC 服务端启动，监听: {IPC_PATH}");

        // Windows 和 Unix 使用不同的实现
        #[cfg(windows)]
        {
            self.run_windows(shutdown_rx).await?;
        }

        #[cfg(not(windows))]
        {
            self.run_unix(shutdown_rx).await?;
        }

        // 清理
        #[cfg(not(windows))]
        {
            let _ = std::fs::remove_file(IPC_PATH);
        }

        Ok(())
    }

    // Windows 平台运行
    #[cfg(windows)]
    async fn run_windows(&self, mut shutdown_rx: mpsc::Receiver<()>) -> Result<()> {
        log::info!("准备创建 Named Pipe: {IPC_PATH}");

        // 创建允许已认证用户访问的安全描述符
        let security_descriptor = create_permissive_security_attributes()
            .map_err(|e| IpcError::Other(format!("创建安全描述符失败: {e}")))?;

        // 第一次循环创建第一个实例
        let mut is_first_instance = true;

        loop {
            // 为每个连接创建新的 Named Pipe 实例
            let server = if is_first_instance {
                log::info!("创建第一个 Named Pipe 实例（允许已认证用户访问）");

                // 使用 Windows API 创建带权限的 Named Pipe
                let pipe = create_named_pipe_with_security(IPC_PATH, true, &security_descriptor)
                    .map_err(|e| {
                        log::error!("创建第一个 Named Pipe 实例失败: {e}");
                        IpcError::Other(format!("创建 Named Pipe 失败: {e}"))
                    })?;

                log::debug!("Named Pipe 实例创建成功（初始化）");
                is_first_instance = false;
                pipe
            } else {
                let pipe = create_named_pipe_with_security(IPC_PATH, false, &security_descriptor)
                    .map_err(|e| {
                    log::error!("创建 Named Pipe 实例失败: {e}");
                    IpcError::Other(format!("创建 Named Pipe 失败: {e}"))
                })?;

                log::debug!("Named Pipe 实例创建成功（等待新连接）");
                pipe
            };

            tokio::select! {
                // 等待客户端连接
                result = server.connect() => {
                    if let Err(e) = result {
                        log::error!("接受连接失败: {e}");
                        continue;
                    }

                    // 处理连接
                    let handler = self.handler.clone();
                    tokio::spawn(async move {
                        if let Err(e) = Self::handle_client(server, handler).await {
                            log::error!("处理客户端连接失败: {e}");
                        }
                    });
                }

                // 接收关闭信号
                _ = shutdown_rx.recv() => {
                    log::info!("收到关闭信号，停止 IPC 服务端");
                    break;
                }
            }
        }

        Ok(())
    }

    // Unix 平台运行
    #[cfg(not(windows))]
    async fn run_unix(&self, mut shutdown_rx: mpsc::Receiver<()>) -> Result<()> {
        use tokio::net::UnixListener;

        let listener = UnixListener::bind(IPC_PATH)
            .map_err(|e| IpcError::Other(format!("创建 Unix Socket 失败: {}", e)))?;

        // 设置 Unix Socket 文件权限为 0600（仅所有者可读写）
        #[cfg(unix)]
        {
            use std::os::unix::fs::PermissionsExt;
            std::fs::set_permissions(IPC_PATH, std::fs::Permissions::from_mode(0o600))
                .map_err(|e| IpcError::Other(format!("设置 Unix Socket 权限失败: {}", e)))?;
            log::info!("Unix Socket 权限已设置为 0600（仅所有者可读写）");
        }

        loop {
            tokio::select! {
                // 接受新连接
                result = listener.accept() => {
                    match result {
                        Ok((stream, _)) => {
                            let handler = self.handler.clone();
                            tokio::spawn(async move {
                                if let Err(e) = Self::handle_client(stream, handler).await {
                                    log::error!("处理客户端连接失败: {}", e);
                                }
                            });
                        }
                        Err(e) => {
                            log::error!("接受连接失败: {}", e);
                        }
                    }
                }

                // 接收关闭信号
                _ = shutdown_rx.recv() => {
                    log::info!("收到关闭信号，停止 IPC 服务端");
                    break;
                }
            }
        }

        Ok(())
    }

    // 处理客户端连接
    async fn handle_client<S>(mut stream: S, handler: CommandHandler) -> Result<()>
    where
        S: AsyncReadExt + AsyncWriteExt + Unpin,
    {
        // 读取命令长度
        let mut len_buf = [0u8; 4];
        stream.read_exact(&mut len_buf).await?;
        let command_len = u32::from_le_bytes(len_buf) as usize;

        // 防止恶意请求
        if command_len > 1024 * 1024 {
            // 最大 1MB
            return Err(IpcError::Other("命令数据过大".to_string()));
        }

        // 读取命令数据
        let mut command_buf = vec![0u8; command_len];
        stream.read_exact(&mut command_buf).await?;

        // 反序列化命令
        let command: IpcCommand = serde_json::from_slice(&command_buf)?;
        log::trace!("收到命令: {command:?}");

        // 处理 StreamLogs 特殊命令（流式推送）
        if matches!(command, IpcCommand::StreamLogs) {
            log::info!("启动日志流订阅");
            return Self::handle_log_stream(stream).await;
        }

        // 处理普通命令（请求-响应）
        let response = handler(command).await;

        // 记录响应（避免日志递归：GetLogs 响应不打印完整内容）
        match &response {
            IpcResponse::Logs { lines } => {
                log::trace!("返回响应: Logs (共 {} 行)", lines.len());
            }
            _ => {
                log::trace!("返回响应: {response:?}");
            }
        }

        // 序列化响应
        let response_json = serde_json::to_string(&response)?;
        let response_bytes = response_json.as_bytes();

        // 发送响应长度 + 响应数据
        let len = response_bytes.len() as u32;
        stream.write_all(&len.to_le_bytes()).await?;
        stream.write_all(response_bytes).await?;
        stream.flush().await?;

        Ok(())
    }

    // 处理日志流订阅（持续推送）
    async fn handle_log_stream<S>(mut stream: S) -> Result<()>
    where
        S: AsyncReadExt + AsyncWriteExt + Unpin,
    {
        use crate::logger;

        // 订阅日志流
        let mut log_receiver = logger::subscribe_logs();

        // 发送初始成功响应
        let initial_response = IpcResponse::Success {
            message: Some("日志流已启用".to_string()),
        };
        let response_json = serde_json::to_string(&initial_response)?;
        let response_bytes = response_json.as_bytes();
        let len = response_bytes.len() as u32;
        stream.write_all(&len.to_le_bytes()).await?;
        stream.write_all(response_bytes).await?;
        stream.flush().await?;

        log::debug!("日志流订阅已激活，开始推送日志");

        // 持续推送日志
        loop {
            match log_receiver.recv().await {
                Ok(log_line) => {
                    // 构造日志流响应
                    let log_response = IpcResponse::LogStream { line: log_line };
                    let response_json = serde_json::to_string(&log_response)?;
                    let response_bytes = response_json.as_bytes();
                    let len = response_bytes.len() as u32;

                    // 发送日志行
                    if let Err(e) = stream.write_all(&len.to_le_bytes()).await {
                        log::debug!("日志流客户端断开连接: {}", e);
                        break;
                    }
                    if let Err(e) = stream.write_all(response_bytes).await {
                        log::debug!("日志流客户端断开连接: {}", e);
                        break;
                    }
                    if let Err(e) = stream.flush().await {
                        log::debug!("日志流客户端断开连接: {}", e);
                        break;
                    }
                }
                Err(tokio::sync::broadcast::error::RecvError::Lagged(skipped)) => {
                    log::warn!("日志流客户端处理过慢，跳过了 {} 条日志", skipped);
                    // 继续处理，不中断连接
                }
                Err(tokio::sync::broadcast::error::RecvError::Closed) => {
                    log::info!("日志广播通道已关闭，停止日志流");
                    break;
                }
            }
        }

        log::info!("日志流订阅结束");
        Ok(())
    }
}

// ============================================================================
// Windows 安全描述符辅助函数
// ============================================================================

#[cfg(windows)]
// RAII 包装器，用于自动释放 SecurityDescriptor
struct SecurityDescriptorWrapper(*mut std::ffi::c_void);

#[cfg(windows)]
unsafe impl Send for SecurityDescriptorWrapper {}

#[cfg(windows)]
impl Drop for SecurityDescriptorWrapper {
    fn drop(&mut self) {
        if !self.0.is_null() {
            unsafe {
                let _ = LocalFree(Some(HLOCAL(self.0)));
            }
        }
    }
}

#[cfg(windows)]
// 创建限制性安全描述符（仅允许当前用户、管理员和系统访问）
//
// SDDL 字符串说明：
// - D: = DACL（访问控制列表）
// - (A;;GA;;;CO) = 允许 (A)，通用访问 (GA)，创建者所有者 (CO) - 即当前用户
// - (A;;GA;;;BA) = 允许 (A)，通用访问 (GA)，管理员组 (BA)
// - (A;;GA;;;SY) = 允许 (A)，通用访问 (GA)，系统 (SY)
//
// 注意：移除了 AU（已认证用户），只允许创建者、管理员和系统访问
fn create_permissive_security_attributes() -> std::result::Result<SecurityDescriptorWrapper, String>
{
    use windows::core::PCWSTR;

    // SDDL 字符串：只允许创建者所有者、管理员和系统访问
    let sddl = "D:(A;;GA;;;CO)(A;;GA;;;BA)(A;;GA;;;SY)";

    let sddl_wide: Vec<u16> = sddl.encode_utf16().chain(std::iter::once(0)).collect();

    let mut security_descriptor: *mut std::ffi::c_void = std::ptr::null_mut();

    unsafe {
        ConvertStringSecurityDescriptorToSecurityDescriptorW(
            PCWSTR(sddl_wide.as_ptr()),
            SDDL_REVISION_1,
            std::ptr::addr_of_mut!(security_descriptor) as *mut _,
            None,
        )
        .map_err(|e| format!("创建安全描述符失败: {e}"))?;
    }

    log::info!("创建安全描述符成功（仅允许创建者所有者、管理员和系统访问）");
    Ok(SecurityDescriptorWrapper(security_descriptor))
}

#[cfg(windows)]
// 使用安全描述符创建 Named Pipe
fn create_named_pipe_with_security(
    path: &str,
    is_first_instance: bool,
    security_descriptor: &SecurityDescriptorWrapper,
) -> std::result::Result<tokio::net::windows::named_pipe::NamedPipeServer, String> {
    use windows::Win32::Foundation::ERROR_PIPE_BUSY;
    use windows::Win32::Security::SECURITY_ATTRIBUTES;
    use windows::Win32::Storage::FileSystem::{
        FILE_FLAG_FIRST_PIPE_INSTANCE, FILE_FLAG_OVERLAPPED, PIPE_ACCESS_DUPLEX,
    };
    use windows::Win32::System::Pipes::{
        CreateNamedPipeW, PIPE_READMODE_BYTE, PIPE_TYPE_BYTE, PIPE_UNLIMITED_INSTANCES, PIPE_WAIT,
    };
    use windows::core::PCWSTR;

    let path_wide: Vec<u16> = path.encode_utf16().chain(std::iter::once(0)).collect();

    // 设置 SECURITY_ATTRIBUTES
    let security_attrs = SECURITY_ATTRIBUTES {
        nLength: std::mem::size_of::<SECURITY_ATTRIBUTES>() as u32,
        lpSecurityDescriptor: security_descriptor.0 as *mut _,
        bInheritHandle: false.into(),
    };

    let open_mode = if is_first_instance {
        PIPE_ACCESS_DUPLEX | FILE_FLAG_OVERLAPPED | FILE_FLAG_FIRST_PIPE_INSTANCE
    } else {
        PIPE_ACCESS_DUPLEX | FILE_FLAG_OVERLAPPED
    };

    unsafe {
        let handle = CreateNamedPipeW(
            PCWSTR(path_wide.as_ptr()),
            open_mode,
            PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT,
            PIPE_UNLIMITED_INSTANCES,
            65536, // 输出缓冲区大小
            65536, // 输入缓冲区大小
            0,     // 默认超时
            Some(&security_attrs as *const _),
        );

        if handle.is_invalid() {
            let err = std::io::Error::last_os_error();
            if err.raw_os_error() == Some(ERROR_PIPE_BUSY.0 as i32) {
                return Err("Named Pipe 繁忙".to_string());
            }
            return Err(format!("CreateNamedPipeW 失败: {err}"));
        }

        // 包装成 Tokio 的 NamedPipeServer
        let raw_handle = handle.0 as std::os::windows::io::RawHandle;
        tokio::net::windows::named_pipe::NamedPipeServer::from_raw_handle(raw_handle)
            .map_err(|e| format!("包装 Named Pipe 失败: {e}"))
    }
}

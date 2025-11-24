// Clash 服务模式管理
//
// 通过 Windows Service/systemd 以管理员权限运行 Clash 核心

use crate::clash::messages::ClashProcessResult;
use anyhow::{Context, Result};
use rinf::{DartSignal, RustSignal};
use serde::{Deserialize, Serialize};
use std::path::PathBuf;
#[cfg(not(windows))]
use std::process::Command;
use stelliberty_service::ipc::{IpcClient, IpcCommand, IpcResponse};

// 服务管理器

// 服务状态
#[derive(Debug, Clone)]
pub enum ServiceStatus {
    // 服务已安装并运行
    Running {
        pid: u32,
        uptime: u64,
    },
    // 服务已安装但未运行
    Stopped,
    // 服务未安装
    #[cfg(windows)]
    NotInstalled,
    // 无法检测（IPC 连接失败）
    Unknown,
}

// 服务管理器
pub struct ServiceManager {
    ipc_client: IpcClient,
    service_exe_path: PathBuf,
}

impl ServiceManager {
    // 创建服务管理器
    pub fn new() -> Result<Self> {
        let service_exe_path = Self::get_service_exe_path()?;
        Ok(Self {
            ipc_client: IpcClient::default(),
            service_exe_path,
        })
    }

    // 获取服务状态
    pub async fn get_status(&self) -> ServiceStatus {
        #[cfg(windows)]
        {
            // 先检查服务是否安装
            if !Self::is_service_installed() {
                log::debug!("服务未安装");
                return ServiceStatus::NotInstalled;
            }

            // 服务已安装，快速检测是否运行（不带重试的 Ping）
            let is_running = tokio::time::timeout(
                std::time::Duration::from_millis(300),
                self.ipc_client.send_command(IpcCommand::Ping),
            )
            .await
            .ok()
            .and_then(|r| r.ok())
            .map(|resp| matches!(resp, IpcResponse::Pong))
            .unwrap_or(false);

            if !is_running {
                log::debug!("服务已安装但未运行");
                return ServiceStatus::Stopped;
            }

            // 服务正在运行，获取详细状态
            match self.ipc_client.send_command(IpcCommand::GetStatus).await {
                Ok(IpcResponse::Status {
                    clash_running: _,
                    clash_pid,
                    service_uptime,
                }) => {
                    if let Some(pid) = clash_pid {
                        // Clash 核心正在运行
                        ServiceStatus::Running {
                            pid,
                            uptime: service_uptime,
                        }
                    } else {
                        // 服务进程运行，但 Clash 核心未运行
                        log::debug!("服务进程运行中，但 Clash 核心未启动");
                        ServiceStatus::Stopped
                    }
                }
                _ => ServiceStatus::Unknown,
            }
        }

        #[cfg(not(windows))]
        {
            // 非 Windows 平台，尝试 IPC 连接
            if !self.ipc_client.is_service_running().await {
                return ServiceStatus::Unknown;
            }

            match self.ipc_client.send_command(IpcCommand::GetStatus).await {
                Ok(IpcResponse::Status {
                    clash_running: _,
                    clash_pid,
                    service_uptime,
                }) => {
                    if let Some(pid) = clash_pid {
                        // Clash 核心正在运行
                        ServiceStatus::Running {
                            pid,
                            uptime: service_uptime,
                        }
                    } else {
                        // 服务进程运行，但 Clash 核心未运行
                        log::debug!("服务进程运行中，但 Clash 核心未启动");
                        ServiceStatus::Stopped
                    }
                }
                _ => ServiceStatus::Unknown,
            }
        }
    }

    // 安装服务
    pub async fn install_service(&self) -> Result<()> {
        log::info!("安装 Stelliberty Service…");

        // 记录安装前核心是否在运行
        let clash_was_running = matches!(self.get_status().await, ServiceStatus::Running { .. });

        if clash_was_running {
            log::info!("检测到 Clash 核心正在运行，将在权限确认后停止");
        }

        // 安装前始终复制最新的服务二进制到私有目录
        self.copy_service_binary_to_private()?;

        #[cfg(windows)]
        {
            // 执行提权安装命令（会弹 UAC，用户可能取消）
            // 如果用户取消，这里会返回错误，核心不会被停止
            self.run_elevated_command("install").await?;

            // 走到这里说明用户确认了权限，安装成功
            // 现在可以安全地停止核心了
            if clash_was_running {
                log::info!("权限确认成功，停止 Clash 核心...");
                if let Err(e) = self.stop_clash().await {
                    log::warn!("停止 Clash 核心失败：{}，但服务已安装", e);
                } else {
                    log::info!("Clash 核心已停止");
                }
            }
        }

        #[cfg(not(windows))]
        {
            let output = Command::new(&self.service_exe_path)
                .arg("install")
                .output()
                .context("执行安装命令失败")?;

            if !output.status.success() {
                let stderr = String::from_utf8_lossy(&output.stderr);
                anyhow::bail!("安装服务失败：{}", stderr);
            }
        }

        Ok(())
    }

    // 卸载服务
    pub async fn uninstall_service(&self) -> Result<()> {
        log::info!("卸载 Stelliberty Service…");

        // 记录卸载前核心是否在运行
        let clash_was_running = if Self::is_service_installed() {
            matches!(self.get_status().await, ServiceStatus::Running { .. })
        } else {
            false
        };

        if clash_was_running {
            log::info!("检测到 Clash 核心正在运行，将在权限确认后停止");
        }

        // 执行卸载命令（会弹 UAC，用户可能取消）
        #[cfg(windows)]
        {
            // 如果用户取消，这里会返回错误，核心不会被停止
            self.run_elevated_command("uninstall").await?;

            // 走到这里说明用户确认了权限，卸载成功
            // 现在可以安全地停止核心了
            if clash_was_running {
                log::info!("权限确认成功，停止 Clash 核心...");
                if let Err(e) = self.stop_clash().await {
                    log::warn!("通过 IPC 停止 Clash 失败：{}，但服务已卸载", e);
                } else {
                    log::info!("Clash 核心已通过 IPC 停止");
                    // 等待服务完全停止
                    tokio::time::sleep(std::time::Duration::from_millis(500)).await;
                }
            }
        }

        #[cfg(not(windows))]
        {
            let output = Command::new(&self.service_exe_path)
                .arg("uninstall")
                .output()
                .context("执行卸载命令失败")?;

            if !output.status.success() {
                let stderr = String::from_utf8_lossy(&output.stderr);
                anyhow::bail!("卸载服务失败：{}", stderr);
            }
        }

        // 只有卸载成功后才删除私有目录中的服务二进制文件
        self.remove_service_binary_from_private().await?;

        Ok(())
    }

    // 复制服务二进制到私有目录（安装时调用）
    fn copy_service_binary_to_private(&self) -> Result<()> {
        let app_data_dir = Self::get_app_data_dir()?;
        let source_service_exe = Self::get_source_service_exe_path()?;

        #[cfg(windows)]
        let private_service_exe = app_data_dir.join("stelliberty-service.exe");

        #[cfg(not(windows))]
        let private_service_exe = app_data_dir.join("stelliberty-service");

        // 检查是否需要复制（通过文件大小和修改时间判断）
        let need_copy = if private_service_exe.exists() {
            match (
                std::fs::metadata(&source_service_exe),
                std::fs::metadata(&private_service_exe),
            ) {
                (Ok(source_meta), Ok(private_meta)) => {
                    // 比较文件大小和修改时间
                    let size_different = source_meta.len() != private_meta.len();
                    let time_different = source_meta
                        .modified()
                        .ok()
                        .zip(private_meta.modified().ok())
                        .map(|(s, p)| s > p)
                        .unwrap_or(true);

                    if size_different || time_different {
                        log::info!("检测到服务程序更新（大小或时间不同），将覆盖私有目录中的文件");
                        true
                    } else {
                        log::info!("私有目录中的服务程序已是最新版本，跳过复制");
                        false
                    }
                }
                _ => {
                    // 元数据获取失败，安全起见重新复制
                    log::warn!("无法获取文件元数据，将重新复制");
                    true
                }
            }
        } else {
            log::info!("私有目录中不存在服务程序，需要复制");
            true
        };

        if !need_copy {
            return Ok(());
        }

        // 确保私有目录存在
        if !app_data_dir.exists() {
            std::fs::create_dir_all(&app_data_dir)
                .with_context(|| format!("无法创建私有目录：{}", app_data_dir.display()))?;
        }

        log::info!(
            "复制服务程序到私有目录：{} -> {}",
            source_service_exe.display(),
            private_service_exe.display()
        );

        // 获取源文件大小用于验证
        let source_size = std::fs::metadata(&source_service_exe)
            .with_context(|| format!("无法获取源文件元数据：{}", source_service_exe.display()))?
            .len();

        std::fs::copy(&source_service_exe, &private_service_exe).with_context(|| {
            format!(
                "无法复制服务程序从 {} 到 {}",
                source_service_exe.display(),
                private_service_exe.display()
            )
        })?;

        // 问题 13：验证文件复制完整性（通过文件大小）
        let copied_size = std::fs::metadata(&private_service_exe)
            .with_context(|| {
                format!(
                    "无法获取已复制文件元数据：{}",
                    private_service_exe.display()
                )
            })?
            .len();

        if copied_size != source_size {
            anyhow::bail!(
                "文件复制完整性验证失败：期望 {} 字节，实际 {} 字节。可能原因：磁盘空间不足或杀毒软件拦截",
                source_size,
                copied_size
            );
        }

        log::info!(
            "服务程序已复制到私有目录并验证完整性（{} 字节）",
            copied_size
        );
        Ok(())
    }

    // 删除私有目录中的服务二进制（卸载时调用）
    async fn remove_service_binary_from_private(&self) -> Result<()> {
        let app_data_dir = Self::get_app_data_dir()?;

        #[cfg(windows)]
        let private_service_exe = app_data_dir.join("stelliberty-service.exe");

        #[cfg(not(windows))]
        let private_service_exe = app_data_dir.join("stelliberty-service");

        if private_service_exe.exists() {
            log::info!(
                "删除私有目录中的服务程序：{}",
                private_service_exe.display()
            );

            // 问题 14：卸载后服务进程可能还在释放文件句柄，需要等待并重试
            let mut retry_count = 0;
            const MAX_RETRIES: u32 = 15; // 最多重试 15 次（3 秒）

            loop {
                match std::fs::remove_file(&private_service_exe) {
                    Ok(_) => {
                        log::info!("服务程序已从私有目录删除");
                        break;
                    }
                    Err(e) if retry_count < MAX_RETRIES => {
                        // 文件被占用（Windows 错误码 32）或权限不足
                        log::debug!(
                            "删除文件失败（第 {} 次尝试）：{}，200ms 后重试",
                            retry_count + 1,
                            e
                        );
                        retry_count += 1;
                        tokio::time::sleep(std::time::Duration::from_millis(200)).await;
                    }
                    Err(_e) => {
                        anyhow::bail!(
                            "无法删除服务程序：{}。可能原因：\n1. 文件被服务进程占用（请等待服务完全退出）\n2. 文件被杀毒软件锁定\n3. 权限不足",
                            private_service_exe.display()
                        );
                    }
                }
            }
        } else {
            log::info!("私有目录中不存在服务程序，无需删除");
        }

        Ok(())
    }

    // 以管理员权限运行命令（Windows）
    #[cfg(windows)]
    async fn run_elevated_command(&self, operation: &str) -> Result<()> {
        use windows::Win32::UI::Shell::ShellExecuteW;
        use windows::Win32::UI::WindowsAndMessaging::SW_HIDE;
        use windows::core::{HSTRING, PCWSTR};

        let binary_path = self
            .service_exe_path
            .to_str()
            .context("服务程序路径包含无效字符")?;

        log::info!("以管理员权限执行：{} {}", binary_path, operation);

        // 再次验证服务程序是否存在（防止文件被删除）
        if !self.service_exe_path.exists() {
            anyhow::bail!("服务程序文件不存在：{}。可能已被删除或移动", binary_path);
        }

        let verb = HSTRING::from("runas");
        let file = HSTRING::from(binary_path);
        let parameters = HSTRING::from(operation);

        unsafe {
            let result = ShellExecuteW(
                None, // 使用 None 表示无父窗口
                PCWSTR(verb.as_ptr()),
                PCWSTR(file.as_ptr()),
                PCWSTR(parameters.as_ptr()),
                PCWSTR::null(),
                SW_HIDE,
            );

            // ShellExecuteW 返回 HINSTANCE，值 > 32 表示成功
            let result_value = result.0 as isize;
            if result_value <= 32 {
                // 根据返回值提供详细错误信息
                let error_detail = match result_value {
                    0 => "系统内存或资源不足",
                    2 => "找不到指定的服务程序文件",
                    3 => "找不到指定的路径",
                    5 => "拒绝访问（权限不足）",
                    8 => "内存不足",
                    11 => "服务程序文件损坏或无效",
                    26 => "无法共享",
                    27 => "文件名关联不完整或无效",
                    28 => "操作超时",
                    29 => "DDE 事务失败",
                    30 => "DDE 事务正在处理中",
                    31 => "没有关联的应用程序",
                    32 => "未找到或未注册 DLL",
                    _ if result_value == 1223 => "用户取消了 UAC 权限提升对话框",
                    _ => "未知错误",
                };

                anyhow::bail!(
                    "服务{}失败（错误代码：{}）：{}。\n\n请确保：\n1. 已在 UAC 对话框中点击\"是\"\n2. 服务程序文件完整且未被杀毒软件隔离\n3. 当前用户具有管理员权限",
                    operation,
                    result_value,
                    error_detail
                );
            }
        }

        // 问题 12：使用轮询代替固定等待，更精确地检测操作完成
        // 每 200ms 检查一次服务状态，最多检查 20 次（4 秒超时）
        let is_install = operation == "install";
        let mut operation_completed = false;

        for i in 0..20 {
            tokio::time::sleep(std::time::Duration::from_millis(200)).await;

            let service_exists = Self::is_service_installed();

            // 安装：等待服务出现；卸载：等待服务消失
            if (is_install && service_exists) || (!is_install && !service_exists) {
                let operation_name = if is_install { "安装" } else { "卸载" };
                log::info!(
                    "服务 {} 操作完成（检测到状态变化，耗时 {} ms）",
                    operation_name,
                    (i + 1) * 200
                );
                operation_completed = true;
                break;
            }
        }

        if !operation_completed {
            log::warn!(
                "服务{}操作未在 4 秒内完成状态检测，可能需要更多时间",
                operation
            );
        }

        Ok(())
    }

    // 启动 Clash 核心（通过服务）
    pub async fn start_clash(
        &self,
        core_path: String,
        config_path: String,
        data_dir: String,
        external_controller: String,
    ) -> Result<Option<u32>> {
        log::debug!("通过服务启动 Clash 核心…");
        let response = self
            .ipc_client
            .send_command(IpcCommand::StartClash {
                core_path,
                config_path,
                data_dir,
                external_controller,
            })
            .await
            .context("发送启动命令失败")?;

        match response {
            IpcResponse::Success { message } => {
                log::debug!("Clash 启动成功：{:?}", message);

                // 启动后立即获取 PID
                match self.ipc_client.send_command(IpcCommand::GetStatus).await {
                    Ok(IpcResponse::Status { clash_pid, .. }) => {
                        log::debug!("获取到 Clash PID：{:?}", clash_pid);
                        Ok(clash_pid)
                    }
                    _ => {
                        log::warn!("无法获取 Clash PID");
                        Ok(None)
                    }
                }
            }
            IpcResponse::Error { code, message } => {
                anyhow::bail!("Clash 启动失败（code={}）：{}", code, message)
            }
            _ => anyhow::bail!("收到意外响应：{:?}", response),
        }
    }

    // 停止 Clash 核心（通过服务）
    pub async fn stop_clash(&self) -> Result<()> {
        log::debug!("通过服务停止 Clash 核心…");
        let response = self
            .ipc_client
            .send_command(IpcCommand::StopClash)
            .await
            .context("发送停止命令失败")?;

        match response {
            IpcResponse::Success { message } => {
                log::debug!("Clash 停止成功：{:?}", message);
                Ok(())
            }
            IpcResponse::Error { code, message } => {
                anyhow::bail!("Clash 停止失败（code={}）：{}", code, message)
            }
            _ => anyhow::bail!("收到意外响应：{:?}", response),
        }
    }

    // 获取服务二进制路径（始终使用私有目录）
    fn get_service_exe_path() -> Result<PathBuf> {
        let app_data_dir = Self::get_app_data_dir()?;

        #[cfg(windows)]
        let service_exe = app_data_dir.join("stelliberty-service.exe");

        #[cfg(not(windows))]
        let service_exe = app_data_dir.join("stelliberty-service");

        Ok(service_exe)
    }

    // 获取便携式目录中的服务二进制路径
    fn get_source_service_exe_path() -> Result<PathBuf> {
        let current_exe = std::env::current_exe().context("无法获取当前程序路径")?;
        let binary_dir = current_exe.parent().context("无法获取当前程序目录")?;

        #[cfg(windows)]
        let source_service_exe = binary_dir
            .join("data")
            .join("flutter_assets")
            .join("assets")
            .join("service")
            .join("stelliberty-service.exe");

        #[cfg(not(windows))]
        let source_service_exe = binary_dir
            .join("data")
            .join("flutter_assets")
            .join("assets")
            .join("service")
            .join("stelliberty-service");

        if !source_service_exe.exists() {
            anyhow::bail!(
                "服务程序不存在：{}。请检查应用打包是否正确",
                source_service_exe.display()
            );
        }

        Ok(source_service_exe)
    }

    // 获取应用数据目录（私有目录）
    fn get_app_data_dir() -> Result<PathBuf> {
        #[cfg(windows)]
        {
            // Windows: %LOCALAPPDATA%\Stelliberty\service
            let local_app_data =
                std::env::var("LOCALAPPDATA").context("无法获取 LOCALAPPDATA 环境变量")?;
            Ok(PathBuf::from(local_app_data)
                .join("Stelliberty")
                .join("service"))
        }

        #[cfg(target_os = "linux")]
        {
            // Linux: ~/.local/share/stelliberty/service
            let home = std::env::var("HOME").context("无法获取 HOME 环境变量")?;
            Ok(PathBuf::from(home)
                .join(".local")
                .join("share")
                .join("stelliberty")
                .join("service"))
        }

        #[cfg(target_os = "macos")]
        {
            // macOS: ~/Library/Application Support/Stelliberty/service
            let home = std::env::var("HOME").context("无法获取 HOME 环境变量")?;
            Ok(PathBuf::from(home)
                .join("Library")
                .join("Application Support")
                .join("Stelliberty")
                .join("service"))
        }

        #[cfg(not(any(windows, target_os = "linux", target_os = "macos")))]
        {
            anyhow::bail!("不支持的操作系统")
        }
    }

    #[cfg(windows)]
    fn is_service_installed() -> bool {
        use windows_service::{
            service::ServiceAccess,
            service_manager::{ServiceManager, ServiceManagerAccess},
        };

        const SERVICE_NAME: &str = "StellibertyService";

        let Ok(manager) =
            ServiceManager::local_computer(None::<&str>, ServiceManagerAccess::CONNECT)
        else {
            return false;
        };

        manager
            .open_service(SERVICE_NAME, ServiceAccess::QUERY_STATUS)
            .is_ok()
    }

    #[cfg(not(windows))]
    fn is_service_installed() -> bool {
        // 非 Windows 平台：通过 IPC 检测服务是否运行来判断
        // 这里返回 true 让后续逻辑通过 IPC 检测
        true
    }
}

impl Default for ServiceManager {
    fn default() -> Self {
        Self::new().unwrap_or_else(|e| {
            log::error!("创建 ServiceManager 失败：{}", e);

            // 使用备用路径（尝试从私有目录或便携式目录）
            let service_exe_path = Self::get_app_data_dir()
                .ok()
                .and_then(|app_data_dir| {
                    #[cfg(windows)]
                    let path = app_data_dir.join("stelliberty-service.exe");

                    #[cfg(not(windows))]
                    let path = app_data_dir.join("stelliberty-service");

                    if path.exists() { Some(path) } else { None }
                })
                .unwrap_or_else(|| {
                    // 备用：尝试从便携式目录
                    let current_exe =
                        std::env::current_exe().unwrap_or_else(|_| std::path::PathBuf::from("."));
                    let binary_dir = current_exe
                        .parent()
                        .unwrap_or_else(|| std::path::Path::new("."));

                    #[cfg(windows)]
                    let fallback_path = binary_dir
                        .join("data")
                        .join("flutter_assets")
                        .join("assets")
                        .join("service")
                        .join("stelliberty-service.exe");

                    #[cfg(not(windows))]
                    let fallback_path = binary_dir
                        .join("data")
                        .join("flutter_assets")
                        .join("assets")
                        .join("service")
                        .join("stelliberty-service");

                    fallback_path
                });

            Self {
                ipc_client: IpcClient::default(),
                service_exe_path,
            }
        })
    }
}

// Rinf 消息定义

// Dart → Rust：获取服务状态请求
#[derive(Deserialize, DartSignal)]
pub struct GetServiceStatus;

// Dart → Rust：安装服务请求
#[derive(Deserialize, DartSignal)]
pub struct InstallService;

// Dart → Rust：卸载服务请求
#[derive(Deserialize, DartSignal)]
pub struct UninstallService;

// Dart → Rust：通过服务启动 Clash
#[derive(Deserialize, DartSignal)]
pub struct StartClash {
    pub core_path: String,
    pub config_path: String,
    pub data_dir: String,
    pub external_controller: String,
}

// Dart → Rust：通过服务停止 Clash
#[derive(Deserialize, DartSignal)]
pub struct StopClash;

// Rust → Dart：服务状态响应
#[derive(Serialize, RustSignal)]
pub struct ServiceStatusResponse {
    pub status: String,
    pub pid: Option<u32>,
    pub uptime: Option<u64>,
}

// Rust → Dart：服务操作结果
#[derive(Serialize, RustSignal)]
pub struct ServiceOperationResult {
    pub success: bool,
    pub error_message: Option<String>,
}

// 消息处理逻辑

impl GetServiceStatus {
    pub async fn handle(&self) {
        let service_manager = match ServiceManager::new() {
            Ok(sm) => sm,
            Err(e) => {
                log::error!("创建 ServiceManager 失败：{}", e);
                ServiceStatusResponse {
                    status: "unknown".to_string(),
                    pid: None,
                    uptime: None,
                }
                .send_signal_to_dart();
                return;
            }
        };

        let status = service_manager.get_status().await;
        let response = match status {
            ServiceStatus::Running { pid, uptime } => ServiceStatusResponse {
                status: "running".to_string(),
                pid: Some(pid),
                uptime: Some(uptime),
            },
            ServiceStatus::Stopped => ServiceStatusResponse {
                status: "stopped".to_string(),
                pid: None,
                uptime: None,
            },
            #[cfg(windows)]
            ServiceStatus::NotInstalled => ServiceStatusResponse {
                status: "not_installed".to_string(),
                pid: None,
                uptime: None,
            },
            ServiceStatus::Unknown => ServiceStatusResponse {
                status: "unknown".to_string(),
                pid: None,
                uptime: None,
            },
        };

        response.send_signal_to_dart();
    }
}

impl InstallService {
    pub async fn handle(&self) {
        let service_manager = match ServiceManager::new() {
            Ok(sm) => sm,
            Err(e) => {
                log::error!("创建 ServiceManager 失败：{}", e);
                ServiceOperationResult {
                    success: false,
                    error_message: Some(format!("创建服务管理器失败：{}", e)),
                }
                .send_signal_to_dart();
                return;
            }
        };

        match service_manager.install_service().await {
            Ok(()) => {
                log::info!("服务安装成功");
                ServiceOperationResult {
                    success: true,
                    error_message: None,
                }
                .send_signal_to_dart();
            }
            Err(e) => {
                log::error!("服务安装失败：{}", e);
                ServiceOperationResult {
                    success: false,
                    error_message: Some(e.to_string()),
                }
                .send_signal_to_dart();
            }
        }
    }
}

impl UninstallService {
    pub async fn handle(&self) {
        let service_manager = match ServiceManager::new() {
            Ok(sm) => sm,
            Err(e) => {
                log::error!("创建 ServiceManager 失败：{}", e);
                ServiceOperationResult {
                    success: false,
                    error_message: Some(format!("创建服务管理器失败：{}", e)),
                }
                .send_signal_to_dart();
                return;
            }
        };

        match service_manager.uninstall_service().await {
            Ok(()) => {
                log::info!("服务卸载成功");
                ServiceOperationResult {
                    success: true,
                    error_message: None,
                }
                .send_signal_to_dart();
            }
            Err(e) => {
                log::error!("服务卸载失败：{}", e);
                ServiceOperationResult {
                    success: false,
                    error_message: Some(e.to_string()),
                }
                .send_signal_to_dart();
            }
        }
    }
}

impl StartClash {
    pub async fn handle(&self) {
        let service_manager = match ServiceManager::new() {
            Ok(sm) => sm,
            Err(e) => {
                log::error!("创建 ServiceManager 失败：{}", e);
                ClashProcessResult {
                    success: false,
                    error_message: Some(format!("创建服务管理器失败：{}", e)),
                    pid: None,
                }
                .send_signal_to_dart();
                return;
            }
        };

        match service_manager
            .start_clash(
                self.core_path.clone(),
                self.config_path.clone(),
                self.data_dir.clone(),
                self.external_controller.clone(),
            )
            .await
        {
            Ok(pid) => {
                log::info!("通过服务启动 Clash 成功，PID：{:?}", pid);
                ClashProcessResult {
                    success: true,
                    error_message: None,
                    pid,
                }
                .send_signal_to_dart();
            }
            Err(e) => {
                log::error!("通过服务启动 Clash 失败：{}", e);
                ClashProcessResult {
                    success: false,
                    error_message: Some(e.to_string()),
                    pid: None,
                }
                .send_signal_to_dart();
            }
        }
    }
}

impl StopClash {
    pub async fn handle(&self) {
        let service_manager = match ServiceManager::new() {
            Ok(sm) => sm,
            Err(e) => {
                log::error!("创建 ServiceManager 失败：{}", e);
                ClashProcessResult {
                    success: false,
                    error_message: Some(format!("创建服务管理器失败：{}", e)),
                    pid: None,
                }
                .send_signal_to_dart();
                return;
            }
        };

        match service_manager.stop_clash().await {
            Ok(()) => {
                log::info!("通过服务停止 Clash 成功");
                ClashProcessResult {
                    success: true,
                    error_message: None,
                    pid: None,
                }
                .send_signal_to_dart();
            }
            Err(e) => {
                log::error!("通过服务停止 Clash 失败：{}", e);
                ClashProcessResult {
                    success: false,
                    error_message: Some(e.to_string()),
                    pid: None,
                }
                .send_signal_to_dart();
            }
        }
    }
}

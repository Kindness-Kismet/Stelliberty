// Clash 服务模式管理
//
// 通过 Windows Service/systemd 以管理员权限运行 Clash 核心

use crate::molecules::clash_process::process_manager::ClashProcessResult;
use anyhow::{Context, Result};
use rinf::{DartSignal, RustSignal};
use serde::{Deserialize, Serialize};
use std::path::PathBuf;
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
    service_binary_path: PathBuf,
}

impl ServiceManager {
    // 创建服务管理器
    pub fn new() -> Result<Self> {
        // 使用 assets 中的服务二进制（而非私有目录）以便 install 命令比对版本
        // 首次安装时私有目录不存在，更新时比较 assets 版本和私有目录版本
        let service_binary_path = crate::atoms::path_service::assets_service_binary();
        Ok(Self {
            ipc_client: IpcClient::default(),
            service_binary_path,
        })
    }

    // 获取已安装服务的版本号（从私有目录中的服务程序）
    pub fn get_installed_service_version() -> Option<String> {
        let service_binary_path = crate::atoms::path_service::service_private_binary();

        // 检查私有目录中的服务程序是否存在
        if !service_binary_path.exists() {
            log::debug!(
                "私有目录中不存在服务程序：{}",
                service_binary_path.display()
            );
            return None;
        }

        // 执行 stelliberty-service version 命令
        // Windows 平台使用 CREATE_NO_WINDOW 避免终端窗口闪屏
        let mut cmd = Command::new(&service_binary_path);
        cmd.arg("version");

        #[cfg(windows)]
        {
            use std::os::windows::process::CommandExt;
            const CREATE_NO_WINDOW: u32 = 0x08000000;
            cmd.creation_flags(CREATE_NO_WINDOW);
        }

        let output = match cmd.output() {
            Ok(output) => output,
            Err(e) => {
                log::error!("执行私有目录服务程序 version 命令失败：{}", e);
                return None;
            }
        };

        if !output.status.success() {
            log::error!("私有目录服务程序 version 命令返回错误");
            return None;
        }

        // 解析输出：Stelliberty Service v1.5.0
        let stdout = String::from_utf8_lossy(&output.stdout);
        let version = stdout
            .trim()
            .strip_prefix("Stelliberty Service v")
            .map(|v| v.to_string());

        log::debug!("已安装服务版本：{:?}", version);
        version
    }

    // 获取内置服务的版本号（从应用 assets 中的服务二进制）
    pub fn get_bundled_service_version() -> Option<String> {
        let source_service_binary = crate::atoms::path_service::assets_service_binary();

        // 检查 assets 中的服务程序是否存在
        if !source_service_binary.exists() {
            log::error!(
                "服务程序不存在：{}。请检查应用打包是否正确",
                source_service_binary.display()
            );
            return None;
        }

        // 执行 stelliberty-service version 命令
        // Windows 平台使用 CREATE_NO_WINDOW 避免终端窗口闪屏
        let mut cmd = Command::new(&source_service_binary);
        cmd.arg("version");

        #[cfg(windows)]
        {
            use std::os::windows::process::CommandExt;
            const CREATE_NO_WINDOW: u32 = 0x08000000;
            cmd.creation_flags(CREATE_NO_WINDOW);
        }

        let output = match cmd.output() {
            Ok(output) => output,
            Err(e) => {
                log::error!("执行服务程序 version 命令失败：{}", e);
                return None;
            }
        };

        if !output.status.success() {
            log::error!("服务程序 version 命令返回错误");
            return None;
        }

        // 解析输出：Stelliberty Service v1.5.0
        let stdout = String::from_utf8_lossy(&output.stdout);
        let version = stdout
            .trim()
            .strip_prefix("Stelliberty Service v")
            .map(|v| v.to_string());

        log::debug!("内置服务版本：{:?}", version);
        version
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

            // 服务已安装，快速检测是否运行（不带重试的 Heartbeat）
            let is_running = tokio::time::timeout(
                std::time::Duration::from_millis(300),
                self.ipc_client.send_command(IpcCommand::Heartbeat),
            )
            .await
            .ok()
            .and_then(|r| r.ok())
            .map(|resp| matches!(resp, IpcResponse::HeartbeatAck))
            .unwrap_or(false);

            if !is_running {
                log::debug!("服务已安装但未运行");
                return ServiceStatus::Stopped;
            }

            // 服务正在运行，获取详细状态
            match self.ipc_client.send_command(IpcCommand::GetStatus).await {
                Ok(IpcResponse::Status {
                    is_clash_running: _,
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
            // Linux/macOS：先检查服务是否已安装（避免不必要的 IPC 连接尝试）
            #[cfg(target_os = "linux")]
            {
                if !Self::is_systemd_service_installed() {
                    // 服务未安装，直接返回 Unknown（类似 Windows 的 NotInstalled）
                    log::debug!("服务未安装");
                    return ServiceStatus::Unknown;
                }

                // 服务已安装，检查是否运行中
                if Self::is_systemd_service_active() {
                    // 服务正在运行，尝试 IPC 获取详细状态
                    if let Ok(IpcResponse::Status {
                        is_clash_running: _,
                        clash_pid,
                        service_uptime,
                    }) = self.ipc_client.send_command(IpcCommand::GetStatus).await
                    {
                        if let Some(pid) = clash_pid {
                            ServiceStatus::Running {
                                pid,
                                uptime: service_uptime,
                            }
                        } else {
                            log::debug!("服务进程运行中，但 Clash 核心未启动");
                            ServiceStatus::Stopped
                        }
                    } else {
                        // IPC 失败但 systemd 显示 active，可能刚启动
                        log::debug!("systemd 服务 active，但 IPC 连接失败");
                        ServiceStatus::Stopped
                    }
                } else {
                    // 服务已安装但未运行
                    log::debug!("systemd 服务已安装但未运行");
                    ServiceStatus::Stopped
                }
            }

            // macOS 或其他平台：使用 IPC 检测
            #[cfg(not(target_os = "linux"))]
            {
                if self.ipc_client.is_service_running().await
                    && let Ok(IpcResponse::Status {
                        is_clash_running: _,
                        clash_pid,
                        service_uptime,
                    }) = self.ipc_client.send_command(IpcCommand::GetStatus).await
                {
                    if let Some(pid) = clash_pid {
                        return ServiceStatus::Running {
                            pid,
                            uptime: service_uptime,
                        };
                    } else {
                        log::debug!("服务进程运行中，但 Clash 核心未启动");
                        return ServiceStatus::Stopped;
                    }
                }
                ServiceStatus::Unknown
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

        // 注意：不在此处复制服务二进制文件
        // 由 stelliberty-service 的 install 命令自行处理更新检测和文件复制
        // 这样才能正确判断是首次安装还是更新

        #[cfg(windows)]
        {
            // 执行提权安装命令（会弹 UAC，用户可能取消）
            // 如果用户取消，这里会返回错误，核心不会被停止
            self.run_elevated_command("install").await?;

            // 走到这里说明用户确认了权限，安装成功
            // 如果核心未运行，无需停止
            if !clash_was_running {
                return Ok(());
            }

            // 核心正在运行，现在可以安全地停止了
            log::info!("权限确认成功，停止 Clash 核心...");
            if let Err(e) = self.stop_clash().await {
                log::warn!("停止 Clash 核心失败：{}，但服务已安装", e);
            } else {
                log::info!("Clash 核心已停止");
            }
        }

        #[cfg(target_os = "linux")]
        {
            // 检查是否已有 root 权限
            let has_root = nix::unistd::geteuid().is_root();

            if has_root {
                // 已有 root 权限，直接执行
                let output = Command::new(&self.service_binary_path)
                    .arg("install")
                    .output()
                    .context("执行安装命令失败")?;

                if !output.status.success() {
                    let stderr = String::from_utf8_lossy(&output.stderr);
                    let stdout = String::from_utf8_lossy(&output.stdout);
                    anyhow::bail!("安装服务失败：{}{}", stderr, stdout);
                }
            } else {
                // 尝试 pkexec 提权
                let output = Command::new("pkexec")
                    .arg(&self.service_binary_path)
                    .arg("install")
                    .output();

                match output {
                    Ok(output) if output.status.success() => {
                        // pkexec 成功
                    }
                    Ok(output) => {
                        // pkexec 执行了但失败
                        let code = output.status.code().unwrap_or(-1);
                        if code == 126 || code == 127 {
                            // 126: 用户取消授权，127: pkexec 未找到
                            anyhow::bail!("安装失败，请以 sudo 运行应用后重试");
                        }
                        let stderr = String::from_utf8_lossy(&output.stderr);
                        anyhow::bail!("安装失败：{}", stderr.trim());
                    }
                    Err(_) => {
                        // pkexec 命令不存在
                        anyhow::bail!("安装失败，请以 sudo 运行应用后重试");
                    }
                }
            }
        }

        #[cfg(target_os = "macos")]
        {
            // macOS 使用 osascript 进行图形化提权（已在 stelliberty_service 中实现）
            let output = Command::new(&self.service_binary_path)
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

        // 执行卸载命令（会弹 UAC，用户可能取消）
        // uninstall 命令会自动停止服务进程（包括 Clash 核心）
        #[cfg(windows)]
        {
            self.run_elevated_command("uninstall").await?;
            log::info!("服务已卸载（服务进程已自动停止）");
        }

        #[cfg(target_os = "linux")]
        {
            // 检查是否已有 root 权限
            let has_root = nix::unistd::geteuid().is_root();

            if has_root {
                // 已有 root 权限，直接执行
                let output = Command::new(&self.service_binary_path)
                    .arg("uninstall")
                    .output()
                    .context("执行卸载命令失败")?;

                if !output.status.success() {
                    let stderr = String::from_utf8_lossy(&output.stderr);
                    let stdout = String::from_utf8_lossy(&output.stdout);
                    anyhow::bail!("卸载服务失败：{}{}", stderr, stdout);
                }
            } else {
                // 尝试 pkexec 提权
                let output = Command::new("pkexec")
                    .arg(&self.service_binary_path)
                    .arg("uninstall")
                    .output();

                match output {
                    Ok(output) if output.status.success() => {
                        // pkexec 成功
                    }
                    Ok(output) => {
                        let code = output.status.code().unwrap_or(-1);
                        if code == 126 || code == 127 {
                            anyhow::bail!("卸载失败，请以 sudo 运行应用后重试");
                        }
                        let stderr = String::from_utf8_lossy(&output.stderr);
                        anyhow::bail!("卸载失败：{}", stderr.trim());
                    }
                    Err(_) => {
                        anyhow::bail!("卸载失败，请以 sudo 运行应用后重试");
                    }
                }
            }
        }

        #[cfg(target_os = "macos")]
        {
            let output = Command::new(&self.service_binary_path)
                .arg("uninstall")
                .output()
                .context("执行卸载命令失败")?;

            if !output.status.success() {
                let stderr = String::from_utf8_lossy(&output.stderr);
                anyhow::bail!("卸载服务失败：{}", stderr);
            }
        }

        // 通知 Dart 端核心已停止
        ClashProcessResult {
            is_successful: true,
            error_message: None,
            pid: None,
        }
        .send_signal_to_dart();

        // 只有卸载成功后才删除私有目录中的服务二进制文件
        self.remove_service_binary_from_private().await?;

        Ok(())
    }

    // 删除私有目录中的服务二进制（卸载时调用）
    async fn remove_service_binary_from_private(&self) -> Result<()> {
        let private_service_binary = crate::atoms::path_service::service_private_binary();

        if private_service_binary.exists() {
            log::info!(
                "删除私有目录中的服务程序：{}",
                private_service_binary.display()
            );

            // 问题 14：卸载后服务进程可能还在释放文件句柄，需要等待并重试
            let mut retry_count = 0;
            const MAX_RETRIES: u32 = 15; // 最多重试 15 次（3 秒）

            loop {
                match std::fs::remove_file(&private_service_binary) {
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
                            private_service_binary.display()
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
            .service_binary_path
            .to_str()
            .context("服务程序路径包含无效字符")?;

        log::info!("以管理员权限执行：{} {}", binary_path, operation);

        // 再次验证服务程序是否存在（防止文件被删除）
        if !self.service_binary_path.exists() {
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
        let mut is_operation_completed = false;

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
                is_operation_completed = true;
                break;
            }
        }

        if !is_operation_completed {
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

    // 检查 systemd 服务是否已安装（仅 Linux）
    #[cfg(target_os = "linux")]
    fn is_systemd_service_installed() -> bool {
        const SERVICE_FILE: &str = "/etc/systemd/system/StellibertyService.service";
        std::path::Path::new(SERVICE_FILE).exists()
    }

    // 检查 systemd 服务是否正在运行（仅 Linux）
    #[cfg(target_os = "linux")]
    fn is_systemd_service_active() -> bool {
        const SERVICE_NAME: &str = "StellibertyService";
        Command::new("systemctl")
            .args(["is-active", "--quiet", SERVICE_NAME])
            .status()
            .map(|s| s.success())
            .unwrap_or(false)
    }
}

impl Default for ServiceManager {
    fn default() -> Self {
        Self::new().unwrap_or_else(|e| {
            log::error!("创建 ServiceManager 失败：{}", e);

            // 使用备用路径（尝试从私有目录或便携式目录）
            let service_binary_path = {
                let private_binary = crate::atoms::path_service::service_private_binary();
                if private_binary.exists() {
                    private_binary
                } else {
                    // 备用：尝试从 assets 目录
                    crate::atoms::path_service::assets_service_binary()
                }
            };

            Self {
                ipc_client: IpcClient::default(),
                service_binary_path,
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

// Dart -> Rust: 向服务发送心跳
#[derive(Deserialize, DartSignal)]
pub struct SendServiceHeartbeat;

// Dart → Rust：获取服务版本号
#[derive(Deserialize, DartSignal)]
pub struct GetServiceVersion;

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
    pub is_successful: bool,
    pub error_message: Option<String>,
}

// Rust → Dart：服务版本号响应
#[derive(Serialize, RustSignal)]
pub struct ServiceVersionResponse {
    // 已安装服务的版本号（如果服务未安装或未运行，则为 None）
    pub installed_version: Option<String>,
    // 应用内置的服务版本号
    pub bundled_version: String,
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
                    is_successful: false,
                    error_message: Some(format!("创建服务管理器失败：{}", e)),
                }
                .send_signal_to_dart();
                return;
            }
        };

        match service_manager.install_service().await {
            Ok(()) => {
                log::info!("服务安装成功");
                // install_service 已调用 stop_clash 清理网络资源

                ServiceOperationResult {
                    is_successful: true,
                    error_message: None,
                }
                .send_signal_to_dart();
            }
            Err(e) => {
                log::error!("服务安装失败：{}", e);
                ServiceOperationResult {
                    is_successful: false,
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
                    is_successful: false,
                    error_message: Some(format!("创建服务管理器失败：{}", e)),
                }
                .send_signal_to_dart();
                return;
            }
        };

        match service_manager.uninstall_service().await {
            Ok(()) => {
                log::info!("服务卸载成功");

                // 清理网络资源（连接已失效）
                tokio::spawn(async {
                    log::info!("开始清理网络资源（服务卸载）");
                    crate::molecules::clash_network::handlers::cleanup_all_network_resources()
                        .await;
                    log::info!("网络资源清理完成（服务卸载）");
                });

                // 清理进程管理器状态
                crate::molecules::clash_process::process_manager::cleanup_process_manager().await;

                ServiceOperationResult {
                    is_successful: true,
                    error_message: None,
                }
                .send_signal_to_dart();
            }
            Err(e) => {
                log::error!("服务卸载失败：{}", e);
                ServiceOperationResult {
                    is_successful: false,
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
                    is_successful: false,
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
                    is_successful: true,
                    error_message: None,
                    pid,
                }
                .send_signal_to_dart();
            }
            Err(e) => {
                log::error!("通过服务启动 Clash 失败：{}", e);
                ClashProcessResult {
                    is_successful: false,
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
                    is_successful: false,
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

                // 异步清理网络资源（IPC 连接池和 WebSocket）
                tokio::spawn(async {
                    log::info!("开始清理网络资源（服务模式）");
                    crate::molecules::clash_network::handlers::cleanup_all_network_resources()
                        .await;
                    log::info!("网络资源清理完成（服务模式）");
                });

                ClashProcessResult {
                    is_successful: true,
                    error_message: None,
                    pid: None,
                }
                .send_signal_to_dart();
            }
            Err(e) => {
                log::error!("通过服务停止 Clash 失败：{}", e);
                ClashProcessResult {
                    is_successful: false,
                    error_message: Some(e.to_string()),
                    pid: None,
                }
                .send_signal_to_dart();
            }
        }
    }
}

impl SendServiceHeartbeat {
    pub async fn handle(&self) {
        let client = IpcClient::new()
            .with_timeout(std::time::Duration::from_secs(2))
            .with_max_retries(0);

        match client.send_command(IpcCommand::Heartbeat).await {
            Ok(IpcResponse::HeartbeatAck) => {
                log::trace!("服务心跳发送成功");
                // 成功时不需要向 Dart 发送信号
            }
            Ok(resp) => {
                log::warn!("发送心跳时收到意外响应: {:?}", resp);
            }
            Err(e) => {
                log::warn!("发送服务心跳失败: {}", e);
            }
        }
    }
}

impl GetServiceVersion {
    pub async fn handle(&self) {
        // 获取已安装服务的版本号（从私有目录）
        let installed_version = ServiceManager::get_installed_service_version();

        // 获取内置服务的版本号（从 assets）
        let bundled_version =
            ServiceManager::get_bundled_service_version().unwrap_or_else(|| "unknown".to_string());

        log::debug!(
            "服务版本信息 - 已安装: {:?}, 内置: {}",
            installed_version,
            bundled_version
        );

        ServiceVersionResponse {
            installed_version,
            bundled_version,
        }
        .send_signal_to_dart();
    }
}

// 初始化 Dart 信号监听器
pub fn init_dart_signal_listeners() {
    use tokio::spawn;

    // 获取服务状态
    spawn(async {
        let receiver = GetServiceStatus::get_dart_signal_receiver();
        while let Some(dart_signal) = receiver.recv().await {
            let message = dart_signal.message;
            tokio::spawn(async move {
                message.handle().await;
            });
        }
    });

    // 安装服务
    spawn(async {
        let receiver = InstallService::get_dart_signal_receiver();
        while let Some(dart_signal) = receiver.recv().await {
            let message = dart_signal.message;
            tokio::spawn(async move {
                message.handle().await;
            });
        }
    });

    // 卸载服务
    spawn(async {
        let receiver = UninstallService::get_dart_signal_receiver();
        while let Some(dart_signal) = receiver.recv().await {
            let message = dart_signal.message;
            tokio::spawn(async move {
                message.handle().await;
            });
        }
    });

    // 通过服务启动 Clash
    spawn(async {
        let receiver = StartClash::get_dart_signal_receiver();
        while let Some(dart_signal) = receiver.recv().await {
            let message = dart_signal.message;
            tokio::spawn(async move {
                message.handle().await;
            });
        }
    });

    // 通过服务停止 Clash
    spawn(async {
        let receiver = StopClash::get_dart_signal_receiver();
        while let Some(dart_signal) = receiver.recv().await {
            let message = dart_signal.message;
            tokio::spawn(async move {
                message.handle().await;
            });
        }
    });

    // 向服务发送心跳
    spawn(async {
        let receiver = SendServiceHeartbeat::get_dart_signal_receiver();
        while let Some(dart_signal) = receiver.recv().await {
            let message = dart_signal.message;
            tokio::spawn(async move {
                message.handle().await;
            });
        }
    });

    // 获取服务版本号
    spawn(async {
        let receiver = GetServiceVersion::get_dart_signal_receiver();
        while let Some(dart_signal) = receiver.recv().await {
            let message = dart_signal.message;
            tokio::spawn(async move {
                message.handle().await;
            });
        }
    });
}

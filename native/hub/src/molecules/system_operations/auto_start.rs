// 开机自启动管理
//
// 目的：提供跨平台的开机自启动配置能力(Windows/macOS/Linux)
// Windows: 使用任务计划程序（避免 Win11 启动延迟）
// macOS/Linux: 使用 auto-launch 库

use rinf::{DartSignal, RustSignal};
use serde::{Deserialize, Serialize};

// macOS/Linux 平台使用 auto-launch 库
#[cfg(any(target_os = "macos", target_os = "linux"))]
use auto_launch::AutoLaunchBuilder;
#[cfg(any(target_os = "macos", target_os = "linux"))]
use once_cell::sync::Lazy;
#[cfg(any(target_os = "macos", target_os = "linux"))]
use std::sync::Mutex;

// Windows 平台使用任务计划程序
#[cfg(target_os = "windows")]
use std::path::PathBuf;
#[cfg(target_os = "windows")]
use std::process::Command;

// Dart → Rust：获取开机自启状态
#[derive(Deserialize, DartSignal)]
pub struct GetAutoStartStatus;

// Dart → Rust：设置开机自启状态
#[derive(Deserialize, DartSignal)]
pub struct SetAutoStartStatus {
    pub is_enabled: bool,
}

// Rust → Dart：开机自启状态响应
#[derive(Serialize, RustSignal)]
pub struct AutoStartStatusResult {
    pub is_enabled: bool,
    pub error_message: Option<String>,
}

impl GetAutoStartStatus {
    // 查询当前自启动配置状态
    //
    // 目的：读取系统中的开机自启动设置
    pub fn handle(&self) {
        log::info!("收到获取开机自启动状态请求");

        let (enabled, error_message) = match get_auto_start_status() {
            Ok(status) => (status, None),
            Err(err) => {
                log::error!("获取开机自启状态失败：{}", err);
                (false, Some(err))
            }
        };

        let response = AutoStartStatusResult {
            is_enabled: enabled,
            error_message,
        };

        response.send_signal_to_dart();
    }
}

impl SetAutoStartStatus {
    // 修改自启动配置
    //
    // 目的：启用或禁用应用程序的开机自启动
    pub fn handle(&self) {
        log::info!("收到设置开机自启动状态请求：enabled={}", self.is_enabled);

        let (enabled, error_message) = match set_auto_start_status(self.is_enabled) {
            Ok(status) => (status, None),
            Err(err) => {
                log::error!("设置开机自启状态失败：{}", err);
                (false, Some(err))
            }
        };

        let response = AutoStartStatusResult {
            is_enabled: enabled,
            error_message,
        };

        response.send_signal_to_dart();
    }
}

// 全局自启动配置实例（仅 macOS/Linux）
#[cfg(any(target_os = "macos", target_os = "linux"))]
static AUTO_LAUNCH: Lazy<Mutex<Option<auto_launch::AutoLaunch>>> = Lazy::new(|| Mutex::new(None));

// Windows 任务计划程序实现

#[cfg(target_os = "windows")]
const APP_NAME: &str = "Stelliberty";

#[cfg(target_os = "windows")]
fn get_binary_path() -> Result<String, String> {
    use once_cell::sync::Lazy;
    static CACHED_BINARY_PATH: Lazy<Result<String, String>> = Lazy::new(|| {
        std::env::current_exe()
            .map(|p| p.to_string_lossy().to_string())
            .map_err(|e| format!("无法获取当前可执行文件路径：{}", e))
    });
    CACHED_BINARY_PATH.clone()
}

#[cfg(target_os = "windows")]
fn get_task_dir() -> Result<PathBuf, String> {
    let task_dir = crate::atoms::path_service::tasks_dir();

    // 确保任务目录存在
    if !task_dir.exists() {
        std::fs::create_dir_all(&task_dir).map_err(|e| format!("创建任务目录失败：{}", e))?;
    }

    Ok(task_dir)
}

#[cfg(target_os = "windows")]
fn generate_task_xml(binary_path: &str) -> String {
    format!(
        r#"<?xml version="1.0" encoding="UTF-16"?>
<Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
  <RegistrationInfo>
    <Description>登录时自动启动应用（5秒延迟）</Description>
  </RegistrationInfo>
  <Triggers>
    <LogonTrigger>
      <Enabled>true</Enabled>
      <Delay>PT5S</Delay>
    </LogonTrigger>
  </Triggers>
  <Principals>
    <Principal id="Author">
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>HighestAvailable</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>true</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <IdleSettings>
      <StopOnIdleEnd>false</StopOnIdleEnd>
      <RestartOnIdle>false</RestartOnIdle>
    </IdleSettings>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <WakeToRun>false</WakeToRun>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Priority>4</Priority>
  </Settings>
  <Actions Context="Author">
    <Exec>
      <Command>{}</Command>
      <Arguments>--silent-start</Arguments>
    </Exec>
  </Actions>
</Task>"#,
        binary_path
    )
}

#[cfg(target_os = "windows")]
fn enable_auto_start_windows() -> Result<(), String> {
    log::info!("开始启用开机自启动（Windows 任务计划程序）");

    let binary_path = get_binary_path()?;
    log::debug!("可执行文件路径：{}", binary_path);

    let task_dir = get_task_dir()?;
    log::debug!("任务目录：{}", task_dir.display());

    let xml_path = task_dir.join(format!("{}.xml", APP_NAME));
    log::debug!("XML 配置路径：{}", xml_path.display());

    let xml_content = generate_task_xml(&binary_path);
    log::trace!("生成的 XML 配置:\n{}", xml_content);

    // 写入 XML 文件（UTF-16LE 编码，带 BOM）
    let mut utf16_bytes: Vec<u8> = vec![0xFF, 0xFE];
    for c in xml_content.encode_utf16() {
        utf16_bytes.push((c & 0xFF) as u8);
        utf16_bytes.push((c >> 8) as u8);
    }

    std::fs::write(&xml_path, utf16_bytes).map_err(|e| format!("写入 XML 文件失败：{}", e))?;
    log::debug!("已写入 XML 配置文件");

    // 使用 UAC 提权执行 schtasks.exe
    log::debug!(
        "以管理员权限执行: schtasks.exe /create /tn {} /xml \"{}\" /f",
        APP_NAME,
        xml_path.display()
    );

    run_elevated_schtasks("create", &xml_path)?;

    log::info!("✅ 已成功启用开机自启动（任务计划程序）");
    Ok(())
}

#[cfg(target_os = "windows")]
fn run_elevated_schtasks(operation: &str, xml_path: &std::path::Path) -> Result<(), String> {
    use windows::Win32::UI::Shell::ShellExecuteW;
    use windows::Win32::UI::WindowsAndMessaging::SW_HIDE;
    use windows::core::{HSTRING, PCWSTR};

    let args = match operation {
        "create" => format!(
            "/create /tn {} /xml \"{}\" /f",
            APP_NAME,
            xml_path.display()
        ),
        "delete" => format!("/delete /tn {} /f", APP_NAME),
        _ => return Err(format!("未知操作：{}", operation)),
    };

    log::debug!("UAC 提权执行：schtasks.exe {}", args);

    let verb = HSTRING::from("runas");
    let file = HSTRING::from("schtasks.exe");
    let parameters = HSTRING::from(args);

    unsafe {
        let result = ShellExecuteW(
            None,
            PCWSTR(verb.as_ptr()),
            PCWSTR(file.as_ptr()),
            PCWSTR(parameters.as_ptr()),
            PCWSTR::null(),
            SW_HIDE,
        );

        let result_value = result.0 as isize;
        if result_value <= 32 {
            return Err(format!(
                "ShellExecuteW 失败 (代码: {})，用户可能取消了 UAC 提示",
                result_value
            ));
        }
    }

    // 等待操作完成
    std::thread::sleep(std::time::Duration::from_secs(2));

    Ok(())
}

#[cfg(target_os = "windows")]
fn run_elevated_schtasks_delete() -> Result<(), String> {
    use windows::Win32::UI::Shell::ShellExecuteW;
    use windows::Win32::UI::WindowsAndMessaging::SW_HIDE;
    use windows::core::{HSTRING, PCWSTR};

    let args = format!("/delete /tn {} /f", APP_NAME);

    log::debug!("UAC 提权执行：schtasks.exe {}", args);

    let verb = HSTRING::from("runas");
    let file = HSTRING::from("schtasks.exe");
    let parameters = HSTRING::from(args);

    unsafe {
        let result = ShellExecuteW(
            None,
            PCWSTR(verb.as_ptr()),
            PCWSTR(file.as_ptr()),
            PCWSTR(parameters.as_ptr()),
            PCWSTR::null(),
            SW_HIDE,
        );

        let result_value = result.0 as isize;
        if result_value <= 32 {
            return Err(format!(
                "ShellExecuteW 失败 (代码: {})，用户可能取消了 UAC 提示",
                result_value
            ));
        }
    }

    // 等待操作完成
    std::thread::sleep(std::time::Duration::from_secs(2));

    Ok(())
}

#[cfg(target_os = "windows")]
fn disable_auto_start_windows() -> Result<(), String> {
    log::info!("开始禁用开机自启动（Windows 任务计划程序）");

    // 先检查任务是否存在
    if !is_auto_start_enabled_windows()? {
        log::debug!("任务不存在，已经是禁用状态");
        log::info!("✅ 开机自启动已禁用（任务不存在）");
        return Ok(());
    }

    run_elevated_schtasks_delete()?;

    log::info!("✅ 已成功禁用开机自启动（任务计划程序）");
    Ok(())
}

#[cfg(target_os = "windows")]
fn is_auto_start_enabled_windows() -> Result<bool, String> {
    use std::os::windows::process::CommandExt;
    const CREATE_NO_WINDOW: u32 = 0x08000000;

    log::debug!("检查开机自启动状态（Windows 任务计划程序）");

    // 直接执行 schtasks.exe 查询任务（隐藏控制台窗口）
    let output = Command::new("schtasks.exe")
        .args(["/query", "/tn", APP_NAME])
        .creation_flags(CREATE_NO_WINDOW)
        .output()
        .map_err(|e| format!("执行 schtasks.exe 失败：{}", e))?;

    // 命令成功执行说明任务存在
    let enabled = output.status.success();

    if enabled {
        log::debug!("任务计划程序任务 [{}] 存在，自启动已启用", APP_NAME);
    } else {
        log::debug!("任务计划程序任务 [{}] 不存在，自启动已禁用", APP_NAME);
    }

    Ok(enabled)
}

// macOS/Linux 实现

// 初始化自启动配置（仅 macOS/Linux）
#[cfg(any(target_os = "macos", target_os = "linux"))]
fn init_auto_launch() -> Result<(), String> {
    let mut instance = AUTO_LAUNCH
        .lock()
        .map_err(|e| format!("获取锁失败：{}", e))?;

    if instance.is_some() {
        return Ok(());
    }

    let binary_path = get_cached_binary_path()?;

    let app_name = "Stelliberty";

    #[cfg(target_os = "macos")]
    let auto_launch = {
        let app_path = get_macos_app_path(&binary_path)
            .map_err(|e| format!("无法获取 macOS .app 路径：{}", e))?;

        AutoLaunchBuilder::new()
            .set_app_name(app_name)
            .set_app_path(&app_path)
            .set_use_launch_agent(true)
            .build()
            .map_err(|e| format!("初始化自启动功能失败：{}", e))?
    };

    #[cfg(target_os = "linux")]
    let auto_launch = {
        AutoLaunchBuilder::new()
            .set_app_name(app_name)
            .set_app_path(&binary_path.to_string_lossy())
            .build()
            .map_err(|e| format!("初始化自启动功能失败：{}", e))?
    };

    *instance = Some(auto_launch);
    Ok(())
}

// 从可执行文件路径提取 macOS .app 包路径
#[cfg(target_os = "macos")]
fn get_macos_app_path(binary_path: &std::path::Path) -> Result<String, String> {
    let path_str = binary_path.to_string_lossy();

    if let Some(app_pos) = path_str.find(".app") {
        let app_path = &path_str[..app_pos + 4];
        return Ok(app_path.to_string());
    }

    Err("无法从可执行文件路径解析 macOS .app 路径".to_string())
}

// 获取缓存的可执行文件路径（Unix）
#[cfg(any(target_os = "macos", target_os = "linux"))]
fn get_cached_binary_path() -> Result<std::path::PathBuf, String> {
    use once_cell::sync::Lazy;
    static CACHED_BINARY_PATH: Lazy<Result<std::path::PathBuf, String>> = Lazy::new(|| {
        std::env::current_exe().map_err(|e| format!("无法获取当前可执行文件路径：{}", e))
    });
    CACHED_BINARY_PATH.clone()
}

// 查询当前自启动配置状态
//
// 目的：读取系统自启动注册表/配置文件中的设置
pub fn get_auto_start_status() -> Result<bool, String> {
    #[cfg(target_os = "windows")]
    {
        is_auto_start_enabled_windows()
    }

    #[cfg(any(target_os = "macos", target_os = "linux"))]
    {
        init_auto_launch()?;

        let instance = AUTO_LAUNCH
            .lock()
            .map_err(|e| format!("获取锁失败：{}", e))?;

        match &*instance {
            Some(auto_launch) => auto_launch
                .is_enabled()
                .map_err(|e| format!("获取自启动状态失败：{}", e)),
            None => Err("自启动模块未初始化".to_string()),
        }
    }

    #[cfg(not(any(target_os = "windows", target_os = "macos", target_os = "linux")))]
    {
        // 移动平台 (Android/iOS) 不支持开机自启
        Ok(false)
    }
}

// 修改自启动配置
//
// 目的：在系统中注册或移除应用的开机自启动
pub fn set_auto_start_status(enabled: bool) -> Result<bool, String> {
    #[cfg(target_os = "windows")]
    {
        if enabled {
            enable_auto_start_windows()?;
        } else {
            disable_auto_start_windows()?;
        }

        // 验证设置是否成功（带重试，因为 UAC 操作是异步的）
        let mut status = is_auto_start_enabled_windows()?;
        let mut retries = 0;

        while status != enabled && retries < 10 {
            log::debug!("状态验证中...（尝试 {}/10）", retries + 1);
            std::thread::sleep(std::time::Duration::from_millis(500));
            status = is_auto_start_enabled_windows()?;
            retries += 1;
        }

        if status == enabled {
            log::debug!("✅ 自启动状态已确认变更为: {}", status);
        } else {
            log::debug!("⚠️ 状态验证失败，期望 {}，实际 {}", enabled, status);
        }

        Ok(status)
    }

    #[cfg(any(target_os = "macos", target_os = "linux"))]
    {
        init_auto_launch()?;

        let instance = AUTO_LAUNCH
            .lock()
            .map_err(|e| format!("获取锁失败：{}", e))?;

        match &*instance {
            Some(auto_launch) => {
                if enabled {
                    auto_launch
                        .enable()
                        .map_err(|e| format!("启用开机自启失败：{}", e))?;
                } else {
                    auto_launch
                        .disable()
                        .map_err(|e| format!("禁用开机自启失败：{}", e))?;
                }

                let status = auto_launch
                    .is_enabled()
                    .map_err(|e| format!("获取自启动状态失败：{}", e))?;

                log::debug!("已设置开机自启状态为：{}", status);
                Ok(status)
            }
            None => Err("自启动模块未初始化".to_string()),
        }
    }

    #[cfg(not(any(target_os = "windows", target_os = "macos", target_os = "linux")))]
    {
        // 移动平台 (Android/iOS) 不支持开机自启
        Err("移动平台不支持开机自启动功能".to_string())
    }
}

// 模块初始化入口
//
// 目的：预加载自启动配置，提升首次查询速度
pub fn init() {
    #[cfg(target_os = "windows")]
    {
        // Windows 使用任务计划程序，无需预加载
        log::debug!("Auto-start module initialized (Windows Task Scheduler mode)");
    }

    #[cfg(any(target_os = "macos", target_os = "linux"))]
    {
        if let Err(err) = init_auto_launch() {
            log::error!("Failed to initialize auto-start module: {}", err);
        } else {
            log::debug!("Auto-start module initialized");
        }
    }

    #[cfg(not(any(target_os = "windows", target_os = "macos", target_os = "linux")))]
    {
        // 移动平台不支持开机自启
        log::debug!("Auto-start module not available on mobile platforms");
    }
}

// 初始化 Dart 信号监听器
pub fn init_dart_signal_listeners() {
    use tokio::spawn;

    spawn(async {
        let receiver = GetAutoStartStatus::get_dart_signal_receiver();
        while let Some(dart_signal) = receiver.recv().await {
            let message = dart_signal.message;
            message.handle();
        }
    });

    spawn(async {
        let receiver = SetAutoStartStatus::get_dart_signal_receiver();
        while let Some(dart_signal) = receiver.recv().await {
            let message = dart_signal.message;
            message.handle();
        }
    });
}

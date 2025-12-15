// 应用日志系统（App Log，区别于 Core Log）
// 功能：统一格式、双输出（控制台+文件）、自动轮转（10MB）、受 Dart 端开关控制

use chrono::Local;
use env_logger;
use log;
use once_cell::sync::Lazy;
use rinf::{DartSignal, RustSignal};
use serde::{Deserialize, Serialize};
use std::fs::{self, OpenOptions};
use std::io::Write;
use std::path::PathBuf;
use std::sync::Mutex;
use tokio::spawn;

// Dart → Rust：设置应用日志开关请求
#[derive(Deserialize, DartSignal)]
pub struct SetAppLogEnabled {
    pub enabled: bool,
}

// Rust → Dart：设置应用日志开关响应
#[derive(Serialize, RustSignal)]
pub struct SetAppLogEnabledResult {
    pub success: bool,
}

impl SetAppLogEnabled {
    // 处理设置应用日志开关请求
    pub fn handle(&self) {
        set_app_log_enabled(self.enabled);
        SetAppLogEnabledResult { success: true }.send_signal_to_dart();
    }
}

const MAX_LOG_FILE_SIZE: u64 = 10 * 1024 * 1024; // 10MB 轮转阈值

static LOG_FILE_PATH: Lazy<Mutex<Option<PathBuf>>> = Lazy::new(|| Mutex::new(None));
static APP_LOG_ENABLED: Lazy<Mutex<bool>> = Lazy::new(|| Mutex::new(true)); // 应用日志开关（Dart 端控制）

static LOGGER: Lazy<()> = Lazy::new(|| {
    // 初始化日志文件路径（与 Dart PathService 一致）
    if let Ok(app_data) = get_app_data_dir() {
        let log_path = app_data.join("running.logs");
        if let Ok(mut path_guard) = LOG_FILE_PATH.lock() {
            *path_guard = Some(log_path.clone());
            eprintln!("[RustLog] 应用日志文件路径: {}", log_path.display());
        }
    } else {
        eprintln!("[RustLog] 无法获取应用数据目录，应用日志将被禁用");
    }

    // 日志级别：Release 不能为 "off"（否则 format 回调不执行，文件无法写入）
    // Debug: debug，Release: info，第三方库: warn
    let default_level = if cfg!(debug_assertions) {
        "debug,tungstenite=warn,tokio_tungstenite=warn,reqwest=warn,hyper=warn,h2=warn"
    } else {
        "info,tungstenite=warn,tokio_tungstenite=warn,reqwest=warn,hyper=warn,h2=warn"
    };
    let env = env_logger::Env::default().default_filter_or(default_level);

    env_logger::Builder::from_env(env)
        .format(|buf, record| {
            let timestamp = Local::now().format("%Y/%m/%d %H:%M:%S");
            let file = record.file().unwrap_or("unknown");
            let path_with_dots = file.replace(['/', '\\'], ".");

            // ANSI 颜色代码（用于控制台）
            const GREEN: &str = "\x1B[32m";
            const YELLOW: &str = "\x1B[33m";
            const RED: &str = "\x1B[31m";
            const CYAN: &str = "\x1B[36m";
            const RESET: &str = "\x1B[0m";

            let (level_str, color) = match record.level() {
                log::Level::Error => ("RustError", RED),
                log::Level::Warn => ("RustWarn", YELLOW),
                log::Level::Info => ("RustInfo", GREEN),
                log::Level::Debug => ("RustDebug", CYAN),
                log::Level::Trace => ("RustTrace", CYAN),
            };

            // 控制台输出：仅 Debug 模式
            if cfg!(debug_assertions) {
                writeln!(
                    buf,
                    "{}[{}]{} {} {} >> {}",
                    color,
                    level_str,
                    RESET,
                    timestamp,
                    path_with_dots,
                    record.args()
                )?;
            }

            // 文件输出：所有模式写入
            let file_log = if cfg!(debug_assertions) {
                // Debug：包含文件路径（便于定位）
                format!(
                    "[{}] {} {} >> {}",
                    level_str,
                    timestamp,
                    path_with_dots,
                    record.args()
                )
            } else {
                // Release：简洁格式（无文件路径）
                format!("[{}] {} >> {}", level_str, timestamp, record.args())
            };

            // 异步写入文件（失败静默）
            let _ = write_to_file(&file_log);

            Ok(())
        })
        .init();
});

// 写入日志到文件（受 Dart 端开关控制，多进程安全，失败静默）
fn write_to_file(log_line: &str) -> std::io::Result<()> {
    // 检查开关状态
    let enabled = APP_LOG_ENABLED.lock().map(|g| *g).unwrap_or(true);
    if !enabled {
        return Ok(());
    }

    let path_guard = match LOG_FILE_PATH.lock() {
        Ok(guard) => guard,
        Err(_) => return Ok(()), // 锁失败，静默返回
    };

    if let Some(ref path) = *path_guard {
        check_and_rotate_log(path)?;

        // 追加写入（OS 保证原子性）
        let mut file = OpenOptions::new()
            .create(true)
            .append(true) // 关键：追加模式
            .open(path)?;

        writeln!(file, "{}", log_line)?;
        file.flush()?; // 立即刷新到磁盘
    }

    Ok(())
}

// 检查并轮转日志文件
fn check_and_rotate_log(path: &PathBuf) -> std::io::Result<()> {
    if let Ok(metadata) = fs::metadata(path)
        && metadata.len() > MAX_LOG_FILE_SIZE
    {
        let backup_path = path.with_extension("logs.old");
        let _ = fs::remove_file(&backup_path);
        let _ = fs::rename(path, &backup_path); // 失败时下次再试

        // 写入新文件首行提示
        let mut file = OpenOptions::new()
            .create(true)
            .write(true)
            .truncate(true)
            .open(path)?;

        let clear_msg = format!(
            "[RustInfo] {} >> 日志文件已达 {:.2} MB，已轮转\n",
            Local::now().format("%Y/%m/%d %H:%M:%S"),
            metadata.len() as f64 / 1024.0 / 1024.0
        );
        file.write_all(clear_msg.as_bytes())?;
        file.flush()?;
    }

    Ok(())
}

// 获取应用数据目录（便携模式：可执行文件同级 data/ 目录）
fn get_app_data_dir() -> Result<PathBuf, String> {
    use std::env;

    let binary_path = env::current_exe().map_err(|e| format!("无法获取可执行文件路径：{}", e))?;

    let binary_dir = binary_path
        .parent()
        .ok_or_else(|| "无法获取可执行文件目录".to_string())?;

    Ok(binary_dir.join("data"))
}

/// 设置应用日志启用状态（由 Dart 端通过 rinf 消息调用，线程安全，实时生效）
pub fn set_app_log_enabled(enabled: bool) {
    if let Ok(mut guard) = APP_LOG_ENABLED.lock() {
        *guard = enabled;
    }
}

/// 初始化日志系统（幂等、懒加载、线程安全）
pub fn setup_logger() {
    Lazy::force(&LOGGER);
}

/// 初始化消息监听器
pub fn init_message_listener() {
    spawn(async {
        let receiver = SetAppLogEnabled::get_dart_signal_receiver();
        while let Some(dart_signal) = receiver.recv().await {
            let message = dart_signal.message;
            message.handle();
        }
        log::info!("应用日志开关消息通道已关闭，退出监听器");
    });
}

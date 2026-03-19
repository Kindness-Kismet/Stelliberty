use chrono::Local;
use log;
use once_cell::sync::Lazy;
use rinf::{DartSignal, RustSignal};
use serde::{Deserialize, Serialize};
use std::fs::{self, OpenOptions};
use std::io::Write;
use std::path::PathBuf;
use std::sync::Mutex;
use tokio::spawn;

#[cfg(not(target_os = "android"))]
use env_logger;

#[cfg(target_os = "android")]
use android_logger::{Config, FilterBuilder};

// Dart → Rust：设置应用日志开关请求
#[derive(Deserialize, DartSignal)]
pub struct SetAppLogEnabled {
    pub is_enabled: bool,
}

// Rust → Dart：设置应用日志开关响应
#[derive(Serialize, RustSignal)]
pub struct SetAppLogEnabledResult {
    pub is_successful: bool,
}

impl SetAppLogEnabled {
    // 处理设置应用日志开关请求
    pub fn handle(&self) {
        set_app_log_enabled(self.is_enabled);
        SetAppLogEnabledResult {
            is_successful: true,
        }
        .send_signal_to_dart();
    }
}

const MAX_LOG_FILE_SIZE: u64 = 10 * 1024 * 1024; // 10MB 轮转阈值

static LOG_FILE_PATH: Lazy<Mutex<Option<PathBuf>>> = Lazy::new(|| Mutex::new(None));
static APP_LOG_ENABLED: Lazy<Mutex<bool>> = Lazy::new(|| Mutex::new(true)); // 应用日志开关（Dart 端控制）

static LOGGER: Lazy<()> = Lazy::new(|| {
    #[cfg(target_os = "android")]
    {
        // Android 平台：使用 android_logger 输出到 logcat，自定义格式
        android_logger::init_once(
            Config::default()
                .with_max_level(if cfg!(debug_assertions) {
                    log::LevelFilter::Debug
                } else {
                    log::LevelFilter::Info
                })
                .with_tag("hub")
                // 使用 FilterBuilder 过滤第三方库日志
                .with_filter(
                    FilterBuilder::new()
                        .parse("debug,tungstenite=warn,tokio_tungstenite=warn,reqwest=warn,hyper=warn,h2=warn")
                        .build()
                )
                // 自定义格式：添加时间戳和等级标签
                .format(|f, record| {
                    // 时间戳
                    let timestamp = Local::now().format("%Y/%m/%d %H:%M:%S");

                    // 模块路径（将 :: 替换为 .）
                    let module = record.module_path().unwrap_or("unknown");
                    let path_with_dots = module.replace("::", ".");

                    // 等级标签
                    let level_str = match record.level() {
                        log::Level::Error => "[RsError]",
                        log::Level::Warn => "[RsWarn]",
                        log::Level::Info => "[RsInfo]",
                        log::Level::Debug => "[RsDebug]",
                        log::Level::Trace => "[RsTrace]",
                    };

                    write!(f, "{} {} {} >> {}", level_str, timestamp, path_with_dots, record.args())
                })
        );
    }

    #[cfg(not(target_os = "android"))]
    {
        // 桌面平台：使用 env_logger
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
                    log::Level::Error => ("RsError", RED),
                    log::Level::Warn => ("RsWarn", YELLOW),
                    log::Level::Info => ("RsInfo", GREEN),
                    log::Level::Debug => ("RsDebug", CYAN),
                    log::Level::Trace => ("RsTrace", CYAN),
                };

                // 控制台输出：所有模式（临时调试）
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
    }
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
            "[RsInfo] {} >> 日志文件已达 {:.2} MB，已轮转\n",
            Local::now().format("%Y/%m/%d %H:%M:%S"),
            metadata.len() as f64 / 1024.0 / 1024.0
        );
        file.write_all(clear_msg.as_bytes())?;
        file.flush()?;
    }

    Ok(())
}

// 设置应用日志启用状态（由 Dart 端通过 rinf 消息调用，线程安全，实时生效）
pub fn set_app_log_enabled(enabled: bool) {
    if let Ok(mut guard) = APP_LOG_ENABLED.lock() {
        *guard = enabled;
    }
}

// 设置日志文件路径（必须在 setup_logger 之前调用）
pub fn set_log_file_path(log_path: PathBuf) {
    if let Ok(mut path_guard) = LOG_FILE_PATH.lock() {
        *path_guard = Some(log_path.clone());
        eprintln!("[RustLog] 应用日志文件路径: {}", log_path.display());
    }
}

// 初始化日志系统（幂等、懒加载、线程安全）
pub fn setup_logger() {
    Lazy::force(&LOGGER);
}

// 初始化消息监听器
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

// 统一初始化函数：设置日志路径、初始化日志系统和消息监听器
pub fn init(log_file_path: PathBuf) {
    set_log_file_path(log_file_path);
    setup_logger();
    init_message_listener();
}

// 核心更新服务
//
// 目的：处理 Mihomo 核心的下载、解压和替换

use flate2::read::GzDecoder;
use reqwest::Client;
use rinf::{DartSignal, RustSignal};
use serde::{Deserialize, Serialize};
use serde_json::Value;
use std::io::Read;
use std::path::Path;
use tokio::fs as async_fs;
use tokio::spawn;
use zip::ZipArchive;

const GITHUB_REPO: &str = "MetaCubeX/mihomo";
const API_BASE_URL: &str = "https://api.github.com/repos";

// ============================================================================
// 消息定义
// ============================================================================

// Dart → Rust：获取最新核心版本信息请求
#[derive(Deserialize, DartSignal)]
pub struct GetLatestCoreVersionRequest {}

// Rust → Dart：获取最新核心版本信息响应
#[derive(Serialize, RustSignal)]
pub struct GetLatestCoreVersionResponse {
    pub success: bool,
    pub version: Option<String>,
    pub error_message: Option<String>,
}

// Dart → Rust：下载核心请求
#[derive(Deserialize, DartSignal)]
pub struct DownloadCoreRequest {
    pub platform: String,
    pub arch: String,
}

// Rust → Dart：下载核心进度通知
#[derive(Serialize, RustSignal)]
pub struct DownloadCoreProgress {
    pub progress: f64,   // 0.0 - 1.0
    pub message: String, // 当前步骤描述
    pub downloaded: u64, // 已下载字节数
    pub total: u64,      // 总字节数
}

// Rust → Dart：下载核心响应
#[derive(Serialize, RustSignal)]
pub struct DownloadCoreResponse {
    pub success: bool,
    pub version: Option<String>,
    pub core_bytes: Option<Vec<u8>>,
    pub error_message: Option<String>,
}

// Dart → Rust：替换核心请求
#[derive(Deserialize, DartSignal)]
pub struct ReplaceCoreRequest {
    pub core_dir: String,
    pub core_bytes: Vec<u8>,
    pub platform: String,
}

// Rust → Dart：替换核心响应
#[derive(Serialize, RustSignal)]
pub struct ReplaceCoreResponse {
    pub success: bool,
    pub error_message: Option<String>,
}

// ============================================================================
// 消息处理器
// ============================================================================

impl GetLatestCoreVersionRequest {
    pub async fn handle(self) {
        let response = match get_latest_release().await {
            Ok(release_info) => {
                let version = release_info
                    .get("tag_name")
                    .and_then(|v| v.as_str())
                    .map(|s| s.to_string());

                GetLatestCoreVersionResponse {
                    success: true,
                    version,
                    error_message: None,
                }
            }
            Err(e) => GetLatestCoreVersionResponse {
                success: false,
                version: None,
                error_message: Some(e.to_string()),
            },
        };

        response.send_signal_to_dart();
    }
}

impl DownloadCoreRequest {
    pub async fn handle(self) {
        match download_core(&self.platform, &self.arch).await {
            Ok((version, core_bytes)) => {
                let response = DownloadCoreResponse {
                    success: true,
                    version: Some(version),
                    core_bytes: Some(core_bytes),
                    error_message: None,
                };
                response.send_signal_to_dart();
            }
            Err(e) => {
                let response = DownloadCoreResponse {
                    success: false,
                    version: None,
                    core_bytes: None,
                    error_message: Some(e.to_string()),
                };
                response.send_signal_to_dart();
            }
        }
    }
}

impl ReplaceCoreRequest {
    pub async fn handle(self) {
        let response = match replace_core(&self.core_dir, &self.core_bytes, &self.platform).await {
            Ok(_) => ReplaceCoreResponse {
                success: true,
                error_message: None,
            },
            Err(e) => ReplaceCoreResponse {
                success: false,
                error_message: Some(e.to_string()),
            },
        };

        response.send_signal_to_dart();
    }
}

// ============================================================================
// 核心更新逻辑
// ============================================================================

// 获取最新的 Release 信息
async fn get_latest_release() -> Result<Value, Box<dyn std::error::Error + Send + Sync>> {
    let url = format!("{}/{}/releases/latest", API_BASE_URL, GITHUB_REPO);
    log::info!("获取最新版本信息：{}", url);

    let client = Client::builder()
        .timeout(std::time::Duration::from_secs(10))
        .user_agent("stelliberty")
        .build()?;

    let response = client.get(&url).send().await?;

    if !response.status().is_success() {
        return Err(format!("获取版本信息失败: HTTP {}", response.status()).into());
    }

    let json: Value = response.json().await?;
    Ok(json)
}

// 下载核心文件
//
// 参数：
// - platform: 平台名称（windows, linux, darwin）
// - arch: 架构名称（amd64, arm64）
//
// 返回：(版本号, 核心字节数据)
async fn download_core(
    platform: &str,
    arch: &str,
) -> Result<(String, Vec<u8>), Box<dyn std::error::Error + Send + Sync>> {
    log::info!("开始下载核心：{}-{}", platform, arch);

    // 发送进度通知
    send_progress(0.0, "获取版本信息", 0, 0);

    // 1. 获取最新版本信息
    let release_info = get_latest_release().await?;
    let version = release_info
        .get("tag_name")
        .and_then(|v| v.as_str())
        .ok_or("无法获取版本号")?
        .to_string();

    log::info!("发现新版本：{}", version);
    send_progress(0.1, &format!("找到版本: {}", version), 0, 0);

    // 2. 查找对应平台的资源
    let (download_url, file_name) = find_asset(&release_info, platform, arch)
        .ok_or_else(|| format!("未找到适用于 {}-{} 的核心文件", platform, arch))?;

    log::info!("下载链接：{}", download_url);
    send_progress(0.2, "开始下载", 0, 0);

    // 3. 下载核心文件
    let core_bytes = download_file(&download_url).await?;

    send_progress(0.8, "解压文件", 0, 0);

    // 4. 解压核心文件
    let extracted_bytes = extract_core(&file_name, &core_bytes)?;

    send_progress(1.0, "下载完成", 0, 0);

    log::info!("核心下载成功：{}", version);
    Ok((version, extracted_bytes))
}

// 查找匹配的资源文件
fn find_asset(release_info: &Value, platform: &str, arch: &str) -> Option<(String, String)> {
    let assets = release_info.get("assets")?.as_array()?;
    let keyword = format!("{}-{}", platform, arch);

    for asset in assets {
        if let Some(name) = asset.get("name").and_then(|v| v.as_str())
            && name.contains(&keyword)
        {
            let download_url = asset.get("browser_download_url")?.as_str()?;
            return Some((download_url.to_string(), name.to_string()));
        }
    }

    None
}

// 下载文件（支持进度回调）
async fn download_file(url: &str) -> Result<Vec<u8>, Box<dyn std::error::Error + Send + Sync>> {
    let client = Client::builder()
        .timeout(std::time::Duration::from_secs(30)) // 总超时 30 秒
        .connect_timeout(std::time::Duration::from_secs(10)) // 连接超时 10 秒
        .user_agent("stelliberty")
        .build()?;

    let response = client.get(url).send().await?;

    if !response.status().is_success() {
        return Err(format!("下载失败: HTTP {}", response.status()).into());
    }

    let total = response.content_length().unwrap_or(0);
    let mut downloaded = 0u64;
    let mut bytes = Vec::new();

    let mut stream = response.bytes_stream();
    use futures_util::StreamExt;

    while let Some(chunk) = stream.next().await {
        let chunk = chunk?;
        bytes.extend_from_slice(&chunk);
        downloaded += chunk.len() as u64;

        // 发送进度通知
        if total > 0 {
            let progress = 0.2 + (downloaded as f64 / total as f64) * 0.6;
            let mb_downloaded = downloaded as f64 / 1024.0 / 1024.0;
            let mb_total = total as f64 / 1024.0 / 1024.0;
            let message = format!("下载中 {:.1}/{:.1} MB", mb_downloaded, mb_total);
            send_progress(progress, &message, downloaded, total);
        }
    }

    Ok(bytes)
}

// 解压核心文件
fn extract_core(
    file_name: &str,
    file_bytes: &[u8],
) -> Result<Vec<u8>, Box<dyn std::error::Error + Send + Sync>> {
    log::info!("解压文件：{}", file_name);

    if file_name.ends_with(".zip") {
        // 解压 ZIP 文件
        let cursor = std::io::Cursor::new(file_bytes);
        let mut archive = ZipArchive::new(cursor)?;

        // 查找可执行文件
        for i in 0..archive.len() {
            let mut file = archive.by_index(i)?;
            let name = file.name().to_string();

            // 查找可执行文件（.exe 或无扩展名）
            if file.is_file() && (name.ends_with(".exe") || !name.contains('.')) {
                log::info!("找到核心文件：{}", name);
                let mut bytes = Vec::new();
                file.read_to_end(&mut bytes)?;
                return Ok(bytes);
            }
        }

        Err("压缩包中未找到可执行文件".into())
    } else if file_name.ends_with(".gz") {
        // 解压 GZ 文件
        let mut decoder = GzDecoder::new(file_bytes);
        let mut bytes = Vec::new();
        decoder.read_to_end(&mut bytes)?;
        Ok(bytes)
    } else {
        Err(format!("不支持的文件格式: {}", file_name).into())
    }
}

// 替换核心文件
//
// 参数：
// - core_dir: 核心文件目录
// - core_bytes: 新核心字节数据
// - platform: 平台名称
async fn replace_core(
    core_dir: &str,
    core_bytes: &[u8],
    platform: &str,
) -> Result<(), Box<dyn std::error::Error + Send + Sync>> {
    log::info!("开始替换核心文件：{}", core_dir);

    let core_name = if platform == "windows" {
        "clash-core.exe"
    } else {
        "clash-core"
    };

    let core_path = Path::new(core_dir).join(core_name);
    let backup_path = Path::new(core_dir).join(format!("{}_old", core_name));

    // 1. 备份旧核心
    if core_path.exists() {
        log::info!("备份旧核心：{}", backup_path.display());
        async_fs::rename(&core_path, &backup_path).await?;
    }

    // 2. 写入新核心
    match async_fs::write(&core_path, core_bytes).await {
        Ok(_) => {
            log::info!("新核心写入成功");

            // 3. 设置可执行权限（Linux/macOS）
            if platform != "windows" {
                #[cfg(unix)]
                {
                    use std::os::unix::fs::PermissionsExt;
                    let mut perms = async_fs::metadata(&core_path).await?.permissions();
                    perms.set_mode(0o755);
                    async_fs::set_permissions(&core_path, perms).await?;
                    log::info!("已设置可执行权限");
                }
            }

            Ok(())
        }
        Err(e) => {
            // 如果失败，尝试恢复备份
            log::error!("写入新核心失败：{}", e);

            if backup_path.exists() {
                log::info!("尝试恢复旧核心");
                if core_path.exists() {
                    let _ = async_fs::remove_file(&core_path).await;
                }
                async_fs::rename(&backup_path, &core_path).await?;
                log::info!("已恢复旧核心");
            }

            Err(format!("替换核心失败: {}", e).into())
        }
    }
}

// 发送进度通知到 Dart
fn send_progress(progress: f64, message: &str, downloaded: u64, total: u64) {
    let progress_msg = DownloadCoreProgress {
        progress,
        message: message.to_string(),
        downloaded,
        total,
    };
    progress_msg.send_signal_to_dart();
}

// ============================================================================
// 消息监听器
// ============================================================================

// 初始化消息监听器
pub fn init_message_listeners() {
    // 监听获取最新版本信号
    spawn(async {
        let receiver = GetLatestCoreVersionRequest::get_dart_signal_receiver();
        while let Some(dart_signal) = receiver.recv().await {
            let message = dart_signal.message;
            tokio::spawn(async move {
                message.handle().await;
            });
        }
        log::info!("获取最新核心版本消息通道已关闭，退出监听器");
    });

    // 监听下载核心信号
    spawn(async {
        let receiver = DownloadCoreRequest::get_dart_signal_receiver();
        while let Some(dart_signal) = receiver.recv().await {
            let message = dart_signal.message;
            tokio::spawn(async move {
                message.handle().await;
            });
        }
        log::info!("下载核心消息通道已关闭，退出监听器");
    });

    // 监听替换核心信号
    spawn(async {
        let receiver = ReplaceCoreRequest::get_dart_signal_receiver();
        while let Some(dart_signal) = receiver.recv().await {
            let message = dart_signal.message;
            tokio::spawn(async move {
                message.handle().await;
            });
        }
        log::info!("替换核心消息通道已关闭，退出监听器");
    });
}

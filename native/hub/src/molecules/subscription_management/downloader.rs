// 订阅下载器
// 处理订阅配置的 HTTP 下载，支持多种代理模式

use crate::molecules::ProxyMode;
use reqwest::{Client, Proxy};
use rinf::{DartSignal, RustSignal};
use serde::{Deserialize, Serialize};
use std::time::Duration;

// Dart → Rust：下载订阅请求
#[derive(Deserialize, DartSignal)]
pub struct DownloadSubscriptionRequest {
    pub request_id: String, // 请求标识符，用于响应匹配
    pub url: String,
    pub proxy_mode: ProxyMode,
    pub user_agent: String,
    pub timeout_seconds: u64,
    pub mixed_port: u16, // Clash 混合端口
}

// Rust → Dart：下载订阅响应
#[derive(Serialize, RustSignal)]
pub struct DownloadSubscriptionResponse {
    pub request_id: String, // 请求标识符，用于请求匹配
    pub is_successful: bool,
    pub content: String,
    pub subscription_info: Option<SubscriptionInfoData>,
    pub error_message: Option<String>,
}

// 订阅信息
#[derive(Serialize, Deserialize, Clone, Debug, rinf::SignalPiece)]
pub struct SubscriptionInfoData {
    pub upload: Option<u64>,
    pub download: Option<u64>,
    pub total: Option<u64>,
    pub expire: Option<i64>,
}

impl DownloadSubscriptionRequest {
    pub async fn handle(self) {
        log::info!("收到下载订阅请求 [{}]：{}", self.request_id, self.url);

        let result = download_subscription(
            &self.url,
            self.proxy_mode,
            &self.user_agent,
            self.timeout_seconds,
            self.mixed_port,
        )
        .await;

        let response = match result {
            Ok((content, info)) => {
                log::info!(
                    "订阅下载成功 [{}]，内容长度：{} 字节",
                    self.request_id,
                    content.len()
                );
                DownloadSubscriptionResponse {
                    request_id: self.request_id,
                    is_successful: true,
                    content,
                    subscription_info: info,
                    error_message: None,
                }
            }
            Err(e) => {
                log::error!("订阅下载失败 [{}]：{}", self.request_id, e);
                DownloadSubscriptionResponse {
                    request_id: self.request_id,
                    is_successful: false,
                    content: String::new(),
                    subscription_info: None,
                    error_message: Some(e.to_string()),
                }
            }
        };

        response.send_signal_to_dart();
    }
}

// 下载订阅配置
//
// 参数：
// - url: 订阅链接
// - proxy_mode: 代理模式
// - user_agent: User-Agent 头
// - timeout_seconds: 超时时间（秒）
// - mixed_port: Clash 混合端口
//
// 返回：(配置内容, 订阅信息)
pub async fn download_subscription(
    url: &str,
    proxy_mode: ProxyMode,
    user_agent: &str,
    timeout_seconds: u64,
    mixed_port: u16,
) -> Result<(String, Option<SubscriptionInfoData>), Box<dyn std::error::Error + Send + Sync>> {
    log::info!("开始下载订阅：{}", url);
    log::info!("代理模式：{:?}", proxy_mode);

    // 创建 HTTP 客户端
    let client = create_http_client(proxy_mode, timeout_seconds, mixed_port)?;

    // 发送 HTTP GET 请求
    let response = client
        .get(url)
        .header("User-Agent", user_agent)
        .send()
        .await?;

    // 检查 HTTP 状态码
    let status = response.status();
    if !status.is_success() {
        return Err(format!(
            "HTTP {}: {}",
            status.as_u16(),
            status.canonical_reason().unwrap_or("Unknown")
        )
        .into());
    }

    // 解析订阅信息头
    let subscription_info = parse_subscription_info(response.headers());

    // 读取响应体
    let content = response.text().await?;

    if content.is_empty() {
        return Err("订阅内容为空".into());
    }

    log::info!("订阅下载成功，内容长度：{} 字节", content.len());

    Ok((content, subscription_info))
}

// 创建 HTTP 客户端
fn create_http_client(
    proxy_mode: ProxyMode,
    timeout_seconds: u64,
    mixed_port: u16,
) -> Result<Client, Box<dyn std::error::Error + Send + Sync>> {
    let mut builder = Client::builder()
        .timeout(Duration::from_secs(timeout_seconds))
        .connect_timeout(Duration::from_secs(10)) // 连接超时
        .danger_accept_invalid_certs(false); // 验证 SSL 证书

    // 根据代理模式配置客户端
    match proxy_mode {
        ProxyMode::Direct => {
            log::debug!("使用直连模式");
            // 不设置代理
        }
        ProxyMode::System => {
            log::debug!("使用系统代理模式");
            // reqwest 默认会读取系统环境变量（HTTP_PROXY, HTTPS_PROXY）
            // 无需额外配置
        }
        ProxyMode::Core => {
            log::debug!("使用核心代理模式：127.0.0.1:{}", mixed_port);
            let proxy_url = format!("http://127.0.0.1:{}", mixed_port);
            let proxy = Proxy::all(&proxy_url)?;
            builder = builder.proxy(proxy);
        }
    }

    Ok(builder.build()?)
}

// 解析订阅信息头
//
// 示例：subscription-userinfo: upload=0; download=123456; total=1073741824; expire=1735689600
fn parse_subscription_info(headers: &reqwest::header::HeaderMap) -> Option<SubscriptionInfoData> {
    let header_value = headers.get("subscription-userinfo")?.to_str().ok()?;

    log::debug!("解析订阅信息头：{}", header_value);

    let mut upload = None;
    let mut download = None;
    let mut total = None;
    let mut expire = None;

    // 解析键值对
    for pair in header_value.split(';') {
        let pair = pair.trim();
        if let Some((key, value)) = pair.split_once('=') {
            let key = key.trim();
            let value = value.trim();

            match key {
                "upload" => upload = value.parse::<u64>().ok(),
                "download" => download = value.parse::<u64>().ok(),
                "total" => total = value.parse::<u64>().ok(),
                "expire" => expire = value.parse::<i64>().ok(),
                _ => {}
            }
        }
    }

    // 如果至少有一个字段有值，则返回订阅信息
    if upload.is_some() || download.is_some() || total.is_some() || expire.is_some() {
        Some(SubscriptionInfoData {
            upload,
            download,
            total,
            expire,
        })
    } else {
        None
    }
}

// 初始化 Dart 信号监听器
pub fn init_dart_signal_listeners() {
    use tokio::spawn;

    // 订阅下载请求监听器
    spawn(async {
        let receiver = DownloadSubscriptionRequest::get_dart_signal_receiver();
        while let Some(dart_signal) = receiver.recv().await {
            tokio::spawn(async move {
                dart_signal.message.handle().await;
            });
        }
    });
}

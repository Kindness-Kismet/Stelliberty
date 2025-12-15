// 覆写文件下载器
//
// 目的：处理覆写文件的 HTTP 下载，支持多种代理模式
// 复用订阅下载的 HTTP 客户端逻辑

use crate::clash::subscription::downloader::ProxyMode;
use reqwest::Client;
use std::time::Duration;

// 下载覆写文件
//
// 参数：
// - url: 覆写文件链接
// - proxy_mode: 代理模式
// - user_agent: User-Agent 头
// - timeout_seconds: 超时时间（秒）
// - mixed_port: Clash 混合端口
//
// 返回：文件内容
pub async fn download_override(
    url: &str,
    proxy_mode: ProxyMode,
    user_agent: &str,
    timeout_seconds: u64,
    mixed_port: u16,
) -> Result<String, Box<dyn std::error::Error + Send + Sync>> {
    log::info!("开始下载覆写文件：{}", url);
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

    // 读取响应体
    let content = response.text().await?;

    if content.is_empty() {
        return Err("覆写文件内容为空".into());
    }

    log::info!("覆写文件下载成功，内容长度：{} 字节", content.len());

    Ok(content)
}

// 创建 HTTP 客户端（复用订阅下载的逻辑）
fn create_http_client(
    proxy_mode: ProxyMode,
    timeout_seconds: u64,
    mixed_port: u16,
) -> Result<Client, Box<dyn std::error::Error + Send + Sync>> {
    use reqwest::Proxy;

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

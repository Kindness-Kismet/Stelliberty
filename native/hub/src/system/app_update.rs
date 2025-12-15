// 应用更新服务：GitHub Release 检查

use once_cell::sync::Lazy;
use reqwest;
use rinf::{DartSignal, RustSignal};
use serde::{Deserialize, Serialize};
use std::cmp::Ordering;

// Dart → Rust：检查应用更新请求
#[derive(Debug, Clone, Serialize, Deserialize, DartSignal)]
pub struct CheckAppUpdateRequest {
    pub current_version: String,
    pub github_repo: String,
}

// Rust → Dart：应用更新检查响应
#[derive(Debug, Clone, Serialize, Deserialize, RustSignal)]
pub struct AppUpdateResult {
    pub current_version: String,
    pub latest_version: String,
    pub has_update: bool,
    pub download_url: String,
    pub release_notes: String,
    pub html_url: String,
    pub error_message: Option<String>,
}

impl CheckAppUpdateRequest {
    pub fn handle(&self) {
        let current_version = self.current_version.clone();
        let github_repo = self.github_repo.clone();

        // 使用 tokio::spawn 异步处理更新检查
        // 任务会独立运行，完成后自动清理
        tokio::spawn(async move {
            log::info!("检查更新: {} (当前版本: {})", github_repo, current_version);

            let result = check_github_update(&current_version, &github_repo).await;

            match result {
                Ok(update_result) => {
                    log::info!("更新检查成功: 最新版本 {}", update_result.latest_version);

                    AppUpdateResult {
                        current_version: update_result.current_version,
                        latest_version: update_result.latest_version,
                        has_update: update_result.has_update,
                        download_url: update_result.download_url.unwrap_or_default(),
                        release_notes: update_result.release_notes.unwrap_or_default(),
                        html_url: update_result.html_url.unwrap_or_default(),
                        error_message: None,
                    }
                    .send_signal_to_dart();
                }
                Err(e) => {
                    log::error!("更新检查失败: {}", e);

                    AppUpdateResult {
                        current_version,
                        latest_version: String::new(),
                        has_update: false,
                        download_url: String::new(),
                        release_notes: String::new(),
                        html_url: String::new(),
                        error_message: Some(e),
                    }
                    .send_signal_to_dart();
                }
            }
        });
    }
}

// GitHub Release API 响应
#[derive(Debug, Deserialize)]
struct GitHubRelease {
    tag_name: String,
    #[serde(rename = "html_url")]
    html_url: String,
    body: Option<String>,
    assets: Vec<GitHubAsset>,
}

#[derive(Debug, Deserialize)]
struct GitHubAsset {
    name: String,
    browser_download_url: String,
}

// 平台匹配规则
struct PlatformMatchRules {
    file_extension: &'static str,
    platform_keywords: &'static [&'static str],
    arch_keywords: &'static [&'static str],
    required_keywords: &'static [&'static str],
}

// HTTP 客户端单例 - 避免重复创建连接导致的内存泄漏
static HTTP_CLIENT: Lazy<Result<reqwest::Client, String>> = Lazy::new(|| {
    reqwest::Client::builder()
        .timeout(std::time::Duration::from_secs(10))
        .user_agent("Stelliberty-App")
        .build()
        .map_err(|e| format!("HTTP 客户端初始化失败: {}", e))
});

// 获取 HTTP 客户端引用
fn get_http_client() -> Result<&'static reqwest::Client, String> {
    HTTP_CLIENT.as_ref().map_err(|e| e.clone())
}

// 检查 GitHub Release 更新
pub async fn check_github_update(
    current_version: &str,
    github_repo: &str,
) -> Result<UpdateCheckResult, String> {
    log::info!("开始检查 GitHub 更新: {}", github_repo);
    log::info!("当前版本: {}", current_version);

    // 构建 GitHub API URL
    let api_url = format!(
        "https://api.github.com/repos/{}/releases/latest",
        github_repo
    );

    // 发送 HTTP 请求 - 使用单例客户端避免连接泄漏
    let client = get_http_client()?;
    let response = client
        .get(&api_url)
        .header("Accept", "application/vnd.github.v3+json")
        .header("User-Agent", "Stelliberty-App")
        .send()
        .await
        .map_err(|e| format!("HTTP 请求失败: {}", e))?;

    if !response.status().is_success() {
        return Err(format!("GitHub API 返回错误: {}", response.status()));
    }

    // 解析 JSON 响应
    let release: GitHubRelease = response
        .json()
        .await
        .map_err(|e| format!("JSON 解析失败: {}", e))?;

    // 处理版本号
    let latest_version = release.tag_name.trim_start_matches('v');
    log::info!("最新版本: {}", latest_version);

    // 比较版本
    let has_update = compare_versions(current_version, latest_version) == Ordering::Less;

    // 检测当前平台和架构并查找匹配的安装包
    let platform = get_platform_name();
    let arch = get_architecture();
    log::info!("当前平台: {}, 架构: {}", platform, arch);

    let download_url = find_matching_asset(&release.assets, &platform, &arch);
    match &download_url {
        Some(_) => log::info!("找到匹配的下载链接"),
        None => log::warn!("未找到匹配当前平台的安装包"),
    }

    Ok(UpdateCheckResult {
        current_version: current_version.to_string(),
        latest_version: latest_version.to_string(),
        has_update,
        download_url,
        release_notes: release.body,
        html_url: Some(release.html_url),
    })
}

// 比较版本号（语义化版本）
fn compare_versions(v1: &str, v2: &str) -> Ordering {
    let parts1: Vec<u32> = v1.split('.').filter_map(|s| s.parse().ok()).collect();
    let parts2: Vec<u32> = v2.split('.').filter_map(|s| s.parse().ok()).collect();

    // 逐段比较版本号
    for i in 0..parts1.len().max(parts2.len()) {
        match parts1.get(i).unwrap_or(&0).cmp(parts2.get(i).unwrap_or(&0)) {
            Ordering::Equal => continue,
            other => return other,
        }
    }
    Ordering::Equal
}

// 查找匹配的安装包
fn find_matching_asset(assets: &[GitHubAsset], platform: &str, arch: &str) -> Option<String> {
    let rules = get_platform_match_rules(platform, arch)?;

    assets.iter().find_map(|asset| {
        let name_lower = asset.name.to_lowercase();

        // 检查所有匹配条件
        let matches = name_lower.ends_with(rules.file_extension)
            && rules
                .platform_keywords
                .iter()
                .any(|k| name_lower.contains(k))
            && (rules.arch_keywords.is_empty()
                || rules.arch_keywords.iter().any(|k| name_lower.contains(k)))
            && (rules.required_keywords.is_empty()
                || rules
                    .required_keywords
                    .iter()
                    .all(|k| name_lower.contains(k)));

        if matches {
            log::info!("找到匹配的安装包: {}", asset.name);
            Some(asset.browser_download_url.clone())
        } else {
            None
        }
    })
}

// 获取平台匹配规则
fn get_platform_match_rules(platform: &str, arch: &str) -> Option<PlatformMatchRules> {
    match platform {
        "windows" => Some(PlatformMatchRules {
            file_extension: ".exe",
            platform_keywords: &["win", "windows"],
            arch_keywords: if arch == "arm64" {
                &["arm64", "aarch64"]
            } else {
                &["x64", "amd64", "x86_64"]
            },
            required_keywords: &["setup"],
        }),

        "linux" => Some(PlatformMatchRules {
            file_extension: ".appimage",
            platform_keywords: &["linux"],
            arch_keywords: if arch == "arm64" {
                &["arm64", "aarch64"]
            } else {
                &["x64", "amd64", "x86_64"]
            },
            required_keywords: &[],
        }),

        "macos" => Some(PlatformMatchRules {
            file_extension: ".dmg",
            platform_keywords: &["macos", "darwin", "osx"],
            arch_keywords: if arch == "arm64" {
                &["arm64", "aarch64", "apple-silicon"]
            } else {
                &["x64", "intel", "amd64"]
            },
            required_keywords: &[],
        }),

        "android" => Some(PlatformMatchRules {
            file_extension: ".apk",
            platform_keywords: &["android"],
            arch_keywords: &[],
            required_keywords: &[],
        }),

        _ => None,
    }
}

// 获取平台名称
fn get_platform_name() -> String {
    std::env::consts::OS.to_string()
}

// 获取系统架构（标准化为常用命名）
fn get_architecture() -> String {
    match std::env::consts::ARCH {
        "aarch64" => "arm64",
        "x86_64" => "x64",
        arch => arch,
    }
    .to_string()
}

// ============================================================================
// 结果结构
// ============================================================================

#[derive(Debug, Serialize)]
pub struct UpdateCheckResult {
    pub current_version: String,
    pub latest_version: String,
    pub has_update: bool,
    pub download_url: Option<String>,
    pub release_notes: Option<String>,
    pub html_url: Option<String>,
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_version_comparison() {
        assert_eq!(compare_versions("1.0.0", "1.0.0"), Ordering::Equal);
        assert_eq!(compare_versions("1.0.0", "1.0.1"), Ordering::Less);
        assert_eq!(compare_versions("1.0.1", "1.0.0"), Ordering::Greater);
        assert_eq!(compare_versions("1.2.3", "1.10.0"), Ordering::Less);
        assert_eq!(compare_versions("2.0.0", "1.9.9"), Ordering::Greater);
    }

    #[test]
    fn test_platform_detection() {
        let platform = get_platform_name();
        assert!(!platform.is_empty());

        let arch = get_architecture();
        assert!(arch == "x64" || arch == "arm64");
    }
}

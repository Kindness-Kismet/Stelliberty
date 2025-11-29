// 系统配置消息协议
//
// 目的：定义开机自启动、URL 启动、UWP 回环豁免等系统配置的通信接口

use crate::system::auto_start;
use rinf::{DartSignal, RustSignal};
use serde::{Deserialize, Serialize};

// ============================================================================
// 开机自启动消息协议
// ============================================================================

// Dart → Rust：获取开机自启状态
#[derive(Deserialize, DartSignal)]
pub struct GetAutoStartStatus;

// Dart → Rust：设置开机自启状态
#[derive(Deserialize, DartSignal)]
pub struct SetAutoStartStatus {
    pub enabled: bool,
}

// Rust → Dart：开机自启状态响应
#[derive(Serialize, RustSignal)]
pub struct AutoStartStatusResult {
    pub enabled: bool,
    pub error_message: Option<String>,
}

impl GetAutoStartStatus {
    // 查询当前自启动配置状态
    //
    // 目的：读取系统中的开机自启动设置
    pub fn handle(&self) {
        log::info!("收到获取开机自启动状态请求");

        let (enabled, error_message) = match auto_start::get_auto_start_status() {
            Ok(status) => (status, None),
            Err(err) => {
                log::error!("获取开机自启状态失败：{}", err);
                (false, Some(err))
            }
        };

        let response = AutoStartStatusResult {
            enabled,
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
        log::info!("收到设置开机自启动状态请求：enabled={}", self.enabled);

        let (enabled, error_message) = match auto_start::set_auto_start_status(self.enabled) {
            Ok(status) => (status, None),
            Err(err) => {
                log::error!("设置开机自启状态失败：{}", err);
                (false, Some(err))
            }
        };

        let response = AutoStartStatusResult {
            enabled,
            error_message,
        };

        response.send_signal_to_dart();
    }
}

// ============================================================================
// URL 启动器消息协议
// ============================================================================

// Dart → Rust：打开 URL
#[derive(Deserialize, DartSignal)]
pub struct OpenUrl {
    pub url: String,
}

// Rust → Dart：打开 URL 结果
#[derive(Serialize, RustSignal)]
pub struct OpenUrlResult {
    pub success: bool,
    pub error_message: Option<String>,
}

impl OpenUrl {
    // 在系统默认浏览器中打开 URL
    //
    // 目的：提供跨平台的 URL 打开能力
    pub fn handle(&self) {
        log::info!("收到打开 URL 请求：{}", self.url);

        let (success, error_message) = match crate::system::url_launcher::open_url(&self.url) {
            Ok(()) => (true, None),
            Err(err) => {
                log::error!("打开 URL 失败：{}", err);
                (false, Some(err))
            }
        };

        let response = OpenUrlResult {
            success,
            error_message,
        };

        response.send_signal_to_dart();
    }
}

// ============================================================================
// UWP 回环豁免消息协议（仅 Windows）
// ============================================================================

#[cfg(target_os = "windows")]
pub mod loopback_messages {
    use rinf::{DartSignal, RustSignal};
    use serde::{Deserialize, Serialize};

    // Dart → Rust：获取所有应用容器
    #[derive(Deserialize, DartSignal)]
    pub struct GetAppContainers;

    // Dart → Rust：设置回环豁免
    #[derive(Deserialize, DartSignal)]
    pub struct SetLoopback {
        pub package_family_name: String,
        pub enabled: bool,
    }

    // Dart → Rust：保存配置（使用 SID 字符串）
    #[derive(Deserialize, DartSignal)]
    pub struct SaveLoopbackConfiguration {
        pub sid_strings: Vec<String>,
    }

    // Rust → Dart：应用容器列表（用于初始化）
    #[derive(Serialize, RustSignal)]
    pub struct AppContainersList {
        pub containers: Vec<String>,
    }

    // Rust → Dart：单个应用容器信息
    #[derive(Serialize, RustSignal)]
    pub struct AppContainerInfo {
        pub container_name: String,
        pub display_name: String,
        pub package_family_name: String,
        pub sid: Vec<u8>,
        pub sid_string: String,
        pub loopback_enabled: bool,
    }

    // Rust → Dart：设置回环豁免结果
    #[derive(Serialize, RustSignal)]
    pub struct SetLoopbackResult {
        pub success: bool,
        pub error_message: Option<String>,
    }

    // Rust → Dart：应用容器流传输完成信号
    #[derive(Serialize, RustSignal)]
    pub struct AppContainersComplete;

    // Rust → Dart：保存配置结果
    #[derive(Serialize, RustSignal)]
    pub struct SaveLoopbackConfigurationResult {
        pub success: bool,
        pub error_message: Option<String>,
    }

    impl GetAppContainers {
        // 处理获取应用容器请求
        //
        // 目的：枚举所有 UWP 应用并返回其回环状态
        pub fn handle(&self) {
            log::info!("处理获取应用容器请求");

            match crate::system::loopback::enumerate_app_containers() {
                Ok(containers) => {
                    log::info!("发送{}个容器信息到 Dart", containers.len());
                    AppContainersList { containers: vec![] }.send_signal_to_dart();

                    for c in containers {
                        AppContainerInfo {
                            container_name: c.app_container_name,
                            display_name: c.display_name,
                            package_family_name: c.package_family_name,
                            sid: c.sid,
                            sid_string: c.sid_string,
                            loopback_enabled: c.is_loopback_enabled,
                        }
                        .send_signal_to_dart();
                    }

                    // 发送流传输完成信号
                    AppContainersComplete.send_signal_to_dart();
                    log::info!("应用容器流传输完成");
                }
                Err(e) => {
                    log::error!("获取应用容器失败：{}", e);
                    AppContainersList { containers: vec![] }.send_signal_to_dart();
                    // 即使失败也发送完成信号，避免 Dart 端无限等待
                    AppContainersComplete.send_signal_to_dart();
                }
            }
        }
    }

    impl SetLoopback {
        // 处理设置回环豁免请求
        //
        // 目的：为单个应用启用或禁用回环豁免
        pub fn handle(self) {
            log::info!(
                "处理设置回环豁免请求：{} - {}",
                self.package_family_name,
                self.enabled
            );

            match crate::system::loopback::set_loopback_exemption(
                &self.package_family_name,
                self.enabled,
            ) {
                Ok(()) => {
                    log::info!("回环豁免设置成功");
                    SetLoopbackResult {
                        success: true,
                        error_message: None,
                    }
                    .send_signal_to_dart();
                }
                Err(e) => {
                    log::error!("回环豁免设置失败：{}", e);
                    SetLoopbackResult {
                        success: false,
                        error_message: Some(e),
                    }
                    .send_signal_to_dart();
                }
            }
        }
    }

    impl SaveLoopbackConfiguration {
        // 处理保存配置请求
        //
        // 目的：批量设置多个应用的回环豁免状态
        pub fn handle(self) {
            log::info!("处理保存配置请求，期望启用{}个容器", self.sid_strings.len());

            // 获取所有容器
            let containers = match crate::system::loopback::enumerate_app_containers() {
                Ok(c) => c,
                Err(e) => {
                    log::error!("枚举容器失败：{}", e);
                    SaveLoopbackConfigurationResult {
                        success: false,
                        error_message: Some(format!("无法枚举容器：{}", e)),
                    }
                    .send_signal_to_dart();
                    return;
                }
            };

            // 性能优化：使用 HashSet 进行 O(1) 查找，避免 O(n²) 复杂度
            use std::collections::HashSet;
            let enabled_sids: HashSet<&str> = self.sid_strings.iter().map(|s| s.as_str()).collect();

            let mut errors = Vec::new();
            let mut skipped = Vec::new();
            let mut success_count = 0;
            let mut skipped_count = 0;

            // 对每个容器，检查是否应该启用（现在是 O(1) 查找）
            for container in containers {
                let should_enable = enabled_sids.contains(container.sid_string.as_str());

                if container.is_loopback_enabled != should_enable {
                    log::info!(
                        "修改容器：{}(SID：{}) | {} -> {}",
                        container.display_name,
                        container.sid_string,
                        container.is_loopback_enabled,
                        should_enable
                    );

                    if let Err(e) = crate::system::loopback::set_loopback_exemption_by_sid(
                        &container.sid,
                        should_enable,
                    ) {
                        // 检查是否是系统保护的应用（ERROR_ACCESS_DENIED）
                        if e.contains("0x80070005")
                            || e.contains("0x00000005")
                            || e.contains("ERROR_ACCESS_DENIED")
                        {
                            log::info!("跳过系统保护的应用：{}", container.display_name);
                            skipped.push(container.display_name.clone());
                            skipped_count += 1;
                        } else {
                            log::error!("设置容器失败：{} - {}", container.display_name, e);
                            errors.push(format!("{}：{}", container.display_name, e));
                        }
                    } else {
                        success_count += 1;
                    }
                }
            }

            log::info!(
                "配置保存完成，成功：{}，跳过：{}，错误：{}",
                success_count,
                skipped_count,
                errors.len()
            );

            // 构建结果消息
            let mut message_parts = Vec::new();

            if success_count > 0 {
                message_parts.push(format!("成功修改：{}个", success_count));
            }

            if skipped_count > 0 {
                message_parts.push(format!("跳过系统保护应用：{}个", skipped_count));
                if skipped.len() <= 3 {
                    // 如果跳过的应用少于等于3个，显示具体名称
                    message_parts.push(format!("（{}）", skipped.join("、")));
                }
            }

            if errors.is_empty() {
                SaveLoopbackConfigurationResult {
                    success: true,
                    error_message: if message_parts.is_empty() {
                        Some("配置保存成功（无需修改）".to_string())
                    } else {
                        Some(message_parts.join("，"))
                    },
                }
                .send_signal_to_dart();
            } else {
                message_parts.push(format!("失败：{}个", errors.len()));
                SaveLoopbackConfigurationResult {
                    success: false,
                    error_message: Some(format!(
                        "{}。\n错误详情：\n{}",
                        message_parts.join("，"),
                        errors.join("\n")
                    )),
                }
                .send_signal_to_dart();
            }
        }
    }
}

#[cfg(target_os = "windows")]
pub use loopback_messages::*;

// ============================================================================
// 应用更新消息协议
// ============================================================================

// 检查应用更新请求
#[derive(Debug, Clone, Serialize, Deserialize, DartSignal)]
pub struct CheckAppUpdateRequest {
    pub current_version: String,
    pub github_repo: String,
}

// 应用更新检查响应
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

            let result =
                crate::system::app_update::check_github_update(&current_version, &github_repo)
                    .await;

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

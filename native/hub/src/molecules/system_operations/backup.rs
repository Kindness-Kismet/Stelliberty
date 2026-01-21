// 备份与还原服务：负责导出与导入应用数据。
// 使用结构化元信息描述版本与路径。

use base64::{Engine as _, engine::general_purpose};
use rinf::{DartSignal, RustSignal};
use serde::{Deserialize, Serialize};
use serde_json;
use std::collections::HashMap;
use std::path::Path;
use tokio::fs as async_fs;

// Dart → Rust：创建备份请求
#[derive(Deserialize, DartSignal)]
pub struct CreateBackupRequest {
    pub target_path: String,
    pub app_version: String,
    // 路径配置
    pub preferences_path: String,
    pub subscriptions_dir: String,
    pub subscriptions_list_path: String,
    pub overrides_dir: String,
    pub overrides_list_path: String,
    pub dns_config_path: String,
    pub pac_file_path: String,
}

// Dart → Rust：还原备份请求
#[derive(Deserialize, DartSignal)]
pub struct RestoreBackupRequest {
    pub backup_path: String,
    // 路径配置
    pub preferences_path: String,
    pub subscriptions_dir: String,
    pub subscriptions_list_path: String,
    pub overrides_dir: String,
    pub overrides_list_path: String,
    pub dns_config_path: String,
    pub pac_file_path: String,
}

// Rust → Dart：备份操作响应
#[derive(Serialize, RustSignal)]
pub struct BackupOperationResult {
    pub is_successful: bool,
    pub message: String,
    pub error_message: Option<String>,
}

impl CreateBackupRequest {
    // 处理创建备份请求
    pub async fn handle(self) {
        log::info!("收到创建备份请求：{}", self.target_path);

        let paths = BackupPaths {
            preferences_path: &self.preferences_path,
            subscriptions_dir: &self.subscriptions_dir,
            subscriptions_list_path: &self.subscriptions_list_path,
            overrides_dir: &self.overrides_dir,
            overrides_list_path: &self.overrides_list_path,
            dns_config_path: &self.dns_config_path,
            pac_file_path: &self.pac_file_path,
        };

        let result = create_backup(&self.target_path, &self.app_version, paths).await;

        let response = match result {
            Ok(path) => {
                log::info!("备份创建成功：{}", path);
                BackupOperationResult {
                    is_successful: true,
                    message: path,
                    error_message: None,
                }
            }
            Err(e) => {
                log::error!("备份创建失败：{}", e);
                BackupOperationResult {
                    is_successful: false,
                    message: String::new(),
                    error_message: Some(e.to_string()),
                }
            }
        };

        response.send_signal_to_dart();
    }
}

impl RestoreBackupRequest {
    // 处理还原备份请求
    pub async fn handle(self) {
        log::info!("收到还原备份请求：{}", self.backup_path);

        let paths = BackupPaths {
            preferences_path: &self.preferences_path,
            subscriptions_dir: &self.subscriptions_dir,
            subscriptions_list_path: &self.subscriptions_list_path,
            overrides_dir: &self.overrides_dir,
            overrides_list_path: &self.overrides_list_path,
            dns_config_path: &self.dns_config_path,
            pac_file_path: &self.pac_file_path,
        };

        let result = restore_backup(&self.backup_path, paths).await;

        let response = match result {
            Ok(()) => {
                log::info!("备份还原成功");
                BackupOperationResult {
                    is_successful: true,
                    message: "备份还原成功".to_string(),
                    error_message: None,
                }
            }
            Err(e) => {
                log::error!("备份还原失败：{}", e);
                BackupOperationResult {
                    is_successful: false,
                    message: String::new(),
                    error_message: Some(e.to_string()),
                }
            }
        };

        response.send_signal_to_dart();
    }
}

// 备份版本
const BACKUP_VERSION: &str = "1.0.0";
const EXCLUDED_PREFERENCE_KEYS: [&str; 12] = [
    "auto_start_enabled",
    "clash_tun_enable",
    "clash_tun_stack",
    "clash_tun_device",
    "clash_tun_auto_route",
    "clash_tun_auto_redirect",
    "clash_tun_auto_detect_interface",
    "clash_tun_dns_hijack",
    "clash_tun_strict_route",
    "clash_tun_route_exclude_address",
    "clash_tun_disable_icmp_forwarding",
    "clash_tun_mtu",
];

// 备份数据结构
#[derive(Serialize, Deserialize, Debug)]
pub struct BackupData {
    pub version: String,
    pub timestamp: String, // ISO 8601 格式
    pub app_version: String,
    pub platform: String,
    pub data: BackupContent,
}

// 备份内容
#[derive(Serialize, Deserialize, Debug)]
pub struct BackupContent {
    pub app_preferences: HashMap<String, serde_json::Value>,
    pub clash_preferences: HashMap<String, serde_json::Value>,
    pub subscriptions: SubscriptionBackup,
    pub overrides: OverrideBackup,
    pub dns_config: Option<String>, // Base64 编码
    pub pac_file: Option<String>,   // Base64 编码
}

// 订阅备份数据
#[derive(Serialize, Deserialize, Debug)]
pub struct SubscriptionBackup {
    pub list: Option<String>,             // list.json 内容
    pub configs: HashMap<String, String>, // 文件名 -> Base64 内容
}

// 覆写备份数据
#[derive(Serialize, Deserialize, Debug)]
pub struct OverrideBackup {
    pub list: Option<String>,           // list.json 内容
    pub files: HashMap<String, String>, // 文件名 -> Base64 内容
}

// 备份路径配置（用于减少函数参数）
pub struct BackupPaths<'a> {
    pub preferences_path: &'a str,
    pub subscriptions_dir: &'a str,
    pub subscriptions_list_path: &'a str,
    pub overrides_dir: &'a str,
    pub overrides_list_path: &'a str,
    pub dns_config_path: &'a str,
    pub pac_file_path: &'a str,
}

// 创建备份
pub async fn create_backup(
    target_path: &str,
    app_version: &str,
    paths: BackupPaths<'_>,
) -> Result<String, Box<dyn std::error::Error + Send + Sync>> {
    log::info!("开始创建备份到：{}", target_path);

    // 收集应用配置
    let app_prefs = collect_preferences(paths.preferences_path).await?;

    // Clash 配置与应用配置共享同一文件
    let clash_prefs = HashMap::new();

    // 收集订阅数据
    let subscriptions =
        collect_subscriptions(paths.subscriptions_dir, paths.subscriptions_list_path).await?;

    // 收集覆写数据
    let overrides = collect_overrides(paths.overrides_dir, paths.overrides_list_path).await?;

    // 收集 DNS 配置
    let dns_config = collect_file_base64(paths.dns_config_path).await;

    // 收集 PAC 文件
    let pac_file = collect_file_base64(paths.pac_file_path).await;

    // 构建备份数据
    let backup_data = BackupData {
        version: BACKUP_VERSION.to_string(),
        timestamp: chrono::Utc::now().to_rfc3339(),
        app_version: app_version.to_string(),
        platform: std::env::consts::OS.to_string(),
        data: BackupContent {
            app_preferences: app_prefs,
            clash_preferences: clash_prefs,
            subscriptions,
            overrides,
            dns_config,
            pac_file,
        },
    };

    // 写入文件
    let output_path = Path::new(target_path);
    if let Some(parent) = output_path.parent() {
        async_fs::create_dir_all(parent).await?;
    }

    let json_str = serde_json::to_string_pretty(&backup_data)?;
    async_fs::write(output_path, json_str).await?;

    log::info!("备份创建成功：{}", target_path);
    Ok(target_path.to_string())
}

// 还原备份
pub async fn restore_backup(
    backup_path: &str,
    paths: BackupPaths<'_>,
) -> Result<(), Box<dyn std::error::Error + Send + Sync>> {
    log::info!("开始还原备份：{}", backup_path);

    // 读取并验证备份文件
    let json_str = async_fs::read_to_string(backup_path).await?;
    let backup_data: BackupData = serde_json::from_str(&json_str)?;

    // 验证版本兼容性
    if backup_data.version != BACKUP_VERSION {
        log::warn!(
            "备份版本不匹配：{} != {}",
            backup_data.version,
            BACKUP_VERSION
        );
        if backup_data.version != "1.0.0" {
            return Err(format!("不支持的备份版本：{}", backup_data.version).into());
        }
    }

    log::info!(
        "备份版本：{}，时间：{}",
        backup_data.version,
        backup_data.timestamp
    );

    // 还原应用配置
    restore_preferences(&backup_data.data.app_preferences, paths.preferences_path).await?;

    // 还原订阅数据
    restore_subscriptions(
        &backup_data.data.subscriptions,
        paths.subscriptions_dir,
        paths.subscriptions_list_path,
    )
    .await?;

    // 还原覆写数据
    restore_overrides(
        &backup_data.data.overrides,
        paths.overrides_dir,
        paths.overrides_list_path,
    )
    .await?;

    // 还原 DNS 配置
    if let Some(dns_config) = &backup_data.data.dns_config {
        restore_file_base64(dns_config, paths.dns_config_path).await?;
    }

    // 还原 PAC 文件
    if let Some(pac_file) = &backup_data.data.pac_file {
        restore_file_base64(pac_file, paths.pac_file_path).await?;
    }

    log::info!("备份还原成功");
    Ok(())
}

// 收集配置文件
async fn collect_preferences(
    path: &str,
) -> Result<HashMap<String, serde_json::Value>, Box<dyn std::error::Error + Send + Sync>> {
    if !Path::new(path).exists() {
        return Ok(HashMap::new());
    }

    let content = async_fs::read_to_string(path).await?;
    let mut prefs: HashMap<String, serde_json::Value> = serde_json::from_str(&content)?;
    for key in EXCLUDED_PREFERENCE_KEYS {
        prefs.remove(key);
    }
    Ok(prefs)
}

// 收集订阅数据
async fn collect_subscriptions(
    subscriptions_dir: &str,
    subscriptions_list_path: &str,
) -> Result<SubscriptionBackup, Box<dyn std::error::Error + Send + Sync>> {
    let mut backup = SubscriptionBackup {
        list: None,
        configs: HashMap::new(),
    };

    // 读取订阅列表
    if Path::new(subscriptions_list_path).exists() {
        backup.list = Some(async_fs::read_to_string(subscriptions_list_path).await?);
    }

    // 读取所有订阅配置文件
    if Path::new(subscriptions_dir).exists() {
        let mut entries = async_fs::read_dir(subscriptions_dir).await?;
        while let Some(entry) = entries.next_entry().await? {
            let path = entry.path();
            if path.extension().and_then(|s| s.to_str()) == Some("yaml")
                && let Some(file_name) = path.file_stem().and_then(|s| s.to_str())
            {
                let content = async_fs::read(&path).await?;
                backup.configs.insert(
                    file_name.to_string(),
                    general_purpose::STANDARD.encode(&content),
                );
            }
        }
    }

    Ok(backup)
}

// 收集覆写数据
async fn collect_overrides(
    overrides_dir: &str,
    overrides_list_path: &str,
) -> Result<OverrideBackup, Box<dyn std::error::Error + Send + Sync>> {
    let mut backup = OverrideBackup {
        list: None,
        files: HashMap::new(),
    };

    // 读取覆写列表
    if Path::new(overrides_list_path).exists() {
        backup.list = Some(async_fs::read_to_string(overrides_list_path).await?);
    }

    // 读取所有覆写文件
    if Path::new(overrides_dir).exists() {
        let mut entries = async_fs::read_dir(overrides_dir).await?;
        while let Some(entry) = entries.next_entry().await? {
            let path = entry.path();
            if path.is_file()
                && let Some(file_name) = path.file_name().and_then(|s| s.to_str())
            {
                let content = async_fs::read(&path).await?;
                backup.files.insert(
                    file_name.to_string(),
                    general_purpose::STANDARD.encode(&content),
                );
            }
        }
    }

    Ok(backup)
}

// 收集文件并 Base64 编码
async fn collect_file_base64(path: &str) -> Option<String> {
    if !Path::new(path).exists() {
        return None;
    }

    match async_fs::read(path).await {
        Ok(content) => Some(general_purpose::STANDARD.encode(&content)),
        Err(e) => {
            log::warn!("读取文件失败：{} - {}", path, e);
            None
        }
    }
}

// 还原配置文件
async fn restore_preferences(
    prefs: &HashMap<String, serde_json::Value>,
    path: &str,
) -> Result<(), Box<dyn std::error::Error + Send + Sync>> {
    let mut merged_prefs = prefs.clone();
    if Path::new(path).exists() {
        match async_fs::read_to_string(path).await {
            Ok(content) => match serde_json::from_str::<HashMap<String, serde_json::Value>>(&content) {
                Ok(existing_prefs) => {
                    for key in EXCLUDED_PREFERENCE_KEYS {
                        if let Some(value) = existing_prefs.get(key) {
                            merged_prefs.insert(key.to_string(), value.clone());
                        }
                    }
                }
                Err(e) => {
                    log::warn!("读取现有配置失败：{}", e);
                }
            },
            Err(e) => {
                log::warn!("读取现有配置失败：{}", e);
            }
        }
    }

    let json_str = serde_json::to_string_pretty(&merged_prefs)?;

    if let Some(parent) = Path::new(path).parent() {
        async_fs::create_dir_all(parent).await?;
    }

    async_fs::write(path, json_str).await?;
    log::info!("配置已还原：{}", path);
    Ok(())
}

// 还原订阅数据
async fn restore_subscriptions(
    backup: &SubscriptionBackup,
    subscriptions_dir: &str,
    subscriptions_list_path: &str,
) -> Result<(), Box<dyn std::error::Error + Send + Sync>> {
    // 清空现有订阅配置文件
    if Path::new(subscriptions_dir).exists() {
        let mut entries = async_fs::read_dir(subscriptions_dir).await?;
        while let Some(entry) = entries.next_entry().await? {
            let path = entry.path();
            if path.extension().and_then(|s| s.to_str()) == Some("yaml") {
                async_fs::remove_file(path).await?;
            }
        }
    }

    // 还原订阅列表
    if let Some(list_content) = &backup.list {
        async_fs::create_dir_all(subscriptions_dir).await?;
        async_fs::write(subscriptions_list_path, list_content).await?;
    }

    // 还原订阅配置文件
    for (file_name, base64_content) in &backup.configs {
        let content = general_purpose::STANDARD.decode(base64_content)?;
        let file_path = format!("{}/{}.yaml", subscriptions_dir, file_name);
        async_fs::write(&file_path, content).await?;
    }

    log::info!("订阅数据已还原");
    Ok(())
}

// 还原覆写数据
async fn restore_overrides(
    backup: &OverrideBackup,
    overrides_dir: &str,
    overrides_list_path: &str,
) -> Result<(), Box<dyn std::error::Error + Send + Sync>> {
    // 清空现有覆写文件
    if Path::new(overrides_dir).exists() {
        let mut entries = async_fs::read_dir(overrides_dir).await?;
        while let Some(entry) = entries.next_entry().await? {
            let path = entry.path();
            if path.is_file() {
                async_fs::remove_file(path).await?;
            }
        }
    }

    // 还原覆写列表
    if let Some(list_content) = &backup.list {
        async_fs::create_dir_all(overrides_dir).await?;
        async_fs::write(overrides_list_path, list_content).await?;
    }

    // 还原覆写文件
    for (file_name, base64_content) in &backup.files {
        let content = general_purpose::STANDARD.decode(base64_content)?;
        let file_path = format!("{}/{}", overrides_dir, file_name);
        async_fs::write(&file_path, content).await?;
    }

    log::info!("覆写数据已还原");
    Ok(())
}

// 还原文件（Base64 解码）
async fn restore_file_base64(
    base64_content: &str,
    path: &str,
) -> Result<(), Box<dyn std::error::Error + Send + Sync>> {
    let content = general_purpose::STANDARD.decode(base64_content)?;

    if let Some(parent) = Path::new(path).parent() {
        async_fs::create_dir_all(parent).await?;
    }

    async_fs::write(path, content).await?;
    log::info!("文件已还原：{}", path);
    Ok(())
}

pub fn init() {
    use tokio::spawn;

    spawn(async {
        let receiver = CreateBackupRequest::get_dart_signal_receiver();
        while let Some(dart_signal) = receiver.recv().await {
            let message = dart_signal.message;
            spawn(async move {
                message.handle().await;
            });
        }
    });

    spawn(async {
        let receiver = RestoreBackupRequest::get_dart_signal_receiver();
        while let Some(dart_signal) = receiver.recv().await {
            let message = dart_signal.message;
            spawn(async move {
                message.handle().await;
            });
        }
    });
}

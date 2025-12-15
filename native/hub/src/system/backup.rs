// 备份与还原服务
//
// 目的：处理应用数据的备份和还原操作

use base64::{engine::general_purpose, Engine as _};
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
    pub app_data_path: String,
    pub app_version: String,
}

// Dart → Rust：还原备份请求
#[derive(Deserialize, DartSignal)]
pub struct RestoreBackupRequest {
    pub backup_path: String,
    pub app_data_path: String,
}

// Rust → Dart：备份操作响应
#[derive(Serialize, RustSignal)]
pub struct BackupOperationResult {
    pub success: bool,
    pub message: String,
    pub error_message: Option<String>,
}

impl CreateBackupRequest {
    // 处理创建备份请求
    pub async fn handle(self) {
        log::info!("收到创建备份请求：{}", self.target_path);

        let result = create_backup(&self.target_path, &self.app_data_path, &self.app_version).await;

        let response = match result {
            Ok(path) => {
                log::info!("备份创建成功：{}", path);
                BackupOperationResult {
                    success: true,
                    message: path,
                    error_message: None,
                }
            }
            Err(e) => {
                log::error!("备份创建失败：{}", e);
                BackupOperationResult {
                    success: false,
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

        let result = restore_backup(&self.backup_path, &self.app_data_path).await;

        let response = match result {
            Ok(()) => {
                log::info!("备份还原成功");
                BackupOperationResult {
                    success: true,
                    message: "备份还原成功".to_string(),
                    error_message: None,
                }
            }
            Err(e) => {
                log::error!("备份还原失败：{}", e);
                BackupOperationResult {
                    success: false,
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

// 创建备份
//
// 参数：
// - target_path: 备份文件保存路径
// - app_data_path: 应用数据目录
// - app_version: 应用版本号
//
// 返回：备份文件路径
pub async fn create_backup(
    target_path: &str,
    app_data_path: &str,
    app_version: &str,
) -> Result<String, Box<dyn std::error::Error + Send + Sync>> {
    log::info!("开始创建备份到：{}", target_path);

    // 1. 收集应用配置
    let app_prefs = collect_preferences(&format!("{}/app_preferences.json", app_data_path)).await?;

    // 2. 收集 Clash 配置
    let clash_prefs =
        collect_preferences(&format!("{}/clash_preferences.json", app_data_path)).await?;

    // 3. 收集订阅数据
    let subscriptions = collect_subscriptions(app_data_path).await?;

    // 4. 收集覆写数据
    let overrides = collect_overrides(app_data_path).await?;

    // 5. 收集 DNS 配置
    let dns_config = collect_file_base64(&format!("{}/dns_config.json", app_data_path)).await;

    // 6. 收集 PAC 文件
    let pac_file = collect_file_base64(&format!("{}/proxy.pac", app_data_path)).await;

    // 7. 构建备份数据
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

    // 8. 写入文件
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
//
// 参数：
// - backup_path: 备份文件路径
// - app_data_path: 应用数据目录
pub async fn restore_backup(
    backup_path: &str,
    app_data_path: &str,
) -> Result<(), Box<dyn std::error::Error + Send + Sync>> {
    log::info!("开始还原备份：{}", backup_path);

    // 1. 读取并验证备份文件
    let json_str = async_fs::read_to_string(backup_path).await?;
    let backup_data: BackupData = serde_json::from_str(&json_str)?;

    // 2. 验证版本兼容性
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

    // 3. 还原应用配置
    restore_preferences(
        &backup_data.data.app_preferences,
        &format!("{}/app_preferences.json", app_data_path),
    )
    .await?;

    // 4. 还原 Clash 配置
    restore_preferences(
        &backup_data.data.clash_preferences,
        &format!("{}/clash_preferences.json", app_data_path),
    )
    .await?;

    // 5. 还原订阅数据
    restore_subscriptions(&backup_data.data.subscriptions, app_data_path).await?;

    // 6. 还原覆写数据
    restore_overrides(&backup_data.data.overrides, app_data_path).await?;

    // 7. 还原 DNS 配置
    if let Some(dns_config) = &backup_data.data.dns_config {
        restore_file_base64(dns_config, &format!("{}/dns_config.json", app_data_path)).await?;
    }

    // 8. 还原 PAC 文件
    if let Some(pac_file) = &backup_data.data.pac_file {
        restore_file_base64(pac_file, &format!("{}/proxy.pac", app_data_path)).await?;
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
    let prefs: HashMap<String, serde_json::Value> = serde_json::from_str(&content)?;
    Ok(prefs)
}

// 收集订阅数据
async fn collect_subscriptions(
    app_data_path: &str,
) -> Result<SubscriptionBackup, Box<dyn std::error::Error + Send + Sync>> {
    let subscriptions_dir = format!("{}/subscriptions", app_data_path);
    let list_path = format!("{}/list.json", subscriptions_dir);

    let mut backup = SubscriptionBackup {
        list: None,
        configs: HashMap::new(),
    };

    // 读取订阅列表
    if Path::new(&list_path).exists() {
        backup.list = Some(async_fs::read_to_string(&list_path).await?);
    }

    // 读取所有订阅配置文件
    if Path::new(&subscriptions_dir).exists() {
        let mut entries = async_fs::read_dir(&subscriptions_dir).await?;
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
    app_data_path: &str,
) -> Result<OverrideBackup, Box<dyn std::error::Error + Send + Sync>> {
    let overrides_dir = format!("{}/overrides", app_data_path);
    let list_path = format!("{}/list.json", overrides_dir);

    let mut backup = OverrideBackup {
        list: None,
        files: HashMap::new(),
    };

    // 读取覆写列表
    if Path::new(&list_path).exists() {
        backup.list = Some(async_fs::read_to_string(&list_path).await?);
    }

    // 读取所有覆写文件
    if Path::new(&overrides_dir).exists() {
        let mut entries = async_fs::read_dir(&overrides_dir).await?;
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
    let json_str = serde_json::to_string_pretty(prefs)?;

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
    app_data_path: &str,
) -> Result<(), Box<dyn std::error::Error + Send + Sync>> {
    let subscriptions_dir = format!("{}/subscriptions", app_data_path);
    let list_path = format!("{}/list.json", subscriptions_dir);

    // 清空现有订阅配置文件
    if Path::new(&subscriptions_dir).exists() {
        let mut entries = async_fs::read_dir(&subscriptions_dir).await?;
        while let Some(entry) = entries.next_entry().await? {
            let path = entry.path();
            if path.extension().and_then(|s| s.to_str()) == Some("yaml") {
                async_fs::remove_file(path).await?;
            }
        }
    }

    // 还原订阅列表
    if let Some(list_content) = &backup.list {
        async_fs::create_dir_all(&subscriptions_dir).await?;
        async_fs::write(&list_path, list_content).await?;
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
    app_data_path: &str,
) -> Result<(), Box<dyn std::error::Error + Send + Sync>> {
    let overrides_dir = format!("{}/overrides", app_data_path);
    let list_path = format!("{}/list.json", overrides_dir);

    // 清空现有覆写文件
    if Path::new(&overrides_dir).exists() {
        let mut entries = async_fs::read_dir(&overrides_dir).await?;
        while let Some(entry) = entries.next_entry().await? {
            let path = entry.path();
            if path.is_file() {
                async_fs::remove_file(path).await?;
            }
        }
    }

    // 还原覆写列表
    if let Some(list_content) = &backup.list {
        async_fs::create_dir_all(&overrides_dir).await?;
        async_fs::write(&list_path, list_content).await?;
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

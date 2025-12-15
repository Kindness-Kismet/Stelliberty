// Windows UWP 回环豁免管理模块
//
// 目的：为 Flutter 应用提供 Windows 回环豁免的完整管理能力

use rinf::{DartSignal, RustSignal};
use serde::{Deserialize, Serialize};
use tokio::spawn;

#[cfg(windows)]
use std::collections::HashSet;
#[cfg(windows)]
use std::ptr;
#[cfg(windows)]
use windows::core::PWSTR;
#[cfg(windows)]
use windows::Win32::Foundation::{LocalFree, HLOCAL};
#[cfg(windows)]
use windows::Win32::NetworkManagement::WindowsFirewall::{
    NetworkIsolationEnumAppContainers, NetworkIsolationFreeAppContainers,
    NetworkIsolationGetAppContainerConfig, NetworkIsolationSetAppContainerConfig,
    INET_FIREWALL_APP_CONTAINER,
};
#[cfg(windows)]
use windows::Win32::Security::{PSID, SID, SID_AND_ATTRIBUTES};

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

        match enumerate_app_containers() {
            Ok(containers) => {
                log::info!("发送{}个容器信息到 Dart", containers.len());
                AppContainersList {
                    containers: vec![],
                }
                .send_signal_to_dart();

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
                AppContainersList {
                    containers: vec![],
                }
                .send_signal_to_dart();
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

        match set_loopback_exemption(&self.package_family_name, self.enabled) {
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
        log::info!(
            "处理保存配置请求，期望启用{}个容器",
            self.sid_strings.len()
        );

        // 获取所有容器
        let containers = match enumerate_app_containers() {
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
        use std::collections::HashSet as StdHashSet;
        let enabled_sids: StdHashSet<&str> = self.sid_strings.iter().map(|s| s.as_str()).collect();

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

                if let Err(e) = set_loopback_exemption_by_sid(&container.sid, should_enable) {
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

// UWP 应用容器结构
#[derive(Debug, Clone)]
pub struct AppContainer {
    pub app_container_name: String,
    pub display_name: String,
    pub package_family_name: String,
    pub sid: Vec<u8>,
    pub sid_string: String,
    pub is_loopback_enabled: bool,
}

// 将 PWSTR 转换为 String
#[cfg(windows)]
unsafe fn pwstr_to_string(pwstr: PWSTR) -> String {
    if pwstr.is_null() {
        return String::new();
    }

    unsafe {
        match pwstr.to_string() {
            Ok(s) => s,
            Err(e) => {
                log::warn!("PWSTR 转 String 失败：{:?}", e);
                String::new()
            }
        }
    }
}

// 将 SID 指针转换为字节数组
#[cfg(windows)]
unsafe fn sid_to_bytes(sid: *mut SID) -> Option<Vec<u8>> {
    if sid.is_null() {
        return None;
    }

    unsafe {
        let sid_ptr = sid as *const u8;
        let length = (*(sid_ptr.offset(1)) as usize) * 4 + 8;
        Some(std::slice::from_raw_parts(sid_ptr, length).to_vec())
    }
}

// 将 SID 指针转换为字符串格式 (S-1-15-...)
#[cfg(windows)]
unsafe fn sid_to_string(sid: *mut SID) -> String {
    if sid.is_null() {
        return String::new();
    }

    let sid_bytes = match unsafe { sid_to_bytes(sid) } {
        Some(bytes) => bytes,
        None => return String::new(),
    };

    if sid_bytes.len() < 8 {
        return String::new();
    }

    let revision = sid_bytes[0];
    let sub_authority_count = sid_bytes[1] as usize;

    if sid_bytes.len() < 8 + (sub_authority_count * 4) {
        return String::new();
    }

    let identifier_authority = u64::from_be_bytes([
        0,
        0,
        sid_bytes[2],
        sid_bytes[3],
        sid_bytes[4],
        sid_bytes[5],
        sid_bytes[6],
        sid_bytes[7],
    ]);

    let mut sid_string = format!("S-{}-{}", revision, identifier_authority);

    for i in 0..sub_authority_count {
        let offset = 8 + (i * 4);
        let sub_authority = u32::from_le_bytes([
            sid_bytes[offset],
            sid_bytes[offset + 1],
            sid_bytes[offset + 2],
            sid_bytes[offset + 3],
        ]);
        sid_string.push_str(&format!("-{}", sub_authority));
    }

    sid_string
}

// 枚举所有 UWP 应用容器
//
// 目的：获取系统中所有已安装的 UWP 应用及其回环状态
#[cfg(windows)]
pub fn enumerate_app_containers() -> Result<Vec<AppContainer>, String> {
    unsafe {
        log::info!("开始枚举应用容器");
        let mut count: u32 = 0;
        let mut containers: *mut INET_FIREWALL_APP_CONTAINER = ptr::null_mut();

        let result = NetworkIsolationEnumAppContainers(1, &mut count, &mut containers);

        if result != 0 {
            log::error!("枚举应用容器失败：{}", result);
            return Err(format!("枚举应用容器失败：{}", result));
        }

        if count == 0 || containers.is_null() {
            log::warn!("未找到任何应用容器");
            return Ok(Vec::new());
        }

        let mut loopback_count: u32 = 0;
        let mut loopback_sids: *mut SID_AND_ATTRIBUTES = ptr::null_mut();
        let _ = NetworkIsolationGetAppContainerConfig(&mut loopback_count, &mut loopback_sids);

        let loopback_slice = if loopback_count > 0 && !loopback_sids.is_null() {
            std::slice::from_raw_parts(loopback_sids, loopback_count as usize)
        } else {
            &[]
        };

        // 性能优化：使用 HashSet 存储已启用回环的 SID 字节数组
        // 将 O(n²) 复杂度优化到 O(n)
        let loopback_sid_set: HashSet<Vec<u8>> = loopback_slice
            .iter()
            .filter_map(|item| sid_to_bytes(item.Sid.0 as *mut SID))
            .collect();

        let mut result_containers = Vec::new();
        let container_slice = std::slice::from_raw_parts(containers, count as usize);

        for container in container_slice {
            let app_container_name = pwstr_to_string(container.appContainerName);
            let display_name = pwstr_to_string(container.displayName);
            let package_full_name = pwstr_to_string(container.packageFullName);

            let sid_bytes = sid_to_bytes(container.appContainerSid).unwrap_or_default();
            let sid_string = sid_to_string(container.appContainerSid);

            // O(1) 查找，而不是 O(n) 的线性搜索
            let is_loopback_enabled = loopback_sid_set.contains(&sid_bytes);

            result_containers.push(AppContainer {
                app_container_name,
                display_name,
                package_family_name: package_full_name,
                sid: sid_bytes,
                sid_string,
                is_loopback_enabled,
            });
        }

        if !loopback_sids.is_null() {
            let _ = LocalFree(Some(HLOCAL(loopback_sids as *mut _)));
        }
        NetworkIsolationFreeAppContainers(containers);

        log::info!("成功枚举{}个应用容器", result_containers.len());
        Ok(result_containers)
    }
}

// 通过 SID 字节数组设置回环豁免
//
// 目的：为指定的 UWP 应用启用或禁用网络回环豁免
#[cfg(windows)]
pub fn set_loopback_exemption_by_sid(sid_bytes: &[u8], enabled: bool) -> Result<(), String> {
    // 验证 SID 字节数组的最小长度
    if sid_bytes.len() < 8 {
        return Err("SID 字节数组无效：长度过短".to_string());
    }

    unsafe {
        // 直接使用字节数组指针，生命周期由调用者保证
        let target_sid = sid_bytes.as_ptr() as *mut SID;
        let sid_string = sid_to_string(target_sid);
        log::info!("设置回环豁免(SID：{})：{}", sid_string, enabled);

        let mut loopback_count: u32 = 0;
        let mut loopback_sids: *mut SID_AND_ATTRIBUTES = ptr::null_mut();
        let _ = NetworkIsolationGetAppContainerConfig(&mut loopback_count, &mut loopback_sids);

        let loopback_slice = if loopback_count > 0 && !loopback_sids.is_null() {
            std::slice::from_raw_parts(loopback_sids, loopback_count as usize)
        } else {
            &[]
        };

        // 性能优化：直接比较字节数组，避免重复调用 compare_sids
        let target_sid_bytes = std::slice::from_raw_parts(target_sid as *const u8, sid_bytes.len());
        let mut new_sids: Vec<SID_AND_ATTRIBUTES> = loopback_slice
            .iter()
            .filter(|item| {
                if let Some(item_bytes) = sid_to_bytes(item.Sid.0 as *mut SID) {
                    item_bytes.as_slice() != target_sid_bytes
                } else {
                    true
                }
            })
            .copied()
            .collect();

        if enabled {
            new_sids.push(SID_AND_ATTRIBUTES {
                Sid: PSID(target_sid as *mut _),
                Attributes: 0,
            });
        }

        let result = if new_sids.is_empty() {
            NetworkIsolationSetAppContainerConfig(&[])
        } else {
            NetworkIsolationSetAppContainerConfig(&new_sids)
        };

        if !loopback_sids.is_null() {
            let _ = LocalFree(Some(HLOCAL(loopback_sids as *mut _)));
        }

        if result == 0 {
            log::info!("回环豁免设置成功(SID：{})", sid_string);
            Ok(())
        } else {
            let error_code = result as u32;
            let error_msg = format!(
                "设置回环豁免失败 (错误码: 0x{:08X}, 十进制: {})",
                error_code, error_code
            );
            log::error!("{} (SID：{})", error_msg, sid_string);

            // 添加常见错误码的解释（精简版，适合 UI 显示）
            // 注意：Windows API 可能返回 HRESULT (0x80070005) 或 Win32 错误码 (5)
            let error_detail = match error_code {
                // HRESULT 格式
                0x80070005 => "权限不足",
                0x80070057 => "参数无效",
                0x80004005 => "系统限制",
                // Win32 原始错误码格式
                5 => "权限不足",
                87 => "参数无效",
                _ => "未知错误",
            };

            log::error!("错误详情：{}", error_detail);
            Err(format!("{} - {}", error_msg, error_detail))
        }
    }
}

// 通过包家族名称设置回环豁免
//
// 目的：使用更友好的包名方式设置回环豁免
#[cfg(windows)]
pub fn set_loopback_exemption(package_family_name: &str, enabled: bool) -> Result<(), String> {
    unsafe {
        log::info!("设置回环豁免：{} - {}", package_family_name, enabled);
        let mut count: u32 = 0;
        let mut containers: *mut INET_FIREWALL_APP_CONTAINER = ptr::null_mut();

        let result = NetworkIsolationEnumAppContainers(1, &mut count, &mut containers);

        if result != 0 {
            log::error!("枚举应用容器失败：{}", result);
            return Err(format!("枚举应用容器失败：{}", result));
        }

        if count == 0 || containers.is_null() {
            NetworkIsolationFreeAppContainers(containers);
            log::warn!("未找到任何应用容器");
            return Err("未找到应用容器".to_string());
        }

        let container_slice = std::slice::from_raw_parts(containers, count as usize);
        let target_sid = container_slice
            .iter()
            .find(|c| pwstr_to_string(c.packageFullName) == package_family_name)
            .map(|c| c.appContainerSid);

        if target_sid.is_none() {
            NetworkIsolationFreeAppContainers(containers);
            log::error!("未找到包：{}", package_family_name);
            return Err(format!("未找到包：{}", package_family_name));
        }

        let mut loopback_count: u32 = 0;
        let mut loopback_sids: *mut SID_AND_ATTRIBUTES = ptr::null_mut();
        let _ = NetworkIsolationGetAppContainerConfig(&mut loopback_count, &mut loopback_sids);

        let loopback_slice = if loopback_count > 0 && !loopback_sids.is_null() {
            std::slice::from_raw_parts(loopback_sids, loopback_count as usize)
        } else {
            &[]
        };

        let target_sid_unwrapped = target_sid.ok_or("目标 SID 为空")?;

        // 性能优化：获取目标 SID 字节数组用于比较
        let target_sid_bytes = sid_to_bytes(target_sid_unwrapped);

        let mut new_sids: Vec<SID_AND_ATTRIBUTES> = loopback_slice
            .iter()
            .filter(|item| {
                if let (Some(target_bytes), Some(item_bytes)) =
                    (&target_sid_bytes, sid_to_bytes(item.Sid.0 as *mut SID))
                {
                    item_bytes != *target_bytes
                } else {
                    true
                }
            })
            .copied()
            .collect();

        if enabled {
            new_sids.push(SID_AND_ATTRIBUTES {
                Sid: PSID(target_sid_unwrapped as *mut _),
                Attributes: 0,
            });
        }

        let result = if new_sids.is_empty() {
            NetworkIsolationSetAppContainerConfig(&[])
        } else {
            NetworkIsolationSetAppContainerConfig(&new_sids)
        };

        if !loopback_sids.is_null() {
            let _ = LocalFree(Some(HLOCAL(loopback_sids as *mut _)));
        }
        NetworkIsolationFreeAppContainers(containers);

        if result == 0 {
            log::info!("回环豁免设置成功");
            Ok(())
        } else {
            let error_code = result as u32;
            let error_msg = format!(
                "设置回环豁免失败 (错误码: 0x{:08X}, 十进制: {})",
                error_code, error_code
            );
            log::error!("{}", error_msg);

            // 添加常见错误码的解释
            let error_detail = match error_code {
                // HRESULT 格式
                0x80070005 => "权限不足",
                0x80070057 => "参数无效",
                0x80004005 => "系统限制",
                // Win32 原始错误码格式
                5 => "权限不足",
                87 => "参数无效",
                _ => "未知错误",
            };

            log::error!("错误详情：{}", error_detail);
            Err(format!("{} - {}", error_msg, error_detail))
        }
    }
}

// 初始化 UWP 回环豁免消息监听器
pub fn init() {
    // 修复：避免嵌套 spawn，直接在监听循环中处理消息
    spawn(async {
        let receiver = GetAppContainers::get_dart_signal_receiver();
        while let Some(dart_signal) = receiver.recv().await {
            dart_signal.message.handle();
        }
    });

    spawn(async {
        let receiver = SetLoopback::get_dart_signal_receiver();
        while let Some(dart_signal) = receiver.recv().await {
            dart_signal.message.handle();
        }
    });

    spawn(async {
        let receiver = SaveLoopbackConfiguration::get_dart_signal_receiver();
        while let Some(dart_signal) = receiver.recv().await {
            dart_signal.message.handle();
        }
    });
}

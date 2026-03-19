// Windows UWP 回环豁免管理：提供回环豁免的查询与配置能力。
// 仅在 Windows 平台启用。

use rinf::{DartSignal, RustSignal};
use serde::{Deserialize, Serialize};
use tokio::spawn;

#[cfg(windows)]
use std::collections::HashSet;
#[cfg(windows)]
use std::ptr;
#[cfg(windows)]
use windows::Win32::Foundation::{HLOCAL, LocalFree};
#[cfg(windows)]
use windows::Win32::NetworkManagement::WindowsFirewall::{
    INET_FIREWALL_APP_CONTAINER, NetworkIsolationEnumAppContainers,
    NetworkIsolationFreeAppContainers, NetworkIsolationGetAppContainerConfig,
    NetworkIsolationSetAppContainerConfig,
};
#[cfg(windows)]
use windows::Win32::Security::{GetLengthSid, IsValidSid, PSID, SID, SID_AND_ATTRIBUTES};
#[cfg(windows)]
use windows::Win32::UI::Shell::IsUserAnAdmin;
#[cfg(windows)]
use windows::core::PWSTR;

// Dart → Rust：获取所有应用容器
#[derive(Deserialize, DartSignal)]
pub struct GetAppContainers;

// Dart → Rust：设置回环豁免
#[derive(Deserialize, DartSignal)]
pub struct SetLoopback {
    pub package_family_name: String,
    pub is_enabled: bool,
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
    pub is_loopback_enabled: bool,
}

// Rust → Dart：设置回环豁免结果
#[derive(Serialize, RustSignal)]
pub struct SetLoopbackResult {
    pub is_successful: bool,
    pub error_message: Option<String>,
}

// Rust → Dart：应用容器流传输完成信号
#[derive(Serialize, RustSignal)]
pub struct AppContainersComplete;

// Rust → Dart：保存配置结果
#[derive(Serialize, RustSignal)]
pub struct SaveLoopbackConfigurationResult {
    pub is_successful: bool,
    pub error_message: Option<String>,
}

impl GetAppContainers {
    // 获取应用容器列表并返回回环状态。
    pub fn handle(&self) {
        log::info!("处理获取应用容器请求");
        #[cfg(windows)]
        log_current_process_context("GetAppContainers::handle");

        match enumerate_app_containers() {
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
                        is_loopback_enabled: c.is_loopback_enabled,
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
    // 为单个应用启用或禁用回环豁免。
    pub fn handle(self) {
        log::info!(
            "处理设置回环豁免请求：{} - {}",
            self.package_family_name,
            self.is_enabled
        );

        match set_loopback_exemption(&self.package_family_name, self.is_enabled) {
            Ok(()) => {
                log::info!("回环豁免设置成功");
                SetLoopbackResult {
                    is_successful: true,
                    error_message: None,
                }
                .send_signal_to_dart();
            }
            Err(e) => {
                log::error!("回环豁免设置失败：{}", e);
                SetLoopbackResult {
                    is_successful: false,
                    error_message: Some(e),
                }
                .send_signal_to_dart();
            }
        }
    }
}

impl SaveLoopbackConfiguration {
    // 批量保存回环豁免配置。
    pub fn handle(self) {
        log::info!("处理保存配置请求，期望启用{}个容器", self.sid_strings.len());

        // 获取所有容器
        let containers = match enumerate_app_containers() {
            Ok(c) => c,
            Err(e) => {
                log::error!("枚举容器失败：{}", e);
                SaveLoopbackConfigurationResult {
                    is_successful: false,
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
                // 如果跳过的应用少于等于 3 个，显示具体名称
                message_parts.push(format!("（{}）", skipped.join("、")));
            }
        }

        if errors.is_empty() {
            SaveLoopbackConfigurationResult {
                is_successful: true,
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
                is_successful: false,
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

#[cfg(windows)]
const APP_CONTAINER_ENUM_FLAGS: u32 = 1;
#[cfg(windows)]
const APP_CONTAINER_DEBUG_LOG_LIMIT: usize = 10;

#[cfg(windows)]
fn explain_network_isolation_error(error_code: u32) -> &'static str {
    match error_code {
        0x80070005 | 5 => "权限不足，通常表示需要管理员权限或目标受系统保护",
        0x80070057 | 87 => "参数无效，通常表示传入参数或 SID 配置不符合 API 要求",
        0x80004005 => "未指定错误，通常表示系统限制或底层组件拒绝操作",
        0x000006F4 => "RPC 空引用指针，通常表示 NetIso 或 RPC 返回异常状态",
        0x00000490 => "未找到元素，通常表示目标容器或配置项不存在",
        _ => "未知错误",
    }
}

#[cfg(windows)]
fn extract_win32_error_code(error_code: u32) -> Option<u32> {
    if (error_code & 0xFFFF0000) == 0x80070000 {
        Some(error_code & 0xFFFF)
    } else if error_code <= u16::MAX as u32 {
        Some(error_code)
    } else {
        None
    }
}

#[cfg(windows)]
fn format_network_isolation_error_detail(error_code: u32) -> String {
    let mut details = vec![format!(
        "解释：{}",
        explain_network_isolation_error(error_code)
    )];

    if let Some(win32_code) = extract_win32_error_code(error_code) {
        details.push(format!("Win32 代码：{}", win32_code));
        details.push(format!(
            "系统消息：{}",
            std::io::Error::from_raw_os_error(win32_code as i32)
        ));
    }

    if error_code & 0x80000000 != 0 {
        details.push(format!(
            "HRESULT facility={} code={}",
            (error_code >> 16) & 0x1FFF,
            error_code & 0xFFFF
        ));
    }

    details.join("，")
}

#[cfg(windows)]
fn build_network_isolation_error_message(api_name: &str, error_code: u32) -> String {
    format!(
        "{} 失败 (错误码: 0x{:08X}, 十进制: {}) - {}",
        api_name,
        error_code,
        error_code,
        format_network_isolation_error_detail(error_code)
    )
}

#[cfg(windows)]
fn current_process_context() -> String {
    let exe_path = std::env::current_exe()
        .map(|path| path.display().to_string())
        .unwrap_or_else(|e| format!("<unavailable: {}>", e));
    let is_user_an_admin = unsafe { IsUserAnAdmin().as_bool() };

    format!(
        "pid={}，arch={}，pointer_width={}bit，exe_path={}，is_user_an_admin={}",
        std::process::id(),
        std::env::consts::ARCH,
        std::mem::size_of::<usize>() * 8,
        exe_path,
        is_user_an_admin
    )
}

#[cfg(windows)]
fn log_current_process_context(operation: &str) {
    log::info!("{} 上下文：{}", operation, current_process_context());
}

#[cfg(windows)]
unsafe fn validated_sid_bytes(sid: *mut SID) -> Option<Vec<u8>> {
    if sid.is_null() {
        return None;
    }

    let sid_ptr = PSID(sid as *mut _);
    if !unsafe { IsValidSid(sid_ptr) }.as_bool() {
        return None;
    }

    let length = unsafe { GetLengthSid(sid_ptr) } as usize;
    if length < 8 {
        return None;
    }

    Some(unsafe { std::slice::from_raw_parts(sid as *const u8, length) }.to_vec())
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
    unsafe { validated_sid_bytes(sid) }
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

// 枚举 UWP 应用容器并返回回环状态。
#[cfg(windows)]
pub fn enumerate_app_containers() -> Result<Vec<AppContainer>, String> {
    unsafe {
        log::info!("开始枚举应用容器");
        log_current_process_context("enumerate_app_containers");

        let mut count: u32 = 0;
        let mut containers: *mut INET_FIREWALL_APP_CONTAINER = ptr::null_mut();

        log::info!(
            "调用 NetworkIsolationEnumAppContainers：flags={}，initial_count={}，initial_containers_is_null={}",
            APP_CONTAINER_ENUM_FLAGS,
            count,
            containers.is_null()
        );
        let result = NetworkIsolationEnumAppContainers(
            APP_CONTAINER_ENUM_FLAGS,
            &mut count,
            &mut containers,
        );

        if result != 0 {
            let error_code = result as u32;
            let error_message = build_network_isolation_error_message(
                "NetworkIsolationEnumAppContainers",
                error_code,
            );
            log::error!(
                "{}，flags={}，returned_count={}，containers_is_null={}，{}",
                error_message,
                APP_CONTAINER_ENUM_FLAGS,
                count,
                containers.is_null(),
                current_process_context()
            );
            return Err(error_message);
        }

        log::debug!(
            "NetworkIsolationEnumAppContainers 成功：count={}，containers_is_null={}",
            count,
            containers.is_null()
        );

        if count == 0 {
            log::debug!(
                "NetworkIsolationEnumAppContainers 返回成功但 count=0，containers_is_null={}",
                containers.is_null()
            );
        }
        if containers.is_null() {
            log::debug!(
                "NetworkIsolationEnumAppContainers 返回成功但 containers_is_null=true，count={}",
                count
            );
        }
        if count == 0 || containers.is_null() {
            if !containers.is_null() {
                NetworkIsolationFreeAppContainers(containers);
            }
            log::warn!("未找到任何应用容器");
            return Ok(Vec::new());
        }

        let mut loopback_count: u32 = 0;
        let mut loopback_sids: *mut SID_AND_ATTRIBUTES = ptr::null_mut();
        let loopback_result =
            NetworkIsolationGetAppContainerConfig(&mut loopback_count, &mut loopback_sids);
        log::debug!(
            "NetworkIsolationGetAppContainerConfig 返回：0x{:08X} / {}，loopback_count={}，loopback_sids_is_null={}",
            loopback_result as u32,
            loopback_result,
            loopback_count,
            loopback_sids.is_null()
        );
        if loopback_result != 0 {
            let error_code = loopback_result as u32;
            log::warn!(
                "{}，读取 loopback 配置时将按空集合处理",
                build_network_isolation_error_message(
                    "NetworkIsolationGetAppContainerConfig",
                    error_code,
                )
            );
        }
        if loopback_count == 0 {
            log::debug!(
                "NetworkIsolationGetAppContainerConfig 返回 loopback_count=0，loopback_sids_is_null={}",
                loopback_sids.is_null()
            );
        }
        if loopback_sids.is_null() {
            log::debug!(
                "NetworkIsolationGetAppContainerConfig 返回 loopback_sids_is_null=true，loopback_count={}",
                loopback_count
            );
        }

        let loopback_slice =
            if loopback_result == 0 && loopback_count > 0 && !loopback_sids.is_null() {
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

        for (index, container) in container_slice.iter().enumerate() {
            let app_container_name = pwstr_to_string(container.appContainerName);
            let display_name = pwstr_to_string(container.displayName);
            let package_full_name = pwstr_to_string(container.packageFullName);

            let sid_bytes = sid_to_bytes(container.appContainerSid).unwrap_or_default();
            let sid_string = sid_to_string(container.appContainerSid);

            // O(1) 查找，而不是 O(n) 的线性搜索
            let is_loopback_enabled = loopback_sid_set.contains(&sid_bytes);

            if index < APP_CONTAINER_DEBUG_LOG_LIMIT {
                log::debug!(
                    "容器摘要[{}]：display_name={}，package_family_name={}，app_container_name={}，sid_string={}，is_loopback_enabled={}",
                    index,
                    display_name,
                    package_full_name,
                    app_container_name,
                    sid_string,
                    is_loopback_enabled
                );
            }

            result_containers.push(AppContainer {
                app_container_name,
                display_name,
                package_family_name: package_full_name,
                sid: sid_bytes,
                sid_string,
                is_loopback_enabled,
            });
        }

        if result_containers.len() > APP_CONTAINER_DEBUG_LOG_LIMIT {
            log::debug!(
                "容器摘要仅打印前 {} 个，总计 {} 个",
                APP_CONTAINER_DEBUG_LOG_LIMIT,
                result_containers.len()
            );
        }

        if !loopback_sids.is_null() {
            let _ = LocalFree(Some(HLOCAL(loopback_sids as *mut _)));
        }
        NetworkIsolationFreeAppContainers(containers);

        log::info!("成功枚举{}个应用容器", result_containers.len());
        Ok(result_containers)
    }
}

// 通过 SID 字节数组设置回环豁免。
#[cfg(windows)]
pub fn set_loopback_exemption_by_sid(sid_bytes: &[u8], enabled: bool) -> Result<(), String> {
    // 验证 SID 字节数组的最小长度
    if sid_bytes.len() < 8 {
        return Err("SID 字节数组无效：长度过短".to_string());
    }

    unsafe {
        // 直接使用字节数组指针，生命周期由调用者保证
        let target_sid = sid_bytes.as_ptr() as *mut SID;
        let Some(validated_target_sid_bytes) = validated_sid_bytes(target_sid) else {
            return Err("SID 字节数组无效：结构校验失败".to_string());
        };
        let sid_string = sid_to_string(target_sid);
        log::info!("设置回环豁免(SID：{})：{}", sid_string, enabled);
        log_current_process_context("set_loopback_exemption_by_sid");

        let mut loopback_count: u32 = 0;
        let mut loopback_sids: *mut SID_AND_ATTRIBUTES = ptr::null_mut();
        let config_result =
            NetworkIsolationGetAppContainerConfig(&mut loopback_count, &mut loopback_sids);
        log::debug!(
            "set_loopback_exemption_by_sid 读取当前配置：target_sid={}，enabled={}，config_result=0x{:08X} / {}，loopback_count={}，loopback_sids_is_null={}",
            sid_string,
            enabled,
            config_result as u32,
            config_result,
            loopback_count,
            loopback_sids.is_null()
        );
        if config_result != 0 {
            let error_code = config_result as u32;
            let error_message = build_network_isolation_error_message(
                "NetworkIsolationGetAppContainerConfig",
                error_code,
            );
            log::error!("{}，target_sid={}", error_message, sid_string);
            if !loopback_sids.is_null() {
                let _ = LocalFree(Some(HLOCAL(loopback_sids as *mut _)));
            }
            return Err(error_message);
        }

        let loopback_slice = if loopback_count > 0 && !loopback_sids.is_null() {
            std::slice::from_raw_parts(loopback_sids, loopback_count as usize)
        } else {
            &[]
        };

        // 性能优化：一次遍历同时完成命中判断与过滤
        let mut existed_before = false;
        let mut next_sids: Vec<SID_AND_ATTRIBUTES> = Vec::with_capacity(loopback_slice.len());
        for item in loopback_slice.iter().copied() {
            let should_keep = if let Some(item_bytes) = sid_to_bytes(item.Sid.0 as *mut SID) {
                let is_target = item_bytes == validated_target_sid_bytes;
                existed_before |= is_target;
                !is_target
            } else {
                true
            };

            if should_keep {
                next_sids.push(item);
            }
        }
        log::debug!(
            "set_loopback_exemption_by_sid 命中情况：target_sid={}，existed_before={}，before_count={}",
            sid_string,
            existed_before,
            loopback_slice.len()
        );

        if enabled {
            next_sids.push(SID_AND_ATTRIBUTES {
                Sid: PSID(target_sid as *mut _),
                Attributes: 0,
            });
        }

        log::debug!(
            "set_loopback_exemption_by_sid 准备提交：target_sid={}，enabled={}，before_count={}，after_count={}",
            sid_string,
            enabled,
            loopback_slice.len(),
            next_sids.len()
        );

        let result = if next_sids.is_empty() {
            NetworkIsolationSetAppContainerConfig(&[])
        } else {
            NetworkIsolationSetAppContainerConfig(&next_sids)
        };

        if !loopback_sids.is_null() {
            let _ = LocalFree(Some(HLOCAL(loopback_sids as *mut _)));
        }

        if result == 0 {
            log::info!(
                "回环豁免设置成功(SID：{})，before_count={}，after_count={}，existed_before={}",
                sid_string,
                loopback_slice.len(),
                next_sids.len(),
                existed_before
            );
            Ok(())
        } else {
            let error_code = result as u32;
            let error_message = build_network_isolation_error_message(
                "NetworkIsolationSetAppContainerConfig",
                error_code,
            );
            log::error!(
                "{}，target_sid={}，enabled={}，before_count={}，after_count={}，existed_before={}",
                error_message,
                sid_string,
                enabled,
                loopback_slice.len(),
                next_sids.len(),
                existed_before
            );
            Err(error_message)
        }
    }
}

// 通过包家族名称设置回环豁免。
#[cfg(windows)]
pub fn set_loopback_exemption(package_family_name: &str, enabled: bool) -> Result<(), String> {
    unsafe {
        log::info!("设置回环豁免：{} - {}", package_family_name, enabled);
        log_current_process_context("set_loopback_exemption");

        let mut count: u32 = 0;
        let mut containers: *mut INET_FIREWALL_APP_CONTAINER = ptr::null_mut();

        log::info!(
            "set_loopback_exemption 调用枚举：package_family_name={}，enabled={}，flags={}，initial_count={}，initial_containers_is_null={}",
            package_family_name,
            enabled,
            APP_CONTAINER_ENUM_FLAGS,
            count,
            containers.is_null()
        );
        let result = NetworkIsolationEnumAppContainers(
            APP_CONTAINER_ENUM_FLAGS,
            &mut count,
            &mut containers,
        );

        if result != 0 {
            let error_code = result as u32;
            let error_message = build_network_isolation_error_message(
                "NetworkIsolationEnumAppContainers",
                error_code,
            );
            log::error!(
                "{}，package_family_name={}，flags={}，returned_count={}，containers_is_null={}，{}",
                error_message,
                package_family_name,
                APP_CONTAINER_ENUM_FLAGS,
                count,
                containers.is_null(),
                current_process_context()
            );
            return Err(error_message);
        }

        log::debug!(
            "set_loopback_exemption 枚举成功：package_family_name={}，count={}，containers_is_null={}",
            package_family_name,
            count,
            containers.is_null()
        );

        if count == 0 {
            log::debug!(
                "set_loopback_exemption 枚举成功但 count=0：package_family_name={}，containers_is_null={}",
                package_family_name,
                containers.is_null()
            );
        }
        if containers.is_null() {
            log::debug!(
                "set_loopback_exemption 枚举成功但 containers_is_null=true：package_family_name={}，count={}",
                package_family_name,
                count
            );
        }
        if count == 0 || containers.is_null() {
            NetworkIsolationFreeAppContainers(containers);
            log::warn!("未找到任何应用容器");
            return Err("未找到应用容器".to_string());
        }

        let container_slice = std::slice::from_raw_parts(containers, count as usize);
        let target_container = container_slice
            .iter()
            .find(|c| pwstr_to_string(c.packageFullName) == package_family_name);

        if target_container.is_none() {
            NetworkIsolationFreeAppContainers(containers);
            log::error!(
                "未找到包：{}，枚举总数={}，containers_is_null={}",
                package_family_name,
                count,
                containers.is_null()
            );
            return Err(format!("未找到包：{}", package_family_name));
        }

        let target_container = target_container.ok_or("目标容器为空")?;
        let target_sid_unwrapped = target_container.appContainerSid;
        let Some(target_sid_bytes) = validated_sid_bytes(target_sid_unwrapped) else {
            NetworkIsolationFreeAppContainers(containers);
            return Err(format!("目标容器 SID 无效：{}", package_family_name));
        };
        let target_sid_string = sid_to_string(target_sid_unwrapped);
        if log::log_enabled!(log::Level::Debug) {
            let target_app_container_name = pwstr_to_string(target_container.appContainerName);
            let target_display_name = pwstr_to_string(target_container.displayName);
            log::debug!(
                "set_loopback_exemption 命中目标容器：display_name={}，package_family_name={}，app_container_name={}，sid_string={}",
                target_display_name,
                package_family_name,
                target_app_container_name,
                target_sid_string
            );
        }

        let mut loopback_count: u32 = 0;
        let mut loopback_sids: *mut SID_AND_ATTRIBUTES = ptr::null_mut();
        let config_result =
            NetworkIsolationGetAppContainerConfig(&mut loopback_count, &mut loopback_sids);
        log::debug!(
            "set_loopback_exemption 读取当前配置：package_family_name={}，config_result=0x{:08X} / {}，loopback_count={}，loopback_sids_is_null={}",
            package_family_name,
            config_result as u32,
            config_result,
            loopback_count,
            loopback_sids.is_null()
        );
        if config_result != 0 {
            let error_code = config_result as u32;
            let error_message = build_network_isolation_error_message(
                "NetworkIsolationGetAppContainerConfig",
                error_code,
            );
            log::error!(
                "{}，package_family_name={}",
                error_message,
                package_family_name
            );
            if !loopback_sids.is_null() {
                let _ = LocalFree(Some(HLOCAL(loopback_sids as *mut _)));
            }
            NetworkIsolationFreeAppContainers(containers);
            return Err(error_message);
        }

        let loopback_slice = if loopback_count > 0 && !loopback_sids.is_null() {
            std::slice::from_raw_parts(loopback_sids, loopback_count as usize)
        } else {
            &[]
        };

        // 性能优化：一次遍历同时完成命中判断与过滤
        let matched_target = true;
        let mut existed_before = false;
        let mut next_sids: Vec<SID_AND_ATTRIBUTES> = Vec::with_capacity(loopback_slice.len());
        for item in loopback_slice.iter().copied() {
            let should_keep = if let Some(item_bytes) = sid_to_bytes(item.Sid.0 as *mut SID) {
                let is_target = item_bytes == target_sid_bytes;
                existed_before |= is_target;
                !is_target
            } else {
                true
            };

            if should_keep {
                next_sids.push(item);
            }
        }
        log::debug!(
            "set_loopback_exemption 命中情况：package_family_name={}，matched_target={}，existed_before={}，before_count={}",
            package_family_name,
            matched_target,
            existed_before,
            loopback_slice.len()
        );

        if enabled {
            next_sids.push(SID_AND_ATTRIBUTES {
                Sid: PSID(target_sid_unwrapped as *mut _),
                Attributes: 0,
            });
        }

        log::debug!(
            "set_loopback_exemption 准备提交：package_family_name={}，sid_string={}，enabled={}，before_count={}，after_count={}",
            package_family_name,
            target_sid_string,
            enabled,
            loopback_slice.len(),
            next_sids.len()
        );

        let result = if next_sids.is_empty() {
            NetworkIsolationSetAppContainerConfig(&[])
        } else {
            NetworkIsolationSetAppContainerConfig(&next_sids)
        };

        if !loopback_sids.is_null() {
            let _ = LocalFree(Some(HLOCAL(loopback_sids as *mut _)));
        }
        NetworkIsolationFreeAppContainers(containers);

        if result == 0 {
            log::info!(
                "回环豁免设置成功：package_family_name={}，sid_string={}，before_count={}，after_count={}，existed_before={}",
                package_family_name,
                target_sid_string,
                loopback_slice.len(),
                next_sids.len(),
                existed_before
            );
            Ok(())
        } else {
            let error_code = result as u32;
            let error_message = build_network_isolation_error_message(
                "NetworkIsolationSetAppContainerConfig",
                error_code,
            );
            log::error!(
                "{}，package_family_name={}，sid_string={}，enabled={}，before_count={}，after_count={}，matched_target={}，existed_before={}",
                error_message,
                package_family_name,
                target_sid_string,
                enabled,
                loopback_slice.len(),
                next_sids.len(),
                matched_target,
                existed_before
            );
            Err(error_message)
        }
    }
}

// 初始化 UWP 回环豁免消息监听器
pub fn init() {
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

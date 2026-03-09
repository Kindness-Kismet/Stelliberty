// 系统代理配置管理：提供跨平台的系统级代理设置能力。
// 对外暴露启用、禁用与状态查询接口。

use rinf::{DartSignal, RustSignal};
use serde::{Deserialize, Serialize};
use tokio::spawn;

// Dart → Rust：启用系统代理
#[derive(Deserialize, DartSignal)]
pub struct EnableSystemProxy {
    pub host: String,
    pub port: u16,
    pub bypass_domains: Vec<String>,
    pub should_use_pac_mode: bool,
    pub pac_script: String,
    pub pac_file_path: String,
}

// Dart → Rust：禁用系统代理
#[derive(Deserialize, DartSignal)]
pub struct DisableSystemProxy;

// Dart → Rust：获取系统代理状态
#[derive(Deserialize, DartSignal)]
pub struct GetSystemProxy;

// Rust → Dart：代理操作结果
#[derive(Serialize, RustSignal)]
pub struct SystemProxyResult {
    pub is_successful: bool,
    pub error_message: Option<String>,
}

// Rust → Dart：系统代理状态信息
#[derive(Serialize, RustSignal)]
pub struct SystemProxyInfo {
    pub is_enabled: bool,
    pub server: Option<String>,
}

// 代理操作结果
#[derive(Debug)]
pub enum ProxyResult {
    Success,
    Error(String),
}

// 系统代理配置信息
#[derive(Debug, Clone)]
pub struct ProxyInfo {
    pub is_enabled: bool,
    pub server: Option<String>,
}

impl EnableSystemProxy {
    // 启用系统代理并应用相关配置。
    pub async fn handle(self) {
        if self.should_use_pac_mode {
            log::info!("收到启用代理请求 (PAC 模式)");
        } else {
            log::info!("收到启用代理请求：{}:{}", self.host, self.port);
        }

        let result = enable_proxy(
            &self.host,
            self.port,
            self.bypass_domains,
            self.should_use_pac_mode,
            &self.pac_script,
            &self.pac_file_path,
        )
        .await;

        let response = match result {
            ProxyResult::Success => SystemProxyResult {
                is_successful: true,
                error_message: None,
            },
            ProxyResult::Error(msg) => {
                log::error!("启用代理失败：{}", msg);
                SystemProxyResult {
                    is_successful: false,
                    error_message: Some(msg),
                }
            }
        };

        response.send_signal_to_dart();
    }
}

impl DisableSystemProxy {
    // 禁用系统代理并清理相关配置。
    pub async fn handle(&self) {
        log::info!("收到禁用代理请求");

        let result = disable_proxy().await;

        let response = match result {
            ProxyResult::Success => SystemProxyResult {
                is_successful: true,
                error_message: None,
            },
            ProxyResult::Error(msg) => {
                log::error!("禁用代理失败：{}", msg);
                SystemProxyResult {
                    is_successful: false,
                    error_message: Some(msg),
                }
            }
        };

        response.send_signal_to_dart();
    }
}

impl GetSystemProxy {
    // 查询当前系统代理状态与配置信息。
    pub async fn handle(&self) {
        log::info!("收到获取系统代理状态请求");

        let proxy_info = get_proxy_info().await;

        let response = SystemProxyInfo {
            is_enabled: proxy_info.is_enabled,
            server: proxy_info.server,
        };

        response.send_signal_to_dart();
    }
}

#[cfg(target_os = "windows")]
mod windows_impl {
    use super::{ProxyInfo, ProxyResult};
    use std::ffi::OsStr;
    use std::fs;
    use std::os::windows::ffi::OsStrExt;
    use windows::Win32::Foundation::ERROR_SUCCESS;
    use windows::Win32::NetworkManagement::Rras::{RASENTRYNAMEW, RasEnumEntriesW};
    use windows::Win32::Networking::WinInet::{
        INTERNET_OPTION_PER_CONNECTION_OPTION, INTERNET_OPTION_REFRESH,
        INTERNET_OPTION_SETTINGS_CHANGED, INTERNET_PER_CONN_AUTOCONFIG_URL,
        INTERNET_PER_CONN_FLAGS, INTERNET_PER_CONN_OPTION_LISTW, INTERNET_PER_CONN_OPTIONW,
        INTERNET_PER_CONN_PROXY_BYPASS, INTERNET_PER_CONN_PROXY_SERVER, InternetQueryOptionW,
        InternetSetOptionW, PROXY_TYPE_AUTO_PROXY_URL, PROXY_TYPE_DIRECT, PROXY_TYPE_PROXY,
    };
    use windows::core::PWSTR;

    // 配置并启用系统代理，可选使用 PAC 脚本。
    pub async fn enable_proxy(
        host: &str,
        port: u16,
        bypass_domains: Vec<String>,
        should_use_pac_mode: bool,
        pac_script: &str,
        pac_file_path: &str,
    ) -> ProxyResult {
        if should_use_pac_mode {
            log::info!("正在设置系统代理 (PAC 模式)");
            return enable_proxy_pac(host, port, pac_script, pac_file_path);
        }

        let proxy_server = format!("{}:{}", host, port);
        log::info!("正在设置系统代理：{}", proxy_server);

        unsafe {
            // 转换为 wide string
            let mut proxy_server_wide: Vec<u16> = OsStr::new(&proxy_server)
                .encode_wide()
                .chain(std::iter::once(0))
                .collect();

            let bypasses = bypass_domains.join(";");
            let mut bypasses_wide: Vec<u16> = OsStr::new(&bypasses)
                .encode_wide()
                .chain(std::iter::once(0))
                .collect();

            // 构造选项数组
            let mut option1 = INTERNET_PER_CONN_OPTIONW {
                dwOption: INTERNET_PER_CONN_FLAGS,
                Value: std::mem::zeroed(),
            };
            *(&mut option1.Value as *mut _ as *mut u32) = PROXY_TYPE_DIRECT | PROXY_TYPE_PROXY;

            let mut option2 = INTERNET_PER_CONN_OPTIONW {
                dwOption: INTERNET_PER_CONN_PROXY_SERVER,
                Value: std::mem::zeroed(),
            };
            *(&mut option2.Value as *mut _ as *mut PWSTR) = PWSTR(proxy_server_wide.as_mut_ptr());

            let mut option3 = INTERNET_PER_CONN_OPTIONW {
                dwOption: INTERNET_PER_CONN_PROXY_BYPASS,
                Value: std::mem::zeroed(),
            };
            *(&mut option3.Value as *mut _ as *mut PWSTR) = PWSTR(bypasses_wide.as_mut_ptr());

            let mut options = [option1, option2, option3];

            let mut list = INTERNET_PER_CONN_OPTION_LISTW {
                dwSize: std::mem::size_of::<INTERNET_PER_CONN_OPTION_LISTW>() as u32,
                pszConnection: PWSTR::null(),
                dwOptionCount: options.len() as u32,
                dwOptionError: 0,
                pOptions: options.as_mut_ptr(),
            };

            // 设置默认连接的代理
            let result = InternetSetOptionW(
                None,
                INTERNET_OPTION_PER_CONNECTION_OPTION,
                Some(&list as *const _ as *const _),
                std::mem::size_of::<INTERNET_PER_CONN_OPTION_LISTW>() as u32,
            );

            match result {
                Ok(_) => {}
                Err(_) => {
                    return ProxyResult::Error("设置默认连接代理失败".to_string());
                }
            }

            // 设置 RAS 连接
            set_ras_proxy(&mut list);

            // 通知系统刷新
            let _ = InternetSetOptionW(None, INTERNET_OPTION_SETTINGS_CHANGED, None, 0);
            let _ = InternetSetOptionW(None, INTERNET_OPTION_REFRESH, None, 0);

            log::info!("系统代理设置成功：{}", proxy_server);
            ProxyResult::Success
        }
    }

    // 使用 PAC 脚本配置系统代理。
    // 由 PAC 规则决定请求的代理策略。
    fn enable_proxy_pac(
        host: &str,
        port: u16,
        pac_script: &str,
        pac_file_path: &str,
    ) -> ProxyResult {
        unsafe {
            // 使用传入的 PAC 文件路径
            let pac_path = std::path::Path::new(pac_file_path);

            // 替换 PAC 脚本中的占位符
            let processed_script = pac_script
                .replace("${getProxyHost()}", host)
                .replace("${ClashDefaults.httpPort}", &port.to_string());

            // 写入 PAC 文件
            if let Err(e) = fs::write(pac_path, processed_script.as_bytes()) {
                return ProxyResult::Error(format!("无法写入 PAC 文件：{}", e));
            }

            // 构造 file:// URL
            let pac_url = format!(
                "file:///{}",
                pac_path.display().to_string().replace("\\", "/")
            );
            log::info!("PAC 文件路径：{}", pac_url);

            // 转换为 wide string
            let mut pac_url_wide: Vec<u16> = OsStr::new(&pac_url)
                .encode_wide()
                .chain(std::iter::once(0))
                .collect();

            // 构造选项数组 - 启用自动配置
            let mut option1 = INTERNET_PER_CONN_OPTIONW {
                dwOption: INTERNET_PER_CONN_FLAGS,
                Value: std::mem::zeroed(),
            };
            *(&mut option1.Value as *mut _ as *mut u32) =
                PROXY_TYPE_AUTO_PROXY_URL | PROXY_TYPE_DIRECT;

            let mut option2 = INTERNET_PER_CONN_OPTIONW {
                dwOption: INTERNET_PER_CONN_AUTOCONFIG_URL,
                Value: std::mem::zeroed(),
            };
            *(&mut option2.Value as *mut _ as *mut PWSTR) = PWSTR(pac_url_wide.as_mut_ptr());

            let mut options = [option1, option2];

            let mut list = INTERNET_PER_CONN_OPTION_LISTW {
                dwSize: std::mem::size_of::<INTERNET_PER_CONN_OPTION_LISTW>() as u32,
                pszConnection: PWSTR::null(),
                dwOptionCount: options.len() as u32,
                dwOptionError: 0,
                pOptions: options.as_mut_ptr(),
            };

            // 设置默认连接的 PAC 代理
            let result = InternetSetOptionW(
                None,
                INTERNET_OPTION_PER_CONNECTION_OPTION,
                Some(&list as *const _ as *const _),
                std::mem::size_of::<INTERNET_PER_CONN_OPTION_LISTW>() as u32,
            );

            match result {
                Ok(_) => {}
                Err(_) => {
                    return ProxyResult::Error("设置默认连接 PAC 代理失败".to_string());
                }
            }

            // 设置 RAS 连接
            set_ras_proxy(&mut list);

            // 通知系统刷新
            let _ = InternetSetOptionW(None, INTERNET_OPTION_SETTINGS_CHANGED, None, 0);
            let _ = InternetSetOptionW(None, INTERNET_OPTION_REFRESH, None, 0);

            log::info!("系统代理设置成功(PAC 模式)：{}", pac_url);
            ProxyResult::Success
        }
    }

    // 移除系统代理配置并恢复直连。
    pub async fn disable_proxy() -> ProxyResult {
        log::info!("正在禁用系统代理");

        unsafe {
            let mut option1 = INTERNET_PER_CONN_OPTIONW {
                dwOption: INTERNET_PER_CONN_FLAGS,
                Value: std::mem::zeroed(),
            };
            *(&mut option1.Value as *mut _ as *mut u32) = PROXY_TYPE_DIRECT;

            let mut options = [option1];

            let mut list = INTERNET_PER_CONN_OPTION_LISTW {
                dwSize: std::mem::size_of::<INTERNET_PER_CONN_OPTION_LISTW>() as u32,
                pszConnection: PWSTR::null(),
                dwOptionCount: options.len() as u32,
                dwOptionError: 0,
                pOptions: options.as_mut_ptr(),
            };

            let result = InternetSetOptionW(
                None,
                INTERNET_OPTION_PER_CONNECTION_OPTION,
                Some(&list as *const _ as *const _),
                std::mem::size_of::<INTERNET_PER_CONN_OPTION_LISTW>() as u32,
            );

            match result {
                Ok(_) => {}
                Err(_) => return ProxyResult::Error("禁用代理失败".to_string()),
            }

            // 禁用 RAS 连接的代理
            set_ras_proxy(&mut list);

            // 通知系统刷新
            let _ = InternetSetOptionW(None, INTERNET_OPTION_SETTINGS_CHANGED, None, 0);
            let _ = InternetSetOptionW(None, INTERNET_OPTION_REFRESH, None, 0);

            log::info!("系统代理已禁用");
            ProxyResult::Success
        }
    }

    // 同步 RAS 拨号连接的代理配置
    fn set_ras_proxy(list: &mut INTERNET_PER_CONN_OPTION_LISTW) {
        unsafe {
            let mut entry = RASENTRYNAMEW {
                dwSize: std::mem::size_of::<RASENTRYNAMEW>() as u32,
                ..Default::default()
            };

            let mut size = std::mem::size_of::<RASENTRYNAMEW>() as u32;
            let mut count = 0u32;

            // 第一次调用获取需要的缓冲区大小
            let result = RasEnumEntriesW(None, None, Some(&mut entry), &mut size, &mut count);

            // 检查是否需要更大的缓冲区
            if result != ERROR_SUCCESS.0 && count > 0 {
                let mut entries = vec![
                    RASENTRYNAMEW {
                        dwSize: std::mem::size_of::<RASENTRYNAMEW>() as u32,
                        ..Default::default()
                    };
                    count as usize
                ];

                let result = RasEnumEntriesW(
                    None,
                    None,
                    Some(entries.as_mut_ptr()),
                    &mut size,
                    &mut count,
                );

                if result == ERROR_SUCCESS.0 {
                    for entry in &mut entries {
                        list.pszConnection = PWSTR(entry.szEntryName.as_mut_ptr());
                        let _ = InternetSetOptionW(
                            None,
                            INTERNET_OPTION_PER_CONNECTION_OPTION,
                            Some(list as *const _ as *const _),
                            std::mem::size_of::<INTERNET_PER_CONN_OPTION_LISTW>() as u32,
                        );
                    }
                }
            }
        }
    }

    // 查询当前系统代理状态与服务器地址。
    pub async fn get_proxy_info() -> ProxyInfo {
        unsafe {
            // 准备查询选项
            let option_flags = INTERNET_PER_CONN_OPTIONW {
                dwOption: INTERNET_PER_CONN_FLAGS,
                Value: std::mem::zeroed(),
            };

            let option_server = INTERNET_PER_CONN_OPTIONW {
                dwOption: INTERNET_PER_CONN_PROXY_SERVER,
                Value: std::mem::zeroed(),
            };

            let mut options = [option_flags, option_server];

            let mut list = INTERNET_PER_CONN_OPTION_LISTW {
                dwSize: std::mem::size_of::<INTERNET_PER_CONN_OPTION_LISTW>() as u32,
                pszConnection: PWSTR::null(),
                dwOptionCount: options.len() as u32,
                dwOptionError: 0,
                pOptions: options.as_mut_ptr(),
            };

            let mut size = std::mem::size_of::<INTERNET_PER_CONN_OPTION_LISTW>() as u32;

            // 查询代理设置
            let result = InternetQueryOptionW(
                None,
                INTERNET_OPTION_PER_CONNECTION_OPTION,
                Some(&mut list as *mut _ as *mut _),
                &mut size,
            );

            match result {
                Ok(_) => {}
                Err(_) => {
                    log::warn!("查询系统代理设置失败");
                    return ProxyInfo {
                        is_enabled: false,
                        server: None,
                    };
                }
            }

            // 读取代理标志
            let flags = *(&options[0].Value as *const _ as *const u32);
            let is_proxy_enabled = (flags & PROXY_TYPE_PROXY) != 0;

            if !is_proxy_enabled {
                return ProxyInfo {
                    is_enabled: false,
                    server: None,
                };
            }

            // 读取代理服务器地址
            let server_ptr = *(&options[1].Value as *const _ as *const PWSTR);
            if server_ptr.is_null() {
                return ProxyInfo {
                    is_enabled: true,
                    server: None,
                };
            }

            // 转换为 Rust String
            let server_wide = {
                let mut len = 0;
                let mut ptr = server_ptr.0;
                while *ptr != 0 {
                    len += 1;
                    ptr = ptr.add(1);
                }
                std::slice::from_raw_parts(server_ptr.0, len)
            };

            let server_string = String::from_utf16_lossy(server_wide);

            log::info!("当前系统代理：{}", server_string);

            ProxyInfo {
                is_enabled: true,
                server: Some(server_string),
            }
        }
    }
}

// ==================== macOS 实现 ====================
// 使用 networksetup 命令行工具管理网络代理

#[cfg(target_os = "macos")]
mod macos_impl {
    use super::{ProxyInfo, ProxyResult};
    use std::process::Command;

    // 获取所有网络设备列表
    async fn get_network_devices() -> Result<Vec<String>, String> {
        let output = Command::new("/usr/sbin/networksetup")
            .arg("-listallnetworkservices")
            .output()
            .map_err(|e| format!("执行 networksetup 失败: {}", e))?;

        if !output.status.success() {
            return Err("获取网络设备列表失败".to_string());
        }

        let stdout = String::from_utf8_lossy(&output.stdout);
        let devices: Vec<String> = stdout
            .lines()
            .filter(|line| !line.is_empty() && !line.contains('*'))
            .map(|s| s.to_string())
            .collect();

        log::info!("找到 {} 个网络设备", devices.len());
        Ok(devices)
    }

    // 启用 macOS 系统代理
    pub async fn enable_proxy(
        host: &str,
        port: u16,
        bypass_domains: Vec<String>,
        _should_use_pac_mode: bool,
        _pac_script: &str,
        _pac_file_path: &str,
    ) -> ProxyResult {
        log::info!("正在设置 macOS 系统代理：{}:{}", host, port);

        let devices = match get_network_devices().await {
            Ok(d) if !d.is_empty() => d,
            Ok(_) => return ProxyResult::Error("未找到网络设备".to_string()),
            Err(e) => return ProxyResult::Error(e),
        };

        let port_str = port.to_string();

        for device in &devices {
            // 设置 HTTP 代理
            let _ = Command::new("/usr/sbin/networksetup")
                .args(["-setwebproxystate", device, "on"])
                .status();

            let _ = Command::new("/usr/sbin/networksetup")
                .args(["-setwebproxy", device, host, &port_str])
                .status();

            // 设置 HTTPS 代理
            let _ = Command::new("/usr/sbin/networksetup")
                .args(["-setsecurewebproxystate", device, "on"])
                .status();

            let _ = Command::new("/usr/sbin/networksetup")
                .args(["-setsecurewebproxy", device, host, &port_str])
                .status();

            // 设置 SOCKS 代理
            let _ = Command::new("/usr/sbin/networksetup")
                .args(["-setsocksfirewallproxystate", device, "on"])
                .status();

            let _ = Command::new("/usr/sbin/networksetup")
                .args(["-setsocksfirewallproxy", device, host, &port_str])
                .status();

            // 设置绕过域名
            if !bypass_domains.is_empty() {
                let mut args = vec!["-setproxybypassdomains", device];
                let bypass_refs: Vec<&str> = bypass_domains.iter().map(|s| s.as_str()).collect();
                args.extend(bypass_refs);

                let _ = Command::new("/usr/sbin/networksetup").args(&args).status();
            }
        }

        log::info!("macOS 系统代理设置成功");
        ProxyResult::Success
    }

    // 禁用 macOS 系统代理
    pub async fn disable_proxy() -> ProxyResult {
        log::info!("正在禁用 macOS 系统代理");

        let devices = match get_network_devices().await {
            Ok(d) if !d.is_empty() => d,
            Ok(_) => return ProxyResult::Error("未找到网络设备".to_string()),
            Err(e) => return ProxyResult::Error(e),
        };

        for device in &devices {
            // 禁用所有类型的代理
            let _ = Command::new("/usr/sbin/networksetup")
                .args(["-setautoproxystate", device, "off"])
                .status();

            let _ = Command::new("/usr/sbin/networksetup")
                .args(["-setwebproxystate", device, "off"])
                .status();

            let _ = Command::new("/usr/sbin/networksetup")
                .args(["-setsecurewebproxystate", device, "off"])
                .status();

            let _ = Command::new("/usr/sbin/networksetup")
                .args(["-setsocksfirewallproxystate", device, "off"])
                .status();

            let _ = Command::new("/usr/sbin/networksetup")
                .args(["-setproxybypassdomains", device, ""])
                .status();
        }

        log::info!("macOS 系统代理已禁用");
        ProxyResult::Success
    }

    // 获取 macOS 系统代理状态
    pub async fn get_proxy_info() -> ProxyInfo {
        log::info!("正在查询 macOS 系统代理状态");

        let devices = match get_network_devices().await {
            Ok(d) => d,
            Err(_) => {
                return ProxyInfo {
                    is_enabled: false,
                    server: None,
                };
            }
        };

        // 查询第一个启用代理的设备
        for device in &devices {
            let output = match Command::new("/usr/sbin/networksetup")
                .args(["-getwebproxy", device])
                .output()
            {
                Ok(o) => o,
                Err(_) => continue,
            };

            let stdout = String::from_utf8_lossy(&output.stdout);
            let mut enabled = false;
            let mut server = String::new();
            let mut port = String::new();

            for line in stdout.lines() {
                if line.starts_with("Enabled:") {
                    enabled = line.contains("Yes");
                } else if line.starts_with("Server:") {
                    server = line.split(':').nth(1).unwrap_or("").trim().to_string();
                } else if line.starts_with("Port:") {
                    port = line.split(':').nth(1).unwrap_or("").trim().to_string();
                }
            }

            if enabled && !server.is_empty() {
                let server_str = if port.is_empty() {
                    server
                } else {
                    format!("{}:{}", server, port)
                };

                log::info!("当前 macOS 系统代理：{}", server_str);
                return ProxyInfo {
                    is_enabled: true,
                    server: Some(server_str),
                };
            }
        }

        ProxyInfo {
            is_enabled: false,
            server: None,
        }
    }
}

// ==================== Linux 实现 ====================
// 支持 GNOME (gsettings) 和 KDE (kwriteconfig5)

#[cfg(target_os = "linux")]
mod linux_impl {
    use super::{ProxyInfo, ProxyResult};
    use std::ffi::OsStr;
    use std::process::{Command, Output};

    const GNOME_PROXY_SCHEMA: &str = "org.gnome.system.proxy";
    const PROXY_TYPES: [&str; 3] = ["http", "https", "socks"];
    const KWRITECONFIG_COMMANDS: [&str; 2] = ["kwriteconfig6", "kwriteconfig5"];
    const KREADCONFIG_COMMANDS: [&str; 2] = ["kreadconfig6", "kreadconfig5"];

    // 检测桌面环境类型
    fn detect_desktop_environment() -> String {
        std::env::var("XDG_CURRENT_DESKTOP").unwrap_or_default()
    }

    // 判断是否为 KDE 桌面
    fn is_kde() -> bool {
        detect_desktop_environment().to_uppercase().contains("KDE")
    }

    // 返回禁用状态的代理信息
    fn disabled_proxy_info() -> ProxyInfo {
        ProxyInfo {
            is_enabled: false,
            server: None,
        }
    }

    // 按顺序选择第一个可执行成功的命令
    fn find_working_command(candidates: &[&'static str], probe_arg: &str) -> Option<&'static str> {
        candidates.iter().copied().find(|candidate| {
            Command::new(candidate)
                .arg(probe_arg)
                .output()
                .is_ok_and(|output| output.status.success())
        })
    }

    // 选择可用的 KDE 写配置命令
    fn kwriteconfig_command() -> Option<&'static str> {
        find_working_command(&KWRITECONFIG_COMMANDS, "--help")
    }

    // 选择可用的 KDE 读配置命令
    fn kreadconfig_command() -> Option<&'static str> {
        find_working_command(&KREADCONFIG_COMMANDS, "--help")
    }

    // 转义 GVariant 字符串值
    fn quote_variant_string(value: &str) -> String {
        let escaped = value.replace('\\', "\\\\").replace('\'', "\\'");
        format!("'{}'", escaped)
    }

    // 构造 GVariant 字符串数组
    fn format_variant_string_list(values: &[String]) -> String {
        if values.is_empty() {
            return "[]".to_string();
        }

        let quoted_values = values
            .iter()
            .map(|value| quote_variant_string(value))
            .collect::<Vec<_>>()
            .join(", ");
        format!("[{}]", quoted_values)
    }

    // 统一执行命令并返回输出
    fn execute_command<I, S>(command: &str, args: I) -> Result<Output, String>
    where
        I: IntoIterator<Item = S>,
        S: AsRef<OsStr>,
    {
        let output = Command::new(command)
            .args(args)
            .output()
            .map_err(|e| format!("执行 {command} 失败：{e}"))?;

        if output.status.success() {
            return Ok(output);
        }

        let stderr = String::from_utf8_lossy(&output.stderr).trim().to_string();
        if stderr.is_empty() {
            return Err(format!(
                "{command} 执行失败，退出码：{:?}",
                output.status.code()
            ));
        }

        Err(format!("{command} 执行失败：{stderr}"))
    }

    // 执行命令并确保成功退出
    fn run_command<I, S>(command: &str, args: I) -> Result<(), String>
    where
        I: IntoIterator<Item = S>,
        S: AsRef<OsStr>,
    {
        execute_command(command, args).map(|_| ())
    }

    // 执行命令并读取标准输出
    fn read_command_output<I, S>(command: &str, args: I) -> Result<String, String>
    where
        I: IntoIterator<Item = S>,
        S: AsRef<OsStr>,
    {
        let output = execute_command(command, args)?;
        Ok(String::from_utf8_lossy(&output.stdout).trim().to_string())
    }

    // 读取 KDE 配置文件路径
    fn kioslaverc_path() -> Result<String, String> {
        let home_dir = std::env::var("HOME").map_err(|_| "无法获取 HOME 环境变量".to_string())?;
        Ok(format!("{}/.config/kioslaverc", home_dir))
    }

    // 启用 GNOME 系统代理
    fn enable_proxy_gsettings(
        host: &str,
        port: u16,
        bypass_domains: &[String],
    ) -> Result<(), String> {
        let mode = quote_variant_string("manual");
        run_command(
            "gsettings",
            ["set", GNOME_PROXY_SCHEMA, "mode", mode.as_str()],
        )?;

        let ignore_hosts = format_variant_string_list(bypass_domains);
        run_command(
            "gsettings",
            [
                "set",
                GNOME_PROXY_SCHEMA,
                "ignore-hosts",
                ignore_hosts.as_str(),
            ],
        )?;

        let quoted_host = quote_variant_string(host);
        let port_str = port.to_string();

        for proxy_type in PROXY_TYPES {
            let schema = format!("{GNOME_PROXY_SCHEMA}.{proxy_type}");
            run_command(
                "gsettings",
                ["set", schema.as_str(), "host", quoted_host.as_str()],
            )?;
            run_command(
                "gsettings",
                ["set", schema.as_str(), "port", port_str.as_str()],
            )?;
        }

        Ok(())
    }

    // 直接写入 dconf，兼容仅读取该后端的程序
    fn enable_proxy_dconf(host: &str, port: u16, bypass_domains: &[String]) -> Result<(), String> {
        let mode = quote_variant_string("manual");
        run_command("dconf", ["write", "/system/proxy/mode", mode.as_str()])?;

        let ignore_hosts = format_variant_string_list(bypass_domains);
        run_command(
            "dconf",
            ["write", "/system/proxy/ignore-hosts", ignore_hosts.as_str()],
        )?;

        let quoted_host = quote_variant_string(host);
        let port_str = port.to_string();

        for proxy_type in PROXY_TYPES {
            let host_path = format!("/system/proxy/{proxy_type}/host");
            let port_path = format!("/system/proxy/{proxy_type}/port");
            run_command("dconf", ["write", host_path.as_str(), quoted_host.as_str()])?;
            run_command("dconf", ["write", port_path.as_str(), port_str.as_str()])?;
        }

        Ok(())
    }

    // 启用 KDE 系统代理
    fn enable_proxy_kde(
        command: &str,
        host: &str,
        port: u16,
        bypass_domains: &[String],
    ) -> Result<(), String> {
        let config_file = kioslaverc_path()?;

        run_command(
            command,
            [
                "--file",
                config_file.as_str(),
                "--group",
                "Proxy Settings",
                "--key",
                "ProxyType",
                "1",
            ],
        )?;

        let bypasses = bypass_domains.join(",");
        run_command(
            command,
            [
                "--file",
                config_file.as_str(),
                "--group",
                "Proxy Settings",
                "--key",
                "NoProxyFor",
                bypasses.as_str(),
            ],
        )?;

        for proxy_type in PROXY_TYPES {
            let key = format!("{proxy_type}Proxy");
            let scheme = if proxy_type == "socks" {
                "socks"
            } else {
                "http"
            };
            let value = format!("{scheme}://{host} {port}");

            run_command(
                command,
                [
                    "--file",
                    config_file.as_str(),
                    "--group",
                    "Proxy Settings",
                    "--key",
                    key.as_str(),
                    value.as_str(),
                ],
            )?;
        }

        Ok(())
    }

    // 禁用 GNOME 系统代理
    fn disable_proxy_gsettings() -> Result<(), String> {
        let mode = quote_variant_string("none");
        run_command(
            "gsettings",
            ["set", GNOME_PROXY_SCHEMA, "mode", mode.as_str()],
        )
    }

    // 禁用 dconf 系统代理
    fn disable_proxy_dconf() -> Result<(), String> {
        let mode = quote_variant_string("none");
        run_command("dconf", ["write", "/system/proxy/mode", mode.as_str()])
    }

    // 禁用 KDE 系统代理
    fn disable_proxy_kde(command: &str) -> Result<(), String> {
        let config_file = kioslaverc_path()?;
        run_command(
            command,
            [
                "--file",
                config_file.as_str(),
                "--group",
                "Proxy Settings",
                "--key",
                "ProxyType",
                "0",
            ],
        )
    }

    // 解析 KDE 代理配置值
    fn parse_kde_proxy_server(proxy: &str) -> Option<String> {
        let proxy = proxy.trim();
        if proxy.is_empty() {
            return None;
        }

        let server = proxy
            .strip_prefix("http://")
            .or_else(|| proxy.strip_prefix("https://"))
            .or_else(|| proxy.strip_prefix("socks://"))
            .unwrap_or(proxy)
            .trim();

        let parts = server.split_whitespace().collect::<Vec<_>>();
        if parts.len() == 2 {
            return Some(format!("{}:{}", parts[0], parts[1]));
        }

        Some(server.to_string())
    }

    // 获取 GNOME 系统代理状态
    async fn get_proxy_info_gnome() -> ProxyInfo {
        let mode = match read_command_output("gsettings", ["get", GNOME_PROXY_SCHEMA, "mode"]) {
            Ok(mode) => mode,
            Err(_) => return disabled_proxy_info(),
        };

        if !mode.contains("manual") {
            return disabled_proxy_info();
        }

        let host = match read_command_output(
            "gsettings",
            ["get", "org.gnome.system.proxy.http", "host"],
        ) {
            Ok(host) => host.trim_matches('\'').to_string(),
            Err(_) => return disabled_proxy_info(),
        };

        let port = match read_command_output(
            "gsettings",
            ["get", "org.gnome.system.proxy.http", "port"],
        ) {
            Ok(port) => port,
            Err(_) => return disabled_proxy_info(),
        };

        if host.is_empty() {
            return disabled_proxy_info();
        }

        let server_str = format!("{}:{}", host, port);
        log::info!("当前 Linux GNOME 系统代理：{}", server_str);
        ProxyInfo {
            is_enabled: true,
            server: Some(server_str),
        }
    }

    // 获取 dconf 系统代理状态
    async fn get_proxy_info_dconf() -> ProxyInfo {
        let mode = match read_command_output("dconf", ["read", "/system/proxy/mode"]) {
            Ok(mode) => mode,
            Err(_) => return disabled_proxy_info(),
        };

        if !mode.contains("manual") {
            return disabled_proxy_info();
        }

        let host = match read_command_output("dconf", ["read", "/system/proxy/http/host"]) {
            Ok(host) => host.trim_matches('\'').to_string(),
            Err(_) => return disabled_proxy_info(),
        };

        let port = match read_command_output("dconf", ["read", "/system/proxy/http/port"]) {
            Ok(port) => port,
            Err(_) => return disabled_proxy_info(),
        };

        if host.is_empty() {
            return disabled_proxy_info();
        }

        let server_str = format!("{}:{}", host, port);
        log::info!("当前 Linux dconf 系统代理：{}", server_str);
        ProxyInfo {
            is_enabled: true,
            server: Some(server_str),
        }
    }

    // 获取 KDE 系统代理状态
    async fn get_proxy_info_kde() -> ProxyInfo {
        let Some(command) = kreadconfig_command() else {
            return disabled_proxy_info();
        };

        let config_file = match kioslaverc_path() {
            Ok(path) => path,
            Err(_) => return disabled_proxy_info(),
        };

        let proxy_type = match read_command_output(
            command,
            [
                "--file",
                config_file.as_str(),
                "--group",
                "Proxy Settings",
                "--key",
                "ProxyType",
            ],
        ) {
            Ok(proxy_type) => proxy_type,
            Err(_) => return disabled_proxy_info(),
        };

        if proxy_type != "1" {
            return disabled_proxy_info();
        }

        let proxy = match read_command_output(
            command,
            [
                "--file",
                config_file.as_str(),
                "--group",
                "Proxy Settings",
                "--key",
                "httpProxy",
            ],
        ) {
            Ok(proxy) => proxy,
            Err(_) => return disabled_proxy_info(),
        };

        let Some(server_str) = parse_kde_proxy_server(&proxy) else {
            return disabled_proxy_info();
        };

        log::info!("当前 Linux KDE 系统代理：{}", server_str);
        ProxyInfo {
            is_enabled: true,
            server: Some(server_str),
        }
    }

    // 聚合 Linux 代理后端执行结果
    fn collect_backend_result(
        backend_name: &'static str,
        result: Result<(), String>,
        applied_backends: &mut Vec<&'static str>,
        errors: &mut Vec<String>,
    ) {
        match result {
            Ok(()) => applied_backends.push(backend_name),
            Err(e) => errors.push(e),
        }
    }

    // 根据聚合结果构造 Linux 代理操作返回值
    fn build_proxy_result(
        action_label: &str,
        success_log: &str,
        applied_backends: Vec<&'static str>,
        errors: Vec<String>,
    ) -> ProxyResult {
        if applied_backends.is_empty() {
            if errors.is_empty() {
                return ProxyResult::Error("未找到可用的 Linux 系统代理配置命令".to_string());
            }
            return ProxyResult::Error(errors.join("；"));
        }

        if !errors.is_empty() {
            log::warn!(
                "部分 Linux 代理后端{}失败：{}",
                action_label,
                errors.join("；")
            );
        }

        log::info!(
            "{}，已应用后端：{}",
            success_log,
            applied_backends.join(", ")
        );
        ProxyResult::Success
    }

    // 启用 Linux 系统代理
    pub async fn enable_proxy(
        host: &str,
        port: u16,
        bypass_domains: Vec<String>,
        _should_use_pac_mode: bool,
        _pac_script: &str,
        _pac_file_path: &str,
    ) -> ProxyResult {
        log::info!("正在设置 Linux 系统代理：{}:{}", host, port);

        let mut applied_backends = Vec::new();
        let mut errors = Vec::new();

        if let Some(command) = kwriteconfig_command() {
            collect_backend_result(
                "KDE",
                enable_proxy_kde(command, host, port, &bypass_domains),
                &mut applied_backends,
                &mut errors,
            );
        }

        collect_backend_result(
            "gsettings",
            enable_proxy_gsettings(host, port, &bypass_domains),
            &mut applied_backends,
            &mut errors,
        );

        collect_backend_result(
            "dconf",
            enable_proxy_dconf(host, port, &bypass_domains),
            &mut applied_backends,
            &mut errors,
        );

        build_proxy_result("设置", "Linux 系统代理设置成功", applied_backends, errors)
    }

    // 禁用 Linux 系统代理
    pub async fn disable_proxy() -> ProxyResult {
        log::info!("正在禁用 Linux 系统代理");

        let mut applied_backends = Vec::new();
        let mut errors = Vec::new();

        if let Some(command) = kwriteconfig_command() {
            collect_backend_result(
                "KDE",
                disable_proxy_kde(command),
                &mut applied_backends,
                &mut errors,
            );
        }

        collect_backend_result(
            "gsettings",
            disable_proxy_gsettings(),
            &mut applied_backends,
            &mut errors,
        );

        collect_backend_result(
            "dconf",
            disable_proxy_dconf(),
            &mut applied_backends,
            &mut errors,
        );

        build_proxy_result("禁用", "Linux 系统代理已禁用", applied_backends, errors)
    }

    // 获取 Linux 系统代理状态
    pub async fn get_proxy_info() -> ProxyInfo {
        log::info!("正在查询 Linux 系统代理状态");

        let should_prefer_kde = is_kde();
        if should_prefer_kde {
            let kde_proxy_info = get_proxy_info_kde().await;
            if kde_proxy_info.is_enabled {
                return kde_proxy_info;
            }
        }

        let gnome_proxy_info = get_proxy_info_gnome().await;
        if gnome_proxy_info.is_enabled {
            return gnome_proxy_info;
        }

        let dconf_proxy_info = get_proxy_info_dconf().await;
        if dconf_proxy_info.is_enabled {
            return dconf_proxy_info;
        }

        if !should_prefer_kde {
            let kde_proxy_info = get_proxy_info_kde().await;
            if kde_proxy_info.is_enabled {
                return kde_proxy_info;
            }
        }

        disabled_proxy_info()
    }
}

// ==================== 平台导出 ====================

// Windows 导出
#[cfg(target_os = "windows")]
pub use windows_impl::{disable_proxy, enable_proxy, get_proxy_info};

// macOS 导出
#[cfg(target_os = "macos")]
pub use macos_impl::{disable_proxy, enable_proxy, get_proxy_info};

// Linux 导出
#[cfg(target_os = "linux")]
pub use linux_impl::{disable_proxy, enable_proxy, get_proxy_info};

// Android/其他平台 stub
#[cfg(not(any(target_os = "windows", target_os = "macos", target_os = "linux")))]
pub async fn enable_proxy(
    _host: &str,
    _port: u16,
    _bypass_domains: Vec<String>,
    _should_use_pac_mode: bool,
    _pac_script: &str,
    _pac_file_path: &str,
) -> ProxyResult {
    ProxyResult::Error("当前平台不支持系统代理设置".to_string())
}

#[cfg(not(any(target_os = "windows", target_os = "macos", target_os = "linux")))]
pub async fn disable_proxy() -> ProxyResult {
    ProxyResult::Error("当前平台不支持系统代理设置".to_string())
}

#[cfg(not(any(target_os = "windows", target_os = "macos", target_os = "linux")))]
pub async fn get_proxy_info() -> ProxyInfo {
    ProxyInfo {
        is_enabled: false,
        server: None,
    }
}

pub fn init() {
    spawn(async {
        let receiver = EnableSystemProxy::get_dart_signal_receiver();
        while let Some(dart_signal) = receiver.recv().await {
            dart_signal.message.handle().await;
        }
        log::info!("启用代理消息通道已关闭，退出监听器");
    });

    spawn(async {
        let receiver = DisableSystemProxy::get_dart_signal_receiver();
        while let Some(dart_signal) = receiver.recv().await {
            dart_signal.message.handle().await;
        }
        log::info!("禁用代理消息通道已关闭，退出监听器");
    });

    spawn(async {
        let receiver = GetSystemProxy::get_dart_signal_receiver();
        while let Some(dart_signal) = receiver.recv().await {
            dart_signal.message.handle().await;
        }
        log::info!("获取系统代理状态消息通道已关闭，退出监听器");
    });
}

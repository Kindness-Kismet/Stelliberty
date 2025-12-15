// 系统代理配置管理
//
// 目的：提供跨平台的系统级代理设置能力

use rinf::{DartSignal, RustSignal};
use serde::{Deserialize, Serialize};

// Dart → Rust：启用系统代理
#[derive(Deserialize, DartSignal)]
pub struct EnableSystemProxy {
    pub host: String,
    pub port: u16,
    pub bypass_domains: Vec<String>,
    pub use_pac_mode: bool,
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
    pub success: bool,
    pub error_message: Option<String>,
}

// Rust → Dart：系统代理状态信息
#[derive(Serialize, RustSignal)]
pub struct SystemProxyInfo {
    pub enabled: bool,
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
    pub enabled: bool,
    pub server: Option<String>,
}

impl EnableSystemProxy {
    // 执行启用代理操作
    //
    // 目的：配置系统级代理设置，使所有网络流量经过指定代理服务器
    pub async fn handle(self) {
        if self.use_pac_mode {
            log::info!("收到启用代理请求 (PAC 模式)");
        } else {
            log::info!("收到启用代理请求：{}:{}", self.host, self.port);
        }

        let result = enable_proxy(
            &self.host,
            self.port,
            self.bypass_domains,
            self.use_pac_mode,
            &self.pac_script,
            &self.pac_file_path,
        )
        .await;

        let response = match result {
            ProxyResult::Success => SystemProxyResult {
                success: true,
                error_message: None,
            },
            ProxyResult::Error(msg) => {
                log::error!("启用代理失败：{}", msg);
                SystemProxyResult {
                    success: false,
                    error_message: Some(msg),
                }
            }
        };

        response.send_signal_to_dart();
    }
}

impl DisableSystemProxy {
    // 执行禁用代理操作
    //
    // 目的：移除系统代理配置，恢复直连网络访问
    pub async fn handle(&self) {
        log::info!("收到禁用代理请求");

        let result = disable_proxy().await;

        let response = match result {
            ProxyResult::Success => SystemProxyResult {
                success: true,
                error_message: None,
            },
            ProxyResult::Error(msg) => {
                log::error!("禁用代理失败：{}", msg);
                SystemProxyResult {
                    success: false,
                    error_message: Some(msg),
                }
            }
        };

        response.send_signal_to_dart();
    }
}

impl GetSystemProxy {
    // 查询当前系统代理状态
    //
    // 目的：获取系统代理的启用状态和配置信息
    pub async fn handle(&self) {
        log::info!("收到获取系统代理状态请求");

        let proxy_info = get_proxy_info().await;

        let response = SystemProxyInfo {
            enabled: proxy_info.enabled,
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

    // 配置并启用系统代理
    //
    // 目的：将系统网络流量重定向到指定的代理服务器或使用 PAC 脚本
    pub async fn enable_proxy(
        host: &str,
        port: u16,
        bypass_domains: Vec<String>,
        use_pac_mode: bool,
        pac_script: &str,
        pac_file_path: &str,
    ) -> ProxyResult {
        if use_pac_mode {
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

            let bypass_list = bypass_domains.join(";");
            let mut bypass_list_wide: Vec<u16> = OsStr::new(&bypass_list)
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
            *(&mut option3.Value as *mut _ as *mut PWSTR) = PWSTR(bypass_list_wide.as_mut_ptr());

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

    // 使用 PAC 脚本配置系统代理
    //
    // 目的：通过 PAC 脚本自动决定流量走向
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

    // 移除系统代理配置
    //
    // 目的：恢复系统直连网络访问
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

    // 查询当前系统代理配置
    //
    // 目的：读取系统代理的启用状态和服务器地址
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
                        enabled: false,
                        server: None,
                    };
                }
            }

            // 读取代理标志
            let flags = *(&options[0].Value as *const _ as *const u32);
            let is_proxy_enabled = (flags & PROXY_TYPE_PROXY) != 0;

            if !is_proxy_enabled {
                return ProxyInfo {
                    enabled: false,
                    server: None,
                };
            }

            // 读取代理服务器地址
            let server_ptr = *(&options[1].Value as *const _ as *const PWSTR);
            if server_ptr.is_null() {
                return ProxyInfo {
                    enabled: true,
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
                enabled: true,
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
        _use_pac_mode: bool,
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
                    enabled: false,
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
                    enabled: true,
                    server: Some(server_str),
                };
            }
        }

        ProxyInfo {
            enabled: false,
            server: None,
        }
    }
}

// ==================== Linux 实现 ====================
// 支持 GNOME (gsettings) 和 KDE (kwriteconfig5)

#[cfg(target_os = "linux")]
mod linux_impl {
    use super::{ProxyInfo, ProxyResult};
    use std::process::Command;

    // 检测桌面环境类型
    fn detect_desktop_environment() -> String {
        std::env::var("XDG_CURRENT_DESKTOP").unwrap_or_default()
    }

    // 判断是否为 KDE 桌面
    fn is_kde() -> bool {
        detect_desktop_environment().to_uppercase().contains("KDE")
    }

    // 启用 Linux 系统代理
    pub async fn enable_proxy(
        host: &str,
        port: u16,
        bypass_domains: Vec<String>,
        _use_pac_mode: bool,
        _pac_script: &str,
        _pac_file_path: &str,
    ) -> ProxyResult {
        log::info!("正在设置 Linux 系统代理：{}:{}", host, port);

        if is_kde() {
            enable_proxy_kde(host, port, bypass_domains).await
        } else {
            enable_proxy_gnome(host, port, bypass_domains).await
        }
    }

    // 启用 GNOME 系统代理 (gsettings)
    async fn enable_proxy_gnome(host: &str, port: u16, bypass_domains: Vec<String>) -> ProxyResult {
        // 设置代理模式为手动
        let result = Command::new("gsettings")
            .args(["set", "org.gnome.system.proxy", "mode", "manual"])
            .status();

        match result {
            Ok(_) => {}
            Err(_) => return ProxyResult::Error("设置 GNOME 代理模式失败".to_string()),
        }

        // 设置忽略的主机列表
        let ignore_hosts = format!("['{}']", bypass_domains.join("', '"));
        let _ = Command::new("gsettings")
            .args([
                "set",
                "org.gnome.system.proxy",
                "ignore-hosts",
                &ignore_hosts,
            ])
            .status();

        let port_str = port.to_string();

        // 为 HTTP、HTTPS、SOCKS 设置代理
        for proxy_type in &["http", "https", "socks"] {
            let schema = format!("org.gnome.system.proxy.{}", proxy_type);

            let _ = Command::new("gsettings")
                .args(["set", &schema, "host", host])
                .status();

            let _ = Command::new("gsettings")
                .args(["set", &schema, "port", &port_str])
                .status();
        }

        log::info!("Linux GNOME 系统代理设置成功");
        ProxyResult::Success
    }

    // 启用 KDE 系统代理 (kwriteconfig5)
    async fn enable_proxy_kde(host: &str, port: u16, bypass_domains: Vec<String>) -> ProxyResult {
        let home_dir = match std::env::var("HOME") {
            Ok(h) => h,
            Err(_) => return ProxyResult::Error("无法获取 HOME 环境变量".to_string()),
        };

        let config_file = format!("{}/.config/kioslaverc", home_dir);

        // 设置代理类型为手动 (1)
        let _ = Command::new("kwriteconfig5")
            .args([
                "--file",
                &config_file,
                "--group",
                "Proxy Settings",
                "--key",
                "ProxyType",
                "1",
            ])
            .status();

        // 设置绕过域名
        let bypass_list = bypass_domains.join(",");
        let _ = Command::new("kwriteconfig5")
            .args([
                "--file",
                &config_file,
                "--group",
                "Proxy Settings",
                "--key",
                "NoProxyFor",
                &bypass_list,
            ])
            .status();

        // 为 HTTP、HTTPS、SOCKS 设置代理
        for proxy_type in &["http", "https", "socks"] {
            let key = format!("{}Proxy", proxy_type);
            let value = format!("{}://{}:{}", proxy_type, host, port);

            let _ = Command::new("kwriteconfig5")
                .args([
                    "--file",
                    &config_file,
                    "--group",
                    "Proxy Settings",
                    "--key",
                    &key,
                    &value,
                ])
                .status();
        }

        log::info!("Linux KDE 系统代理设置成功");
        ProxyResult::Success
    }

    // 禁用 Linux 系统代理
    pub async fn disable_proxy() -> ProxyResult {
        log::info!("正在禁用 Linux 系统代理");

        if is_kde() {
            disable_proxy_kde().await
        } else {
            disable_proxy_gnome().await
        }
    }

    // 禁用 GNOME 系统代理
    async fn disable_proxy_gnome() -> ProxyResult {
        let result = Command::new("gsettings")
            .args(["set", "org.gnome.system.proxy", "mode", "none"])
            .status();

        match result {
            Ok(_) => {}
            Err(_) => return ProxyResult::Error("禁用 GNOME 代理失败".to_string()),
        }

        log::info!("Linux GNOME 系统代理已禁用");
        ProxyResult::Success
    }

    // 禁用 KDE 系统代理
    async fn disable_proxy_kde() -> ProxyResult {
        let home_dir = match std::env::var("HOME") {
            Ok(h) => h,
            Err(_) => return ProxyResult::Error("无法获取 HOME 环境变量".to_string()),
        };

        let config_file = format!("{}/.config/kioslaverc", home_dir);

        // 设置代理类型为无代理 (0)
        let result = Command::new("kwriteconfig5")
            .args([
                "--file",
                &config_file,
                "--group",
                "Proxy Settings",
                "--key",
                "ProxyType",
                "0",
            ])
            .status();

        match result {
            Ok(_) => {}
            Err(_) => return ProxyResult::Error("禁用 KDE 代理失败".to_string()),
        }

        log::info!("Linux KDE 系统代理已禁用");
        ProxyResult::Success
    }

    // 获取 Linux 系统代理状态
    pub async fn get_proxy_info() -> ProxyInfo {
        log::info!("正在查询 Linux 系统代理状态");

        if is_kde() {
            get_proxy_info_kde().await
        } else {
            get_proxy_info_gnome().await
        }
    }

    // 获取 GNOME 系统代理状态
    async fn get_proxy_info_gnome() -> ProxyInfo {
        // 查询代理模式
        let mode_output = Command::new("gsettings")
            .args(["get", "org.gnome.system.proxy", "mode"])
            .output();

        let mode = match mode_output {
            Ok(o) => String::from_utf8_lossy(&o.stdout).trim().to_string(),
            Err(_) => {
                return ProxyInfo {
                    enabled: false,
                    server: None,
                };
            }
        };

        if !mode.contains("manual") {
            return ProxyInfo {
                enabled: false,
                server: None,
            };
        }

        // 查询 HTTP 代理
        let host_output = Command::new("gsettings")
            .args(["get", "org.gnome.system.proxy.http", "host"])
            .output();

        let port_output = Command::new("gsettings")
            .args(["get", "org.gnome.system.proxy.http", "port"])
            .output();

        match (host_output, port_output) {
            (Ok(h), Ok(p)) => {
                let host = String::from_utf8_lossy(&h.stdout)
                    .trim()
                    .trim_matches('\'')
                    .to_string();
                let port = String::from_utf8_lossy(&p.stdout).trim().to_string();

                if !host.is_empty() && host != "''" {
                    let server_str = format!("{}:{}", host, port);
                    log::info!("当前 Linux GNOME 系统代理：{}", server_str);
                    return ProxyInfo {
                        enabled: true,
                        server: Some(server_str),
                    };
                }

                ProxyInfo {
                    enabled: false,
                    server: None,
                }
            }
            _ => ProxyInfo {
                enabled: false,
                server: None,
            },
        }
    }

    // 获取 KDE 系统代理状态
    async fn get_proxy_info_kde() -> ProxyInfo {
        let home_dir = match std::env::var("HOME") {
            Ok(h) => h,
            Err(_) => {
                return ProxyInfo {
                    enabled: false,
                    server: None,
                };
            }
        };

        let config_file = format!("{}/.config/kioslaverc", home_dir);

        // 查询代理类型
        let type_output = Command::new("kreadconfig5")
            .args([
                "--file",
                &config_file,
                "--group",
                "Proxy Settings",
                "--key",
                "ProxyType",
            ])
            .output();

        let proxy_type = match type_output {
            Ok(o) => String::from_utf8_lossy(&o.stdout).trim().to_string(),
            Err(_) => {
                return ProxyInfo {
                    enabled: false,
                    server: None,
                };
            }
        };

        // 1 = 手动代理
        if proxy_type != "1" {
            return ProxyInfo {
                enabled: false,
                server: None,
            };
        }

        // 查询 HTTP 代理
        let http_output = Command::new("kreadconfig5")
            .args([
                "--file",
                &config_file,
                "--group",
                "Proxy Settings",
                "--key",
                "httpProxy",
            ])
            .output();

        match http_output {
            Ok(o) => {
                let proxy = String::from_utf8_lossy(&o.stdout).trim().to_string();

                if !proxy.is_empty() {
                    // 格式：http://host:port
                    let server_str = proxy.trim_start_matches("http://").to_string();
                    log::info!("当前 Linux KDE 系统代理：{}", server_str);
                    return ProxyInfo {
                        enabled: true,
                        server: Some(server_str),
                    };
                }

                ProxyInfo {
                    enabled: false,
                    server: None,
                }
            }
            Err(_) => ProxyInfo {
                enabled: false,
                server: None,
            },
        }
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
    _use_pac_mode: bool,
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
        enabled: false,
        server: None,
    }
}

// 统一的服务安装/卸载/管理（Windows Service / Linux systemd）

use anyhow::{Context, Result, bail};

const SERVICE_NAME: &str = "StellibertyService";

// ============ Windows Service 实现 ============

#[cfg(windows)]
use std::ffi::OsString;
#[cfg(windows)]
use std::time::Duration;
#[cfg(windows)]
use windows_service::{
    service::{
        ServiceAccess, ServiceErrorControl, ServiceInfo, ServiceStartType, ServiceState,
        ServiceType,
    },
    service_manager::{ServiceManager, ServiceManagerAccess},
};

#[cfg(windows)]
const SERVICE_DISPLAY_NAME: &str = "Stelliberty Service";
#[cfg(windows)]
const SERVICE_DESCRIPTION: &str = "Stelliberty 后台服务，用于管理 Clash 核心和提供系统级 TUN 支持";

#[cfg(windows)]
pub fn install_service() -> Result<()> {
    println!("正在安装 Stelliberty Service...");

    let service_binary = std::env::current_exe().context("无法获取当前程序路径")?;
    println!("服务程序: {}", service_binary.display());

    let manager = ServiceManager::local_computer(
        None::<&str>,
        ServiceManagerAccess::CONNECT | ServiceManagerAccess::CREATE_SERVICE,
    )
    .context("无法连接到服务管理器。请确保以管理员身份运行。")?;

    if let Ok(service) = manager.open_service(SERVICE_NAME, ServiceAccess::QUERY_STATUS) {
        let status = service.query_status()?;
        match status.current_state {
            ServiceState::Running => {
                println!("服务已在运行中");
                return Ok(());
            }
            ServiceState::Stopped => {
                println!("服务已安装但未运行，正在启动...");
                return start_service();
            }
            _ => {
                println!("服务处于 {:?} 状态", status.current_state);
            }
        }
    }

    let service_info = ServiceInfo {
        name: OsString::from(SERVICE_NAME),
        display_name: OsString::from(SERVICE_DISPLAY_NAME),
        service_type: ServiceType::OWN_PROCESS,
        start_type: ServiceStartType::AutoStart,
        error_control: ServiceErrorControl::Normal,
        executable_path: service_binary,
        launch_arguments: vec![],
        dependencies: vec![],
        account_name: None,
        account_password: None,
    };

    let service = manager
        .create_service(
            &service_info,
            ServiceAccess::CHANGE_CONFIG | ServiceAccess::START | ServiceAccess::QUERY_STATUS,
        )
        .context("创建服务失败。请确保以管理员身份运行。")?;

    service
        .set_description(SERVICE_DESCRIPTION)
        .context("设置服务描述失败")?;

    println!("服务创建成功");
    println!("正在启动服务...");

    match service.start(&[] as &[&OsString]) {
        Ok(_) => {}
        Err(e) => {
            println!("警告: {e}, 正在检查服务状态...");
        }
    }

    std::thread::sleep(std::time::Duration::from_millis(500));

    let mut retry = 0;
    loop {
        let status = service.query_status()?;
        match status.current_state {
            ServiceState::Running => {
                println!("服务启动成功 ({SERVICE_NAME})");
                break;
            }
            ServiceState::StartPending => {
                if retry >= 30 {
                    bail!("服务启动超时");
                }
                if retry == 0 {
                    print!("等待启动");
                }
                print!(".");
                std::io::Write::flush(&mut std::io::stdout()).ok();
                std::thread::sleep(Duration::from_millis(500));
                retry += 1;
            }
            other => {
                println!();
                bail!("服务启动失败: {other:?}");
            }
        }
    }
    Ok(())
}

#[cfg(windows)]
pub fn uninstall_service() -> Result<()> {
    println!("正在卸载 Stelliberty Service...");

    let manager = ServiceManager::local_computer(None::<&str>, ServiceManagerAccess::CONNECT)
        .context("无法连接到服务管理器。请确保以管理员身份运行。")?;

    let service = manager
        .open_service(
            SERVICE_NAME,
            ServiceAccess::QUERY_STATUS | ServiceAccess::STOP | ServiceAccess::DELETE,
        )
        .context("无法打开服务。服务可能未安装。")?;

    let status = service.query_status()?;

    if status.current_state != ServiceState::Stopped {
        println!("正在停止服务...");

        match service.stop() {
            Ok(_) => {}
            Err(e) => {
                println!("警告: {e}, 正在检查服务状态...");
            }
        }

        std::thread::sleep(std::time::Duration::from_millis(100));

        let mut retry = 0;
        loop {
            match service.query_status() {
                Ok(status) => match status.current_state {
                    ServiceState::Stopped => {
                        println!("服务已停止");
                        break;
                    }
                    ServiceState::StopPending => {
                        if retry >= 30 {
                            bail!("服务停止超时");
                        }
                        if retry == 0 {
                            print!("等待停止");
                        }
                        print!(".");
                        std::io::Write::flush(&mut std::io::stdout()).ok();
                        std::thread::sleep(Duration::from_millis(100));
                        retry += 1;
                    }
                    other => {
                        if retry >= 30 {
                            println!();
                            bail!("服务停止失败: {other:?}");
                        }
                        std::thread::sleep(Duration::from_millis(100));
                        retry += 1;
                    }
                },
                Err(e) => {
                    println!("警告: {e}, 假定服务已停止");
                    break;
                }
            }
        }
    }

    println!("正在删除服务...");
    service.delete().context("删除服务失败")?;
    println!("服务卸载成功");

    Ok(())
}

#[cfg(windows)]
pub fn start_service() -> Result<()> {
    println!("正在启动 Stelliberty Service...");

    let manager = ServiceManager::local_computer(None::<&str>, ServiceManagerAccess::CONNECT)
        .context("无法连接到服务管理器")?;

    let service = manager
        .open_service(
            SERVICE_NAME,
            ServiceAccess::QUERY_STATUS | ServiceAccess::START,
        )
        .context("无法打开服务。请先安装服务。")?;

    let status = service.query_status()?;
    if status.current_state == ServiceState::Running {
        println!("服务已在运行中");
        return Ok(());
    }

    service.start(&[] as &[&OsString]).context("启动服务失败")?;
    println!("服务启动成功");

    Ok(())
}

#[cfg(windows)]
pub fn stop_service() -> Result<()> {
    println!("正在停止 Stelliberty Service...");

    let manager = ServiceManager::local_computer(None::<&str>, ServiceManagerAccess::CONNECT)
        .context("无法连接到服务管理器")?;

    let service = manager
        .open_service(
            SERVICE_NAME,
            ServiceAccess::QUERY_STATUS | ServiceAccess::STOP,
        )
        .context("无法打开服务。服务可能未安装。")?;

    let status = service.query_status()?;
    if status.current_state == ServiceState::Stopped {
        println!("服务已处于停止状态");
        return Ok(());
    }

    service.stop().context("停止服务失败")?;
    println!("服务停止成功");

    Ok(())
}

// ============ Linux systemd 实现 ============

#[cfg(target_os = "linux")]
use std::fs;
#[cfg(target_os = "linux")]
use std::path::Path;
#[cfg(target_os = "linux")]
use std::process::Command;

#[cfg(target_os = "linux")]
const SERVICE_FILE: &str = "/etc/systemd/system/StellibertyService.service";

#[cfg(target_os = "linux")]
fn get_service_unit(binary_path: &str) -> String {
    format!(
        r#"[Unit]
Description=Stelliberty Service
After=network.target

[Service]
Type=simple
ExecStart={binary_path}
Restart=on-failure
RestartSec=5s
StandardOutput=journal
StandardError=journal
SyslogIdentifier=stelliberty

[Install]
WantedBy=multi-user.target
"#
    )
}

#[cfg(target_os = "linux")]
pub fn install_service() -> Result<()> {
    println!("正在安装 Stelliberty Service (systemd)...");

    let service_binary = std::env::current_exe().context("无法获取当前程序路径")?;
    println!("服务程序: {}", service_binary.display());

    if Path::new(SERVICE_FILE).exists() {
        println!("服务文件已存在，正在检查状态...");

        let status = Command::new("systemctl")
            .args(["is-active", SERVICE_NAME])
            .output();

        if let Ok(output) = status {
            let status_str = String::from_utf8_lossy(&output.stdout).trim().to_string();
            if status_str == "active" {
                println!("服务已在运行中");
                return Ok(());
            } else if status_str == "inactive" {
                println!("服务已安装但未运行，正在启动...");
                return start_service();
            }
        }
    }

    let unit_content = get_service_unit(&service_binary.display().to_string());
    fs::write(SERVICE_FILE, unit_content)
        .context("创建 systemd unit 文件失败，请确保以 root 身份运行")?;

    println!("服务文件创建成功: {}", SERVICE_FILE);
    println!("正在重载 systemd...");

    let reload_status = Command::new("systemctl")
        .arg("daemon-reload")
        .status()
        .context("执行 systemctl daemon-reload 失败")?;

    if !reload_status.success() {
        bail!("systemctl daemon-reload 失败");
    }

    println!("正在启用服务（开机自启）...");
    let enable_status = Command::new("systemctl")
        .args(["enable", SERVICE_NAME])
        .status()
        .context("执行 systemctl enable 失败")?;

    if !enable_status.success() {
        bail!("启用服务失败");
    }

    println!("正在启动服务...");
    let start_status = Command::new("systemctl")
        .args(["start", SERVICE_NAME])
        .status()
        .context("执行 systemctl start 失败")?;

    if !start_status.success() {
        bail!("启动服务失败");
    }

    std::thread::sleep(std::time::Duration::from_millis(500));

    let status = Command::new("systemctl")
        .args(["is-active", SERVICE_NAME])
        .output()
        .context("检查服务状态失败")?;

    let status_str = String::from_utf8_lossy(&status.stdout).trim().to_string();
    if status_str == "active" {
        println!("服务启动成功 ({})", SERVICE_NAME);
        println!();
        println!("可以使用以下命令管理服务:");
        println!("sudo systemctl status {}  - 查看状态", SERVICE_NAME);
        println!("sudo systemctl stop {}    - 停止服务", SERVICE_NAME);
        println!("sudo systemctl restart {} - 重启服务", SERVICE_NAME);
        println!("sudo journalctl -u {} -f  - 查看日志", SERVICE_NAME);
    } else {
        bail!("服务启动失败，状态: {}", status_str);
    }

    Ok(())
}

#[cfg(target_os = "linux")]
pub fn uninstall_service() -> Result<()> {
    println!("正在卸载 Stelliberty Service (systemd)...");

    if !Path::new(SERVICE_FILE).exists() {
        println!("服务未安装");
        return Ok(());
    }

    let status = Command::new("systemctl")
        .args(["is-active", SERVICE_NAME])
        .output();

    if let Ok(output) = status {
        let status_str = String::from_utf8_lossy(&output.stdout).trim().to_string();
        if status_str == "active" {
            println!("正在停止服务...");
            let stop_status = Command::new("systemctl")
                .args(["stop", SERVICE_NAME])
                .status()
                .context("停止服务失败")?;

            if !stop_status.success() {
                bail!("停止服务失败");
            }
            println!("服务已停止");
        }
    }

    println!("正在禁用服务...");
    let disable_status = Command::new("systemctl")
        .args(["disable", SERVICE_NAME])
        .status();

    if let Err(e) = disable_status {
        println!("警告: 禁用服务失败: {}", e);
    }

    println!("正在删除服务文件...");
    fs::remove_file(SERVICE_FILE).context("删除服务文件失败")?;

    println!("正在重载 systemd...");
    let reload_status = Command::new("systemctl")
        .arg("daemon-reload")
        .status()
        .context("执行 systemctl daemon-reload 失败")?;

    if !reload_status.success() {
        bail!("systemctl daemon-reload 失败");
    }

    println!("服务卸载成功");
    Ok(())
}

#[cfg(target_os = "linux")]
pub fn start_service() -> Result<()> {
    println!("正在启动 Stelliberty Service...");

    if !Path::new(SERVICE_FILE).exists() {
        bail!(
            "服务未安装，请先运行: sudo {} install",
            std::env::current_exe()?.display()
        );
    }

    let status = Command::new("systemctl")
        .args(["is-active", SERVICE_NAME])
        .output()
        .context("检查服务状态失败")?;

    let status_str = String::from_utf8_lossy(&status.stdout).trim().to_string();
    if status_str == "active" {
        println!("服务已在运行中");
        return Ok(());
    }

    let start_status = Command::new("systemctl")
        .args(["start", SERVICE_NAME])
        .status()
        .context("启动服务失败")?;

    if !start_status.success() {
        bail!("启动服务失败");
    }

    println!("服务启动成功");
    Ok(())
}

#[cfg(target_os = "linux")]
pub fn stop_service() -> Result<()> {
    println!("正在停止 Stelliberty Service...");

    if !Path::new(SERVICE_FILE).exists() {
        bail!("服务未安装");
    }

    let status = Command::new("systemctl")
        .args(["is-active", SERVICE_NAME])
        .output()
        .context("检查服务状态失败")?;

    let status_str = String::from_utf8_lossy(&status.stdout).trim().to_string();
    if status_str == "inactive" {
        println!("服务已处于停止状态");
        return Ok(());
    }

    let stop_status = Command::new("systemctl")
        .args(["stop", SERVICE_NAME])
        .status()
        .context("停止服务失败")?;

    if !stop_status.success() {
        bail!("停止服务失败");
    }

    println!("服务停止成功");
    Ok(())
}

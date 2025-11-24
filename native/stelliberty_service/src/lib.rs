// Stelliberty Service Library
//
// 后台服务程序，负责以管理员权限运行 Clash 核心

pub mod clash;
pub mod ipc;
pub mod logger;
pub mod service;

use anyhow::Result;
use std::sync::Arc;
use tokio::sync::RwLock;

// 检查是否有足够的权限运行
pub fn check_privileges() -> bool {
    #[cfg(windows)]
    {
        use windows::Win32::UI::Shell::IsUserAnAdmin;
        unsafe { IsUserAnAdmin().as_bool() }
    }

    #[cfg(not(windows))]
    {
        unsafe { libc::geteuid() == 0 }
    }
}

// 打印权限不足的错误信息
pub fn print_privilege_error() {
    // 缓存 binary_path 避免重复系统调用
    let binary_path = std::env::current_exe().ok();

    eprintln!("========================================");
    #[cfg(windows)]
    eprintln!("错误: 需要管理员权限才能运行");
    #[cfg(not(windows))]
    eprintln!("错误: 需要 root 权限才能运行");
    eprintln!("========================================");
    eprintln!();

    // 打印程序路径
    if let Some(ref path) = binary_path {
        eprintln!("程序路径: {}", path.display());
        eprintln!();
    }

    eprintln!("可用命令:");
    eprintln!("install      - 安装并启动服务");
    eprintln!("uninstall    - 停止并卸载服务");
    eprintln!("start        - 启动服务");
    eprintln!("stop         - 停止服务");
    eprintln!();

    #[cfg(windows)]
    {
        eprintln!("请以管理员身份运行:");
        if let Some(ref path) = binary_path {
            eprintln!("{} install", path.display());
        } else {
            eprintln!("stelliberty-service.exe install");
        }
    }

    #[cfg(not(windows))]
    {
        eprintln!("请使用 sudo 运行:");
        if let Some(ref path) = binary_path {
            eprintln!("sudo {} install", path.display());
        } else {
            eprintln!("sudo stelliberty-service install");
        }
    }
}

// 打印使用说明
pub fn print_usage() {
    println!("Stelliberty Service");
    println!();
    println!("用法：");
    println!("stelliberty-service              - 运行服务");
    println!("stelliberty-service install      - 安装并启动服务");
    println!("stelliberty-service uninstall    - 停止并卸载服务");
    println!("stelliberty-service start        - 启动服务");
    println!("stelliberty-service stop         - 停止服务");
    println!("stelliberty-service status       - 查看服务状态");
    println!("stelliberty-service logs [行数]  - 查看指定行数日志");
    println!("stelliberty-service logs         - 实时监控服务日志（按 q 退出）");
    println!();
    #[cfg(windows)]
    println!("注意：安装和卸载需要管理员权限");
    #[cfg(not(windows))]
    println!("注意：安装和卸载需要 root 权限");
}

// 控制台模式运行（用于调试）
pub async fn run_console_mode() -> Result<()> {
    log::info!("以控制台模式运行服务");

    // 创建 Clash 管理器
    let clash_manager = Arc::new(RwLock::new(clash::ClashManager::new()));

    // 创建 IPC 服务端
    let handler = service::handler::create_handler(clash_manager.clone());
    let mut ipc_server = ipc::IpcServer::new(handler);

    // 运行 IPC 服务端
    ipc_server.run().await?;

    log::info!("服务停止");
    Ok(())
}

// 处理命令行参数
pub fn handle_command(args: &[String]) -> Result<Option<()>> {
    if args.len() <= 1 {
        return Ok(None); // 无命令，继续运行服务
    }

    match args[1].as_str() {
        "install" => {
            service::install_service()?;
            Ok(Some(()))
        }
        "uninstall" => {
            service::uninstall_service()?;
            Ok(Some(()))
        }
        "start" => {
            service::start_service()?;
            Ok(Some(()))
        }
        "stop" => {
            service::stop_service()?;
            Ok(Some(()))
        }
        "status" => {
            tokio::runtime::Runtime::new()?.block_on(async { show_status().await })?;
            Ok(Some(()))
        }
        "logs" => {
            if args.len() > 2 {
                // 有参数：显示指定行数的日志
                let lines = match args[2].parse::<usize>() {
                    Ok(n) => n,
                    Err(_) => {
                        eprintln!("警告: 无效的行数参数 '{}', 使用默认值 1000", args[2]);
                        1000
                    }
                };
                tokio::runtime::Runtime::new()?.block_on(async { show_logs(lines).await })?;
            } else {
                // 无参数：实时监控日志
                tokio::runtime::Runtime::new()?.block_on(async { follow_logs().await })?;
            }
            Ok(Some(()))
        }
        _ => {
            println!("未知命令: {}", args[1]);
            print_usage();
            Ok(Some(()))
        }
    }
}

// 显示服务状态
async fn show_status() -> Result<()> {
    use ipc::IpcClient;
    use ipc::protocol::{IpcCommand, IpcResponse};

    println!("正在连接服务...");

    let client = IpcClient::default();

    match client.send_command(IpcCommand::GetStatus).await {
        Ok(IpcResponse::Status {
            clash_running,
            clash_pid,
            service_uptime,
        }) => {
            println!("\n========== Stelliberty Service 状态 ==========");
            println!("服务运行时间: {} 秒", service_uptime);
            println!(
                "Clash 状态: {}",
                if clash_running {
                    "运行中"
                } else {
                    "已停止"
                }
            );
            if let Some(pid) = clash_pid {
                println!("Clash PID: {}", pid);
            }
            println!("===========================================\n");
            Ok(())
        }
        Ok(resp) => {
            anyhow::bail!("收到意外响应: {:?}", resp);
        }
        Err(e) => {
            eprintln!("错误: 无法连接到服务");
            eprintln!("详情: {}", e);
            eprintln!("\n提示: 请确认服务是否正在运行");
            Err(e.into())
        }
    }
}

// 显示服务日志
async fn show_logs(lines: usize) -> Result<()> {
    use ipc::IpcClient;
    use ipc::protocol::{IpcCommand, IpcResponse};

    println!("正在获取日志...");

    let client = IpcClient::default();

    match client.send_command(IpcCommand::GetLogs { lines }).await {
        Ok(IpcResponse::Logs { lines: log_lines }) => {
            println!(
                "\n========== 服务日志 (共 {} 行) ==========",
                log_lines.len()
            );
            for line in log_lines {
                println!("{}", line);
            }
            println!("===========================================\n");
            Ok(())
        }
        Ok(resp) => {
            anyhow::bail!("收到意外响应: {:?}", resp);
        }
        Err(e) => {
            eprintln!("错误: 无法连接到服务");
            eprintln!("详情: {}", e);
            eprintln!("\n提示: 请确认服务是否正在运行");
            Err(e.into())
        }
    }
}

// 实时监控服务日志
async fn follow_logs() -> Result<()> {
    use crossterm::{
        ExecutableCommand,
        event::{self, Event, KeyCode},
        terminal::{self, ClearType},
    };
    use ipc::IpcClient;
    use ipc::protocol::{IpcCommand, IpcResponse};
    use std::io::{Write, stdout};
    use std::time::Duration;

    println!("正在连接服务...");

    let client = IpcClient::default();

    // 先获取历史日志
    match client.send_command(IpcCommand::GetLogs { lines: 50 }).await {
        Ok(IpcResponse::Logs { lines: log_lines }) => {
            println!("========== 历史日志 (最近 50 行) ==========");
            for line in log_lines {
                println!("{}", line);
            }
        }
        Ok(resp) => {
            anyhow::bail!("收到意外响应: {:?}", resp);
        }
        Err(e) => {
            eprintln!("错误: 无法连接到服务");
            eprintln!("详情: {}", e);
            eprintln!("\n提示: 请确认服务是否正在运行");
            return Err(e.into());
        }
    }

    // 启用原始模式以检测按键
    terminal::enable_raw_mode()?;

    let result = async {
        // 记录最后一次看到的日志数量
        let mut last_log_count = 0;

        // 获取当前日志总数
        if let Ok(IpcResponse::Logs {
            lines: initial_logs,
        }) = client
            .send_command(IpcCommand::GetLogs { lines: 1000 })
            .await
        {
            last_log_count = initial_logs.len();
        }

        // 打印分隔线和提示信息（只在顶部显示一次）
        println!("===========================================");
        println!("实时监控中... (按 q 退出)\n");
        stdout().flush()?;

        // 持续轮询日志
        loop {
            // 检查是否按下 q 键
            if event::poll(Duration::from_millis(100))?
                && let Event::Key(key_event) = event::read()?
                && (key_event.code == KeyCode::Char('q') || key_event.code == KeyCode::Char('Q'))
            {
                // 清除最后两行（提示信息）
                stdout().execute(crossterm::cursor::MoveUp(2))?;
                stdout().execute(terminal::Clear(ClearType::FromCursorDown))?;
                println!("退出日志监控");
                break;
            }

            // 获取所有日志并检查是否有新日志
            if let Ok(IpcResponse::Logs { lines: all_logs }) = client
                .send_command(IpcCommand::GetLogs { lines: 1000 })
                .await
            {
                let current_count = all_logs.len();

                // 如果有新日志，打印新增的部分
                if current_count > last_log_count {
                    // 清除提示信息
                    stdout().execute(crossterm::cursor::MoveUp(2))?;
                    stdout().execute(terminal::Clear(ClearType::FromCursorDown))?;

                    // 打印新日志（不带分隔线）
                    for line in all_logs.iter().skip(last_log_count) {
                        println!("{}", line);
                    }

                    // 重新打印提示信息（不带分隔线）
                    println!("实时监控中... (按 q 退出)\n");
                    stdout().flush()?;

                    last_log_count = current_count;
                }
            }

            // 避免过于频繁的轮询
            tokio::time::sleep(Duration::from_millis(200)).await;
        }
        Ok(())
    }
    .await;

    // 恢复终端状态
    terminal::disable_raw_mode()?;

    result
}

// 服务主入口
pub async fn run() -> Result<()> {
    log::info!("Stelliberty Service v{} 启动", env!("CARGO_PKG_VERSION"));

    // Windows 平台作为 Windows Service 运行
    #[cfg(windows)]
    {
        if let Ok(()) = service::run_as_service() {
            return Ok(());
        }
        log::info!("非 Windows Service 模式，以控制台模式运行");
        run_console_mode().await
    }

    // Linux 平台作为 systemd service 运行
    #[cfg(target_os = "linux")]
    {
        service::run_service().await
    }

    // macOS 或其他平台以控制台模式运行
    #[cfg(not(any(windows, target_os = "linux")))]
    {
        run_console_mode().await
    }
}

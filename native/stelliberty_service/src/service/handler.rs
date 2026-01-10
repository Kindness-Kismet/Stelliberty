// IPC 命令处理器

use crate::clash::ClashManager;
use crate::ipc::{IpcCommand, IpcResponse};
use std::sync::Arc;
use std::time::Instant;
use tokio::sync::RwLock;

// 创建命令处理器（异步）
pub fn create_handler(
    clash_manager: Arc<RwLock<ClashManager>>,
    last_heartbeat: Arc<RwLock<Instant>>,
) -> impl Fn(IpcCommand) -> std::pin::Pin<Box<dyn std::future::Future<Output = IpcResponse> + Send>>
+ Send
+ Sync {
    move |command: IpcCommand| {
        let clash_manager = clash_manager.clone();
        let last_heartbeat = last_heartbeat.clone();

        Box::pin(async move {
            match command {
                IpcCommand::StartClash {
                    core_path,
                    config_path,
                    data_dir,
                    external_controller,
                } => {
                    log::info!("收到启动 Clash 命令");

                    let mut manager = clash_manager.write().await;
                    match manager.start(core_path, config_path, data_dir, external_controller) {
                        Ok(()) => {
                            log::info!("Clash 启动成功");
                            IpcResponse::Success {
                                message: Some("Clash 启动成功".to_string()),
                            }
                        }
                        Err(e) => {
                            log::error!("Clash 启动失败: {}", e);
                            IpcResponse::Error {
                                code: 1001,
                                message: format!("Clash 启动失败: {}", e),
                            }
                        }
                    }
                }

                IpcCommand::StopClash => {
                    log::info!("收到停止 Clash 命令");
                    let mut manager = clash_manager.write().await;
                    match manager.stop() {
                        Ok(()) => {
                            log::info!("Clash 停止成功");
                            IpcResponse::Success {
                                message: Some("Clash 停止成功".to_string()),
                            }
                        }
                        Err(e) => {
                            log::error!("Clash 停止失败: {}", e);
                            IpcResponse::Error {
                                code: 1002,
                                message: format!("Clash 停止失败: {}", e),
                            }
                        }
                    }
                }

                IpcCommand::GetStatus => {
                    log::debug!("收到查询状态命令");
                    // 使用读锁，不阻塞其他读操作
                    let manager = clash_manager.read().await;
                    let status = manager.get_status();
                    log::debug!(
                        "Clash 状态: running={}, pid={:?}, uptime={}s",
                        status.is_running,
                        status.pid,
                        status.uptime
                    );
                    IpcResponse::Status {
                        is_clash_running: status.is_running,
                        clash_pid: status.pid,
                        service_uptime: status.uptime,
                    }
                }

                IpcCommand::GetLogs { lines } => {
                    log::trace!("收到获取日志命令 (请求 {} 行)", lines);
                    let log_lines = crate::logger::get_recent_logs(lines);
                    IpcResponse::Logs { lines: log_lines }
                }

                IpcCommand::GetVersion => {
                    let version = env!("CARGO_PKG_VERSION");
                    log::debug!("收到获取版本命令, 版本: {}", version);
                    IpcResponse::Version {
                        version: version.to_string(),
                    }
                }

                IpcCommand::StreamLogs => {
                    log::debug!("收到日志流订阅命令");
                    // 返回成功，客户端将持续轮询获取新日志
                    IpcResponse::Success {
                        message: Some("日志流已启用".to_string()),
                    }
                }

                IpcCommand::Heartbeat => {
                    log::debug!("收到主程序心跳");
                    *last_heartbeat.write().await = Instant::now();
                    IpcResponse::HeartbeatAck
                }
            }
        })
    }
}

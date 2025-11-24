// Clash 直接进程管理
//
// 负责启动、停止和管理 Clash 核心进程

use super::messages::{ClashProcessResult, StartClashProcess, StopClashProcess};
use once_cell::sync::Lazy;
use rinf::RustSignal;
use std::sync::Mutex;

// 全局进程管理器
static PROCESS_MANAGER: Lazy<Mutex<Option<ClashProcess>>> = Lazy::new(|| Mutex::new(None));

// Clash 进程封装
struct ClashProcess {
    #[cfg(unix)]
    child: std::process::Child,
    #[cfg(windows)]
    process_handle: winapi::um::winnt::HANDLE,
    #[cfg(windows)]
    job_handle: winapi::um::winnt::HANDLE,
    #[cfg(windows)]
    pid: u32,
}

#[cfg(windows)]
unsafe impl Send for ClashProcess {}

impl ClashProcess {
    // 启动新的 Clash 进程
    fn start(executable_path: String, args: Vec<String>) -> Result<Self, String> {
        log::info!("启动 Clash 进程：{}", executable_path);
        log::info!("参数：{:?}", args);

        #[cfg(unix)]
        {
            use std::process::{Command, Stdio};

            let child = Command::new(&executable_path)
                .args(&args)
                .stdout(Stdio::null())
                .stderr(Stdio::null())
                .spawn()
                .map_err(|e| format!("启动进程失败：{}", e))?;

            Ok(ClashProcess { child })
        }

        #[cfg(windows)]
        {
            use std::ffi::OsStr;
            use std::os::windows::ffi::OsStrExt;
            use std::ptr;
            use winapi::shared::minwindef::FALSE;
            use winapi::um::handleapi::CloseHandle;
            use winapi::um::jobapi2::{
                AssignProcessToJobObject, CreateJobObjectW, SetInformationJobObject,
            };
            use winapi::um::processthreadsapi::{
                CreateProcessW, PROCESS_INFORMATION, ResumeThread, STARTUPINFOW, TerminateProcess,
            };
            use winapi::um::winbase::{CREATE_NO_WINDOW, CREATE_SUSPENDED, STARTF_USESHOWWINDOW};
            use winapi::um::winnt::{
                JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE, JOBOBJECT_EXTENDED_LIMIT_INFORMATION,
            };
            use winapi::um::winuser::SW_HIDE;

            unsafe {
                // 构建命令行
                let mut command_line = format!("\"{}\"", executable_path);
                for arg in &args {
                    command_line.push(' ');
                    if arg.contains(' ') {
                        command_line.push_str(&format!("\"{}\"", arg));
                    } else {
                        command_line.push_str(arg);
                    }
                }

                let mut command_line_wide: Vec<u16> = OsStr::new(&command_line)
                    .encode_wide()
                    .chain(std::iter::once(0))
                    .collect();

                // 创建 Job Object（确保子进程跟随父进程终止）
                let job_handle = CreateJobObjectW(ptr::null_mut(), ptr::null());
                if job_handle.is_null() {
                    return Err("创建 Job Object 失败".to_string());
                }

                let mut job_info: JOBOBJECT_EXTENDED_LIMIT_INFORMATION = std::mem::zeroed();
                job_info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;

                if SetInformationJobObject(
                    job_handle,
                    winapi::um::winnt::JobObjectExtendedLimitInformation,
                    &mut job_info as *mut _ as *mut _,
                    std::mem::size_of::<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>() as u32,
                ) == FALSE
                {
                    CloseHandle(job_handle);
                    return Err("设置 Job Object 信息失败".to_string());
                }

                // 配置启动信息（隐藏窗口）
                let mut startup_info: STARTUPINFOW = std::mem::zeroed();
                startup_info.cb = std::mem::size_of::<STARTUPINFOW>() as u32;
                startup_info.dwFlags = STARTF_USESHOWWINDOW;
                startup_info.wShowWindow = SW_HIDE as u16;

                let mut process_info: PROCESS_INFORMATION = std::mem::zeroed();

                // 创建进程（挂起状态）
                if CreateProcessW(
                    ptr::null(),
                    command_line_wide.as_mut_ptr(),
                    ptr::null_mut(),
                    ptr::null_mut(),
                    FALSE,
                    CREATE_NO_WINDOW | CREATE_SUSPENDED,
                    ptr::null_mut(),
                    ptr::null(),
                    &mut startup_info,
                    &mut process_info,
                ) == FALSE
                {
                    CloseHandle(job_handle);
                    return Err("创建进程失败".to_string());
                }

                // 将进程分配到 Job Object
                if AssignProcessToJobObject(job_handle, process_info.hProcess) == FALSE {
                    TerminateProcess(process_info.hProcess, 1);
                    CloseHandle(process_info.hProcess);
                    CloseHandle(process_info.hThread);
                    CloseHandle(job_handle);
                    return Err("分配进程到 Job Object 失败".to_string());
                }

                // 恢复进程运行
                if ResumeThread(process_info.hThread) == u32::MAX {
                    TerminateProcess(process_info.hProcess, 1);
                    CloseHandle(process_info.hProcess);
                    CloseHandle(process_info.hThread);
                    CloseHandle(job_handle);
                    return Err("恢复进程线程失败".to_string());
                }

                let pid = process_info.dwProcessId;
                CloseHandle(process_info.hThread);

                Ok(ClashProcess {
                    process_handle: process_info.hProcess,
                    job_handle,
                    pid,
                })
            }
        }
    }

    // 获取进程 PID
    fn pid(&self) -> u32 {
        #[cfg(unix)]
        {
            self.child.id()
        }
        #[cfg(windows)]
        {
            self.pid
        }
    }

    // 停止进程 - Unix 实现
    #[cfg(unix)]
    fn stop(mut self) -> Result<(), String> {
        let pid = self.pid();
        log::info!("正在停止 Clash 进程，PID：{}", pid);

        use nix::sys::signal::{Signal, kill};
        use nix::unistd::Pid;

        // 发送 SIGTERM 信号
        let nix_pid = Pid::from_raw(pid as i32);
        if let Err(e) = kill(nix_pid, Signal::SIGTERM) {
            log::error!("发送 SIGTERM 失败：{}", e);
        }

        // 等待进程退出
        match self.child.wait() {
            Ok(status) => {
                log::info!("进程已退出，状态：{:?}", status);
                Ok(())
            }
            Err(e) => {
                log::error!("等待进程退出失败：{}", e);
                Err(format!("等待进程退出失败：{}", e))
            }
        }
    }

    // 停止进程 - Windows 实现
    #[cfg(windows)]
    fn stop(self) -> Result<(), String> {
        let pid = self.pid();
        log::info!("正在停止 Clash 进程，PID：{}", pid);

        use std::time::Duration;
        use winapi::um::handleapi::CloseHandle;
        use winapi::um::synchapi::WaitForSingleObject;
        use winapi::um::winbase::WAIT_OBJECT_0;

        unsafe {
            // 关闭 Job Object 触发子进程自动终止
            CloseHandle(self.job_handle);

            // 等待进程退出（最多 5 秒）
            let timeout_ms = Duration::from_secs(5).as_millis() as u32;
            let wait_result = WaitForSingleObject(self.process_handle, timeout_ms);

            match wait_result {
                WAIT_OBJECT_0 => {
                    log::info!("进程已安全退出");
                    CloseHandle(self.process_handle);
                    Ok(())
                }
                _ => {
                    log::warn!("进程在 5 秒后仍未退出");
                    CloseHandle(self.process_handle);
                    Ok(())
                }
            }
        }
    }
}

// 处理启动 Clash 进程的请求
impl StartClashProcess {
    pub fn handle(&self) {
        log::info!("收到启动 Clash 进程请求");

        let mut manager = PROCESS_MANAGER.lock().unwrap_or_else(|e| {
            log::error!("获取进程管理器锁失败：{}", e);
            e.into_inner()
        });

        // 检查是否已有进程在运行
        if manager.is_some() {
            log::warn!("Clash 进程已在运行");
            ClashProcessResult {
                success: false,
                error_message: Some("进程已在运行".to_string()),
                pid: None,
            }
            .send_signal_to_dart();
            return;
        }

        // 启动新进程
        match ClashProcess::start(self.executable_path.clone(), self.args.clone()) {
            Ok(process) => {
                let pid = process.pid();
                *manager = Some(process);

                log::info!("Clash 进程启动成功，PID：{}", pid);
                ClashProcessResult {
                    success: true,
                    error_message: None,
                    pid: Some(pid),
                }
                .send_signal_to_dart();
            }
            Err(e) => {
                log::error!("启动 Clash 进程失败：{}", e);
                ClashProcessResult {
                    success: false,
                    error_message: Some(e),
                    pid: None,
                }
                .send_signal_to_dart();
            }
        }
    }
}

// 处理停止 Clash 进程的请求
impl StopClashProcess {
    pub fn handle(&self) {
        log::info!("收到停止 Clash 进程请求");

        let mut manager = PROCESS_MANAGER.lock().unwrap_or_else(|e| {
            log::error!("获取进程管理器锁失败：{}", e);
            e.into_inner()
        });

        match manager.take() {
            Some(process) => match process.stop() {
                Ok(()) => {
                    log::info!("Clash 进程已停止");
                    ClashProcessResult {
                        success: true,
                        error_message: None,
                        pid: None,
                    }
                    .send_signal_to_dart();
                }
                Err(e) => {
                    log::error!("停止 Clash 进程失败：{}", e);
                    ClashProcessResult {
                        success: false,
                        error_message: Some(e),
                        pid: None,
                    }
                    .send_signal_to_dart();
                }
            },
            None => {
                log::warn!("没有运行中的 Clash 进程");
                ClashProcessResult {
                    success: true,
                    error_message: None,
                    pid: None,
                }
                .send_signal_to_dart();
            }
        }
    }
}

// 清理资源（应用退出时调用）
pub fn cleanup() {
    log::info!("清理 Clash 进程管理器…");

    let mut manager = PROCESS_MANAGER.lock().unwrap_or_else(|e| {
        log::error!("获取进程管理器锁失败：{}", e);
        e.into_inner()
    });

    if let Some(process) = manager.take() {
        log::info!("发现运行中的 Clash 进程，正在清理…");
        if let Err(e) = process.stop() {
            log::error!("清理 Clash 进程失败：{}", e);
        }
    }
}

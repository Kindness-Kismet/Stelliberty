use std::future::Future;
use std::path::Path;
use std::pin::Pin;
use std::time::Duration;

use anyhow::Result;

// 子进程句柄因平台而异；Windows 使用 Job，Unix 使用 tokio 子进程。
#[derive(Debug)]
pub struct ChildHandle {
    pub pid: u32,
    #[cfg(windows)]
    pub(crate) inner: windows_impl::WindowsChild,
    #[cfg(unix)]
    pub(crate) inner: unix_impl::UnixChild,
}

pub fn spawn(binary: &Path, yaml_path: &Path, data_core_dir: &Path) -> Result<ChildHandle> {
    #[cfg(windows)]
    {
        windows_impl::spawn(binary, yaml_path, data_core_dir)
    }
    #[cfg(unix)]
    {
        unix_impl::spawn(binary, yaml_path, data_core_dir)
    }
}

pub async fn shutdown(child: ChildHandle, timeout: Duration) -> Result<()> {
    #[cfg(windows)]
    {
        windows_impl::shutdown(child, timeout).await
    }
    #[cfg(unix)]
    {
        unix_impl::shutdown(child, timeout).await
    }
}

pub fn exit_waiter(child: &ChildHandle) -> Result<Pin<Box<dyn Future<Output = u32> + Send>>> {
    #[cfg(windows)]
    {
        windows_impl::exit_waiter(child)
    }
    #[cfg(unix)]
    {
        unix_impl::exit_waiter(child)
    }
}

#[cfg(windows)]
mod windows_impl {
    use super::*;
    use anyhow::{Context, anyhow};
    use std::ffi::OsStr;
    use std::os::windows::ffi::OsStrExt;
    use std::sync::Mutex;
    use windows::Win32::Foundation::{
        CloseHandle, DUPLICATE_SAME_ACCESS, DuplicateHandle, HANDLE, WAIT_FAILED, WAIT_OBJECT_0,
        WAIT_TIMEOUT,
    };
    use windows::Win32::System::JobObjects::{
        AssignProcessToJobObject, CreateJobObjectW, JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE,
        JOBOBJECT_EXTENDED_LIMIT_INFORMATION, JobObjectExtendedLimitInformation,
        SetInformationJobObject,
    };
    use windows::Win32::System::Threading::{
        CREATE_NO_WINDOW, CREATE_SUSPENDED, CreateProcessW, GetCurrentProcess, GetExitCodeProcess,
        PROCESS_INFORMATION, ResumeThread, STARTF_USESHOWWINDOW, STARTUPINFOW, TerminateProcess,
        WaitForSingleObject,
    };
    use windows::Win32::UI::WindowsAndMessaging::SW_HIDE;
    use windows::core::{PCWSTR, PWSTR};

    // HANDLE 不是 Send；存成整数，只在 Win32 调用点还原。
    #[derive(Debug)]
    pub(crate) struct WindowsChild {
        process: Mutex<Option<isize>>,
        job: Mutex<Option<isize>>,
    }

    fn handle_to_isize(h: HANDLE) -> isize {
        h.0 as isize
    }

    fn isize_to_handle(v: isize) -> HANDLE {
        HANDLE(v as *mut std::ffi::c_void)
    }

    fn to_wide(s: &OsStr) -> Vec<u16> {
        s.encode_wide().chain(std::iter::once(0)).collect()
    }

    // Windows 命令行由子进程自行拆分，必须匹配 CommandLineToArgvW 规则。
    // 引号或参数结尾前的反斜杠需要成倍写入，避免路径被改写。
    fn quote_arg(arg: &str) -> String {
        if !arg.is_empty() && !arg.contains([' ', '\t', '"']) {
            return arg.to_string();
        }
        let mut result = String::with_capacity(arg.len() + 2);
        result.push('"');
        let mut backslashes = 0usize;
        for ch in arg.chars() {
            match ch {
                '\\' => backslashes += 1,
                '"' => {
                    result.push_str(&"\\".repeat(backslashes * 2 + 1));
                    result.push('"');
                    backslashes = 0;
                }
                _ => {
                    result.push_str(&"\\".repeat(backslashes));
                    result.push(ch);
                    backslashes = 0;
                }
            }
        }
        result.push_str(&"\\".repeat(backslashes * 2));
        result.push('"');
        result
    }

    pub fn spawn(binary: &Path, yaml_path: &Path, data_core_dir: &Path) -> Result<ChildHandle> {
        // 先挂起进程并加入 Job，再恢复运行。
        // 任一步失败都能清理句柄，避免留下孤儿核心进程。
        let binary_str = binary.to_string_lossy();
        let yaml_str = yaml_path.to_string_lossy();
        let data_str = data_core_dir.to_string_lossy();
        let command_line = format!(
            "{binary} -f {yaml} -d {data}",
            binary = quote_arg(&binary_str),
            yaml = quote_arg(&yaml_str),
            data = quote_arg(&data_str),
        );
        let mut cmd_wide: Vec<u16> = to_wide(OsStr::new(&command_line));
        let cmd_ptr = PWSTR(cmd_wide.as_mut_ptr());

        // 安全前提：创建匿名 Job Object 无外部输入，失败立即转为 Err。
        let job_handle = unsafe { CreateJobObjectW(None, PCWSTR::null()) }
            .map_err(|e| anyhow!("Failed to create Job Object: {e}"))?;

        // 安全前提：zeroed 只用于 C ABI POD 结构，字段会设置或零值有效。
        let mut job_info: JOBOBJECT_EXTENDED_LIMIT_INFORMATION = unsafe { std::mem::zeroed() };
        job_info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;

        // 安全前提：指针和大小匹配 job_info；调用只读取且不持有。
        let info_set = unsafe {
            SetInformationJobObject(
                job_handle,
                JobObjectExtendedLimitInformation,
                &job_info as *const _ as *const _,
                std::mem::size_of::<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>() as u32,
            )
        };
        if info_set.is_err() {
            unsafe {
                let _ = CloseHandle(job_handle);
            }
            return Err(anyhow!("Failed to set Job Object information"));
        }

        let mut startup_info: STARTUPINFOW = unsafe { std::mem::zeroed() };
        startup_info.cb = std::mem::size_of::<STARTUPINFOW>() as u32;
        startup_info.dwFlags = STARTF_USESHOWWINDOW;
        startup_info.wShowWindow = SW_HIDE.0 as u16;

        let mut process_info: PROCESS_INFORMATION = unsafe { std::mem::zeroed() };

        // 安全前提：cmd_ptr 指向存活的可写 UTF-16 缓冲区。
        // CreateProcessW 可能就地改写命令行，但不会在返回后持有指针。
        let created = unsafe {
            CreateProcessW(
                PCWSTR::null(),
                Some(cmd_ptr),
                None,
                None,
                false,
                CREATE_NO_WINDOW | CREATE_SUSPENDED,
                None,
                PCWSTR::null(),
                &startup_info,
                &mut process_info,
            )
        };
        if created.is_err() {
            unsafe {
                let _ = CloseHandle(job_handle);
            }
            return Err(anyhow!("Failed to create process"));
        }

        // 安全前提：进程仍处于挂起状态，失败路径会终止并关闭所有句柄。
        if unsafe { AssignProcessToJobObject(job_handle, process_info.hProcess) }.is_err() {
            unsafe {
                let _ = TerminateProcess(process_info.hProcess, 1);
                let _ = CloseHandle(process_info.hProcess);
                let _ = CloseHandle(process_info.hThread);
                let _ = CloseHandle(job_handle);
            }
            return Err(anyhow!("Failed to assign process to Job Object"));
        }

        if unsafe { ResumeThread(process_info.hThread) } == u32::MAX {
            unsafe {
                let _ = TerminateProcess(process_info.hProcess, 1);
                let _ = CloseHandle(process_info.hProcess);
                let _ = CloseHandle(process_info.hThread);
                let _ = CloseHandle(job_handle);
            }
            return Err(anyhow!("Failed to resume the process thread"));
        }

        unsafe {
            let _ = CloseHandle(process_info.hThread);
        }

        Ok(ChildHandle {
            pid: process_info.dwProcessId,
            inner: WindowsChild {
                process: Mutex::new(Some(handle_to_isize(process_info.hProcess))),
                job: Mutex::new(Some(handle_to_isize(job_handle))),
            },
        })
    }

    pub fn exit_waiter(child: &ChildHandle) -> Result<Pin<Box<dyn Future<Output = u32> + Send>>> {
        let proc = child
            .inner
            .process
            .lock()
            .map_err(|_| anyhow!("process lock is poisoned"))?
            .ok_or_else(|| anyhow!("Child process handle has been released"))?;
        let mut dup = HANDLE::default();
        // 安全前提：原进程句柄仍由本模块持有，副本只供后台等待使用。
        unsafe {
            DuplicateHandle(
                GetCurrentProcess(),
                isize_to_handle(proc),
                GetCurrentProcess(),
                &mut dup,
                0,
                false,
                DUPLICATE_SAME_ACCESS,
            )
        }
        .map_err(|e| anyhow!("Failed to duplicate process handle: {e}"))?;
        let dup = handle_to_isize(dup);
        Ok(Box::pin(async move {
            tokio::task::spawn_blocking(move || unsafe {
                // 安全前提：dup 是 DuplicateHandle 返回的有效进程句柄副本。
                // 0xFFFFFFFF 是 INFINITE，等待后读取退出码并关闭副本。
                let _ = WaitForSingleObject(isize_to_handle(dup), 0xFFFF_FFFF);
                let mut code: u32 = 0;
                let _ = GetExitCodeProcess(isize_to_handle(dup), &mut code);
                let _ = CloseHandle(isize_to_handle(dup));
                code
            })
            .await
            .unwrap_or(u32::MAX)
        }))
    }

    pub async fn shutdown(child: ChildHandle, timeout: Duration) -> Result<()> {
        let inner = child.inner;
        tokio::task::spawn_blocking(move || -> Result<()> {
            let job = inner
                .job
                .lock()
                .map_err(|_| anyhow!("job lock is poisoned"))?
                .take();
            let proc = inner
                .process
                .lock()
                .map_err(|_| anyhow!("process lock is poisoned"))?
                .take();

            // 安全前提：整数句柄只来自本模块保存的有效 Win32 句柄。
            unsafe {
                let mut failure = None;
                if let Some(j) = job {
                    if let Err(error) = CloseHandle(isize_to_handle(j)) {
                        failure = Some(anyhow!("Failed to close core Job Object: {error}"));
                    }
                }
                if let Some(p) = proc {
                    let wait = WaitForSingleObject(isize_to_handle(p), timeout.as_millis() as u32);
                    if wait == WAIT_TIMEOUT {
                        failure
                            .get_or_insert_with(|| anyhow!("Timed out waiting for core to exit"));
                    } else if wait == WAIT_FAILED {
                        failure.get_or_insert_with(|| {
                            anyhow!(
                                "Failed to wait for core to exit: {}",
                                windows::core::Error::from_thread()
                            )
                        });
                    } else if wait != WAIT_OBJECT_0 {
                        failure.get_or_insert_with(|| {
                            anyhow!("Unexpected core wait result: {}", wait.0)
                        });
                    }
                    if let Err(error) = CloseHandle(isize_to_handle(p)) {
                        failure.get_or_insert_with(|| {
                            anyhow!("Failed to close core process handle: {error}")
                        });
                    }
                }
                if let Some(error) = failure {
                    return Err(error);
                }
            }
            Ok(())
        })
        .await
        .context("shutdown spawn_blocking failed")??;
        Ok(())
    }
}

#[cfg(unix)]
mod unix_impl {
    use super::*;
    use anyhow::{Context, anyhow};
    use std::process::Stdio;
    use std::sync::Arc;
    use tokio::process::Command;
    use tokio::sync::{Notify, watch};

    #[derive(Debug)]
    pub(crate) struct UnixChild {
        kill: Arc<Notify>,
        exited: watch::Receiver<Option<UnixExit>>,
    }

    #[derive(Debug, Clone)]
    struct UnixExit {
        code: u32,
        error: Option<String>,
    }

    pub fn spawn(binary: &Path, yaml_path: &Path, data_core_dir: &Path) -> Result<ChildHandle> {
        let mut child = Command::new(binary)
            .arg("-f")
            .arg(yaml_path)
            .arg("-d")
            .arg(data_core_dir)
            .stdout(Stdio::null())
            .stderr(Stdio::null())
            .spawn()
            .context("Failed to start core")?;
        let pid = child
            .id()
            .ok_or_else(|| anyhow!("Unable to get core PID"))?;
        let kill = Arc::new(Notify::new());
        let (exited_tx, exited_rx) = watch::channel(None);
        let kill_listener = kill.clone();
        // 回收任务独占 child，确保自然退出和主动终止只广播一次。
        tokio::spawn(async move {
            let result = tokio::select! {
                result = child.wait() => result,
                _ = kill_listener.notified() => {
                    let _ = child.start_kill();
                    child.wait().await
                }
            };
            let outcome = match result {
                Ok(status) => UnixExit {
                    code: status.code().map(|code| code as u32).unwrap_or(u32::MAX),
                    error: None,
                },
                Err(error) => UnixExit {
                    code: u32::MAX,
                    error: Some(format!("Failed to wait for core: {error}")),
                },
            };
            let _ = exited_tx.send(Some(outcome));
        });
        Ok(ChildHandle {
            pid,
            inner: UnixChild {
                kill,
                exited: exited_rx,
            },
        })
    }

    pub fn exit_waiter(child: &ChildHandle) -> Result<Pin<Box<dyn Future<Output = u32> + Send>>> {
        let mut exited = child.inner.exited.clone();
        Ok(Box::pin(async move {
            exited
                .wait_for(Option::is_some)
                .await
                .ok()
                .and_then(|outcome| outcome.as_ref().map(|value| value.code))
                .unwrap_or(u32::MAX)
        }))
    }

    pub async fn shutdown(child: ChildHandle, timeout: Duration) -> Result<()> {
        child.inner.kill.notify_one();
        let mut exited = child.inner.exited;
        let outcome = tokio::time::timeout(timeout, exited.wait_for(Option::is_some))
            .await
            .map_err(|_| anyhow!("Timed out waiting for core to exit"))?
            .map_err(|_| anyhow!("Core exit monitor stopped unexpectedly"))?
            .clone()
            .ok_or_else(|| anyhow!("Core exit monitor returned no result"))?;
        if let Some(error) = outcome.error {
            return Err(anyhow!(error));
        }
        Ok(())
    }
}

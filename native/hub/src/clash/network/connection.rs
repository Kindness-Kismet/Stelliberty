// IPC 连接工具
//
// 提供 Named Pipe(Windows)和 Unix Socket(Unix)的统一连接接口

#[cfg(unix)]
use tokio::net::UnixStream;

#[cfg(windows)]
use tokio::net::windows::named_pipe::ClientOptions;

// Named Pipe 连接最大重试次数（避免无限等待）
#[cfg(windows)]
const MAX_PIPE_BUSY_RETRIES: u32 = 10;

// Windows：连接到 Named Pipe（带重试机制和超时保护）
#[cfg(windows)]
pub async fn connect_named_pipe(
    pipe_path: &str,
) -> Result<tokio::net::windows::named_pipe::NamedPipeClient, String> {
    use windows_sys::Win32::Foundation::ERROR_PIPE_BUSY;

    let mut retry_count = 0;

    loop {
        match ClientOptions::new().open(pipe_path) {
            Ok(client) => {
                log::trace!("已连接到 Named Pipe：{}", pipe_path);
                return Ok(client);
            }
            Err(e) if e.raw_os_error() == Some(ERROR_PIPE_BUSY as i32) => {
                retry_count += 1;

                if retry_count >= MAX_PIPE_BUSY_RETRIES {
                    return Err(format!(
                        "Named Pipe 连接超时：管道繁忙，重试 {} 次后仍无法连接（{}）",
                        MAX_PIPE_BUSY_RETRIES, pipe_path
                    ));
                }

                log::trace!(
                    "Named Pipe 繁忙，50 ms 后重试（{}/{}）",
                    retry_count,
                    MAX_PIPE_BUSY_RETRIES
                );
                tokio::time::sleep(std::time::Duration::from_millis(50)).await;
            }
            Err(e) => {
                return Err(format!("连接 Named Pipe 失败：{}", e));
            }
        }
    }
}

// Unix：连接到 Unix Socket
#[cfg(unix)]
pub async fn connect_unix_socket(socket_path: &str) -> Result<UnixStream, String> {
    UnixStream::connect(socket_path)
        .await
        .map_err(|e| format!("连接 Unix Socket 失败：{}", e))
}

// IPC 客户端原子模块：提供基础 IPC 通信能力。
// 支持延迟测试场景下的连接复用。

use once_cell::sync::Lazy;
use std::collections::VecDeque;
use std::sync::Arc;
use std::time::Instant;
use tokio::io::{AsyncBufReadExt, AsyncReadExt, AsyncWriteExt, BufReader};
use tokio::sync::Mutex;
use tokio::time::{Duration, timeout};

#[cfg(unix)]
use tokio::net::UnixStream;

#[cfg(windows)]
use tokio::net::windows::named_pipe::{ClientOptions, NamedPipeClient};

#[cfg(windows)]
type IpcStream = NamedPipeClient;

#[cfg(unix)]
type IpcStream = UnixStream;

// HTTP 响应
pub struct IpcHttpResponse {
    pub status_code: u16,
    pub body: String,
}

const MAX_POOL_SIZE: usize = 30;
const IDLE_TIMEOUT_MS: u64 = 35000;

struct PooledConnection {
    conn: IpcStream,
    last_used: Instant,
}

impl PooledConnection {
    fn is_valid(&self) -> bool {
        use std::io::ErrorKind;

        let mut buf = [0u8; 1];
        match self.conn.try_read(&mut buf) {
            Ok(0) => false,
            // 读取到数据说明连接状态不干净，直接丢弃避免污染后续响应。
            Ok(_) => false,
            Err(e) if e.kind() == ErrorKind::WouldBlock => true,
            Err(_) => false,
        }
    }
}

static IPC_CONNECTION_POOL: Lazy<Arc<Mutex<VecDeque<PooledConnection>>>> =
    Lazy::new(|| Arc::new(Mutex::new(VecDeque::new())));

// IPC 客户端（支持可选连接池）
pub struct IpcClient;

impl IpcClient {
    // 获取默认 IPC 路径
    pub fn default_ipc_path() -> String {
        #[cfg(windows)]
        {
            #[cfg(debug_assertions)]
            {
                r"\\.\pipe\stelliberty_dev".to_string()
            }
            #[cfg(not(debug_assertions))]
            {
                r"\\.\pipe\stelliberty".to_string()
            }
        }

        #[cfg(unix)]
        {
            #[cfg(debug_assertions)]
            {
                "/tmp/stelliberty_dev.sock".to_string()
            }
            #[cfg(not(debug_assertions))]
            {
                "/tmp/stelliberty.sock".to_string()
            }
        }
    }

    // 发送 GET 请求（每次创建新连接）
    pub async fn get(path: &str) -> Result<String, String> {
        let ipc_path = Self::default_ipc_path();
        let response = Self::request(&ipc_path, "GET", path, None).await?;

        if response.status_code >= 200 && response.status_code < 300 {
            Ok(response.body)
        } else {
            Err(format!("HTTP {}", response.status_code))
        }
    }

    pub async fn get_with_pool(path: &str) -> Result<String, String> {
        let response = Self::request_with_pool("GET", path, None).await?;

        if response.status_code >= 200 && response.status_code < 300 {
            Ok(response.body)
        } else {
            Err(format!("HTTP {}", response.status_code))
        }
    }

    #[cfg(windows)]
    async fn connect(ipc_path: &str) -> Result<IpcStream, String> {
        let mut last_err = None;
        for retry in 0..20 {
            match ClientOptions::new().open(ipc_path) {
                Ok(stream) => {
                    return Ok(stream);
                }
                Err(e) => {
                    let is_busy = e.raw_os_error() == Some(231);
                    last_err = Some(e);
                    if is_busy {
                        tokio::time::sleep(Duration::from_millis(15 * (retry + 1))).await;
                        continue;
                    }
                    break;
                }
            }
        }

        let err = last_err
            .map(|e| e.to_string())
            .unwrap_or_else(|| "未知错误".to_string());
        Err(format!("连接 Named Pipe 失败：{}", err))
    }

    #[cfg(unix)]
    async fn connect(ipc_path: &str) -> Result<IpcStream, String> {
        UnixStream::connect(ipc_path)
            .await
            .map_err(|e| format!("连接 Unix Socket 失败：{}", e))
    }

    async fn request(
        ipc_path: &str,
        method: &str,
        path: &str,
        body: Option<&str>,
    ) -> Result<IpcHttpResponse, String> {
        let mut stream = Self::connect(ipc_path).await?;
        Self::send_request(&mut stream, method, path, body, false).await
    }

    async fn request_with_pool(
        method: &str,
        path: &str,
        body: Option<&str>,
    ) -> Result<IpcHttpResponse, String> {
        let mut stream = Self::acquire_connection().await?;
        let response = Self::send_request(&mut stream, method, path, body, true).await;
        if response.is_ok() {
            Self::release_connection(stream).await;
        }
        response
    }

    async fn acquire_connection() -> Result<IpcStream, String> {
        loop {
            let pooled = {
                let mut pool = IPC_CONNECTION_POOL.lock().await;
                pool.pop_front()
            };

            if let Some(pooled) = pooled {
                if pooled.last_used.elapsed() < Duration::from_millis(IDLE_TIMEOUT_MS)
                    && pooled.is_valid()
                {
                    return Ok(pooled.conn);
                }
                continue;
            }

            break;
        }

        Self::connect(&Self::default_ipc_path()).await
    }

    async fn release_connection(conn: IpcStream) {
        let mut pool = IPC_CONNECTION_POOL.lock().await;
        if pool.len() < MAX_POOL_SIZE {
            pool.push_back(PooledConnection {
                conn,
                last_used: Instant::now(),
            });
        }
    }

    async fn send_request<S>(
        stream: &mut S,
        method: &str,
        path: &str,
        body: Option<&str>,
        keep_alive: bool,
    ) -> Result<IpcHttpResponse, String>
    where
        S: AsyncReadExt + AsyncWriteExt + Unpin,
    {
        // 构建 HTTP 请求
        let request = Self::build_http_request(method, path, body, keep_alive);

        // 发送请求
        stream
            .write_all(request.as_bytes())
            .await
            .map_err(|e| format!("发送请求失败：{}", e))?;

        // 读取响应
        Self::read_http_response(stream).await
    }

    fn build_http_request(
        method: &str,
        path: &str,
        body: Option<&str>,
        keep_alive: bool,
    ) -> String {
        let mut request = format!("{} {} HTTP/1.1\r\n", method, path);
        request.push_str("Host: localhost\r\n");
        if !keep_alive {
            request.push_str("Connection: close\r\n");
        }

        if let Some(body_str) = body {
            request.push_str("Content-Type: application/json\r\n");
            request.push_str(&format!("Content-Length: {}\r\n", body_str.len()));
            request.push_str("\r\n");
            request.push_str(body_str);
        } else {
            request.push_str("\r\n");
        }

        request
    }

    async fn read_http_response<S>(stream: &mut S) -> Result<IpcHttpResponse, String>
    where
        S: AsyncReadExt + Unpin,
    {
        let mut reader = BufReader::new(stream);

        // 读取 header
        let mut header_lines = Vec::new();
        loop {
            let mut line = String::new();
            let size = reader
                .read_line(&mut line)
                .await
                .map_err(|e| format!("读取响应行失败：{}", e))?;

            if size == 0 {
                return Err("连接意外关闭".to_string());
            }

            if line == "\r\n" {
                break;
            }

            header_lines.push(line);
        }

        // 解析 status line
        let status_line = header_lines.first().ok_or_else(|| "响应为空".to_string())?;
        let status_code = Self::parse_status_code(status_line)?;

        // 解析 headers
        let mut content_length: Option<usize> = None;
        let mut is_chunked = false;

        for line in &header_lines[1..] {
            if let Some((key, value)) = line.split_once(':') {
                let key = key.trim();
                let value = value.trim();

                if key.eq_ignore_ascii_case("content-length") {
                    content_length = value.parse().ok();
                }
                if key.eq_ignore_ascii_case("transfer-encoding") && value.contains("chunked") {
                    is_chunked = true;
                }
            }
        }

        // 读取 body
        let body = if is_chunked {
            Self::read_chunked_body(&mut reader).await?
        } else if let Some(length) = content_length {
            let mut body_bytes = vec![0u8; length];
            reader
                .read_exact(&mut body_bytes)
                .await
                .map_err(|e| format!("读取响应体失败：{}", e))?;
            String::from_utf8(body_bytes).map_err(|e| format!("解码响应体失败：{}", e))?
        } else {
            let mut body_bytes = Vec::new();
            match timeout(Duration::from_secs(5), reader.read_to_end(&mut body_bytes)).await {
                Ok(Ok(_)) => {
                    String::from_utf8(body_bytes).map_err(|e| format!("解码响应体失败：{}", e))?
                }
                Ok(Err(e)) => return Err(format!("读取响应体失败：{}", e)),
                Err(_) => return Err("读取响应体超时".to_string()),
            }
        };

        Ok(IpcHttpResponse { status_code, body })
    }

    fn parse_status_code(status_line: &str) -> Result<u16, String> {
        let parts: Vec<&str> = status_line.split_whitespace().collect();
        if parts.len() < 2 {
            return Err(format!("无效的状态行：{}", status_line));
        }

        parts[1]
            .parse::<u16>()
            .map_err(|_| format!("无效的状态码：{}", parts[1]))
    }

    async fn read_chunked_body<R>(reader: &mut BufReader<R>) -> Result<String, String>
    where
        R: AsyncReadExt + Unpin,
    {
        let mut body = Vec::new();

        loop {
            let mut size_line = String::new();
            reader
                .read_line(&mut size_line)
                .await
                .map_err(|e| format!("读取 chunk 大小失败：{}", e))?;

            let size_line = size_line.trim();
            if size_line.is_empty() {
                continue;
            }

            let chunk_size = usize::from_str_radix(size_line, 16)
                .map_err(|e| format!("解析 chunk 大小失败：{}", e))?;

            if chunk_size == 0 {
                let mut end = String::new();
                reader.read_line(&mut end).await.ok();
                break;
            }

            let mut chunk_data = vec![0u8; chunk_size];
            reader
                .read_exact(&mut chunk_data)
                .await
                .map_err(|e| format!("读取 chunk 数据失败：{}", e))?;
            body.extend_from_slice(&chunk_data);

            let mut crlf = String::new();
            reader.read_line(&mut crlf).await.ok();
        }

        String::from_utf8(body).map_err(|e| format!("解码 chunked body 失败：{}", e))
    }
}

// IPC 客户端原子模块
//
// 提供基础的 IPC 通信能力（不含连接池）

use tokio::io::{AsyncBufReadExt, AsyncReadExt, AsyncWriteExt, BufReader};

#[cfg(unix)]
use tokio::net::UnixStream;

// HTTP 响应
pub struct IpcHttpResponse {
    pub status_code: u16,
    pub body: String,
}

// IPC 客户端（无状态，每次请求创建新连接）
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

        #[cfg(windows)]
        let response = Self::request_windows(&ipc_path, "GET", path, None).await?;

        #[cfg(unix)]
        let response = Self::request_unix(&ipc_path, "GET", path, None).await?;

        if response.status_code >= 200 && response.status_code < 300 {
            Ok(response.body)
        } else {
            Err(format!("HTTP {}", response.status_code))
        }
    }

    #[cfg(windows)]
    async fn request_windows(
        ipc_path: &str,
        method: &str,
        path: &str,
        body: Option<&str>,
    ) -> Result<IpcHttpResponse, String> {
        use tokio::net::windows::named_pipe::ClientOptions;

        let mut stream = ClientOptions::new()
            .open(ipc_path)
            .map_err(|e| format!("连接 Named Pipe 失败：{}", e))?;

        Self::send_request(&mut stream, method, path, body).await
    }

    #[cfg(unix)]
    async fn request_unix(
        ipc_path: &str,
        method: &str,
        path: &str,
        body: Option<&str>,
    ) -> Result<IpcHttpResponse, String> {
        let mut stream = UnixStream::connect(ipc_path)
            .await
            .map_err(|e| format!("连接 Unix Socket 失败：{}", e))?;

        Self::send_request(&mut stream, method, path, body).await
    }

    async fn send_request<S>(
        stream: &mut S,
        method: &str,
        path: &str,
        body: Option<&str>,
    ) -> Result<IpcHttpResponse, String>
    where
        S: AsyncReadExt + AsyncWriteExt + Unpin,
    {
        // 构建 HTTP 请求
        let request = Self::build_http_request(method, path, body);

        // 发送请求
        stream
            .write_all(request.as_bytes())
            .await
            .map_err(|e| format!("发送请求失败：{}", e))?;

        // 读取响应
        Self::read_http_response(stream).await
    }

    fn build_http_request(method: &str, path: &str, body: Option<&str>) -> String {
        let mut request = format!("{} {} HTTP/1.1\r\n", method, path);
        request.push_str("Host: localhost\r\n");

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
            String::new()
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

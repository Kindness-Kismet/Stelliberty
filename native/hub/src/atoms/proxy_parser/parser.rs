// 订阅内容解析器
//
// 支持解析多种订阅源格式：
// - 标准 Clash YAML 配置
// - Base64 编码的代理链接列表
// - 纯文本代理链接列表（vless、vmess、hysteria2、ss、trojan 等）
//
// 将各种格式统一转换为标准 Clash 配置

use base64::{Engine, engine::general_purpose::STANDARD as BASE64};
use serde_json::{Value as JsonValue, json};
use std::collections::HashMap;
use url::Url;

// 代理链接解析器
pub struct ProxyParser;

impl ProxyParser {
    // 解析订阅内容为标准 Clash 配置
    //
    // 支持：
    // 1. 标准 Clash YAML
    // 2. Base64 编码的代理链接列表
    // 3. 纯文本代理链接列表
    pub fn parse_subscription(content: &str) -> Result<String, String> {
        let content = content.trim();

        // 优先尝试 Base64 解码
        let decoded = if Self::is_base64(content) {
            log::info!("检测到 Base64 编码内容，开始解码…");
            // 移除所有空白字符（换行、空格等）
            let clean = content.replace(|c: char| c.is_whitespace(), "");
            match BASE64.decode(clean.as_bytes()) {
                Ok(bytes) => match String::from_utf8(bytes) {
                    Ok(s) => {
                        log::info!("Base64 解码成功（解码后长度：{} 字节）", s.len());
                        s
                    }
                    Err(_) => {
                        log::warn!("Base64 解码后不是有效 UTF-8，使用原始内容");
                        content.to_string()
                    }
                },
                Err(e) => {
                    log::warn!("Base64 解码失败：{}，使用原始内容", e);
                    content.to_string()
                }
            }
        } else {
            content.to_string()
        };

        // 检查解码后的内容是否为 YAML 配置
        if Self::is_yaml_config(&decoded) {
            log::info!("检测到标准 Clash YAML 配置");
            return Ok(decoded);
        }

        // 尝试解析为 YAML + JSON 混合格式
        if let Ok(proxies) = Self::parse_yaml_json_proxies(&decoded)
            && !proxies.is_empty()
        {
            log::info!("成功解析 YAML + JSON 混合格式，{}个代理节点", proxies.len());
            return Self::generate_clash_config(proxies);
        }

        // 解析代理链接
        log::info!("开始解析代理链接…");
        let proxies = Self::parse_proxy_links(&decoded)?;

        if proxies.is_empty() {
            return Err("未找到任何有效的代理链接".to_string());
        }

        log::info!("成功解析{}个代理节点", proxies.len());

        // 生成标准 Clash 配置
        Self::generate_clash_config(proxies)
    }

    // 判断是否为 YAML 配置
    // 必须是合法的 YAML 格式且包含 Clash 配置的关键字段
    fn is_yaml_config(content: &str) -> bool {
        // 首先尝试解析为 YAML
        let yaml_parse_ok = serde_yaml_ng::from_str::<serde_yaml_ng::Value>(content).is_ok();

        if !yaml_parse_ok {
            return false;
        }

        // 确保包含 Clash 配置的必需字段
        // 必须同时包含 proxies 和至少一个其他关键字段
        let has_proxies = content.contains("proxies:");
        let has_groups_or_rules = content.contains("proxy-groups:") || content.contains("rules:");

        has_proxies && has_groups_or_rules
    }

    // 判断是否为 Base64
    fn is_base64(content: &str) -> bool {
        // 移除所有空白字符后检查
        let clean = content.replace(|c: char| c.is_whitespace(), "");

        // Base64 内容长度应该 > 50 且只包含特定字符
        clean.len() > 50
            && clean
                .chars()
                .all(|c| c.is_alphanumeric() || c == '+' || c == '/' || c == '=')
    }

    // 解析 YAML + JSON 混合格式（例如：proxies: 后面跟 JSON 对象列表）
    fn parse_yaml_json_proxies(content: &str) -> Result<Vec<JsonValue>, String> {
        // 尝试解析为 YAML
        let yaml_value: serde_yaml_ng::Value =
            serde_yaml_ng::from_str(content).map_err(|e| format!("YAML 解析失败：{}", e))?;

        // 提取 proxies 字段
        let proxies_value = yaml_value.get("proxies").ok_or("未找到 proxies 字段")?;

        // 转换为 JSON
        let proxies_json =
            serde_json::to_value(proxies_value).map_err(|e| format!("转换为 JSON 失败：{}", e))?;

        // 确保是数组
        let proxies_array = proxies_json.as_array().ok_or("proxies 不是数组")?;

        Ok(proxies_array.clone())
    }

    // 解析代理链接列表
    fn parse_proxy_links(content: &str) -> Result<Vec<JsonValue>, String> {
        let mut proxies = Vec::new();

        for line in content.lines() {
            let line = line.trim();
            if line.is_empty() || line.starts_with('#') {
                continue;
            }

            match Self::parse_single_proxy(line) {
                Ok(proxy) => proxies.push(proxy),
                Err(e) => {
                    // 使用 char_indices 避免 UTF-8 字符边界问题
                    let preview = line
                        .char_indices()
                        .take(50)
                        .map(|(_, c)| c)
                        .collect::<String>();
                    log::warn!("跳过无效代理：{} - {}", preview, e);
                }
            }
        }

        Ok(proxies)
    }

    // 解析单个代理链接
    fn parse_single_proxy(link: &str) -> Result<JsonValue, String> {
        if link.starts_with("vless://") {
            Self::parse_vless(link)
        } else if link.starts_with("vmess://") {
            Self::parse_vmess(link)
        } else if link.starts_with("hysteria2://") || link.starts_with("hy2://") {
            Self::parse_hysteria2(link)
        } else if link.starts_with("hysteria://") {
            Self::parse_hysteria(link)
        } else if link.starts_with("ss://") {
            Self::parse_shadowsocks(link)
        } else if link.starts_with("ssr://") {
            Self::parse_shadowsocksr(link)
        } else if link.starts_with("trojan://") {
            Self::parse_trojan(link)
        } else if link.starts_with("tuic://") {
            Self::parse_tuic(link)
        } else if link.starts_with("http://") || link.starts_with("https://") {
            Self::parse_http(link)
        } else if link.starts_with("socks://") || link.starts_with("socks5://") {
            Self::parse_socks(link)
        } else {
            Err(format!("不支持的协议：{}", &link[..link.len().min(20)]))
        }
    }

    // 解析 VLESS 链接
    fn parse_vless(link: &str) -> Result<JsonValue, String> {
        let url = Url::parse(link).map_err(|e| format!("URL 解析失败：{}", e))?;

        let uuid = url.username();
        let server = url.host_str().ok_or("缺少服务器地址")?.to_string();
        let port = url.port().ok_or("缺少端口")? as i64;

        let params = Self::parse_query_params(url.query().unwrap_or(""));
        let name = Self::url_decode(url.fragment().unwrap_or("VLESS"));

        let mut proxy = json!({
            "name": name,
            "type": "vless",
            "server": server,
            "port": port,
            "uuid": uuid,
            "network": params.get("type").unwrap_or(&"tcp".to_string()).clone(),
            "udp": true,
            "skip-cert-verify": false,
        });

        // Reality 配置
        if params.get("security").map(|s| s.as_str()) == Some("reality") {
            proxy["reality-opts"] = json!({
                "public-key": params.get("pbk").cloned().unwrap_or_default(),
                "short-id": params.get("sid").cloned().unwrap_or_default(),
            });
            proxy["tls"] = json!(true);
            proxy["servername"] = json!(params.get("sni").cloned().unwrap_or_default());
            if let Some(flow) = params.get("flow") {
                proxy["flow"] = json!(flow);
            }
        }

        // TLS 配置
        if params.get("security").map(|s| s.as_str()) == Some("tls") {
            proxy["tls"] = json!(true);
            if let Some(sni) = params.get("sni") {
                proxy["servername"] = json!(sni);
            }
        }

        // WebSocket 配置
        if params.get("type").map(|s| s.as_str()) == Some("ws") {
            let mut ws_opts = json!({
                "path": params.get("path").cloned().unwrap_or_else(|| "/".to_string()),
            });
            if let Some(host) = params.get("host") {
                ws_opts["headers"] = json!({"Host": host});
            }
            proxy["ws-opts"] = ws_opts;
        }

        // gRPC 配置
        if params.get("type").map(|s| s.as_str()) == Some("grpc") {
            proxy["grpc-opts"] = json!({
                "grpc-service-name": params.get("serviceName").cloned().unwrap_or_default(),
            });
        }

        Ok(proxy)
    }

    // 解析 VMess 链接
    fn parse_vmess(link: &str) -> Result<JsonValue, String> {
        let encoded = link.strip_prefix("vmess://").ok_or("无效的 VMess 链接")?;
        let decoded = BASE64
            .decode(encoded.as_bytes())
            .map_err(|e| format!("Base64 解码失败：{}", e))?;
        let json_str = String::from_utf8(decoded).map_err(|e| format!("UTF-8 转换失败：{}", e))?;
        let data: JsonValue =
            serde_json::from_str(&json_str).map_err(|e| format!("JSON 解析失败：{}", e))?;

        let mut proxy = json!({
            "name": data["ps"].as_str().unwrap_or("VMess"),
            "type": "vmess",
            "server": data["add"].as_str().unwrap_or(""),
            "port": data["port"].as_str().unwrap_or("443").parse::<i64>().unwrap_or(443),
            "uuid": data["id"].as_str().unwrap_or(""),
            "alterId": data["aid"].as_str().unwrap_or("0").parse::<i64>().unwrap_or(0),
            "cipher": data["scy"].as_str().unwrap_or("auto"),
            "udp": true,
        });

        // 网络类型
        let network = data["net"].as_str().unwrap_or("tcp");
        proxy["network"] = json!(network);

        // TLS
        if data["tls"].as_str().unwrap_or("") == "tls" {
            proxy["tls"] = json!(true);
            if let Some(sni) = data["sni"].as_str() {
                proxy["servername"] = json!(sni);
            }
        }

        // WebSocket
        if network == "ws" {
            let mut ws_opts = json!({
                "path": data["path"].as_str().unwrap_or("/"),
            });
            if let Some(host) = data["host"].as_str() {
                ws_opts["headers"] = json!({"Host": host});
            }
            proxy["ws-opts"] = ws_opts;
        }

        // gRPC
        if network == "grpc" {
            proxy["grpc-opts"] = json!({
                "grpc-service-name": data["path"].as_str().unwrap_or(""),
            });
        }

        Ok(proxy)
    }

    // 解析 Hysteria2 链接
    fn parse_hysteria2(link: &str) -> Result<JsonValue, String> {
        let link = link.strip_prefix("hy2://").unwrap_or(link);
        let url = Url::parse(link).map_err(|e| format!("URL 解析失败：{}", e))?;

        let password = url.username();
        let server = url.host_str().ok_or("缺少服务器地址")?.to_string();
        let port = url.port().unwrap_or(443) as i64;

        let params = Self::parse_query_params(url.query().unwrap_or(""));
        let name = Self::url_decode(url.fragment().unwrap_or("Hysteria2"));

        let mut proxy = json!({
            "name": name,
            "type": "hysteria2",
            "server": server,
            "port": port,
            "password": password,
            "skip-cert-verify": params.get("insecure").map(|s| s == "1").unwrap_or(false),
        });

        if let Some(sni) = params.get("sni") {
            proxy["sni"] = json!(sni);
        }

        if let Some(obfs) = params.get("obfs") {
            proxy["obfs"] = json!(obfs);
            if let Some(obfs_password) = params.get("obfs-password") {
                proxy["obfs-password"] = json!(obfs_password);
            }
        }

        Ok(proxy)
    }

    // 解析 Hysteria 链接
    fn parse_hysteria(link: &str) -> Result<JsonValue, String> {
        let url = Url::parse(link).map_err(|e| format!("URL 解析失败：{}", e))?;

        let server = url.host_str().ok_or("缺少服务器地址")?.to_string();
        let port = url.port().unwrap_or(443) as i64;
        let auth = url.username();

        let params = Self::parse_query_params(url.query().unwrap_or(""));
        let name = Self::url_decode(url.fragment().unwrap_or("Hysteria"));

        let mut proxy = json!({
            "name": name,
            "type": "hysteria",
            "server": server,
            "port": port,
            "auth": auth,
            "protocol": params.get("protocol").cloned().unwrap_or_else(|| "udp".to_string()),
            "up": params.get("upmbps").and_then(|s| s.parse::<i64>().ok()).unwrap_or(10),
            "down": params.get("downmbps").and_then(|s| s.parse::<i64>().ok()).unwrap_or(50),
            "skip-cert-verify": params.get("insecure").map(|s| s == "1").unwrap_or(false),
        });

        if let Some(obfs) = params.get("obfs") {
            proxy["obfs"] = json!(obfs);
        }

        if let Some(sni) = params.get("peer") {
            proxy["sni"] = json!(sni);
        }

        Ok(proxy)
    }

    // 解析 Shadowsocks 链接
    fn parse_shadowsocks(link: &str) -> Result<JsonValue, String> {
        // ss://method:password@server:port#name
        // 或 ss://base64(method:password)@server:port#name
        let link = link.strip_prefix("ss://").ok_or("无效的 SS 链接")?;

        let (auth_part, rest) = link.split_once('@').ok_or("SS 链接格式错误：缺少 @")?;

        // 解析认证部分
        let decoded_auth = if auth_part.contains(':') {
            auth_part.to_string()
        } else {
            // Base64 编码
            let decoded = BASE64
                .decode(auth_part.as_bytes())
                .map_err(|e| format!("Base64 解码失败：{}", e))?;
            String::from_utf8(decoded).map_err(|e| format!("UTF-8 转换失败：{}", e))?
        };

        let (method, password) = decoded_auth.split_once(':').ok_or("SS 认证格式错误")?;

        // 解析服务器和端口
        let (server_port, name_part) = rest.split_once('#').unwrap_or((rest, "Shadowsocks"));

        let (server, port_str) = server_port
            .rsplit_once(':')
            .ok_or("SS 链接格式错误：缺少端口")?;

        let port = port_str.parse::<i64>().map_err(|_| "端口解析失败")?;

        let name = Self::url_decode(name_part);

        Ok(json!({
            "name": name,
            "type": "ss",
            "server": server,
            "port": port,
            "cipher": method,
            "password": password,
            "udp": true,
        }))
    }
    // 解析 ShadowsocksR 链接
    fn parse_shadowsocksr(link: &str) -> Result<JsonValue, String> {
        // ssr://base64(server:port:protocol:method:obfs:password_base64/?params)
        let encoded = link.strip_prefix("ssr://").ok_or("无效的 SSR 链接")?;
        let decoded = BASE64
            .decode(encoded.as_bytes())
            .map_err(|e| format!("Base64 解码失败：{}", e))?;
        let decoded_str =
            String::from_utf8(decoded).map_err(|e| format!("UTF-8 转换失败：{}", e))?;

        let (main_part, params_part) = decoded_str.split_once("/?").unwrap_or((&decoded_str, ""));

        let parts: Vec<&str> = main_part.split(':').collect();
        if parts.len() < 6 {
            return Err("SSR 链接格式错误".to_string());
        }

        let server = parts[0];
        let port = parts[1].parse::<i64>().map_err(|_| "端口解析失败")?;
        let protocol = parts[2];
        let method = parts[3];
        let obfs = parts[4];
        let password_b64 = parts[5];

        let password = String::from_utf8(
            BASE64
                .decode(password_b64.as_bytes())
                .map_err(|e| format!("密码解码失败：{}", e))?,
        )
        .map_err(|e| format!("密码UTF-8 转换失败：{}", e))?;

        let params = Self::parse_query_params(params_part);
        let name = params
            .get("remarks")
            .and_then(|r| {
                BASE64
                    .decode(r.as_bytes())
                    .ok()
                    .and_then(|b| String::from_utf8(b).ok())
            })
            .unwrap_or_else(|| "ShadowsocksR".to_string());

        let mut proxy = json!({
            "name": name,
            "type": "ssr",
            "server": server,
            "port": port,
            "cipher": method,
            "password": password,
            "protocol": protocol,
            "obfs": obfs,
            "udp": true,
        });

        if let Some(obfs_param) = params.get("obfsparam").and_then(|p| {
            BASE64
                .decode(p.as_bytes())
                .ok()
                .and_then(|b| String::from_utf8(b).ok())
        }) {
            proxy["obfs-param"] = json!(obfs_param);
        }

        if let Some(proto_param) = params.get("protoparam").and_then(|p| {
            BASE64
                .decode(p.as_bytes())
                .ok()
                .and_then(|b| String::from_utf8(b).ok())
        }) {
            proxy["protocol-param"] = json!(proto_param);
        }

        Ok(proxy)
    }

    // 解析 Trojan 链接
    fn parse_trojan(link: &str) -> Result<JsonValue, String> {
        // trojan://password@server:port?params#name
        let url = Url::parse(link).map_err(|e| format!("URL 解析失败：{}", e))?;

        let password = url.username();
        let server = url.host_str().ok_or("缺少服务器地址")?.to_string();
        let port = url.port().unwrap_or(443) as i64;

        let params = Self::parse_query_params(url.query().unwrap_or(""));
        let name = Self::url_decode(url.fragment().unwrap_or("Trojan"));

        let mut proxy = json!({
            "name": name,
            "type": "trojan",
            "server": server,
            "port": port,
            "password": password,
            "udp": true,
            "skip-cert-verify": params.get("allowInsecure").map(|s| s == "1").unwrap_or(false),
        });

        if let Some(sni) = params.get("sni") {
            proxy["sni"] = json!(sni);
        }

        // WebSocket
        if params.get("type").map(|s| s.as_str()) == Some("ws") {
            let mut ws_opts = json!({
                "path": params.get("path").cloned().unwrap_or_else(|| "/".to_string()),
            });
            if let Some(host) = params.get("host") {
                ws_opts["headers"] = json!({"Host": host});
            }
            proxy["network"] = json!("ws");
            proxy["ws-opts"] = ws_opts;
        }

        // gRPC
        if params.get("type").map(|s| s.as_str()) == Some("grpc") {
            proxy["network"] = json!("grpc");
            proxy["grpc-opts"] = json!({
                "grpc-service-name": params.get("serviceName").cloned().unwrap_or_default(),
            });
        }

        Ok(proxy)
    }

    // 解析 TUIC 链接
    fn parse_tuic(link: &str) -> Result<JsonValue, String> {
        let url = Url::parse(link).map_err(|e| format!("URL 解析失败：{}", e))?;

        let uuid = url.username();
        let password = url.password().unwrap_or("");
        let server = url.host_str().ok_or("缺少服务器地址")?.to_string();
        let port = url.port().unwrap_or(443) as i64;

        let params = Self::parse_query_params(url.query().unwrap_or(""));
        let name = Self::url_decode(url.fragment().unwrap_or("TUIC"));

        let mut proxy = json!({
            "name": name,
            "type": "tuic",
            "server": server,
            "port": port,
            "uuid": uuid,
            "password": password,
            "skip-cert-verify": params.get("insecure").map(|s| s == "1").unwrap_or(false),
        });

        if let Some(sni) = params.get("sni") {
            proxy["sni"] = json!(sni);
        }

        if let Some(alpn) = params.get("alpn") {
            proxy["alpn"] = json!(alpn.split(',').collect::<Vec<_>>());
        }

        if let Some(congestion) = params.get("congestion_control") {
            proxy["congestion-control"] = json!(congestion);
        }

        Ok(proxy)
    }

    // 解析 HTTP/HTTPS 代理链接
    fn parse_http(link: &str) -> Result<JsonValue, String> {
        let url = Url::parse(link).map_err(|e| format!("URL 解析失败：{}", e))?;

        let server = url.host_str().ok_or("缺少服务器地址")?.to_string();
        let port = url
            .port()
            .unwrap_or(if link.starts_with("https") { 443 } else { 80 }) as i64;
        let username = if url.username().is_empty() {
            None
        } else {
            Some(url.username().to_string())
        };
        let password = url.password().map(|p| p.to_string());

        let name = Self::url_decode(url.fragment().unwrap_or("HTTP"));

        let mut proxy = json!({
            "name": name,
            "type": "http",
            "server": server,
            "port": port,
        });

        if let Some(user) = username {
            proxy["username"] = json!(user);
        }

        if let Some(pass) = password {
            proxy["password"] = json!(pass);
        }

        if link.starts_with("https") {
            proxy["tls"] = json!(true);
        }

        Ok(proxy)
    }

    // 解析 SOCKS 代理链接
    fn parse_socks(link: &str) -> Result<JsonValue, String> {
        let url = Url::parse(link).map_err(|e| format!("URL 解析失败：{}", e))?;

        let server = url.host_str().ok_or("缺少服务器地址")?.to_string();
        let port = url.port().unwrap_or(1080) as i64;
        let username = if url.username().is_empty() {
            None
        } else {
            Some(url.username().to_string())
        };
        let password = url.password().map(|p| p.to_string());

        let name = Self::url_decode(url.fragment().unwrap_or("SOCKS5"));

        let mut proxy = json!({
            "name": name,
            "type": "socks5",
            "server": server,
            "port": port,
            "udp": true,
        });

        if let Some(user) = username {
            proxy["username"] = json!(user);
        }

        if let Some(pass) = password {
            proxy["password"] = json!(pass);
        }

        Ok(proxy)
    }

    // 解析 URL 查询参数
    fn parse_query_params(query: &str) -> HashMap<String, String> {
        let mut params = HashMap::new();
        for pair in query.split('&') {
            if let Some((key, value)) = pair.split_once('=') {
                params.insert(key.to_string(), Self::url_decode(value));
            }
        }
        params
    }

    // URL 解码
    fn url_decode(s: &str) -> String {
        urlencoding::decode(s).unwrap_or_default().to_string()
    }

    // 生成标准 Clash 配置（精简版）
    //
    // 注意：端口、模式、日志、DNS 等运行时参数会由 ConfigInjector 统一注入
    // 这里只生成核心的代理节点、代理组、规则配置
    fn generate_clash_config(proxies: Vec<JsonValue>) -> Result<String, String> {
        let proxy_names: Vec<String> = proxies
            .iter()
            .filter_map(|p| p["name"].as_str().map(|s| s.to_string()))
            .collect();

        let config = json!({
            // 代理节点（必需）
            "proxies": proxies,

            // 代理组（必需）
            "proxy-groups": [
                {
                    "name": "PROXY",
                    "type": "select",
                    "proxies": proxy_names.clone()
                },
                {
                    "name": "AUTO",
                    "type": "url-test",
                    "proxies": proxy_names,
                    "url": "https://www.gstatic.com/generate_204",
                    "interval": 300
                }
            ],

            // 路由规则（必需）
            "rules": [
                "MATCH,PROXY"
            ]
        });

        let yaml_value: serde_yaml_ng::Value =
            serde_json::from_value(config).map_err(|e| format!("JSON 转 YAML 失败：{}", e))?;

        let yaml_string =
            serde_yaml_ng::to_string(&yaml_value).map_err(|e| format!("YAML 序列化失败：{}", e))?;

        // 为 short-id 字段的值添加单引号（使用正则表达式替换）
        let yaml_string = regex::Regex::new(r"short-id:\s*([^\s']+)")
            .map_err(|e| format!("正则表达式创建失败：{}", e))?
            .replace_all(&yaml_string, "short-id: '$1'")
            .to_string();

        Ok(yaml_string)
    }
}

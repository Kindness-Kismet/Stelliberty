// JavaScript 覆写执行器
//
// 目的：使用 Boa 引擎执行用户的 JavaScript 覆写脚本

use boa_engine::{Context, Source};
use serde_json::Value as JsonValue;
use serde_yaml_ng::Value as YamlValue;

// JavaScript 执行器
pub struct JsExecutor {
    context: Context,
}

impl JsExecutor {
    // 创建新的 JavaScript 执行器
    //
    // 目的：初始化 Boa 上下文
    pub fn new() -> Result<Self, String> {
        let context = Context::default();

        Ok(Self { context })
    }

    // 应用 JavaScript 覆写到基础配置
    //
    // 目的：
    // 1. 将 YAML 配置转换为 JSON
    // 2. 执行用户的 JavaScript 脚本（必须定义 main(config) 函数）
    // 3. 将结果转换回 YAML
    pub fn apply(&mut self, base_content: &str, js_code: &str) -> Result<String, String> {
        log::info!("JavaScript 覆写开始");
        log::info!("基础配置长度：{}字节", base_content.len());
        log::info!("JS 脚本长度：{}字节", js_code.len());

        // 1. 解析 YAML → JSON
        let yaml_val: YamlValue = serde_yaml_ng::from_str(base_content).map_err(|e| {
            log::error!("✗ 解析 YAML 配置失败：{}", e);
            format!("解析配置失败：{}", e)
        })?;

        let json_val: JsonValue = serde_json::to_value(&yaml_val).map_err(|e| {
            log::error!("✗ 转换为 JSON 失败：{}", e);
            format!("转换为 JSON 失败：{}", e)
        })?;

        let config_json = serde_json::to_string(&json_val).map_err(|e| {
            log::error!("✗ 序列化 JSON 失败：{}", e);
            format!("序列化 JSON 失败：{}", e)
        })?;

        log::info!(
            "✓ YAML → JSON 转换成功，JSON 长度：{}字节",
            config_json.len()
        );

        // 检查 proxies 字段
        if let Some(proxies) = json_val.get("proxies") {
            if let Some(arr) = proxies.as_array() {
                log::info!("配置中包含{}个代理节点", arr.len());
                if let Some(first_proxy) = arr.first() {
                    log::info!(
                        "  第一个代理节点：{}",
                        serde_json::to_string(first_proxy).unwrap_or_default()
                    );
                }
            }
        } else {
            log::warn!("配置中未找到 proxies 字段");
        }

        // 转义 JSON 字符串中的反斜杠和单引号，以便安全地嵌入 JavaScript
        let escaped_config = config_json.replace('\\', "\\\\").replace('\'', "\\'");

        // 2. 构建完整的 JavaScript 代码
        // 用户脚本必须定义 main(config) 函数
        let full_js_code = format!(
            r#"
            (function() {{
                // 用户的覆写代码（定义 main 函数）
                {}

                // 初始化配置对象（从基础配置的 JSON）
                var config = JSON.parse('{}');

                // 调用 main 函数并传入配置
                if (typeof main === 'function') {{
                    config = main(config);
                }} else {{
                    throw new Error('覆写脚本必须定义 main(config) 函数');
                }}

                // 返回修改后的配置
                return JSON.stringify(config);
            }})()
            "#,
            js_code, escaped_config
        );

        log::info!(
            "✓ JavaScript 代码构建完成，总长度：{}字节",
            full_js_code.len()
        );

        // 3. 执行 JavaScript
        log::info!("→ 开始执行 JavaScript…");
        let source = Source::from_bytes(&full_js_code);
        let result = self.context.eval(source).map_err(|e| {
            log::error!("✗ JavaScript 执行失败：{}", e);
            format!("JavaScript 执行失败：{}", e)
        })?;

        log::info!("✓ JavaScript 执行成功");

        // 4. 提取结果字符串
        let result_str = result.to_string(&mut self.context).map_err(|e| {
            log::error!("✗ 提取 JavaScript 结果失败：{}", e);
            format!("提取 JavaScript 结果失败：{}", e)
        })?;

        let result_str = result_str.to_std_string().map_err(|e| {
            log::error!("✗ 转换结果字符串失败：{}", e);
            format!("转换结果字符串失败：{}", e)
        })?;

        log::info!("✓ JavaScript 结果长度：{}字节", result_str.len());

        // 5. JSON → YAML
        let json_result: JsonValue = serde_json::from_str(&result_str).map_err(|e| {
            log::error!("✗ 解析 JavaScript 结果失败：{}", e);
            log::error!("✗ 错误的 JSON 内容：{}", result_str);
            format!("解析 JavaScript 结果失败：{}", e)
        })?;

        log::info!("✓ JSON 解析成功");

        // 检查返回的 proxies 字段
        if let Some(proxies) = json_result.get("proxies") {
            if let Some(arr) = proxies.as_array() {
                log::info!("返回的配置中包含{}个代理节点", arr.len());
                if let Some(first_proxy) = arr.first() {
                    log::info!(
                        "  返回的第一个代理节点：{}",
                        serde_json::to_string(first_proxy).unwrap_or_default()
                    );
                }
            }
        } else {
            log::warn!("返回的配置中未找到 proxies 字段");
        }

        let yaml_result: YamlValue = serde_json::from_value(json_result).map_err(|e| {
            log::error!("✗ 转换为 YAML 失败：{}", e);
            format!("转换为 YAML 失败：{}", e)
        })?;

        let final_yaml = serde_yaml_ng::to_string(&yaml_result).map_err(|e| {
            log::error!("✗ 序列化 YAML 失败：{}", e);
            format!("序列化 YAML 失败：{}", e)
        })?;

        log::info!("✓ YAML 序列化成功，最终长度：{} 字节", final_yaml.len());

        log::info!("JavaScript 覆写成功");
        Ok(final_yaml)
    }
}

use std::time::{Duration, Instant};

use anyhow::{Context, Result, anyhow};
use interoptopus::ffi;
use rquickjs::{CatchResultExt, Context as JsContext, Runtime};
use serde_json::Value as Json;
use serde_yaml_ng::{Mapping, Value as Yaml};

const JS_MEMORY_LIMIT: usize = 8 * 1024 * 1024;
const JS_OUTPUT_LIMIT: usize = 1024 * 1024;
const JS_TIMEOUT: Duration = Duration::from_secs(2);
const JS_CONSOLE_POLYFILL: &str = r#"
globalThis.console = globalThis.console || {};
globalThis.console.log = globalThis.console.log || function() {};
globalThis.console.info = globalThis.console.info || function() {};
globalThis.console.warn = globalThis.console.warn || function() {};
globalThis.console.error = globalThis.console.error || function() {};
globalThis.console.debug = globalThis.console.debug || function() {};
"#;

pub fn apply_yaml(base: &str, over: &str) -> Result<String> {
    let base_val: Yaml = serde_yaml_ng::from_str(base).context("Failed to parse base config")?;
    let over_val: Yaml =
        serde_yaml_ng::from_str(over).context("Failed to parse override config")?;
    let merged = deep_merge(base_val, over_val)?;
    serde_yaml_ng::to_string(&merged).context("Failed to serialize override result")
}

pub fn apply_js(base: &str, js_code: &str) -> Result<String> {
    let yaml_val: Yaml = serde_yaml_ng::from_str(base).context("Failed to parse base config")?;
    let json_str =
        serde_json::to_string(&yaml_val).context("Failed to convert base config to JSON")?;

    let runtime = Runtime::new().context("Failed to create JS runtime")?;
    runtime.set_memory_limit(JS_MEMORY_LIMIT);
    // 超时中断在 JS_TIMEOUT 后返回 true，用于停止脚本。
    let deadline = Instant::now() + JS_TIMEOUT;
    runtime.set_interrupt_handler(Some(Box::new(move || Instant::now() >= deadline)));

    let context = JsContext::full(&runtime).context("Failed to create JS context")?;

    let result_json: String = context.with(|ctx| -> Result<String> {
        // console 只兼容常见日志调用，不输出参数，避免泄露订阅内容。
        let script = format!(
            "{JS_CONSOLE_POLYFILL}\nvar proxies;\n{js_code}\n;JSON.stringify(main({json_str})) || '';"
        );
        ctx.eval::<String, _>(script)
            .catch(&ctx)
            .map_err(|e| anyhow!("JS execution failed: {e}"))
    })?;

    if result_json.len() > JS_OUTPUT_LIMIT {
        return Err(anyhow!("JS override output is too large"));
    }
    if result_json.trim().is_empty() {
        return Err(anyhow!("JS override returned an empty config"));
    }

    let json_val: Json =
        serde_json::from_str(&result_json).context("Failed to deserialize JSON returned by JS")?;
    if !json_val.is_object() {
        return Err(anyhow!("JS override returned a non-object config"));
    }
    serde_yaml_ng::to_string(&json_val).context("Failed to convert final result to YAML")
}

fn deep_merge(base: Yaml, over: Yaml) -> Result<Yaml> {
    match (base, over) {
        (Yaml::Mapping(base_map), Yaml::Mapping(over_map)) => {
            let mut merged = base_map;
            for (raw_key, value) in over_map {
                apply_mapping_entry(&mut merged, raw_key, value)?;
            }
            Ok(Yaml::Mapping(merged))
        }
        (_, replacement) => Ok(replacement),
    }
}

// 覆写键后缀表达解包、强制替换、数组前插和后追加。
fn apply_mapping_entry(target: &mut Mapping, raw_key: Yaml, value: Yaml) -> Result<()> {
    let key_str = match raw_key {
        Yaml::String(s) => s,
        other => {
            target.insert(other, value);
            return Ok(());
        }
    };

    if let Some(inner) = key_str.strip_prefix('<').and_then(|s| s.strip_suffix('>')) {
        target.insert(Yaml::String(inner.to_string()), value);
        return Ok(());
    }

    if let Some(key) = key_str.strip_suffix('!') {
        target.insert(Yaml::String(key.to_string()), value);
        return Ok(());
    }

    if let Some(key) = key_str.strip_prefix('+') {
        let key_yaml = Yaml::String(key.to_string());
        let existing = target.remove(&key_yaml).unwrap_or(Yaml::Sequence(vec![]));
        let combined = concat_sequence(value, existing)?;
        target.insert(key_yaml, combined);
        return Ok(());
    }

    if let Some(key) = key_str.strip_suffix('+') {
        let key_yaml = Yaml::String(key.to_string());
        let existing = target.remove(&key_yaml).unwrap_or(Yaml::Sequence(vec![]));
        let combined = concat_sequence(existing, value)?;
        target.insert(key_yaml, combined);
        return Ok(());
    }

    let key_yaml = Yaml::String(key_str);
    let merged = match target.remove(&key_yaml) {
        Some(existing) => deep_merge(existing, value)?,
        None => value,
    };
    target.insert(key_yaml, merged);
    Ok(())
}

fn concat_sequence(head: Yaml, tail: Yaml) -> Result<Yaml> {
    let mut head_seq = into_sequence(head)?;
    let mut tail_seq = into_sequence(tail)?;
    head_seq.append(&mut tail_seq);
    Ok(Yaml::Sequence(head_seq))
}

fn into_sequence(value: Yaml) -> Result<Vec<Yaml>> {
    match value {
        Yaml::Sequence(seq) => Ok(seq),
        Yaml::Null => Ok(vec![]),
        other => Err(anyhow!(
            "Array append target must be a sequence or null, got: {other:?}"
        )),
    }
}

// FFI 错误使用 ERR: 前缀，便于 C# 包装层转换为异常。
#[ffi]
pub fn hub_overrides_apply_yaml(base_yaml: ffi::String, over_yaml: ffi::String) -> ffi::String {
    match apply_yaml(base_yaml.as_str(), over_yaml.as_str()) {
        Ok(out) => ffi::String::from_string(out),
        Err(err) => ffi::String::from_string(format!("ERR:{err:#}")),
    }
}

#[ffi]
pub fn hub_overrides_apply_js(base_yaml: ffi::String, js_code: ffi::String) -> ffi::String {
    match apply_js(base_yaml.as_str(), js_code.as_str()) {
        Ok(out) => ffi::String::from_string(out),
        Err(err) => ffi::String::from_string(format!("ERR:{err:#}")),
    }
}

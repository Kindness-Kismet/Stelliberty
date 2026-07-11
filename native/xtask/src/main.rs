use interoptopus_csharp::RustLibrary;
use interoptopus_csharp::dispatch::Dispatch;
use std::error::Error;
use std::path::{Path, PathBuf};

fn main() -> Result<(), Box<dyn Error>> {
    let command = std::env::args()
        .nth(1)
        .unwrap_or_else(|| "generate-bindings".to_string());
    if command != "generate-bindings" {
        return Err(format!("unknown command: {command}").into());
    }

    generate_bindings()
}

fn generate_bindings() -> Result<(), Box<dyn Error>> {
    let output_dir = PathBuf::from(env!("CARGO_MANIFEST_DIR"))
        .join("..")
        .join("..")
        .join("src")
        .join("Stelliberty.Native")
        .join("Generated");

    std::fs::create_dir_all(&output_dir)?;

    let output = RustLibrary::builder(hub::ffi::inventory())
        .dll_name("hub")
        .dispatch(Dispatch::single_file("Stelliberty.Native.Generated"))
        .build()
        .process()?;

    output.write_buffers_to(&output_dir)?;
    patch_utf8_from(&output_dir)?;
    patch_unit_default(&output_dir)?;
    patch_visibility(&output_dir)?;
    Ok(())
}

// 大型 interoptopus 字符串必须走堆，避免无界 stackalloc 崩溃。
fn patch_utf8_from(output_dir: &Path) -> Result<(), Box<dyn Error>> {
    let path = output_dir.join("Interop.cs");
    let content = std::fs::read_to_string(&path)?;

    // 单行锚点限制模板漂移；<=1024 字节留在栈上。
    let needle =
        "        Span<byte> utf8Bytes = stackalloc byte[Encoding.UTF8.GetByteCount(source)];";
    let replacement = "        var _bc = Encoding.UTF8.GetByteCount(source);\n        Span<byte> utf8Bytes = _bc > 1024 ? new byte[_bc] : stackalloc byte[_bc];";

    if !content.contains(needle) {
        // 已修补则直接跳过，生成步骤保持幂等。
        if content.contains("_bc > 1024 ? new byte[_bc]") {
            return Ok(());
        }
        return Err("Utf8String.From stackalloc anchor was not found; the interoptopus template may have changed".into());
    }

    std::fs::write(&path, content.replacen(needle, replacement, 1))?;
    Ok(())
}

// 新版模板会生成未显式赋值的 Unit.Default，需避开 warning-as-error。
fn patch_unit_default(output_dir: &Path) -> Result<(), Box<dyn Error>> {
    let path = output_dir.join("Interop.cs");
    let content = std::fs::read_to_string(&path)?;

    let needle = "    public static readonly Unit Default;";
    let replacement = "    public static readonly Unit Default = default;";

    if !content.contains(needle) {
        return Ok(());
    }

    std::fs::write(&path, content.replacen(needle, replacement, 1))?;
    Ok(())
}

// 原始 FFI 入口是内部细节，业务程序集不能绕过 Stelliberty.Native。
fn patch_visibility(output_dir: &Path) -> Result<(), Box<dyn Error>> {
    let path = output_dir.join("Interop.cs");
    let content = std::fs::read_to_string(&path)?;

    // 只重写顶层 public 声明；缩进的嵌套成员保持 public。
    let mut patched = content
        .lines()
        .map(|line| {
            if let Some(rest) = line.strip_prefix("public ") {
                format!("internal {rest}")
            } else {
                line.to_string()
            }
        })
        .collect::<Vec<_>>()
        .join("\n");
    if content.ends_with('\n') {
        patched.push('\n');
    }

    std::fs::write(&path, patched)?;
    Ok(())
}

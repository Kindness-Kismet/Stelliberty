use std::sync::OnceLock;

const DEFAULT_APP_NAME: &str = "stelliberty";

pub fn service_name() -> &'static str {
    static VALUE: OnceLock<String> = OnceLock::new();
    VALUE.get_or_init(|| format!("{}Service{}", pascal_identifier(app_name()), dev_suffix()))
}

pub fn service_binary_name() -> &'static str {
    static VALUE: OnceLock<String> = OnceLock::new();
    VALUE.get_or_init(|| {
        let suffix = if cfg!(windows) { ".exe" } else { "" };
        format!("{}_service{suffix}", separated_token(app_name(), '_'))
    })
}

pub fn command_endpoint() -> &'static str {
    static VALUE: OnceLock<String> = OnceLock::new();
    VALUE.get_or_init(|| {
        let token = separated_token(app_name(), '_');
        if cfg!(windows) {
            format!(r"\\.\pipe\{token}_service{}", endpoint_suffix())
        } else {
            format!("/tmp/{token}_service{}.sock", endpoint_suffix())
        }
    })
}

pub fn core_lock_prefix() -> &'static str {
    static VALUE: OnceLock<String> = OnceLock::new();
    VALUE.get_or_init(|| format!(".{}-core-", separated_token(app_name(), '-')))
}

pub fn linux_unit_path() -> &'static str {
    static VALUE: OnceLock<String> = OnceLock::new();
    VALUE.get_or_init(|| format!("/etc/systemd/system/{}.service", service_name()))
}

pub fn launchd_label() -> &'static str {
    static VALUE: OnceLock<String> = OnceLock::new();
    VALUE.get_or_init(|| {
        format!(
            "com.{}.service{}",
            separated_token(app_name(), '-'),
            endpoint_suffix()
        )
    })
}

pub fn launchd_plist_path() -> &'static str {
    static VALUE: OnceLock<String> = OnceLock::new();
    VALUE.get_or_init(|| format!("/Library/LaunchDaemons/{}.plist", launchd_label()))
}

fn app_name() -> &'static str {
    option_env!("STELLIBERTY_APP_NAME").unwrap_or(DEFAULT_APP_NAME)
}

fn dev_suffix() -> &'static str {
    if cfg!(debug_assertions) { "Dev" } else { "" }
}

fn endpoint_suffix() -> &'static str {
    if cfg!(debug_assertions) { "_dev" } else { "" }
}

fn pascal_identifier(value: &str) -> String {
    let mut output = String::with_capacity(value.len());
    let mut capitalize_next = true;
    for ch in value.chars() {
        if !ch.is_ascii_alphanumeric() {
            capitalize_next = true;
            continue;
        }

        if capitalize_next {
            output.push(ch.to_ascii_uppercase());
            capitalize_next = false;
        } else {
            output.push(ch);
        }
    }

    if output.is_empty() {
        "App".to_string()
    } else {
        output
    }
}

fn separated_token(value: &str, separator: char) -> String {
    let mut output = String::with_capacity(value.len());
    let mut previous_was_separator = false;
    for ch in value.chars() {
        if ch.is_ascii_alphanumeric() {
            output.push(ch.to_ascii_lowercase());
            previous_was_separator = false;
            continue;
        }

        if !previous_was_separator {
            output.push(separator);
            previous_was_separator = true;
        }
    }

    let trimmed = output.trim_matches(separator).to_string();
    if trimmed.is_empty() {
        "app".to_string()
    } else {
        trimmed
    }
}

use std::collections::VecDeque;
use std::sync::{Mutex, OnceLock};

use time::{OffsetDateTime, UtcOffset};

const MAX_LOG_LINES: usize = 2000;
const DEFAULT_PRINT_LINES: usize = 200;
static LOGS: OnceLock<Mutex<VecDeque<String>>> = OnceLock::new();

pub fn info(message: impl AsRef<str>) {
    write("I", message.as_ref());
}

pub fn warn(message: impl AsRef<str>) {
    write("W", message.as_ref());
}

pub fn error(message: impl AsRef<str>) {
    write("E", message.as_ref());
}

pub fn recent(lines: usize) -> Vec<String> {
    let count = lines.min(MAX_LOG_LINES);
    let guard = logs().lock().unwrap_or_else(|error| error.into_inner());
    let start = guard.len().saturating_sub(count);
    guard.iter().skip(start).cloned().collect()
}

pub fn default_line_count() -> usize {
    DEFAULT_PRINT_LINES
}

fn write(level: &str, message: &str) {
    let mut guard = logs().lock().unwrap_or_else(|error| error.into_inner());
    if guard.len() >= MAX_LOG_LINES {
        guard.pop_front();
    }
    guard.push_back(format!("[{level}] {} {}", timestamp(), sanitize(message)));
}

fn logs() -> &'static Mutex<VecDeque<String>> {
    LOGS.get_or_init(|| Mutex::new(VecDeque::with_capacity(MAX_LOG_LINES)))
}

fn timestamp() -> String {
    let now = OffsetDateTime::now_utc()
        .to_offset(UtcOffset::current_local_offset().unwrap_or(UtcOffset::UTC));
    format!(
        "{}/{}/{} {:02}:{:02}:{:02}",
        now.year(),
        u8::from(now.month()),
        now.day(),
        now.hour(),
        now.minute(),
        now.second()
    )
}

fn flatten(message: &str) -> String {
    message.replace('\r', "\\r").replace('\n', "\\n")
}

fn sanitize(message: &str) -> String {
    let text = flatten(message);
    let text = sanitize_path_prefixes(&text);
    let text = sanitize_urls(&text);
    let text = sanitize_key_values(&text);
    let text = collapse_adjacent_masks(&text);
    truncate(text)
}

fn sanitize_path_prefixes(message: &str) -> String {
    let mut sanitized = message.to_string();
    for (key, replacement) in [
        ("USERPROFILE", "%USERPROFILE%"),
        ("HOME", "$HOME"),
        ("TEMP", "%TEMP%"),
        ("TMP", "%TEMP%"),
    ] {
        if let Ok(path) = std::env::var(key) {
            sanitized = replace_path_prefix(&sanitized, &path, replacement);
        }
    }
    sanitized
}

fn replace_path_prefix(message: &str, path: &str, replacement: &str) -> String {
    let normalized = path.trim_end_matches(['\\', '/']);
    if normalized.is_empty() {
        return message.to_string();
    }

    message
        .replace(normalized, replacement)
        .replace(&normalized.replace('\\', "/"), replacement)
}

fn sanitize_urls(message: &str) -> String {
    let mut result = String::with_capacity(message.len());
    let mut index = 0;
    while let Some((start, scheme)) = find_next_uri(&message[index..]) {
        let absolute_start = index + start;
        result.push_str(&message[index..absolute_start]);
        let end = find_uri_end(message, absolute_start);
        let raw = &message[absolute_start..end];
        result.push_str(&sanitize_uri(raw, scheme));
        index = end;
    }

    result.push_str(&message[index..]);
    result
}

fn find_next_uri(message: &str) -> Option<(usize, &'static str)> {
    let lower = message.to_ascii_lowercase();
    let mut best: Option<(usize, &'static str)> = None;
    for (prefix, scheme) in [
        ("http://", "http"),
        ("https://", "https"),
        ("ss://", "ss"),
        ("ssr://", "ssr"),
        ("vmess://", "vmess"),
        ("vless://", "vless"),
        ("trojan://", "trojan"),
        ("hysteria2://", "hysteria2"),
        ("hy2://", "hy2"),
        ("tuic://", "tuic"),
        ("socks://", "socks"),
        ("socks5://", "socks5"),
        ("snell://", "snell"),
    ] {
        if let Some(position) = lower.find(prefix)
            && is_none_or(best, |(current, _)| position < current)
        {
            best = Some((position, scheme));
        }
    }
    best
}

fn find_uri_end(message: &str, start: usize) -> usize {
    for (offset, ch) in message[start..].char_indices() {
        if ch.is_whitespace()
            || matches!(
                ch,
                '"' | '\''
                    | '<'
                    | '>'
                    | '\u{ff0c}'
                    | '\u{3002}'
                    | '\u{ff1b}'
                    | '\u{ff1a}'
                    | '\u{ff01}'
                    | '\u{ff1f}'
            )
        {
            return start + offset;
        }
    }
    message.len()
}

fn sanitize_uri(raw: &str, scheme: &str) -> String {
    let (core, suffix) = trim_trailing_punctuation(raw);
    if matches!(scheme, "http" | "https") {
        return sanitize_http_uri(core, suffix, scheme);
    }

    format!("{scheme}://<redacted>{suffix}")
}

fn sanitize_http_uri(core: &str, suffix: &str, scheme: &str) -> String {
    if !core.contains("://") {
        return format!("{scheme}://<redacted>{suffix}");
    }

    format!("{scheme}://<redacted>{suffix}")
}

fn trim_trailing_punctuation(value: &str) -> (&str, &str) {
    let mut end = value.len();
    for (index, ch) in value.char_indices().rev() {
        if !is_trailing_punctuation(ch) {
            break;
        }
        end = index;
    }

    (&value[..end], &value[end..])
}

fn is_trailing_punctuation(ch: char) -> bool {
    matches!(
        ch,
        '.' | ','
            | ';'
            | ':'
            | '!'
            | '?'
            | ')'
            | ']'
            | '}'
            | '\u{3002}'
            | '\u{ff0c}'
            | '\u{ff1b}'
            | '\u{ff1a}'
            | '\u{ff01}'
            | '\u{ff1f}'
            | '\u{ff09}'
            | '\u{3011}'
            | '\u{ff5d}'
    )
}

fn sanitize_key_values(message: &str) -> String {
    let mut result = message.to_string();
    for key in [
        "access-token",
        "access_token",
        "refresh-token",
        "refresh_token",
        "id-token",
        "id_token",
        "token",
        "secret",
        "password",
        "passwd",
        "pwd",
        "api-key",
        "api_key",
        "apikey",
        "authorization",
        "auth",
        "user-agent",
        "user_agent",
        "ua",
        "url",
        "source",
    ] {
        result = sanitize_key(&result, key);
    }
    result
}

fn sanitize_key(message: &str, key: &str) -> String {
    let lower = message.to_ascii_lowercase();
    let mut result = String::with_capacity(message.len());
    let mut index = 0;
    while let Some(relative) = lower[index..].find(key) {
        let key_start = index + relative;
        if !is_key_boundary(message, key_start, key.len()) {
            result.push_str(&message[index..key_start + key.len()]);
            index = key_start + key.len();
            continue;
        }

        let mut cursor = key_start + key.len();
        cursor = skip_spaces(message, cursor);
        let Some(separator) = next_char(message, cursor) else {
            result.push_str(&message[index..key_start + key.len()]);
            index = key_start + key.len();
            continue;
        };
        if separator != ':' && separator != '=' {
            result.push_str(&message[index..key_start + key.len()]);
            index = key_start + key.len();
            continue;
        }

        cursor += separator.len_utf8();
        cursor = skip_spaces(message, cursor);
        let quote = next_char(message, cursor).filter(|ch| matches!(ch, '"' | '\''));
        if let Some(ch) = quote {
            cursor += ch.len_utf8();
        }
        let value_end = find_value_end(message, cursor, quote, key);

        result.push_str(&message[index..cursor]);
        result.push_str("<redacted>");
        if let Some(ch) =
            quote.and_then(|quote| next_char(message, value_end).filter(|ch| *ch == quote))
        {
            result.push(ch);
            index = value_end + ch.len_utf8();
        } else {
            index = value_end;
        }
    }

    result.push_str(&message[index..]);
    result
}

fn is_key_boundary(message: &str, start: usize, len: usize) -> bool {
    let before = previous_char(message, start);
    let after = next_char(message, start + len);
    is_none_or(before, |ch| !is_key_char(ch)) && is_none_or(after, |ch| !is_key_char(ch))
}

fn is_none_or<T>(value: Option<T>, predicate: impl FnOnce(T) -> bool) -> bool {
    match value {
        Some(value) => predicate(value),
        None => true,
    }
}

fn previous_char(message: &str, index: usize) -> Option<char> {
    message[..index].chars().next_back()
}

fn next_char(message: &str, index: usize) -> Option<char> {
    message[index..].chars().next()
}

fn is_key_char(ch: char) -> bool {
    ch.is_ascii_alphanumeric() || matches!(ch, '_' | '-')
}

fn skip_spaces(message: &str, mut index: usize) -> usize {
    while let Some(ch) = next_char(message, index) {
        if !ch.is_whitespace() {
            break;
        }
        index += ch.len_utf8();
    }
    index
}

fn find_value_end(message: &str, start: usize, quote: Option<char>, key: &str) -> usize {
    for (offset, ch) in message[start..].char_indices() {
        if Some(ch) == quote {
            return start + offset;
        }

        if quote.is_none()
            && (matches!(ch, ',' | ';') || (!allows_spaces_in_value(key) && ch.is_whitespace()))
        {
            return start + offset;
        }
    }
    message.len()
}

fn allows_spaces_in_value(key: &str) -> bool {
    matches!(key, "authorization" | "auth" | "user-agent" | "user_agent")
}

fn truncate(message: String) -> String {
    const MAX_MESSAGE_LENGTH: usize = 6000;
    if message.len() <= MAX_MESSAGE_LENGTH {
        return message;
    }

    let mut end = 0;
    for (index, _) in message.char_indices() {
        if index > MAX_MESSAGE_LENGTH {
            break;
        }
        end = index;
    }
    format!("{}...", &message[..end])
}

fn collapse_adjacent_masks(message: &str) -> String {
    let mut sanitized = message.to_string();
    while sanitized.contains("<redacted><redacted>") {
        sanitized = sanitized.replace("<redacted><redacted>", "<redacted>");
    }
    sanitized
}

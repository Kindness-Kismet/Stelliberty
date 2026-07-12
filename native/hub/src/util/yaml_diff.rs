use serde_yaml_ng::Value as Yaml;

// 这些字段变更需要重启进程，因为 mihomo 只在启动时绑定它们。
pub const RESTART_FIELDS: &[&str] = &[
    "external-controller",
    "external-controller-pipe",
    "external-controller-unix",
    "secret",
    "keep-alive-interval",
];

pub fn needs_restart(previous: &Yaml, next: &Yaml) -> bool {
    RESTART_FIELDS
        .iter()
        .any(|path| !equal_at_path(previous, next, path))
}

fn equal_at_path(a: &Yaml, b: &Yaml, dotted: &str) -> bool {
    let segs: Vec<&str> = dotted.split('.').collect();
    get_path(a, &segs) == get_path(b, &segs)
}

fn get_path<'a>(value: &'a Yaml, segs: &[&str]) -> Option<&'a Yaml> {
    let mut cur = value;
    for seg in segs {
        match cur {
            Yaml::Mapping(map) => {
                let key = Yaml::String((*seg).to_string());
                cur = map.get(&key)?;
            }
            _ => return None,
        }
    }
    Some(cur)
}

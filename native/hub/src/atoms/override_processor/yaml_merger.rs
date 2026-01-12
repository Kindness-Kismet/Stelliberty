// YAML 配置深度合并
//
// 目的：实现支持特殊语法的 YAML 配置合并

use serde_yaml_ng::Value as YamlValue;

// YAML 合并器
pub struct YamlMerger;

impl Default for YamlMerger {
    fn default() -> Self {
        Self
    }
}

impl YamlMerger {
    // 创建新的 YAML 合并器
    pub fn new() -> Self {
        Self
    }

    // 应用 YAML 覆写到基础配置
    //
    // 目的：解析两个 YAML 字符串，深度合并后返回结果
    pub fn apply(&self, base_content: &str, override_content: &str) -> Result<String, String> {
        // 解析基础配置
        let base_value: YamlValue = serde_yaml_ng::from_str(base_content)
            .map_err(|e| format!("解析基础配置失败：{}", e))?;

        // 解析覆写配置
        let override_value: YamlValue = serde_yaml_ng::from_str(override_content)
            .map_err(|e| format!("解析覆写配置失败：{}", e))?;

        // 深度合并
        let merged = Self::deep_merge(base_value, override_value)?;

        // 序列化回 YAML
        serde_yaml_ng::to_string(&merged).map_err(|e| format!("序列化配置失败：{}", e))
    }

    // 深度合并两个 YAML 值
    //
    // 支持特殊键名语法：
    // - `key!`: 强制替换（不递归合并）
    // - `+key`: 数组前置（添加到开头）
    // - `key+`: 数组后置（添加到末尾）
    // - `<key>`: 包装标记，自动去除（用于避免冲突）
    fn deep_merge(base: YamlValue, override_val: YamlValue) -> Result<YamlValue, String> {
        match (base, override_val) {
            (YamlValue::Mapping(mut base_map), YamlValue::Mapping(override_map)) => {
                // 直接使用 base_map，不克隆

                for (key, override_value) in override_map {
                    let key_str = key.as_str().ok_or_else(|| "键必须是字符串".to_string())?;

                    // 1. 强制替换模式 (key!)
                    if let Some(actual_key) = key_str.strip_suffix('!') {
                        let yaml_key = YamlValue::String(actual_key.to_string());
                        base_map.insert(yaml_key, override_value);
                        log::debug!("强制替换：{}", actual_key);
                        continue;
                    }

                    // 2. 数组前置模式 (+key)
                    if let Some(actual_key) = key_str.strip_prefix('+') {
                        let yaml_key = YamlValue::String(actual_key.to_string());

                        if let Some(YamlValue::Sequence(base_arr)) = base_map.get_mut(&yaml_key)
                            && let YamlValue::Sequence(mut override_arr) = override_value
                        {
                            // 使用 std::mem::take 避免克隆
                            let old_arr = std::mem::take(base_arr);
                            override_arr.extend(old_arr);
                            *base_arr = override_arr;
                            log::debug!("数组前置：{}（{}项）", actual_key, base_arr.len());
                            continue;
                        }
                        // 如果不是数组或基础配置不存在，当作普通键处理
                        base_map.insert(yaml_key, override_value);
                        continue;
                    }

                    // 3. 数组后置模式 (key+)
                    if let Some(actual_key) = key_str.strip_suffix('+') {
                        let yaml_key = YamlValue::String(actual_key.to_string());

                        if let Some(YamlValue::Sequence(base_arr)) = base_map.get_mut(&yaml_key)
                            && let YamlValue::Sequence(override_arr) = override_value
                        {
                            base_arr.extend(override_arr);
                            log::debug!("数组后置：{}（{}项）", actual_key, base_arr.len());
                            continue;
                        }
                        // 如果不是数组或基础配置不存在，当作普通键处理
                        base_map.insert(yaml_key, override_value);
                        continue;
                    }

                    // 4. 去除包装标记 (<key>)
                    let clean_key = if key_str.starts_with('<')
                        && key_str.ends_with('>')
                        && key_str.len() > 2
                    {
                        &key_str[1..key_str.len() - 1]
                    } else {
                        key_str
                    };

                    let yaml_key = YamlValue::String(clean_key.to_string());

                    // 5. 默认行为：递归合并或替换
                    if let Some(base_value) = base_map.remove(&yaml_key) {
                        // 使用 remove 避免克隆，然后递归合并
                        let merged_value = Self::deep_merge(base_value, override_value)?;
                        base_map.insert(yaml_key, merged_value);
                    } else {
                        // 基础配置中不存在，直接添加
                        base_map.insert(yaml_key, override_value);
                    }
                }

                Ok(YamlValue::Mapping(base_map))
            }
            (YamlValue::Sequence(base_arr), YamlValue::Sequence(override_arr)) => {
                // 两者都是数组，默认完全替换（不合并）
                log::debug!("数组替换：{} → {}项", base_arr.len(), override_arr.len());
                Ok(YamlValue::Sequence(override_arr))
            }
            (_, override_val) => {
                // 其他情况，覆写值替换基础值
                Ok(override_val)
            }
        }
    }
}

use interoptopus::inventory::RustInventory;
use interoptopus::{builtins_string, function};

use crate::capabilities::{age_text, bootstrap, overrides};

pub fn inventory() -> RustInventory {
    RustInventory::new()
        .register(builtins_string!())
        .register(function!(bootstrap::hub_bootstrap))
        .register(function!(bootstrap::hub_bootstrap_start_core))
        .register(function!(bootstrap::hub_bootstrap_stop_core))
        .register(function!(bootstrap::hub_shutdown))
        .register(function!(age_text::hub_age_text_decrypt))
        .register(function!(overrides::hub_overrides_apply_yaml))
        .register(function!(overrides::hub_overrides_apply_js))
        .validate()
}

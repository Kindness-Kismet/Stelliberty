// Android JNI 初始化器

use jni::JNIEnv;
use jni::objects::{GlobalRef, JObject};
use std::sync::OnceLock;

static ACTIVITY_REF: OnceLock<GlobalRef> = OnceLock::new();

#[unsafe(no_mangle)]
pub extern "system" fn Java_io_github_stelliberty_MainActivity_initAndroidContext<'a>(
    mut env: JNIEnv<'a>,
    _class: JObject<'a>,
    activity: JObject<'a>,
) {
    let vm = match env.get_java_vm() {
        Ok(vm) => vm,
        Err(e) => {
            log::error!("获取 JavaVM 失败: {:?}", e);
            return;
        }
    };

    // init_hosted 会消费 activity，先复制一份
    let activity_for_ndk = unsafe { JObject::from_raw(activity.as_raw()) };
    if let Err(e) = rustls_platform_verifier::android::init_hosted(&mut env, activity) {
        log::error!("rustls-platform-verifier 初始化失败: {:?}", e);
        return;
    }
    log::info!("rustls-platform-verifier 初始化成功");

    let global_activity = match env.new_global_ref(&activity_for_ndk) {
        Ok(global) => global,
        Err(e) => {
            log::error!("创建全局引用失败: {:?}", e);
            return;
        }
    };

    let activity_ptr = global_activity.as_raw();
    let _ = ACTIVITY_REF.set(global_activity);

    let vm_ptr = vm.get_java_vm_pointer();
    unsafe {
        ndk_context::initialize_android_context(vm_ptr.cast(), activity_ptr.cast());
    }
    log::info!("ndk-context 初始化成功");
}

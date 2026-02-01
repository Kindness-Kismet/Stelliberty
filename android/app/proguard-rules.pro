# 保留 JNI 层通过 FindClass/GetMethodID 反射访问的回调接口，避免 release 混淆导致 JNI_OnLoad 失败。
-keep class io.github.stelliberty.android.clash_core.ClashCoreResultCallback {
    *;
}

# 保留 ClashCoreBridge 的 native 方法声明
-keep class io.github.stelliberty.android.clash_core.ClashCoreBridge {
    native <methods>;
    *;
}

# 保留 VpnService 相关类（系统通过反射调用）
-keep class io.github.stelliberty.service.VpnService {
    *;
}

# 保留 BootReceiver（系统广播接收器）
-keep class io.github.stelliberty.BootReceiver {
    *;
}

# 保留 rustls-platform-verifier 的 Android 组件（JNI 调用）
-keep, includedescriptorclasses class org.rustls.platformverifier.** { *; }


package io.github.stelliberty

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent

// 开机广播接收器：监听系统启动完成事件，根据用户设置决定是否启动应用
class BootReceiver : BroadcastReceiver() {

    companion object {
        // SharedPreferences 文件名（与 Flutter 端 shared_preferences 一致）
        private const val PREFS_NAME = "FlutterSharedPreferences"
        // 开机自启动开关键名（flutter_ 前缀是 shared_preferences 插件的约定）
        private const val KEY_AUTO_START = "flutter.auto_start_enabled"
    }

    override fun onReceive(context: Context, intent: Intent) {
        val action = intent.action
        if (action != Intent.ACTION_BOOT_COMPLETED &&
            action != "android.intent.action.QUICKBOOT_POWERON"
        ) {
            return
        }

        // 读取开机自启动设置
        val prefs = context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
        val autoStartEnabled = prefs.getBoolean(KEY_AUTO_START, false)

        if (autoStartEnabled) {
            // 启动主 Activity
            val launchIntent =
                context.packageManager.getLaunchIntentForPackage(context.packageName)?.apply {
                    addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
                    addFlags(Intent.FLAG_ACTIVITY_CLEAR_TOP)
                }
            launchIntent?.let { context.startActivity(it) }
        }
    }
}

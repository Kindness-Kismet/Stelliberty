package io.github.stelliberty

import android.app.Activity
import android.content.Intent
import android.net.VpnService as AndroidVpnService
import androidx.core.content.ContextCompat
import io.flutter.embedding.android.FlutterActivity
import io.flutter.embedding.engine.FlutterEngine
import io.flutter.plugin.common.EventChannel
import io.flutter.plugin.common.MethodChannel
import io.github.stelliberty.android.clash_core.ClashCoreBridgeHelper
import io.github.stelliberty.android.clash_core.ClashCoreRuntime
import io.github.stelliberty.service.VpnService
import java.util.concurrent.Executors
import org.json.JSONTokener

// 主 Activity：负责 Flutter 引擎配置和原生通道注册
class MainActivity : FlutterActivity() {
    // VPN 方法通道名称
    private val vpnChannelName = "io.github.stelliberty/vpn"
    // 开机自启设置方法通道名称
    private val autoStartChannelName = "io.github.stelliberty/auto_start"
    // 核心日志事件通道名称
    private val coreLogChannelName = "io.github.stelliberty/core_log"
    // VPN 权限请求码
    private val vpnPrepareRequestCode = 10001
    // 待处理的 VPN 启动结果回调
    private var pendingVpnStartResult: MethodChannel.Result? = null
    // 待处理的配置文件路径
    private var pendingConfigPath: String? = null
    // 核心初始化专用线程池
    private val coreExecutor = Executors.newSingleThreadExecutor()

    companion object {
        // 核心日志事件接收器（由 EventChannel 设置）
        @Volatile var coreLogEventSink: EventChannel.EventSink? = null
        // SharedPreferences 文件名（与 shared_preferences 插件一致）
        private const val PREFS_NAME = "FlutterSharedPreferences"
        // 开机自启动开关键名
        private const val KEY_AUTO_START = "flutter.auto_start_enabled"
    }

    override fun onDestroy() {
        coreExecutor.shutdownNow()
        super.onDestroy()
    }

    override fun configureFlutterEngine(flutterEngine: FlutterEngine) {
        super.configureFlutterEngine(flutterEngine)

        // 核心日志事件通道：用于将核心日志转发到 Flutter 端
        EventChannel(flutterEngine.dartExecutor.binaryMessenger, coreLogChannelName)
            .setStreamHandler(
                object : EventChannel.StreamHandler {
                    override fun onListen(arguments: Any?, events: EventChannel.EventSink?) {
                        coreLogEventSink = events
                    }

                    override fun onCancel(arguments: Any?) {
                        coreLogEventSink = null
                    }
                }
            )

        // 开机自启方法通道：管理开机自启设置
        MethodChannel(flutterEngine.dartExecutor.binaryMessenger, autoStartChannelName)
            .setMethodCallHandler { call, result ->
                when (call.method) {
                    // 获取开机自启状态
                    "getStatus" -> {
                        val prefs = getSharedPreferences(PREFS_NAME, MODE_PRIVATE)
                        val enabled = prefs.getBoolean(KEY_AUTO_START, false)
                        result.success(enabled)
                    }

                    // 设置开机自启状态
                    "setStatus" -> {
                        val enabled = call.argument<Boolean>("enabled") ?: false
                        val prefs = getSharedPreferences(PREFS_NAME, MODE_PRIVATE)
                        prefs.edit().putBoolean(KEY_AUTO_START, enabled).apply()
                        result.success(true)
                    }

                    else -> result.notImplemented()
                }
            }

        // VPN 方法通道：处理核心初始化和 VPN 控制
        MethodChannel(flutterEngine.dartExecutor.binaryMessenger, vpnChannelName)
            .setMethodCallHandler { call, result ->
                when (call.method) {
                    // 初始化核心
                    "initCore" -> {
                        val configPath = call.argument<String>("configPath")
                        coreExecutor.execute {
                            val ok =
                                ClashCoreRuntime.ensureInitialized(applicationContext, configPath)
                            val payload =
                                mapOf(
                                    "isSuccessful" to ok,
                                    "version" to ClashCoreRuntime.getCoreVersion(),
                                    "startedAtMs" to ClashCoreRuntime.getStartedAtMs(),
                                )
                            runOnUiThread { result.success(payload) }
                        }
                    }

                    // 获取核心版本
                    "getCoreVersion" -> result.success(ClashCoreRuntime.getCoreVersion())

                    // 获取核心状态
                    "getCoreState" -> result.success(ClashCoreRuntime.isCoreInitialized())

                    // 获取核心启动时间
                    "getCoreStartedAtMs" -> result.success(ClashCoreRuntime.getStartedAtMs())

                    // 启动 VPN
                    "startVpn" -> {
                        val configPath = call.argument<String>("configPath")
                        handleStartVpn(configPath, result)
                    }

                    // 停止 VPN
                    "stopVpn" -> {
                        stopVpnService()
                        result.success(true)
                    }

                    // 获取 VPN 状态
                    "getVpnState" -> result.success(VpnService.isRunning())

                    // 调用核心方法（通用接口）
                    "invokeAction" -> {
                        val method = call.argument<String>("method")
                        val data = call.argument<String>("data")
                        if (method.isNullOrBlank()) {
                            result.error("INVALID_ARGS", "method 不能为空", null)
                            return@setMethodCallHandler
                        }
                        if (!ClashCoreRuntime.isCoreInitialized()) {
                            result.error("CORE_NOT_INIT", "核心未初始化", null)
                            return@setMethodCallHandler
                        }
                        coreExecutor.execute {
                            try {
                                val dataObj: Any? =
                                    if (data.isNullOrBlank()) null
                                    else JSONTokener(data).nextValue()
                                val res = ClashCoreBridgeHelper.invokeActionSync(method, dataObj)
                                runOnUiThread { result.success(res.toString()) }
                            } catch (e: Exception) {
                                runOnUiThread { result.error("INVOKE_ERROR", e.message, null) }
                            }
                        }
                    }

                    else -> result.notImplemented()
                }
            }
    }

    // 处理 VPN 启动请求
    private fun handleStartVpn(configPath: String?, result: MethodChannel.Result) {
        if (pendingVpnStartResult != null) {
            result.error("VPN_PREPARE_PENDING", "VPN 权限请求正在进行中", null)
            return
        }

        val prepareIntent = AndroidVpnService.prepare(this)
        if (prepareIntent == null) {
            // 已有 VPN 权限，直接启动
            startVpnService(configPath)
            result.success(true)
            return
        }

        // 需要请求 VPN 权限
        pendingVpnStartResult = result
        pendingConfigPath = configPath
        @Suppress("DEPRECATION") startActivityForResult(prepareIntent, vpnPrepareRequestCode)
    }

    // 启动 VPN 服务
    private fun startVpnService(configPath: String?) {
        val intent =
            Intent(this, VpnService::class.java).apply {
                action = VpnService.actionStart
                putExtra(VpnService.extraConfigPath, configPath)
            }
        ContextCompat.startForegroundService(this, intent)
    }

    // 停止 VPN 服务
    private fun stopVpnService() {
        val intent = Intent(this, VpnService::class.java).apply { action = VpnService.actionStop }
        startService(intent)
    }

    // 处理 VPN 权限请求结果
    @Deprecated("Deprecated in Android API")
    override fun onActivityResult(requestCode: Int, resultCode: Int, data: Intent?) {
        super.onActivityResult(requestCode, resultCode, data)

        if (requestCode != vpnPrepareRequestCode) {
            return
        }

        val result = pendingVpnStartResult ?: return
        val configPath = pendingConfigPath
        pendingVpnStartResult = null
        pendingConfigPath = null

        if (resultCode == Activity.RESULT_OK) {
            startVpnService(configPath)
            result.success(true)
        } else {
            result.success(false)
        }
    }
}

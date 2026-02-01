package io.github.stelliberty

import android.Manifest
import android.app.Activity
import android.content.Intent
import android.content.pm.ApplicationInfo
import android.content.pm.PackageManager
import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.drawable.BitmapDrawable
import android.net.VpnService as AndroidVpnService
import android.util.Log
import androidx.core.content.ContextCompat
import io.flutter.embedding.android.FlutterActivity
import io.flutter.embedding.engine.FlutterEngine
import io.flutter.plugin.common.EventChannel
import io.flutter.plugin.common.MethodChannel
import io.github.stelliberty.android.clash_core.ClashCoreBridgeHelper
import io.github.stelliberty.android.clash_core.ClashCoreRuntime
import io.github.stelliberty.service.VpnService
import java.io.ByteArrayOutputStream
import java.util.concurrent.Executors
import org.json.JSONArray
import org.json.JSONObject
import org.json.JSONTokener

// 主 Activity：负责 Flutter 引擎配置和原生通道注册
class MainActivity : FlutterActivity() {
    // VPN 方法通道名称
    private val vpnChannelName = "io.github.stelliberty/vpn"
    // 开机自启设置方法通道名称
    private val autoStartChannelName = "io.github.stelliberty/auto_start"
    // 核心日志事件通道名称
    private val coreLogChannelName = "io.github.stelliberty/core_log"
    // 应用列表方法通道名称
    private val appListChannelName = "io.github.stelliberty/app_list"
    // VPN 权限请求码
    private val vpnPrepareRequestCode = 10001
    // 待处理的 VPN 启动结果回调
    private var pendingVpnStartResult: MethodChannel.Result? = null
    // 待处理的配置文件路径
    private var pendingConfigPath: String? = null
    // 待处理的访问控制模式
    private var pendingAccessControlMode: Int = VpnService.accessControlModeDisabled
    // 待处理的访问控制应用列表
    private var pendingAccessControlList: ArrayList<String> = arrayListOf()
    // 核心初始化专用线程池
    private val coreExecutor = Executors.newSingleThreadExecutor()

    companion object {
        private const val TAG = "MainActivity"
        // 核心日志事件接收器（由 EventChannel 设置）
        @Volatile var coreLogEventSink: EventChannel.EventSink? = null
        // SharedPreferences 文件名（与 shared_preferences 插件一致）
        private const val PREFS_NAME = "FlutterSharedPreferences"
        // 开机自启动开关键名
        private const val KEY_AUTO_START = "flutter.auto_start_enabled"

        init {
            // 加载 Rust hub 库
            System.loadLibrary("hub")
        }
    }

    // JNI 声明：初始化 Android context 到 Rust 端的 ndk-context
    private external fun initAndroidContext(activity: Activity)

    override fun onDestroy() {
        coreExecutor.shutdownNow()
        super.onDestroy()
    }

    override fun configureFlutterEngine(flutterEngine: FlutterEngine) {
        super.configureFlutterEngine(flutterEngine)

        // 初始化 Rust 端的 ndk-context
        try {
            initAndroidContext(this)
            Log.i(TAG, "ndk-context 初始化成功")
        } catch (e: Exception) {
            Log.e(TAG, "ndk-context 初始化失败: ${e.message}")
        }

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

        // 应用列表方法通道：获取已安装应用列表和图标
        MethodChannel(flutterEngine.dartExecutor.binaryMessenger, appListChannelName)
            .setMethodCallHandler { call, result ->
                when (call.method) {
                    // 获取已安装应用列表
                    "getInstalledApps" -> {
                        coreExecutor.execute {
                            try {
                                val apps = getInstalledApps()
                                runOnUiThread { result.success(apps) }
                            } catch (e: Exception) {
                                runOnUiThread {
                                    result.error("GET_APPS_ERROR", e.message, null)
                                }
                            }
                        }
                    }

                    // 获取应用图标
                    "getAppIcon" -> {
                        val packageName = call.argument<String>("packageName")
                        if (packageName.isNullOrBlank()) {
                            result.error("INVALID_ARGS", "packageName 不能为空", null)
                            return@setMethodCallHandler
                        }
                        coreExecutor.execute {
                            try {
                                val iconBytes = getAppIcon(packageName)
                                runOnUiThread { result.success(iconBytes) }
                            } catch (e: Exception) {
                                runOnUiThread { result.success(null) }
                            }
                        }
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
                        val accessControlMode = call.argument<Int>("accessControlMode")
                            ?: VpnService.accessControlModeDisabled
                        val accessControlList = call.argument<List<String>>("accessControlList")
                            ?: emptyList()
                        handleStartVpn(configPath, accessControlMode, accessControlList, result)
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

    // 获取已安装应用列表
    private fun getInstalledApps(): String {
        val pm = packageManager
        val myPackageName = packageName
        val apps = JSONArray()

        pm.getInstalledPackages(PackageManager.GET_META_DATA or PackageManager.GET_PERMISSIONS)
            .filter { it.packageName != myPackageName && it.packageName != "android" }
            .forEach { packageInfo ->
                val appInfo = packageInfo.applicationInfo ?: return@forEach
                val isSystem = (appInfo.flags and ApplicationInfo.FLAG_SYSTEM) != 0
                val hasInternet =
                    packageInfo.requestedPermissions?.contains(Manifest.permission.INTERNET) == true

                val app = JSONObject().apply {
                    put("packageName", packageInfo.packageName)
                    put("label", appInfo.loadLabel(pm).toString())
                    put("isSystem", isSystem)
                    put("hasInternet", hasInternet)
                }
                apps.put(app)
            }

        return apps.toString()
    }

    // 获取应用图标
    private fun getAppIcon(packageName: String): ByteArray? {
        return try {
            val pm = packageManager
            val drawable = pm.getApplicationIcon(packageName)
            val bitmap = when (drawable) {
                is BitmapDrawable -> drawable.bitmap
                else -> {
                    val width = drawable.intrinsicWidth.coerceAtLeast(1)
                    val height = drawable.intrinsicHeight.coerceAtLeast(1)
                    val bmp = Bitmap.createBitmap(width, height, Bitmap.Config.ARGB_8888)
                    val canvas = Canvas(bmp)
                    drawable.setBounds(0, 0, canvas.width, canvas.height)
                    drawable.draw(canvas)
                    bmp
                }
            }
            // 缩放到合适大小
            val scaledBitmap = Bitmap.createScaledBitmap(bitmap, 96, 96, true)
            val stream = ByteArrayOutputStream()
            scaledBitmap.compress(Bitmap.CompressFormat.PNG, 100, stream)
            stream.toByteArray()
        } catch (e: Exception) {
            null
        }
    }

    // 处理 VPN 启动请求
    private fun handleStartVpn(
        configPath: String?,
        accessControlMode: Int,
        accessControlList: List<String>,
        result: MethodChannel.Result,
    ) {
        if (pendingVpnStartResult != null) {
            result.error("VPN_PREPARE_PENDING", "VPN 权限请求正在进行中", null)
            return
        }

        val prepareIntent = AndroidVpnService.prepare(this)
        if (prepareIntent == null) {
            // 已有 VPN 权限，直接启动
            startVpnService(configPath, accessControlMode, accessControlList)
            result.success(true)
            return
        }

        // 需要请求 VPN 权限
        pendingVpnStartResult = result
        pendingConfigPath = configPath
        pendingAccessControlMode = accessControlMode
        pendingAccessControlList = ArrayList(accessControlList)
        @Suppress("DEPRECATION") startActivityForResult(prepareIntent, vpnPrepareRequestCode)
    }

    // 启动 VPN 服务
    private fun startVpnService(
        configPath: String?,
        accessControlMode: Int,
        accessControlList: List<String>,
    ) {
        val intent =
            Intent(this, VpnService::class.java).apply {
                action = VpnService.actionStart
                putExtra(VpnService.extraConfigPath, configPath)
                putExtra(VpnService.extraAccessControlMode, accessControlMode)
                putStringArrayListExtra(
                    VpnService.extraAccessControlList,
                    ArrayList(accessControlList),
                )
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
            startVpnService(configPath, pendingAccessControlMode, pendingAccessControlList)
            result.success(true)
        } else {
            result.success(false)
        }
        pendingAccessControlMode = VpnService.accessControlModeDisabled
        pendingAccessControlList = arrayListOf()
    }
}

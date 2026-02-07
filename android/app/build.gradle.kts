import groovy.json.JsonSlurper

plugins {
    id("com.android.application")
    id("kotlin-android")
    // The Flutter Gradle Plugin must be applied after the Android and Kotlin Gradle plugins.
    id("dev.flutter.flutter-gradle-plugin")
    id("com.diffplug.spotless") version "7.0.2"
}

// 查找 rustls-platform-verifier 的 Maven 仓库路径
fun findRustlsPlatformVerifierProject(): String {
    val dependencyText = providers.exec {
        workingDir = file("../../native/hub")
        commandLine("cargo", "metadata", "--format-version", "1", "--filter-platform", "aarch64-linux-android")
    }.standardOutput.asText.get()

    val dependencyJson = JsonSlurper().parseText(dependencyText) as Map<*, *>
    val packages = dependencyJson["packages"] as List<*>
    val pkg = packages.find { (it as Map<*, *>)["name"] == "rustls-platform-verifier-android" } as Map<*, *>
    val manifestPath = file(pkg["manifest_path"] as String)
    return File(manifestPath.parentFile, "maven").path
}

val shouldSplitPerAbi: Boolean =
    (project.findProperty("split-per-abi")?.toString() == "true") ||
        (project.findProperty("splitPerAbi")?.toString() == "true")

android {
    namespace = "io.github.stelliberty"
    compileSdk = flutter.compileSdkVersion
    ndkVersion = flutter.ndkVersion

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    signingConfigs {
        if (System.getenv("ANDROID_KEYSTORE_PATH") != null) {
            create("release") {
                storeFile = file(System.getenv("ANDROID_KEYSTORE_PATH")!!)
                storePassword = System.getenv("ANDROID_KEYSTORE_PASSWORD")
                keyAlias = System.getenv("ANDROID_KEY_ALIAS")
                keyPassword = System.getenv("ANDROID_KEY_PASSWORD")
            }
        }
    }

    defaultConfig {
        applicationId = "io.github.stelliberty"
        // You can update the following values to match your application needs.
        // For more information, see: https://flutter.dev/to/review-gradle-config.
        minSdk = flutter.minSdkVersion
        targetSdk = flutter.targetSdkVersion
        versionCode = flutter.versionCode
        versionName = flutter.versionName

        // 根据构建参数设置 ABI 过滤器
        // 优先级：目标架构环境变量 > split-per-abi > 默认双架构
        val targetAbi = project.findProperty("targetAbi")?.toString()
        if (targetAbi != null) {
            // 构建脚本指定了目标架构，只包含该架构
            ndk {
                abiFilters.clear()
                when (targetAbi) {
                    "x86_64" -> abiFilters += listOf("x86_64")
                    "arm64-v8a" -> abiFilters += listOf("arm64-v8a")
                    else -> abiFilters += listOf("x86_64", "arm64-v8a")
                }
            }
        } else if (!shouldSplitPerAbi) {
            // 未指定目标架构且未启用 split-per-abi 时，默认包含双架构
            ndk {
                abiFilters.clear()
                abiFilters += listOf("x86_64", "arm64-v8a")
            }
        }

        externalNativeBuild {
            cmake {
                // 编译 JNI 桥接库（clash_core_bridge），用于加载预编译核心 so 并注入回调。
                cppFlags += "-std=c++17"
            }
        }
    }

    externalNativeBuild {
        cmake {
            path = file("src/main/cpp/CMakeLists.txt")
        }
    }

    splits {
        abi {
            isEnable = shouldSplitPerAbi
            reset()
            include("x86_64", "arm64-v8a")
            isUniversalApk = false
        }
    }

    // 添加预编译的核心 so 文件路径
    sourceSets {
        getByName("main") {
            java.setSrcDirs(listOf("src/main/kotlin", "src/main/java"))
            jniLibs.srcDirs("../../assets/jniLibs")
        }
    }

    buildTypes {
        release {
            isMinifyEnabled = true
            isShrinkResources = true
            signingConfig = signingConfigs.findByName("release")
                ?: signingConfigs.getByName("debug")

            proguardFiles(
                getDefaultProguardFile("proguard-android-optimize.txt"),
                "proguard-rules.pro",
            )
        }
    }
}

kotlin {
    compilerOptions {
        jvmTarget.set(org.jetbrains.kotlin.gradle.dsl.JvmTarget.JVM_17)
    }
}

flutter {
    source = "../.."
}

spotless {
    kotlin {
        target("src/**/*.kt")
        ktfmt().kotlinlangStyle()
    }
}

repositories {
    maven {
        url = uri(findRustlsPlatformVerifierProject())
        metadataSources.artifact()
    }
}

dependencies {
    implementation("rustls:rustls-platform-verifier:latest.release")
}

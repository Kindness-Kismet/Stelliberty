<div align="center">

# 🌟 Stelliberty

[![简体中文](https://img.shields.io/badge/简体中文-red)](README.zh-CN.md)
[![English](https://img.shields.io/badge/English-blue)](../../README.md)
[![日本語](https://img.shields.io/badge/日本語-blue)](README.ja.md)
[![한국어](https://img.shields.io/badge/한국어-blue)](README.ko.md)
[![Deutsch](https://img.shields.io/badge/Deutsch-blue)](README.de.md)
[![Français](https://img.shields.io/badge/Français-blue)](README.fr.md)

![正式版](https://img.shields.io/github/v/release/Kindness-Kismet/Stelliberty?style=flat-square&label=正式版)
![最新版](https://img.shields.io/github/v/tag/Kindness-Kismet/Stelliberty?style=flat-square&label=最新版&color=orange)
![Flutter](https://img.shields.io/badge/Flutter-3.38%2B-02569B?style=flat-square&logo=flutter)
![Rust](https://img.shields.io/badge/Rust-1.91%2B-orange?style=flat-square&logo=rust)
![License](https://img.shields.io/badge/license-Stelliberty-green?style=flat-square)

![Windows](https://img.shields.io/badge/Windows-0078D6?style=flat-square&logo=windows11&logoColor=white)
![Linux](https://img.shields.io/badge/Linux-FCC624?style=flat-square&logo=linux&logoColor=black)
![macOS](https://img.shields.io/badge/macOS-实验性-gray?style=flat-square&logo=apple&logoColor=white)
![Android](https://img.shields.io/badge/Android-暂不支持-lightgray?style=flat-square&logo=android&logoColor=white)

基于 Flutter 和 Rust 构建的现代跨平台 Clash 客户端
采用独特的 **MD3M**（Material Design 3 Modern）视觉风格

</div>

## 📸 应用截图

<table>
  <tr>
    <td width="50%"><img src="../../.github/screenshots/home-page.jpg" alt="主页"/></td>
    <td width="50%"><img src="../../.github/screenshots/uwp-loopback-manager.jpg" alt="UWP 回环管理器"/></td>
  </tr>
  <tr>
    <td align="center"><b>主页</b></td>
    <td align="center"><b>UWP 回环管理器</b></td>
  </tr>
</table>

---

## ✨ 特性

- 🎨 **MD3M 设计系统**：独特的 Material Design 3 Modern 风格，结合 MD3 色彩管理与磨砂玻璃效果
- 🦀 **Rust 后端**：高性能 Rust 核心驱动，Flutter UI 呈现
- 🌐 **多语言支持**：使用 slang 框架的内置国际化支持
- 🔧 **订阅管理**：完整的订阅和覆写配置支持
- 📊 **实时监控**：连接跟踪和流量统计
- 🪟 **原生桌面集成**：Windows 服务、系统托盘和开机自启支持
- 🔄 **内置 UWP 回环管理器**：管理 Windows UWP 应用的回环豁免权限（仅限 Windows）

### 🏆 实现亮点

本应用可能是细节做得最好的 Flutter 桌面应用之一：

- ✨ **系统托盘夜间模式**：Windows 托盘图标自动适配深色/浅色主题
- 🚀 **无闪烁启动**：最大化窗口启动不产生视觉闪烁
- 👻 **流畅窗口切换**：显示/隐藏窗口动画无闪烁
- 🎯 **像素级完美 UI**：精心打造的 MD3M 设计系统

---

## 📋 用户指南

### 系统要求

- **Windows**: Windows 10/11 (x64 / arm64)
- **Linux**: 主流发行版 (x64 / arm64)
- **macOS**: 实验性

> ⚠️ **平台状态**：目前已在 Windows 和 Linux 上完整测试。macOS 支持为实验性，部分功能可能不完整。

### 安装方法

**下载选项：**
- **稳定版本**：[Releases](https://github.com/Kindness-Kismet/stelliberty/releases)
- **测试版本**：[预发布页面](https://github.com/Kindness-Kismet/stelliberty/releases?q=prerelease%3Atrue)（体验最新特性）

**安装方式（Windows）：**

#### 方式一：便携版（ZIP 压缩包）
1. 从发布页面下载 `.zip` 文件
2. 解压到任意位置（如 `D:\Stelliberty`）
3. 直接运行解压目录中的 `stelliberty.exe`
4. ✅ 无需安装，开箱即用

#### 方式二：安装程序（EXE）
1. 从发布页面下载 `.exe` 安装程序
2. 运行安装程序并按照向导完成安装
3. 选择安装位置（参见下方限制说明）
4. 从桌面快捷方式启动应用
5. ✅ 包含卸载程序和桌面快捷方式

**安装目录限制说明：**

为确保安全性和稳定性，安装程序对安装路径有以下限制：

- **系统盘（通常是 C: 盘）**：
  - ✅ 允许安装到：`%LOCALAPPDATA%\Programs\*`（如 `C:\Users\用户名\AppData\Local\Programs\Stelliberty`）
  - ❌ 禁止安装到：系统盘根目录（如 `C:\`）
  - ❌ 禁止安装到：系统盘的其他所有路径
  
- **其他盘（D:、E: 等）**：
  - ✅ 完全自由，无任何限制
  - ✅ 可安装到根目录（如 `D:\`、`E:\Stelliberty`）

> 💡 **建议**：为获得最佳体验，建议安装到非系统盘（如 `D:\Stelliberty`、`E:\Apps\Stelliberty`），避免潜在的权限问题。

> 📌 **注意**：默认安装路径 `%LOCALAPPDATA%\Programs\Stelliberty` 无需特殊权限，推荐大多数用户使用。

**安装方式（Linux）：**

#### Arch Linux (AUR)
支持架构：`x86_64`、`aarch64`

使用 `yay`:
```bash
yay -S stelliberty-bin
```

使用 `paru`:
```bash
paru -S stelliberty-bin
```

> AUR 软件包链接：[stelliberty-bin](https://aur.archlinux.org/packages/stelliberty-bin)

---

#### 便携版（ZIP 压缩包）
1. 从发布页面下载适用于您架构（`amd64` 或 `arm64`）的 `.zip` 文件
2. 解压到任意位置（如 `~/Stelliberty`）
3. **重要：** 为应用文件夹赋予权限：
   ```bash
   chmod 777 -R ./stelliberty
   ```
4. 直接运行解压目录中的 `./stelliberty`
5. ✅ 开箱即用

### 问题反馈

如果遇到任何问题：

1. 在 **设置** → **应用行为** 中开启 **应用日志** 功能
2. 重现问题以生成日志
3. 在应用运行目录下的 `data` 目录中找到日志文件
4. 消除日志中的隐私信息
5. 在 GitHub 创建 issue 并附上处理后的日志文件
6. 描述问题和重现步骤

---

## 🛠️ 开发者指南

### 前置条件

在构建本项目之前，请确保已安装以下工具：

- **Flutter SDK**（建议最新稳定版，最低 3.38）
- **Rust 工具链**（建议最新稳定版，最低 1.91）
- **Dart SDK**（Flutter 自带）

> 📖 本指南假设您熟悉 Flutter 和 Rust 开发环境。这些工具的安装说明不在此赘述。

### 依赖安装

#### 1. 安装脚本依赖

预构建脚本需要额外的 Dart 包：

```bash
cd scripts
dart pub get
```

#### 2. 安装 rinf CLI

全局安装 Rust-Flutter 桥接工具：

```bash
cargo install rinf_cli
```

#### 3. 安装项目依赖

```bash
flutter pub get
```

#### 4. 生成必要代码

安装依赖后，生成 Rust-Flutter 桥接代码和国际化翻译文件：

```bash
# 生成 Rust-Flutter 桥接代码
rinf gen

# 生成国际化翻译文件
dart run slang
```

> 💡 **重要**：首次构建项目前必须执行这些代码生成步骤。

### 构建项目

#### 预构建准备

**构建项目前必须先运行预构建脚本：**

```bash
dart run scripts/prebuild.dart
```

**预构建脚本参数：**

```bash
# 显示帮助信息
dart run scripts/prebuild.dart --help

# 安装平台打包工具（Windows: Inno Setup，Linux: dpkg/rpm/appimagetool）
dart run scripts/prebuild.dart --installer

# Android 支持（暂未实现）
dart run scripts/prebuild.dart --android
```

**预构建脚本做什么？**

1. ✅ 清理资源目录（保留 `test/` 文件夹）
2. ✅ 编译 `stelliberty-service`（桌面平台服务模式可执行文件）
3. ✅ 复制平台特定的托盘图标
4. ✅ 下载最新的 Mihomo 核心二进制文件
5. ✅ 下载 GeoIP/GeoSite 数据文件

#### 快速构建

使用构建脚本编译和打包：

```bash
# 显示帮助信息
dart run scripts/build.dart --help

# 构建 Release 版本（默认：仅 ZIP）
dart run scripts/build.dart

# 同时构建 Debug 版本
dart run scripts/build.dart --with-debug

# 同时生成安装包（Windows：ZIP + EXE，Linux：ZIP + DEB/RPM/AppImage）
dart run scripts/build.dart --with-installer

# 仅生成安装包，不含 ZIP（Windows：EXE，Linux：DEB/RPM/AppImage）
dart run scripts/build.dart --installer-only

# 完整构建（Release + Debug，含安装包）
dart run scripts/build.dart --with-debug --with-installer

# 干净构建
dart run scripts/build.dart --clean

# 构建 Android APK（暂不支持）
dart run scripts/build.dart --android
```

**构建脚本参数：**

| 参数 | 说明 |
|------|------|
| `-h, --help` | 显示帮助信息 |
| `--with-debug` | 同时构建 Release 和 Debug 版本 |
| `--with-installer` | 生成 ZIP + 安装包（Windows：EXE，Linux：DEB/RPM/AppImage） |
| `--installer-only` | 仅生成安装包，不含 ZIP |
| `--clean` | 构建前运行 `flutter clean` |
| `--android` | 构建 Android APK（暂不支持） |

**输出位置：**

构建的包将位于 `build/packages/` 目录

#### 已知限制

⚠️ **平台支持状态**：

- ✅ **Windows**：完整测试和支持
- ⚠️ **Linux**：核心功能可用，但系统集成（服务、自启动）未经验证
- ⚠️ **macOS**：核心功能可用，但系统集成为实验性
- ❌ **Android**：尚未实现

⚠️ **不可用的参数**：

- `--android`：Android 平台尚未适配

### 手动开发流程

#### 生成 Rust-Flutter 绑定

修改 Rust 信号结构体（带信号属性）后：

```bash
rinf gen
```

> 📖 Rinf 使用 Rust 结构体上的信号属性来定义消息，而不是 `.proto` 文件。详见 [Rinf 文档](https://rinf.cunarist.com)。

#### 生成国际化翻译

修改 `lib/i18n/strings/` 中的翻译文件后：

```bash
dart run slang
```

#### 运行开发构建

```bash
# 先运行预构建
dart run scripts/prebuild.dart

# 启动开发
flutter run
```

#### 开发测试

项目内置了测试框架，用于隔离测试特定功能：

```bash
# 运行覆写规则测试（支持 YAML 或 JS 规则）
flutter run --dart-define=TEST_TYPE=override

# 运行 IPC API 测试
flutter run --dart-define=TEST_TYPE=ipc-api
```

**所需测试文件** 位于 `assets/test/`：

- **`override` 测试所需文件：**
  ```
  assets/test/
  ├── config/
  │   └── test.yaml          # 用于测试的基础配置文件
  ├── override/
  │   ├── your_script.js     # JS 覆写脚本
  │   └── your_rules.yaml    # YAML 覆写规则
  └── output/
      └── final.yaml         # 应用覆写后预期的最终输出文件
  ```

- **`ipc-api` 测试所需文件：**
  > **提示**：建议在测试前运行预编译脚本（`dart run scripts/prebuild.dart`）以下载所需资源。
  ```
  assets/test/
  └── config/
      └── test.yaml          # 用于测试的基础配置文件
  ```

> 💡 **注意**：测试模式仅在 Debug 构建中可用，Release 模式下自动禁用。

测试实现：`lib/dev_test/`（`override_test.dart`、`ipc_api_test.dart`）

---

## ❓ 故障排查

### 端口被占用（Windows）

如果遇到端口冲突：

```bash
# 1. 查找占用端口的进程
netstat -ano | findstr :端口号

# 2. 结束进程（以管理员身份运行）
taskkill /F /PID XXX
```

> ⚠️ **重要**：必须以管理员身份运行命令提示符。服务模式启动的核心进程需要提升权限才能终止。

### 软件工作不正常

**路径要求**（ZIP 和 EXE 均适用）：

- 路径中不应包含特殊字符（空格除外）
- 路径中不应包含非 ASCII 字符（如中文字符）
- 支持空格：`D:\Program Files\Stelliberty` ✅

**EXE 安装程序的位置限制**：

如果使用 EXE 安装程序，还有额外的安装位置限制：

- **系统盘（C: 盘）**：仅允许安装到 `%LOCALAPPDATA%\Programs\*`
- **其他盘（D:、E: 等）**：无任何限制

> 💡 **提示**：如需安装到 EXE 安装程序不允许的位置，请使用**便携版 ZIP 压缩包**。ZIP 版本无位置限制，但仍可能受到系统目录权限的影响（如解压到 `C:\Windows` 或 `C:\Program Files` 可能需要管理员权限）。

### 缺少运行库（Windows）

如果应用程序在 Windows 上无法启动或立即崩溃，可能是缺少必需的 Visual C++ 运行库。

**解决方案：**

安装 Visual C++ 运行库：[vcredist - Visual C++ 运行库合集](https://gitlab.com/stdout12/vcredist)

---

## 🎨 关于 MD3M 设计

**MD3M（Material Design 3 Modern）** 是一个独特的设计系统，融合了：

- 🎨 **Material Design 3**：现代色彩系统和排版
- 🪟 **磨砂玻璃效果**：半透明背景与模糊效果
- 🌈 **系统主题集成**：自动适配系统强调色
- 🌗 **深色模式支持**：无缝的明暗主题切换

这创造了一种现代、优雅的桌面应用体验，在各平台上都具有原生般的流畅感受。

---

## 📋 代码规范

- ✅ `flutter analyze` 和 `cargo clippy` 无警告
- ✅ 提交前使用 `dart format` 和 `cargo fmt` 格式化代码
- ✅ 不要修改自动生成的文件（`lib/src/bindings/`、`lib/i18n/`）
- ✅ 使用事件驱动架构，避免滥用 `setState`
- ✅ Rust 代码必须使用 `Result<T, E>`，禁止 `unwrap()`
- ✅ Dart 代码必须保持 null safety

---

## 📄 许可证

本项目采用 **Stelliberty License（星辰自由许可证）** - 详见 [LICENSE](../../LICENSE) 文件。

**简而言之**：你可以随心所欲地使用本软件，没有任何限制，无需署名。

---

<div align="center">

由 Flutter 和 Rust 驱动

</div>
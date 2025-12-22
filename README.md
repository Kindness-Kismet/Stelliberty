<div align="center">

# 🌟 Stelliberty

[![English](https://img.shields.io/badge/English-red)](README.md)
[![简体中文](https://img.shields.io/badge/简体中文-blue)](.github/docs/README.zh-CN.md)
[![日本語](https://img.shields.io/badge/日本語-blue)](.github/docs/README.ja.md)
[![한국어](https://img.shields.io/badge/한국어-blue)](.github/docs/README.ko.md)
[![Deutsch](https://img.shields.io/badge/Deutsch-blue)](.github/docs/README.de.md)
[![Français](https://img.shields.io/badge/Français-blue)](.github/docs/README.fr.md)

![Stable Version](https://img.shields.io/github/v/release/Kindness-Kismet/Stelliberty?style=flat-square&label=Stable)
![Latest Version](https://img.shields.io/github/v/tag/Kindness-Kismet/Stelliberty?style=flat-square&label=Latest&color=orange)
![Flutter](https://img.shields.io/badge/Flutter-3.38%2B-02569B?style=flat-square&logo=flutter)
![Rust](https://img.shields.io/badge/Rust-1.91%2B-orange?style=flat-square&logo=rust)
![License](https://img.shields.io/badge/license-Stelliberty-green?style=flat-square)

![Windows](https://img.shields.io/badge/Windows-0078D6?style=flat-square&logo=windows11&logoColor=white)
![Linux](https://img.shields.io/badge/Linux-FCC624?style=flat-square&logo=linux&logoColor=black) ![macOS](https://img.shields.io/badge/macOS-experimental-gray?style=flat-square&logo=apple&logoColor=white)
![Android](https://img.shields.io/badge/Android-not_supported-lightgray?style=flat-square&logo=android&logoColor=white)

A modern cross-platform Clash client built with Flutter and Rust
Featuring the unique **MD3M** (Material Design 3 Modern) visual style

</div>

## 📸 Screenshots

<table>
  <tr>
    <td width="50%"><img src=".github/screenshots/home-page.jpg" alt="Home Page"/></td>
    <td width="50%"><img src=".github/screenshots/uwp-loopback-manager.jpg" alt="UWP Loopback Manager"/></td>
  </tr>
  <tr>
    <td align="center"><b>Home Page</b></td>
    <td align="center"><b>UWP Loopback Manager</b></td>
  </tr>
</table>

---

## ✨ Features

- 🎨 **MD3M Design System**: Unique Material Design 3 Modern style combining MD3 color management with acrylic glass effects
- 🦀 **Rust Backend**: High-performance core powered by Rust with Flutter UI
- 🌐 **Multi-language Support**: Built-in i18n support using slang
- 🔧 **Subscription Management**: Full subscription and override configuration support
- 📊 **Real-time Monitoring**: Connection tracking and traffic statistics
- 🪟 **Native Desktop Integration**: Windows service, system tray, and auto-start support
- 🔄 **Built-in UWP Loopback Manager**: Manage Windows UWP app loopback exemptions (Windows only)

### 🏆 Implementation Highlights

This might be one of the most detail-oriented Flutter desktop applications:

- ✨ **System Tray Dark Mode**: Adaptive tray icons for Windows dark/light themes
- 🚀 **Flicker-Free Launch**: Maximized window startup without visual artifacts
- 👻 **Smooth Window Transitions**: Show/hide animations without flickering
- 🎯 **Pixel-Perfect UI**: Carefully crafted MD3M design system

---

## 📋 User Guide

### System Requirements

- **Windows**: Windows 10/11 (x64 / arm64)
- **Linux**: Mainstream distributions (x64 / arm64)
- **macOS**: Experimental

> ⚠️ **Platform Status**: Fully tested on Windows and Linux. macOS support is experimental and may have incomplete functionality.

### Installation

**Download Options:**
- **Stable Version**: [Releases](https://github.com/Kindness-Kismet/stelliberty/releases)
- **Beta Version**: [Pre-releases](https://github.com/Kindness-Kismet/stelliberty/releases?q=prerelease%3Atrue) (latest features)

**Installation Methods (Windows):**

#### Option 1: Portable Version (ZIP Archive)
1. Download the `.zip` file from the release page
2. Extract to your desired location (e.g., `D:\Stelliberty`)
3. Run `stelliberty.exe` directly from the extracted folder
4. ✅ No installation required, fully portable

#### Option 2: Installer (EXE)
1. Download the `.exe` installer from the release page
2. Run the installer and follow the setup wizard
3. Choose installation location (see restrictions below)
4. Launch the application from the desktop shortcut
5. ✅ Includes uninstaller and desktop shortcut

**Installation Directory Restrictions:**

The installer enforces the following path restrictions for security and stability:

- **System Drive (Usually C:)**:
  - ✅ Allowed: `%LOCALAPPDATA%\Programs\*` (e.g., `C:\Users\YourName\AppData\Local\Programs\Stelliberty`)
  - ❌ Prohibited: System drive root directory (e.g., `C:\`)
  - ❌ Prohibited: All other paths on system drive
  
- **Other Drives (D:, E:, etc.)**:
  - ✅ No restrictions - Install anywhere you like
  - ✅ Root directories allowed (e.g., `D:\`, `E:\Stelliberty`)

> 💡 **Recommendation**: For best experience, install to non-system drives (e.g., `D:\Stelliberty`, `E:\Apps\Stelliberty`) to avoid potential permission issues.

> 📌 **Note**: The default installation path `%LOCALAPPDATA%\Programs\Stelliberty` requires no special permissions and is recommended for most users.

**Installation Methods (Linux):**

#### Arch Linux (AUR)
Supported architectures: `x86_64`, `aarch64`

Using `yay`:
```bash
yay -S stelliberty-bin
```

Using `paru`:
```bash
paru -S stelliberty-bin
```

> AUR Package: [stelliberty-bin](https://aur.archlinux.org/packages/stelliberty-bin)

---

#### Portable Version (ZIP Archive)
1. Download the `.zip` file for your architecture (`amd64` or `arm64`) from the release page.
2. Extract it to your desired location (e.g., `~/Stelliberty`).
3. **Important:** Grant permissions to the application folder:
   ```bash
   chmod 777 -R ./stelliberty
   ```
4. Run `./stelliberty` from the extracted directory.
5. ✅ Ready to use.

### Reporting Issues

If you encounter any issues:

1. Enable **Application Logging** in **Settings** → **App Behavior**
2. Reproduce the issue to generate logs
3. Find log files in the `data` directory under the application installation directory
4. Remove any sensitive/private information from the logs
5. Create an issue on GitHub and attach the sanitized log file
6. Describe the problem and steps to reproduce

---

## 🛠️ For Developers

### Prerequisites

Before building this project, ensure you have the following installed:

- **Flutter SDK** (latest stable version recommended, minimum 3.38)
- **Rust toolchain** (latest stable version recommended, minimum 1.91)
- **Dart SDK** (included with Flutter)

> 📖 This guide assumes you are familiar with Flutter and Rust development. Installation instructions for these tools are not covered here.

### Dependencies Installation

#### 1. Install Script Dependencies

The prebuild script requires additional Dart packages:

```bash
cd scripts
dart pub get
```

#### 2. Install rinf CLI

Install the Rust-Flutter bridge tool globally:

```bash
cargo install rinf_cli
```

#### 3. Install Project Dependencies

```bash
flutter pub get
```

#### 4. Generate Required Code

After installing dependencies, generate Rust-Flutter bindings and i18n translations:

```bash
# Generate Rust-Flutter bridge code
rinf gen

# Generate i18n translation files
dart run slang
```

> 💡 **Important**: These generation steps are required before building the project for the first time.

### Building the Project

#### Pre-build Preparation

**Always run the prebuild script before building the project:**

```bash
dart run scripts/prebuild.dart
```

**Prebuild script parameters:**

```bash
# Show help
dart run scripts/prebuild.dart --help

# Install platform packaging tools (Windows: Inno Setup, Linux: dpkg/rpm/appimagetool)
dart run scripts/prebuild.dart --installer

# Android support (not implemented yet)
dart run scripts/prebuild.dart --android
```

**What does prebuild do?**

1. ✅ Cleans asset directories (preserves `test/` folder)
2. ✅ Compiles `stelliberty-service` (desktop service executable)
3. ✅ Copies platform-specific tray icons
4. ✅ Downloads latest Mihomo core binary
5. ✅ Downloads GeoIP/GeoSite data files

#### Quick Build

Use the build script to compile and package:

```bash
# Show help
dart run scripts/build.dart --help

# Build Release version for current platform (default: ZIP only)
dart run scripts/build.dart

# Build with Debug version too
dart run scripts/build.dart --with-debug

# Build with installer package (Windows: ZIP + EXE, Linux: ZIP + DEB/RPM/AppImage)
dart run scripts/build.dart --with-installer

# Build installer only, no ZIP (Windows: EXE, Linux: DEB/RPM/AppImage)
dart run scripts/build.dart --installer-only

# Full build (Release + Debug, with installer)
dart run scripts/build.dart --with-debug --with-installer

# Clean build
dart run scripts/build.dart --clean

# Build Android APK (not supported yet)
dart run scripts/build.dart --android
```

**Build script parameters:**

| Parameter | Description |
|-----------|-------------|
| `-h, --help` | Show help information |
| `--with-debug` | Build both Release and Debug versions |
| `--with-installer` | Generate ZIP + installer (Windows: EXE, Linux: DEB/RPM/AppImage) |
| `--installer-only` | Generate installer only, no ZIP |
| `--clean` | Run `flutter clean` before building |
| `--android` | Build Android APK (not supported yet) |

**Output location:**

Built packages will be in `build/packages/`

#### Known Limitations

⚠️ **Platform Support Status**:

- ✅ **Windows**: Fully tested and supported
- ⚠️ **Linux**: Core functionality works, but system integration (service, auto-start) is unverified
- ⚠️ **macOS**: Core functionality works, but system integration is experimental
- ❌ **Android**: Not implemented yet

⚠️ **Unsupported Parameters**:

- `--android`: Android platform is not adapted yet

### Manual Development Workflow

#### Generate Rust-Flutter Bindings

After modifying Rust signal structs (with signal attributes):

```bash
rinf gen
```

> 📖 Rinf uses signal attributes on Rust structs to define messages, not `.proto` files. See [Rinf documentation](https://rinf.cunarist.com) for details.

#### Generate i18n Translations

After modifying translation files in `lib/i18n/strings/`:

```bash
dart run slang
```

#### Run Development Build

```bash
# Run prebuild first
dart run scripts/prebuild.dart

# Start development
flutter run
```

#### Development Testing

For developers, the project includes a test framework for isolated feature testing:

```bash
# Run override rule test (supports YAML or JS rules)
flutter run --dart-define=TEST_TYPE=override

# Run IPC API test
flutter run --dart-define=TEST_TYPE=ipc-api
```

**Required test files** in `assets/test/`:

- **For `override` test:**
  ```
  assets/test/
  ├── config/
  │   └── test.yaml          # Base configuration file for testing
  ├── override/
  │   ├── your_script.js     # JS override script
  │   └── your_rules.yaml    # YAML override rules
  └── output/
      └── final.yaml         # Expected final output after applying overrides
  ```

- **For `ipc-api` test:**
  > **Note**: It is recommended to run the pre-build script (`dart run scripts/prebuild.dart`) before this test to download the necessary resources.
  ```
  assets/test/
  └── config/
      └── test.yaml          # Base configuration file for testing
  ```

> 💡 **Note**: Test mode is only available in Debug builds and automatically disabled in Release mode.

Test implementations: `lib/dev_test/` (`override_test.dart`, `ipc_api_test.dart`)

---

## ❓ Troubleshooting

### Port Already in Use (Windows)

If you encounter port conflicts:

```bash
# 1. Find the process using the port
netstat -ano | findstr :端口号

# 2. Kill the process (run as Administrator)
taskkill /F /PID XXX
```

> ⚠️ **Important**: Run Command Prompt as Administrator. Service-mode core processes require elevated privileges to terminate.

### Software Not Working Properly

**Path Requirements** (applies to both ZIP and EXE):

- Path should NOT contain special characters (except spaces)
- Path should NOT contain non-ASCII characters (e.g., Chinese characters)
- Spaces are supported: `D:\Program Files\Stelliberty` ✅

**EXE Installer Location Restrictions**:

If you used the EXE installer, additional location restrictions apply:

- **System Drive (C:)**: Only allowed in `%LOCALAPPDATA%\Programs\*`
- **Other Drives (D:, E:, etc.)**: No restrictions

> 💡 **Tip**: Use the **portable ZIP version** if you need to install to a location not allowed by the EXE installer. The ZIP version has no location restrictions, but may still be affected by system directory permissions (e.g., installing to `C:\Windows` or `C:\Program Files` may require administrator privileges).

### Missing Runtime Libraries (Windows)

If the application fails to start or crashes immediately on Windows, you may be missing required Visual C++ runtime libraries.

**Solution:**

Install Visual C++ runtimes: [vcredist - Visual C++ Redistributable Runtimes AIO](https://gitlab.com/stdout12/vcredist)

---

## 🎨 About MD3M Design

**MD3M (Material Design 3 Modern)** is a unique design system that combines:

- 🎨 **Material Design 3**: Modern color system and typography
- 🪟 **Acrylic Glass Effects**: Translucent backgrounds with blur effects
- 🌈 **System Theme Integration**: Automatically adapts to system accent colors
- 🌗 **Dark Mode Support**: Seamless light/dark theme switching

This creates a modern, elegant desktop application experience with native-like feel across all platforms.

---

## 📋 Code Standards

- ✅ No warnings from `flutter analyze` and `cargo clippy`
- ✅ Format code with `dart format` and `cargo fmt` before committing
- ✅ Do not modify auto-generated files (`lib/src/bindings/`, `lib/i18n/`)
- ✅ Use event-driven architecture, avoid `setState` abuse
- ✅ Rust code must use `Result<T, E>`, no `unwrap()`
- ✅ Dart code must maintain null safety

---

## 📄 License

This project is licensed under the **Stelliberty License** - see the [LICENSE](LICENSE) file for details.

**TL;DR**: Do whatever you want with this software. No restrictions, no attribution required.

---

<div align="center">

Powered by Flutter & Rust

</div>

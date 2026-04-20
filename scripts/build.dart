import 'dart:io';
import 'package:args/args.dart';
import 'package:path/path.dart' as p;
import 'package:archive/archive_io.dart';

// 导入模块化功能
import 'lib/common.dart';
import 'lib/app_version.dart';
import 'lib/inno_setup.dart' as inno;

// 获取当前平台名称
String _getCurrentPlatform() {
  if (Platform.isWindows) return 'windows';
  if (Platform.isMacOS) return 'macos';
  if (Platform.isLinux) return 'linux';
  throw Exception('不支持的平台');
}

// 获取当前架构（x64/arm64/x86，用于 Flutter 构建路径和文件命名）
String _getCurrentArchitecture() {
  final version = Platform.version;
  if (version.contains('arm64') || version.contains('aarch64')) {
    return 'arm64';
  } else if (version.contains('x64') || version.contains('x86_64')) {
    return 'x64';
  } else if (version.contains('ia32') || version.contains('x86')) {
    return 'x86';
  }
  return 'x64'; // 默认
}

// 终止 Rust 编译进程 (跨平台支持, 成功时静默)
Future<void> _killRustProcesses() async {
  try {
    if (Platform.isWindows) {
      // Windows: 终止 rustc.exe
      final result = await Process.run('taskkill', [
        '/F',
        '/IM',
        'rustc.exe',
        '/T',
      ]);
      if (result.exitCode != 0 && result.exitCode != 128) {
        // exitCode 128 表示进程不存在,这是正常的
        log('⚠️  终止 Rust 进程时出现警告: ${result.stderr}');
      }
    } else if (Platform.isLinux || Platform.isMacOS) {
      // Linux/macOS: 终止 rustc
      final result = await Process.run('pkill', ['-9', 'rustc']);
      if (result.exitCode != 0 && result.exitCode != 1) {
        // exitCode 1 表示进程不存在,这是正常的
        log('⚠️  终止 Rust 进程时出现警告: ${result.stderr}');
      }
    }
    await Future.delayed(Duration(milliseconds: 500));
  } catch (e) {
    log('⚠️  终止 Rust 进程失败: $e');
  }
}

// 运行 flutter clean
Future<void> _runFlutterClean(String projectRoot, String flutterCmd) async {
  final result = await Process.run(flutterCmd, [
    'clean',
  ], workingDirectory: projectRoot);

  if (result.exitCode != 0) {
    log('⚠️  flutter clean 执行失败');
    log(result.stderr.toString().trim());
    // 不抛出异常,继续执行其他清理任务
  }
}

// 运行 cargo clean
Future<void> _runCargoClean(String projectRoot) async {
  // 检查是否有 Cargo.toml 文件
  final cargoToml = File(p.join(projectRoot, 'Cargo.toml'));
  if (!await cargoToml.exists()) {
    log('⏭️  跳过 cargo clean (未找到 Cargo.toml)');
    return;
  }

  // 在执行 cargo clean 前先终止 Rust 编译进程
  await _killRustProcesses();

  final result = await Process.run('cargo', [
    'clean',
  ], workingDirectory: projectRoot);

  if (result.exitCode != 0) {
    log('⚠️  cargo clean 执行失败 (可能 cargo 未安装或进程被占用)');
    log(result.stderr.toString().trim());
    // 不抛出异常,继续执行其他清理任务
  }
}

// 运行完整清理流程
Future<void> runFlutterClean(
  String projectRoot, {
  bool skipClean = false,
}) async {
  if (skipClean) {
    log('⏭️  跳过构建缓存清理（--dirty 模式）');
    return;
  }

  final flutterCmd = await resolveFlutterCmd();

  log('🧹 开始清理构建缓存...');

  // 静默终止 Rust 编译进程,避免文件占用
  await _killRustProcesses();

  // Flutter 缓存清理
  await _runFlutterClean(projectRoot, flutterCmd);

  // Rust 缓存清理
  await _runCargoClean(projectRoot);

  log('✅ 所有清理任务已完成');
}

// 获取构建输出目录
String getBuildOutputDir(String projectRoot, String platform, bool isRelease) {
  final mode = isRelease ? 'Release' : 'Debug';
  final arch = _getCurrentArchitecture();

  switch (platform) {
    case 'windows':
      // Windows 支持 x64 和 arm64
      return p.join(projectRoot, 'build', 'windows', arch, 'runner', mode);
    case 'macos':
      return p.join(projectRoot, 'build', 'macos', 'Build', 'Products', mode);
    case 'linux':
      // Linux 支持 x64 和 arm64
      return p.join(
        projectRoot,
        'build',
        'linux',
        arch,
        isRelease ? 'release' : 'debug',
        'bundle',
      );
    case 'apk':
      return p.join(projectRoot, 'build', 'app', 'outputs', 'flutter-apk');
    default:
      throw Exception('不支持的平台: $platform');
  }
}

// 获取 Android 输出文件名
String getAndroidOutputFile(
  String sourceDir,
  bool isRelease,
  bool isAppBundle,
) {
  final dir = Directory(sourceDir);
  if (!dir.existsSync()) {
    throw Exception('构建目录不存在: $sourceDir');
  }

  if (isAppBundle) {
    // AAB 文件
    final files = dir
        .listSync()
        .whereType<File>()
        .where((f) => f.path.endsWith('.aab'))
        .toList();
    if (files.isEmpty) throw Exception('未找到 .aab 文件');
    return files.first.path;
  } else {
    // APK 文件
    final files = dir
        .listSync()
        .whereType<File>()
        .where((f) => f.path.endsWith('.apk'))
        .toList();
    if (files.isEmpty) throw Exception('未找到 .apk 文件');
    return files.first.path;
  }
}

// 获取 Android 构建产物（支持 --split-per-abi 多 APK）
List<String> getAndroidOutputFiles(
  String sourceDir, {
  required bool isRelease,
  required bool isAppBundle,
}) {
  final dir = Directory(sourceDir);
  if (!dir.existsSync()) {
    throw Exception('构建目录不存在: $sourceDir');
  }

  final extension = isAppBundle ? '.aab' : '.apk';
  final files =
      dir
          .listSync()
          .whereType<File>()
          .where((f) => f.path.endsWith(extension))
          .where((f) {
            final name = p.basename(f.path);
            if (isAppBundle) return true;
            return isRelease
                ? name.endsWith('-release.apk')
                : name.endsWith('-debug.apk');
          })
          .map((f) => f.path)
          .toList()
        ..sort();

  if (files.isEmpty) {
    throw Exception('未找到 $extension 文件');
  }

  return files;
}

String _getAndroidAbiLabelFromApkPath(String apkPath) {
  final fileName = p.basename(apkPath);
  final match = RegExp(r'^app-(.+)-(release|debug)\.apk$').firstMatch(fileName);
  if (match == null) return 'universal';
  return match.group(1) ?? 'universal';
}

String? _getAndroidExpectedAbiLabel(String androidArch) {
  switch (androidArch) {
    case 'arm64':
      return 'arm64-v8a';
    case 'x64':
      return 'x86_64';
    case 'all':
      return null;
    default:
      return null;
  }
}

// 获取 Android 目标 ABI（用于 Gradle abiFilters）
String? _getAndroidTargetAbi(String androidArch) {
  switch (androidArch) {
    case 'arm64':
      return 'arm64-v8a';
    case 'x64':
      return 'x86_64';
    case 'all':
      return null;
    default:
      return null;
  }
}

List<String> getAndroidBuildExtraArgs({
  required String androidArch,
  required bool shouldSplitPerAbi,
}) {
  final extraArgs = <String>[];

  switch (androidArch) {
    case 'arm64':
      extraArgs.add('--target-platform=android-arm64');
      break;
    case 'x64':
      extraArgs.add('--target-platform=android-x64');
      break;
    case 'all':
      // Flutter 的默认 ABI 列表包含 armeabi-v7a，但该工程未提供对应核心 so，
      // 在 --split-per-abi 场景下需要显式限制 target-platform，避免产物缺失导致构建失败。
      if (shouldSplitPerAbi) {
        extraArgs.add('--target-platform=android-arm64,android-x64');
      }
      break;
  }

  if (shouldSplitPerAbi) {
    extraArgs.add('--split-per-abi');
  }

  return extraArgs;
}

// 运行 flutter build
Future<void> runFlutterBuild({
  required String projectRoot,
  required String platform,
  required bool isRelease,
  List<String> extraArgs = const [],
  String? androidTargetAbi,
}) async {
  final flutterCmd = await resolveFlutterCmd();
  final mode = isRelease ? 'release' : 'debug';

  final buildTypeLabel = isRelease ? 'Release' : 'Debug';
  log('▶️  正在构建 $platform $buildTypeLabel 版本...');

  // 处理 Android 目标架构的 Gradle 属性
  if (platform == 'android' || platform == 'apk') {
    final gradlePropsPath = p.join(projectRoot, 'android', 'gradle.properties');
    final gradleProps = File(gradlePropsPath);
    final lines = await gradleProps.readAsLines();

    // 移除旧的 targetAbi 属性
    final filteredLines = lines
        .where((l) => !l.startsWith('targetAbi='))
        .toList();

    // 如果指定了目标架构，添加 targetAbi 属性
    if (androidTargetAbi != null) {
      filteredLines.add('targetAbi=$androidTargetAbi');
      log('📝 设置 Gradle targetAbi=$androidTargetAbi');
    }

    await gradleProps.writeAsString(filteredLines.join('\n'));
  }

  // 构建命令
  final buildCommand = ['build', platform, '--$mode', ...extraArgs];

  final result = await Process.run(
    flutterCmd,
    buildCommand,
    workingDirectory: projectRoot,
  );

  if (result.exitCode != 0) {
    log('❌ 构建失败');
    log(result.stdout);
    log(result.stderr);
    throw Exception('Flutter 构建失败');
  }

  log('✅ 构建完成');
}

// 打包为 ZIP（使用 archive 包）
// 便携版会在 data 目录创建 .portable 标识文件
Future<void> packZip({
  required String sourceDir,
  required String outputPath,
}) async {
  log('▶️  正在打包为 ZIP（便携版）...');

  // 确保输出目录存在
  final outputDir = Directory(p.dirname(outputPath));
  if (!await outputDir.exists()) {
    await outputDir.create(recursive: true);
  }

  // 删除已存在的同名文件
  final outputFile = File(outputPath);
  if (await outputFile.exists()) {
    await outputFile.delete();
  }

  // 创建 Archive 对象
  final archive = Archive();

  // 递归添加所有文件
  final sourceDirectory = Directory(sourceDir);
  final files = sourceDirectory.listSync(recursive: true);

  for (final entity in files) {
    if (entity is File) {
      final relativePath = p.relative(entity.path, from: sourceDir);
      final bytes = await entity.readAsBytes();

      // 添加文件到归档
      final archiveFile = ArchiveFile(
        relativePath.replaceAll('\\', '/'), // 统一使用 / 作为路径分隔符
        bytes.length,
        bytes,
      );

      archive.addFile(archiveFile);

      // 显示进度
      log('📦 添加: $relativePath');
    }
  }

  // 添加便携版标识文件到 data 目录
  const portableMarkerPath = 'data/.portable';
  final portableMarkerFile = ArchiveFile(
    portableMarkerPath,
    0,
    [], // 空文件
  );
  archive.addFile(portableMarkerFile);
  log('📦 添加: $portableMarkerPath（便携版标识）');

  log('📦 正在压缩（最大压缩率）...');

  // 使用 ZIP 编码器压缩，设置最大压缩等级（archive 4.x 使用 9）
  final encoder = ZipEncoder();
  final zipData = encoder.encode(archive, level: 9);

  // 写入 ZIP 文件
  await File(outputPath).writeAsBytes(zipData);

  // 显示文件大小
  final fileSize = await File(outputPath).length();
  final sizeInMB = (fileSize / (1024 * 1024)).toStringAsFixed(2);
  log('✅ 打包完成: ${p.basename(outputPath)} ($sizeInMB MB)');
}

// ============================================================================
// Linux 打包函数
// ============================================================================

// Linux 打包入口：生成 deb + rpm + AppImage
Future<void> packLinuxInstallers({
  required String projectRoot,
  required String sourceDir,
  required String outputDir,
  required String appName,
  required String version,
  required String arch,
  required bool isDebug,
}) async {
  final debugSuffix = isDebug ? '-debug' : '';
  final appNameCapitalized =
      '${appName.substring(0, 1).toUpperCase()}${appName.substring(1)}';

  // 转换架构名称
  final debArch = _getDebArch(arch);
  final rpmArch = _getRpmArch(arch);

  // 打包 DEB
  await packDeb(
    projectRoot: projectRoot,
    sourceDir: sourceDir,
    outputPath: p.join(
      outputDir,
      '$appNameCapitalized-v$version-linux-$arch$debugSuffix.deb',
    ),
    appName: appName,
    version: version,
    arch: debArch,
  );

  // 打包 RPM
  await packRpm(
    projectRoot: projectRoot,
    sourceDir: sourceDir,
    outputPath: p.join(
      outputDir,
      '$appNameCapitalized-v$version-linux-$arch$debugSuffix.rpm',
    ),
    appName: appName,
    version: version,
    arch: rpmArch,
  );

  // 打包 AppImage
  await packAppImage(
    projectRoot: projectRoot,
    sourceDir: sourceDir,
    outputPath: p.join(
      outputDir,
      '$appNameCapitalized-v$version-linux-$arch$debugSuffix.AppImage',
    ),
    appName: appName,
    version: version,
  );
}

// 获取 DEB 架构名称
String _getDebArch(String arch) {
  switch (arch) {
    case 'x64':
      return 'amd64';
    case 'arm64':
      return 'arm64';
    default:
      return arch;
  }
}

// 获取 RPM 架构名称
String _getRpmArch(String arch) {
  switch (arch) {
    case 'x64':
      return 'x86_64';
    case 'arm64':
      return 'aarch64';
    default:
      return arch;
  }
}

// 打包为 DEB（Debian/Ubuntu）
Future<void> packDeb({
  required String projectRoot,
  required String sourceDir,
  required String outputPath,
  required String appName,
  required String version,
  required String arch,
}) async {
  log('▶️  正在打包为 DEB...');

  // 检查 dpkg-deb 是否可用
  final dpkgCheck = await Process.run('which', ['dpkg-deb']);
  if (dpkgCheck.exitCode != 0) {
    log('⚠️  dpkg-deb 未安装，跳过 DEB 打包');
    log('   提示：运行 dart run scripts/prebuild.dart --installer 安装打包工具');
    return;
  }

  final appNameCapitalized =
      '${appName.substring(0, 1).toUpperCase()}${appName.substring(1)}';
  final appNameLower = appName.toLowerCase();

  // 创建临时打包目录
  final tempDir = await Directory.systemTemp.createTemp('deb_build_');
  final debRoot = p.join(tempDir.path, '${appNameLower}_$version');

  try {
    // 创建 DEB 目录结构
    final installDir = p.join(debRoot, 'opt', appNameLower);
    final debianDir = p.join(debRoot, 'DEBIAN');
    final applicationsDir = p.join(debRoot, 'usr', 'share', 'applications');
    final iconsDir = p.join(
      debRoot,
      'usr',
      'share',
      'icons',
      'hicolor',
      '256x256',
      'apps',
    );

    await Directory(installDir).create(recursive: true);
    await Directory(debianDir).create(recursive: true);
    await Directory(applicationsDir).create(recursive: true);
    await Directory(iconsDir).create(recursive: true);

    // 复制应用文件
    await _copyDirectory(Directory(sourceDir), Directory(installDir));

    // 生成 control 文件
    final controlContent =
        '''
Package: $appNameLower
Version: $version
Section: net
Priority: optional
Architecture: $arch
Maintainer: $appNameCapitalized Team <support@$appNameLower.app>
Description: $appNameCapitalized - Network Proxy Client
 A modern network proxy client with a beautiful Flutter UI.
 Features system proxy, TUN mode, and traffic monitoring.
Depends: libgtk-3-0, libblkid1, liblzma5
''';
    await File(p.join(debianDir, 'control')).writeAsString(controlContent);

    // 生成 postinst 脚本（安装后执行）
    final postinstContent =
        '''
#!/bin/bash
set -e

# 设置可执行权限
chmod +x /opt/$appNameLower/$appNameLower
if [ -f /opt/$appNameLower/data/flutter_assets/assets/clash/clash-core ]; then
    chmod +x /opt/$appNameLower/data/flutter_assets/assets/clash/clash-core
fi

# 创建符号链接
ln -sf /opt/$appNameLower/$appNameLower /usr/local/bin/$appNameLower

# 更新桌面数据库
if command -v update-desktop-database &> /dev/null; then
    update-desktop-database /usr/share/applications || true
fi
''';
    final postinstFile = File(p.join(debianDir, 'postinst'));
    await postinstFile.writeAsString(postinstContent);
    await Process.run('chmod', ['+x', postinstFile.path]);

    // 生成 prerm 脚本（卸载前执行）
    final prermContent =
        '''
#!/bin/bash
set -e

# 删除符号链接
rm -f /usr/local/bin/$appNameLower
''';
    final prermFile = File(p.join(debianDir, 'prerm'));
    await prermFile.writeAsString(prermContent);
    await Process.run('chmod', ['+x', prermFile.path]);

    // 生成 .desktop 文件
    final desktopContent =
        '''
[Desktop Entry]
Type=Application
Name=$appNameCapitalized
Comment=Network Proxy Client
Exec=/opt/$appNameLower/$appNameLower
Icon=$appNameLower
Terminal=false
Categories=Network;Utility;
StartupNotify=true
''';
    await File(
      p.join(applicationsDir, '$appNameLower.desktop'),
    ).writeAsString(desktopContent);

    // 复制图标（如果存在）
    final iconSource = File(
      p.join(
        projectRoot,
        'scripts',
        'pre_assets',
        'tray_icon',
        'linux',
        'proxy_enabled.png',
      ),
    );
    if (await iconSource.exists()) {
      await iconSource.copy(p.join(iconsDir, '$appNameLower.png'));
    }

    // 确保输出目录存在
    await Directory(p.dirname(outputPath)).create(recursive: true);

    // 构建 DEB 包
    final result = await Process.run('dpkg-deb', [
      '--build',
      '--root-owner-group',
      debRoot,
      outputPath,
    ]);

    if (result.exitCode != 0) {
      log('❌ DEB 打包失败');
      log(result.stderr);
      throw Exception('dpkg-deb 打包失败');
    }

    final fileSize = await File(outputPath).length();
    final sizeInMB = (fileSize / (1024 * 1024)).toStringAsFixed(2);
    log('✅ DEB 打包完成: ${p.basename(outputPath)} ($sizeInMB MB)');
  } finally {
    // 清理临时目录
    await tempDir.delete(recursive: true);
  }
}

// 打包为 RPM（Fedora/RHEL/CentOS）
Future<void> packRpm({
  required String projectRoot,
  required String sourceDir,
  required String outputPath,
  required String appName,
  required String version,
  required String arch,
}) async {
  log('▶️  正在打包为 RPM...');

  // 检查 rpmbuild 是否可用
  final rpmCheck = await Process.run('which', ['rpmbuild']);
  if (rpmCheck.exitCode != 0) {
    log('⚠️  rpmbuild 未安装，跳过 RPM 打包');
    log('   提示：运行 dart run scripts/prebuild.dart --installer 安装打包工具');
    return;
  }

  final appNameCapitalized =
      '${appName.substring(0, 1).toUpperCase()}${appName.substring(1)}';
  final appNameLower = appName.toLowerCase();

  // 创建临时打包目录
  final tempDir = await Directory.systemTemp.createTemp('rpm_build_');
  final rpmBuildDir = tempDir.path;

  try {
    // 创建 RPM 构建目录结构
    final specDir = p.join(rpmBuildDir, 'SPECS');
    final sourcesDir = p.join(rpmBuildDir, 'SOURCES');
    final buildRootDir = p.join(rpmBuildDir, 'BUILDROOT');

    await Directory(specDir).create(recursive: true);
    await Directory(sourcesDir).create(recursive: true);
    await Directory(buildRootDir).create(recursive: true);

    // 创建 tarball
    final tarballName = '$appNameLower-$version.tar.gz';
    final tarballPath = p.join(sourcesDir, tarballName);

    // 创建临时目录用于 tarball
    final tarTempDir = await Directory.systemTemp.createTemp('rpm_tar_');
    final tarSourceDir = p.join(tarTempDir.path, '$appNameLower-$version');
    await Directory(tarSourceDir).create(recursive: true);
    await _copyDirectory(Directory(sourceDir), Directory(tarSourceDir));

    // 创建 tarball
    await Process.run('tar', [
      '-czf',
      tarballPath,
      '-C',
      tarTempDir.path,
      '$appNameLower-$version',
    ]);
    await tarTempDir.delete(recursive: true);

    // 生成 SPEC 文件
    final specContent =
        '''
Name:           $appNameLower
Version:        $version
Release:        1%{?dist}
Summary:        $appNameCapitalized - Network Proxy Client

License:        Proprietary
URL:            https://$appNameLower.app
Source0:        %{name}-%{version}.tar.gz

BuildArch:      $arch
Requires:       gtk3, libblkid, xz-libs

%description
A modern network proxy client with a beautiful Flutter UI.
Features system proxy, TUN mode, and traffic monitoring.

%prep
%setup -q

%install
mkdir -p %{buildroot}/opt/%{name}
cp -r * %{buildroot}/opt/%{name}/

mkdir -p %{buildroot}/usr/share/applications
cat > %{buildroot}/usr/share/applications/%{name}.desktop << EOF
[Desktop Entry]
Type=Application
Name=$appNameCapitalized
Comment=Network Proxy Client
Exec=/opt/%{name}/%{name}
Icon=%{name}
Terminal=false
Categories=Network;Utility;
StartupNotify=true
EOF

mkdir -p %{buildroot}/usr/local/bin
ln -sf /opt/%{name}/%{name} %{buildroot}/usr/local/bin/%{name}

%files
/opt/%{name}
/usr/share/applications/%{name}.desktop
/usr/local/bin/%{name}

%post
chmod +x /opt/%{name}/%{name}
if [ -f /opt/%{name}/data/flutter_assets/assets/clash/clash-core ]; then
    chmod +x /opt/%{name}/data/flutter_assets/assets/clash/clash-core
fi
update-desktop-database /usr/share/applications || true

%preun
# 卸载前无需特殊操作

%changelog
* \$(date '+%a %b %d %Y') $appNameCapitalized Team <support@$appNameLower.app> - $version-1
- Initial package
''';
    await File(
      p.join(specDir, '$appNameLower.spec'),
    ).writeAsString(specContent);

    // 构建 RPM 包
    final result = await Process.run('rpmbuild', [
      '-bb',
      '--define',
      '_topdir $rpmBuildDir',
      p.join(specDir, '$appNameLower.spec'),
    ]);

    if (result.exitCode != 0) {
      log('❌ RPM 打包失败');
      log(result.stderr);
      throw Exception('rpmbuild 打包失败');
    }

    // 查找生成的 RPM 文件
    final rpmsDir = Directory(p.join(rpmBuildDir, 'RPMS', arch));
    if (await rpmsDir.exists()) {
      final rpmFiles = await rpmsDir
          .list()
          .where((f) => f.path.endsWith('.rpm'))
          .toList();
      if (rpmFiles.isNotEmpty) {
        await Directory(p.dirname(outputPath)).create(recursive: true);
        await File(rpmFiles.first.path).copy(outputPath);

        final fileSize = await File(outputPath).length();
        final sizeInMB = (fileSize / (1024 * 1024)).toStringAsFixed(2);
        log('✅ RPM 打包完成: ${p.basename(outputPath)} ($sizeInMB MB)');
      }
    } else {
      log('⚠️  未找到生成的 RPM 文件');
    }
  } finally {
    // 清理临时目录
    await tempDir.delete(recursive: true);
  }
}

// 打包为 AppImage（通用 Linux 格式）
Future<void> packAppImage({
  required String projectRoot,
  required String sourceDir,
  required String outputPath,
  required String appName,
  required String version,
}) async {
  log('▶️  正在打包为 AppImage...');

  // appimagetool 存放在 assets/tools 目录
  final appImageToolPath = p.join(
    projectRoot,
    'assets',
    'tools',
    'appimagetool',
  );
  if (!await File(appImageToolPath).exists()) {
    log('⚠️  appimagetool 未安装，跳过 AppImage 打包');
    log('   提示：运行 dart run scripts/prebuild.dart --installer 安装打包工具');
    return;
  }

  final appNameCapitalized =
      '${appName.substring(0, 1).toUpperCase()}${appName.substring(1)}';
  final appNameLower = appName.toLowerCase();

  // 创建临时 AppDir 目录
  final tempDir = await Directory.systemTemp.createTemp('appimage_build_');
  final appDir = p.join(tempDir.path, '$appNameCapitalized.AppDir');

  try {
    // 创建 AppDir 结构
    final usrBinDir = p.join(appDir, 'usr', 'bin');
    final usrLibDir = p.join(appDir, 'usr', 'lib');
    final usrShareDir = p.join(appDir, 'usr', 'share');
    final applicationsDir = p.join(usrShareDir, 'applications');
    final iconsDir = p.join(usrShareDir, 'icons', 'hicolor', '256x256', 'apps');

    await Directory(usrBinDir).create(recursive: true);
    await Directory(usrLibDir).create(recursive: true);
    await Directory(applicationsDir).create(recursive: true);
    await Directory(iconsDir).create(recursive: true);

    // 复制应用文件到 usr/bin
    await _copyDirectory(Directory(sourceDir), Directory(usrBinDir));

    // 生成 AppRun 脚本
    final appRunContent =
        '''
#!/bin/bash
SELF=\$(readlink -f "\$0")
HERE=\${SELF%/*}
export PATH="\$HERE/usr/bin:\$PATH"
export LD_LIBRARY_PATH="\$HERE/usr/lib:\$HERE/usr/bin/lib:\$LD_LIBRARY_PATH"
exec "\$HERE/usr/bin/$appNameLower" "\$@"
''';
    final appRunFile = File(p.join(appDir, 'AppRun'));
    await appRunFile.writeAsString(appRunContent);
    await Process.run('chmod', ['+x', appRunFile.path]);

    // 生成 .desktop 文件
    final desktopContent =
        '''
[Desktop Entry]
Type=Application
Name=$appNameCapitalized
Comment=Network Proxy Client
Exec=$appNameLower
Icon=$appNameLower
Terminal=false
Categories=Network;Utility;
StartupNotify=true
''';
    await File(
      p.join(appDir, '$appNameLower.desktop'),
    ).writeAsString(desktopContent);
    await File(
      p.join(applicationsDir, '$appNameLower.desktop'),
    ).writeAsString(desktopContent);

    // 复制图标
    final iconSource = File(
      p.join(
        projectRoot,
        'scripts',
        'pre_assets',
        'tray_icon',
        'linux',
        'proxy_enabled.png',
      ),
    );
    if (await iconSource.exists()) {
      await iconSource.copy(p.join(appDir, '$appNameLower.png'));
      await iconSource.copy(p.join(iconsDir, '$appNameLower.png'));
    } else {
      // 创建一个空的占位图标
      log('⚠️  未找到图标文件，将使用默认图标');
    }

    // 确保输出目录存在
    await Directory(p.dirname(outputPath)).create(recursive: true);

    // 构建 AppImage
    final result = await Process.run(
      appImageToolPath,
      [appDir, outputPath],
      environment: {'ARCH': 'x86_64'},
    );

    if (result.exitCode != 0) {
      log('❌ AppImage 打包失败');
      log(result.stderr);
      throw Exception('appimagetool 打包失败');
    }

    final fileSize = await File(outputPath).length();
    final sizeInMB = (fileSize / (1024 * 1024)).toStringAsFixed(2);
    log('✅ AppImage 打包完成: ${p.basename(outputPath)} ($sizeInMB MB)');
  } finally {
    // 清理临时目录
    await tempDir.delete(recursive: true);
  }
}

// 辅助函数：递归复制目录
Future<void> _copyDirectory(Directory source, Directory destination) async {
  if (!await destination.exists()) {
    await destination.create(recursive: true);
  }

  await for (final entity in source.list(recursive: false)) {
    final newPath = p.join(destination.path, p.basename(entity.path));

    if (entity is File) {
      await entity.copy(newPath);
    } else if (entity is Directory) {
      await _copyDirectory(entity, Directory(newPath));
    }
  }
}

// 主函数
Future<void> main(List<String> args) async {
  // 记录开始时间
  final startTime = DateTime.now();

  final parser = ArgParser()
    ..addFlag(
      'with-debug',
      negatable: false,
      help: '同时构建 Debug 版本（默认只构建 Release）',
    )
    ..addFlag('clean', negatable: false, help: '执行 flutter clean 进行干净构建')
    ..addFlag('android', negatable: false, help: '构建 Android APK')
    ..addOption(
      'android-arch',
      allowed: ['all', 'arm64', 'x64'],
      defaultsTo: 'all',
      help: 'Android 仅构建指定架构（用于拆分 CI 工作流）：all/arm64/x64',
    )
    ..addFlag(
      'split-per-abi',
      defaultsTo: false,
      help: 'Android APK 按 ABI 拆分输出（生成多个 APK，而不是合并到一个包）',
    )
    ..addFlag(
      'with-installer',
      negatable: false,
      help:
          '同时生成 ZIP 便携版和平台特定安装包（Windows: ZIP + EXE, Linux: ZIP + deb + rpm + AppImage）',
    )
    ..addFlag(
      'installer-only',
      negatable: false,
      help: '只生成平台特定安装包，不含 ZIP（Windows: 仅 EXE, Linux: 仅 deb + rpm + AppImage）',
    )
    ..addFlag('help', abbr: 'h', negatable: false, help: '显示帮助信息');

  ArgResults argResults;
  try {
    argResults = parser.parse(args);
  } catch (e) {
    log('❌ 参数错误: ${e.toString()}\n');
    log(parser.usage);
    exit(1);
  }

  if (argResults['help'] as bool) {
    log('Flutter 多平台打包脚本（桌面平台自动识别）');
    log('\n用法: dart run scripts/build.dart [选项]\n');
    log('选项:');
    log(parser.usage);
    log('\n支持平台: Windows, macOS, Linux, Android (APK)');
    log('\n示例:');
    log(
      '  dart run scripts/build.dart                            # 默认：Release ZIP',
    );
    log(
      '  dart run scripts/build.dart --with-debug               # Release + Debug ZIP',
    );
    log(
      '  dart run scripts/build.dart --with-installer           # Release ZIP + EXE',
    );
    log(
      '  dart run scripts/build.dart --installer-only           # Release EXE only',
    );
    log('  dart run scripts/build.dart --with-debug --with-installer  # 完整打包');
    log('  dart run scripts/build.dart --clean                    # 干净构建');
    log(
      '  dart run scripts/build.dart --android                  # Android APK',
    );
    log(
      '  dart run scripts/build.dart --android --android-arch=arm64  # Android 仅 arm64',
    );
    log(
      '  dart run scripts/build.dart --android --android-arch=x64    # Android 仅 x86_64',
    );
    log(
      '  dart run scripts/build.dart --android --split-per-abi  # Android APK 按 ABI 分包',
    );
    exit(0); // 显式退出
  }

  final projectRoot = p.dirname(p.dirname(Platform.script.toFilePath()));

  // 获取参数
  final shouldClean = argResults['clean'] as bool;
  final withDebug = argResults['with-debug'] as bool;
  final isAndroid = argResults['android'] as bool;
  final androidArch = argResults['android-arch'] as String;
  final shouldSplitPerAbi = argResults['split-per-abi'] as bool;
  final withInstaller = argResults['with-installer'] as bool;
  final installerOnly = argResults['installer-only'] as bool;

  // 参数冲突检查
  if (withInstaller && installerOnly) {
    log('❌ 错误: --with-installer 和 --installer-only 不能同时使用');
    log('   提示：');
    log('   • 默认：Release ZIP');
    log('   • --with-installer：Release ZIP + 平台安装包');
    log('   • --installer-only：Release 平台安装包');
    log('   • --with-debug：同时构建 Debug 版本');
    exit(1);
  }

  if (!isAndroid && (androidArch != 'all' || shouldSplitPerAbi)) {
    log('⚠️  警告: --android-arch / --split-per-abi 仅在 --android 模式下生效');
  }

  // 打包格式逻辑（简化版）：
  // 默认：只生成 ZIP
  // --with-installer：生成 ZIP + 平台安装包
  // --installer-only：只生成平台安装包
  final shouldPackZip = !installerOnly;
  final shouldPackInstaller =
      (withInstaller || installerOnly) &&
      (Platform.isWindows || Platform.isLinux);

  if (installerOnly && !(Platform.isWindows || Platform.isLinux)) {
    log('❌ 错误: --installer-only 仅支持 Windows 和 Linux 平台');
    exit(1);
  }

  if (withInstaller && !(Platform.isWindows || Platform.isLinux)) {
    log('⚠️  警告: --with-installer 在非 Windows/Linux 平台只生成 ZIP');
    log('    （平台特定安装包仅 Windows 和 Linux 支持）');
  }

  // 版本构建逻辑（简化版）：
  // 默认：只构建 Release
  // --with-debug：同时构建 Release + Debug
  final shouldBuildRelease = true; // 始终构建 Release
  final shouldBuildDebug = withDebug;

  try {
    // 步骤 1: 识别平台
    String platform;
    bool needZipPack = true;

    if (isAndroid) {
      // 检查 Android 支持
      final androidDir = Directory(p.join(projectRoot, 'android'));
      if (!await androidDir.exists()) {
        log('❌ 错误: 项目暂未适配 Android 平台');
        exit(1);
      }

      platform = 'apk';
      needZipPack = false; // Android 不需要打包成 ZIP
      log('📱 构建 Android APK');
    } else {
      platform = _getCurrentPlatform();
      log('🖥️  检测到桌面平台: $platform');
    }

    // 步骤 2: 读取版本信息
    final versionInfo = await readVersionInfo(projectRoot);
    final appName = versionInfo['name']!;
    final version = versionInfo['version']!;

    log('🚀 开始打包 $appName v$version');

    // 步骤 3: 运行 flutter clean（如果指定了 --clean）
    await runFlutterClean(projectRoot, skipClean: !shouldClean);

    // 输出目录
    final outputDir = p.join(projectRoot, 'build', 'packages');

    // 步骤 4: 构建 Release
    if (shouldBuildRelease) {
      final androidTargetAbi = isAndroid
          ? _getAndroidTargetAbi(androidArch)
          : null;
      await runFlutterBuild(
        projectRoot: projectRoot,
        platform: platform,
        isRelease: true,
        extraArgs: isAndroid
            ? getAndroidBuildExtraArgs(
                androidArch: androidArch,
                shouldSplitPerAbi: shouldSplitPerAbi,
              )
            : const [],
        androidTargetAbi: androidTargetAbi,
      );

      if (needZipPack) {
        // 桌面平台：打包成 ZIP 或/和 EXE
        final sourceDir = getBuildOutputDir(projectRoot, platform, true);
        final platformSuffix = platform; // 使用完整平台名：windows, macos, linux
        final arch = _getCurrentArchitecture();

        // 打包为 ZIP
        if (shouldPackZip) {
          final outputPath = p.join(
            outputDir,
            '${appName.substring(0, 1).toUpperCase()}${appName.substring(1)}-v$version-$platformSuffix-$arch.zip',
          );

          await packZip(sourceDir: sourceDir, outputPath: outputPath);
        }

        // 打包为平台安装包
        if (shouldPackInstaller) {
          if (Platform.isWindows) {
            // Windows: Inno Setup EXE
            final outputPath = p.join(
              outputDir,
              '${appName.substring(0, 1).toUpperCase()}${appName.substring(1)}-v$version-$platformSuffix-$arch-setup.exe',
            );

            await inno.packInnoSetup(
              projectRoot: projectRoot,
              sourceDir: sourceDir,
              outputPath: outputPath,
              appName: appName,
              version: version,
              arch: arch,
            );
          } else if (Platform.isLinux) {
            // Linux: deb + rpm + AppImage
            await packLinuxInstallers(
              projectRoot: projectRoot,
              sourceDir: sourceDir,
              outputDir: outputDir,
              appName: appName,
              version: version,
              arch: arch,
              isDebug: false,
            );
          }
        }
      } else {
        // Android：复制 APK 文件（支持 --split-per-abi 多 APK）
        final sourceDir = getBuildOutputDir(projectRoot, platform, true);
        await Directory(outputDir).create(recursive: true);

        if (shouldSplitPerAbi) {
          final sourceFiles =
              getAndroidOutputFiles(
                sourceDir,
                isRelease: true,
                isAppBundle: false,
              ).where((f) {
                final name = p.basename(f);
                return RegExp(r'^app-.+-release\.apk$').hasMatch(name);
              }).toList();

          for (final sourceFile in sourceFiles) {
            final abiLabel = _getAndroidAbiLabelFromApkPath(sourceFile);
            final outputPath = p.join(
              outputDir,
              '${appName.substring(0, 1).toUpperCase()}${appName.substring(1)}-v$version-android-$abiLabel.apk',
            );

            await File(sourceFile).copy(outputPath);

            final fileSize = await File(outputPath).length();
            final sizeInMB = (fileSize / (1024 * 1024)).toStringAsFixed(2);
            log('✅ 已复制: ${p.basename(outputPath)} ($sizeInMB MB)');
          }
        } else {
          final sourceFiles = getAndroidOutputFiles(
            sourceDir,
            isRelease: true,
            isAppBundle: false,
          );

          final expectedAbiLabel = _getAndroidExpectedAbiLabel(androidArch);
          final sourceFile = expectedAbiLabel == null
              ? getAndroidOutputFile(sourceDir, true, false)
              : sourceFiles.firstWhere(
                  (f) => p.basename(f).contains(expectedAbiLabel),
                  orElse: () => sourceFiles.first,
                );

          final outputPath = p.join(
            outputDir,
            expectedAbiLabel == null
                ? '${appName.substring(0, 1).toUpperCase()}${appName.substring(1)}-v$version-android.apk'
                : '${appName.substring(0, 1).toUpperCase()}${appName.substring(1)}-v$version-android-$expectedAbiLabel.apk',
          );

          await File(sourceFile).copy(outputPath);

          final fileSize = await File(outputPath).length();
          final sizeInMB = (fileSize / (1024 * 1024)).toStringAsFixed(2);
          log('✅ 已复制: ${p.basename(outputPath)} ($sizeInMB MB)');
        }
      }
    }

    // 步骤 5: 构建 Debug
    if (shouldBuildDebug) {
      final androidTargetAbi = isAndroid
          ? _getAndroidTargetAbi(androidArch)
          : null;
      await runFlutterBuild(
        projectRoot: projectRoot,
        platform: platform,
        isRelease: false,
        extraArgs: isAndroid
            ? getAndroidBuildExtraArgs(
                androidArch: androidArch,
                shouldSplitPerAbi: shouldSplitPerAbi,
              )
            : const [],
        androidTargetAbi: androidTargetAbi,
      );

      if (needZipPack) {
        // 桌面平台：打包成 ZIP 或/和 EXE
        final sourceDir = getBuildOutputDir(projectRoot, platform, false);
        final platformSuffix = platform; // 使用完整平台名：windows, macos, linux
        final arch = _getCurrentArchitecture();

        // 打包为 ZIP
        if (shouldPackZip) {
          final outputPath = p.join(
            outputDir,
            '${appName.substring(0, 1).toUpperCase()}${appName.substring(1)}-v$version-$platformSuffix-$arch-debug.zip',
          );

          await packZip(sourceDir: sourceDir, outputPath: outputPath);
        }

        // 打包为平台安装包
        if (shouldPackInstaller) {
          if (Platform.isWindows) {
            // Windows: Inno Setup EXE
            final outputPath = p.join(
              outputDir,
              '${appName.substring(0, 1).toUpperCase()}${appName.substring(1)}-v$version-$platformSuffix-$arch-debug-setup.exe',
            );

            await inno.packInnoSetup(
              projectRoot: projectRoot,
              sourceDir: sourceDir,
              outputPath: outputPath,
              appName: appName,
              version: version,
              arch: arch,
            );
          } else if (Platform.isLinux) {
            // Linux: deb + rpm + AppImage
            await packLinuxInstallers(
              projectRoot: projectRoot,
              sourceDir: sourceDir,
              outputDir: outputDir,
              appName: appName,
              version: version,
              arch: arch,
              isDebug: true,
            );
          }
        }
      } else {
        // Android：复制 APK 文件（支持 --split-per-abi 多 APK）
        final sourceDir = getBuildOutputDir(projectRoot, platform, false);
        await Directory(outputDir).create(recursive: true);

        if (shouldSplitPerAbi) {
          final sourceFiles =
              getAndroidOutputFiles(
                sourceDir,
                isRelease: false,
                isAppBundle: false,
              ).where((f) {
                final name = p.basename(f);
                return RegExp(r'^app-.+-debug\.apk$').hasMatch(name);
              }).toList();

          for (final sourceFile in sourceFiles) {
            final abiLabel = _getAndroidAbiLabelFromApkPath(sourceFile);
            final outputPath = p.join(
              outputDir,
              '${appName.substring(0, 1).toUpperCase()}${appName.substring(1)}-v$version-android-$abiLabel-debug.apk',
            );

            await File(sourceFile).copy(outputPath);

            final fileSize = await File(outputPath).length();
            final sizeInMB = (fileSize / (1024 * 1024)).toStringAsFixed(2);
            log('✅ 已复制: ${p.basename(outputPath)} ($sizeInMB MB)');
          }
        } else {
          final sourceFiles = getAndroidOutputFiles(
            sourceDir,
            isRelease: false,
            isAppBundle: false,
          );

          final expectedAbiLabel = _getAndroidExpectedAbiLabel(androidArch);
          final sourceFile = expectedAbiLabel == null
              ? getAndroidOutputFile(sourceDir, false, false)
              : sourceFiles.firstWhere(
                  (f) => p.basename(f).contains(expectedAbiLabel),
                  orElse: () => sourceFiles.first,
                );

          final outputPath = p.join(
            outputDir,
            expectedAbiLabel == null
                ? '${appName.substring(0, 1).toUpperCase()}${appName.substring(1)}-v$version-android-debug.apk'
                : '${appName.substring(0, 1).toUpperCase()}${appName.substring(1)}-v$version-android-$expectedAbiLabel-debug.apk',
          );

          await File(sourceFile).copy(outputPath);

          final fileSize = await File(outputPath).length();
          final sizeInMB = (fileSize / (1024 * 1024)).toStringAsFixed(2);
          log('✅ 已复制: ${p.basename(outputPath)} ($sizeInMB MB)');
        }
      }
    }
    // 计算总耗时
    final endTime = DateTime.now();
    final duration = endTime.difference(startTime);
    final seconds = duration.inMilliseconds / 1000;

    log('🎉 所有打包任务已完成！');
    log('⏱️  总耗时: ${seconds.toStringAsFixed(2)} 秒');
    log('📁 输出目录: $outputDir');
  } catch (e) {
    log('❌ 任务失败: $e');
    exit(1);
  }
}

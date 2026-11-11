# PM 移动端构建指南

> 本文说明如何把 `app/` 目录的 Flutter 工程构建成 APK。
> 实测可行的构建机是 **Win11 工作站（192.168.2.100）**，完整流程见第九节。
> 110 容器本身没有 Flutter / JDK / Android SDK。

---

## 一、构建前必做

### 1. 拉依赖

```bash
cd app
flutter pub get
```

> **2026-11 起终端已改为原生 xterm 渲染**，不再需要下载或内联 `xterm.js`。
> 旧的 `js/` 目录、`scripts/fetch_xterm.sh`、`scripts/inline_terminal.js`
> 均已移除，请勿再执行对应步骤。

---

## 二、方案 A：本机 Android Studio（最快）

### 环境要求

| 工具 | 版本 |
|---|---|
| Flutter SDK | 3.24+（含 Dart 3.5+） |
| JDK | **17**（Gradle 8.x 不支持 21+） |
| Android SDK | Platform 35、Build-Tools 35.x |

### 步骤

```bash
# 1. 检查环境（任何一项红叉都要先修）
flutter doctor -v

# 2. 拉依赖
cd app
flutter pub get

# 3. 出包
flutter build apk --release
```

**产物**：`build/app/outputs/flutter-apk/app-release.apk`

### 安装到手机

```bash
adb install -r build/app/outputs/flutter-apk/app-release.apk
```

或直接把 APK 拷到手机点安装（需允许「安装未知来源应用」）。

---

## 三、方案 B：服务器装 SDK 构建

> ⚠️ 磁盘：SDK 全套约 **7GB**。宿主机 `/` 只剩 17GB（装完剩 10GB），
> 这台机器还跑着 GitLab / PostgreSQL 等，请评估后再装。

```bash
# 1. JDK 17
apt-get update && apt-get install -y openjdk-17-jdk unzip

# 2. Android cmdline-tools
mkdir -p /opt/android-sdk/cmdline-tools
cd /tmp
curl -sSL -o cmdtools.zip \
  https://dl.google.com/android/repository/commandlinetools-linux-11076708_latest.zip
unzip -q cmdtools.zip -d /opt/android-sdk/cmdline-tools
mv /opt/android-sdk/cmdline-tools/cmdline-tools /opt/android-sdk/cmdline-tools/latest

# 3. 环境变量
cat >> /etc/profile.d/android.sh <<'EOS'
export ANDROID_HOME=/opt/android-sdk
export PATH=$PATH:/opt/android-sdk/cmdline-tools/latest/bin:/opt/android-sdk/platform-tools
EOS
source /etc/profile.d/android.sh

# 4. 装 SDK 组件（约 3GB）
yes | sdkmanager --licenses
sdkmanager "platform-tools" "platforms;android-35" "build-tools;35.0.0"

# 5. Flutter SDK（约 1.5GB）
cd /opt
git clone --depth 1 -b stable https://github.com/flutter/flutter.git
echo 'export PATH=$PATH:/opt/flutter/bin' >> /etc/profile.d/android.sh
source /etc/profile.d/android.sh
flutter --version

# 6. 构建
cd /root/projects/saibo/apps/pm/app
flutter pub get
flutter build apk --release
```

---

## 四、方案 C：Docker 构建（不污染宿主机）

```bash
cd /root/projects/saibo/apps/pm/app

docker run --rm -v "$PWD":/app -w /app \
  ghcr.io/cirruslabs/flutter:stable \
  bash -c "flutter pub get && flutter build apk --release"
```

> 镜像约 2GB，构建完 `docker rmi` 即可清理。
> 首次拉镜像较慢，但完全不占用宿主机空间。

---

## 五、iOS 构建（需 macOS）

```bash
cd app
flutter build ios --release
```

必须有一台 Mac + Xcode，且需 Apple 开发者账号才能真正安装到 iPhone。

---

## 六、正式发布签名

默认用 debug 签名（能装但不可上架）。正式包需要：

### 1. 生成 keystore

```bash
keytool -genkey -v -keystore pm-release.jks \
  -keyalg RSA -keysize 2048 -validity 10000 -alias pm
```

### 2. 配置 `android/key.properties`

```properties
storePassword=你的store密码
keyPassword=你的key密码
keyAlias=pm
storeFile=/绝对路径/pm-release.jks
```

`app/android/app/build.gradle.kts` 已自动检测该文件：
存在则用正式签名，不存在则退回 debug 签名。

> ⚠️ `key.properties` 与 `.jks` **不要提交到 git**（已在 .gitignore）。

---

## 七、常见问题

### 构建报错 `Android SDK not found`

```bash
flutter config --android-sdk /opt/android-sdk
```

### 打包时卡在 Gradle 下载

内网慢可换镜像，在 `android/build.gradle.kts` 的 repositories 前加：

```kotlin
repositories {
    maven { url = uri("https://maven.aliyun.com/repository/google") }
    maven { url = uri("https://maven.aliyun.com/repository/public") }
    google()
    mavenCentral()
}
```

### 终端页面白屏 / 无输出

终端已改为原生 xterm 渲染，**不再是 WebView**，排查方向也变了：

1. 确认 `flutter pub get` 里包含 `xterm` 依赖（不再是 `webview_flutter`）
2. 看 App 内终端顶部的错误条，断线会显示重连次数与关闭码
3. 连不上就先用登录页的「测试连接」确认服务器可达

### 连接不上服务器

1. 确认手机与服务器同网段（内网 IP 手机要能访问）
2. Android 9+ 已放开明文 HTTP（`usesCleartextTraffic="true"`）
3. 服务器地址别带尾部斜杠，App 会自动补 `http://`

### 终端连不上但其他功能正常

WebSocket 走的是 `/webterm/ws/{id}?token=xxx`。
检查反向代理是否透传了 `Upgrade` 头（nginx/Caddy 配置见 `docs/release/DEPLOY.md`）。

---

## 八、当前状态

| 项 | 状态 |
|---|---|
| Flutter 工程代码 | ✅ 完成（约 2300 行） |
| xterm.js 内联 | ✅ 已验证（297KB 自包含） |
| Android 平台配置 | ✅ 完成 |
| 构建环境（Win11） | ✅ 已就绪（见第九节） |
| **APK 构建** | ✅ 已在 Win11 成功出包（54.8MB，debug 签名） |
| 真机验证 | ⬜ 未做 |

---

## 九、Windows 11 构建（实测可行）

> 本节是 2026-11-07 在 `192.168.2.100` 上**实际跑通**的流程，不是推测。

### 9.1 环境落点

| 组件 | 路径 / 版本 |
|---|---|
| Flutter | `D:\develop\flutter`（3.47.2 stable） |
| JDK | `D:\develop\jdk17`（Temurin 17.0.20.1） |
| Android SDK | `D:\Users\33136\AppData\Local\Android\Sdk` |
| 工程 | `D:\projects\saibo\apps\pm` |

Flutter 的两项配置（写进 `%USERPROFILE%\.flutter_settings`，只需一次）：

```powershell
flutter config --android-sdk "D:\Users\33136\AppData\Local\Android\Sdk"
flutter config --jdk-dir   "D:\develop\jdk17"
flutter doctor --android-licenses     # 全部输入 y
```

`flutter doctor` 应达到：Android toolchain `[√]`。

### 9.2 必踩的坑

**坑 1：`maven.google.com` 不通**

`flutter doctor` 的 Network resources 永远报红，且 Gradle 默认仓库会卡死。
必须在 `android/build.gradle.kts` 和 `android/settings.gradle.kts` 的 `repositories {}
` 首位加阿里云镜像：

```kotlin
maven { url = uri("https://maven.aliyun.com/repository/google") }
maven { url = uri("https://maven.aliyun.com/repository/public") }
maven { url = uri("https://maven.aliyun.com/repository/gradle-plugin") }
```

**坑 2：不要用 `cat > file` 写远端文件**

Windows 默认 shell 是 PowerShell 5.1，**不认** `cat > path` 重定向。
从 Linux 侧写文件请用 base64：

```bash
base64 -w0 local.txt   # 拿到字符串后在远端：
# [IO.File]::WriteAllBytes($path, [Convert]::FromBase64String("<b64>"))
```

### 9.3 出包

```powershell
$env:JAVA_HOME = "D:\develop\jdk17"
cd D:\projects\saibo\apps\pm\app
flutter pub get
flutter build apk --release
```

产物：`D:\projects\saibo\apps\pm\app\build\app\outputs\flutter-apk\app-release.apk`

> 未配置 `key.properties` 时用 debug 签名（能装，不可上架），见第六节。

### 9.4 远程构建（从 Linux 容器发起）

```bash
ssh -i /root/.ssh/id_ed25519 33136@192.168.2.100 \
  'cd D:\projects\saibo\apps\pm\app; flutter build apk --release'
```

注意：远程 PowerShell 默认输出 GBK 中文会乱码，命令前加
`[Console]::OutputEncoding=[Text.Encoding]::UTF8` 即可，不改系统设置。

---

## 十、2026-11-07 首次出包时修掉的问题（重要）

仓库原状态在 Win11 上**编不过**，以下是实际遇到的失败与修法，按出现顺序：

### 10.1 版本不兼容（Flutter 3.47.2 的硬性下限）

`DependencyVersionChecker.kt` 里的实测阈值：

| 组件 | 低于此值**直接失败** | 原值 | 改为 |
|---|---|---|---|
| Gradle | 8.14.0 | 8.9.0 | **8.14.3** |
| AGP | 8.11.1 | 8.7.0 | **8.13.2** |
| Kotlin | 2.2.20 | 1.9.25 | **2.2.20** |

> 不要上 AGP 9.x —— 会触发 `android.newDsl` 不兼容，Flutter 官方也建议先留在 8.x。

### 10.2 Gradle 分发包下载卡死

`services.gradle.org` 的 215MB 包会卡在 0 字节。wrapper 改走腾讯镜像：

```properties
distributionUrl=https\://mirrors.cloud.tencent.com/gradle/gradle-8.14.3-bin.zip
```

### 10.3 `session.dart` 缺 foundation 导入（真实代码 bug）

```
Error: Type 'ChangeNotifier' not found.
```

`lib/session.dart` 用了 `ChangeNotifier` 却没导入，且以 `../api/` 引用同级目录。
已补：

```dart
import 'package:flutter/foundation.dart';

import 'api/pm_api.dart';
import 'models/auth.dart';
```

### 10.4 「新建会话」按钮不存在（真实 UI bug）

`_createSession()` 实现完整但**无人调用**，而空状态文案写着「点右下角 + 新建」。
已在 `session_list_page.dart` 用 `Stack` 包住列表并加 `FloatingActionButton`。

### 10.5 整个 `res/` 目录缺失

```
AAPT: error: resource mipmap/ic_launcher not found
AAPT: error: resource style/LaunchTheme not found
```

Android 资源目录当初根本没生成。已从 Flutter 官方模板
`packages/flutter_tools/templates/app/android.tmpl/app/src/main/res` 补齐：
`drawable/`、`drawable-v21/`、`values/styles.xml`、`values-night/styles.xml`、
五个密度档的 `ic_launcher.png`。

### 10.6 远程写文件的正确姿势

Windows 默认 shell 是 PowerShell 5.1：

- **不认** `cat > path` 重定向
- **不认** UTF-8 无 BOM 的中文脚本（按 GBK 解析会报 `missing terminator`）

远端写文件用 base64 传字节；PowerShell 脚本内容**保持纯 ASCII**（注释也别用中文）。

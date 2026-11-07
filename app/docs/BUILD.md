# PM 移动端构建指南

> 本文说明如何把 `app/` 目录的 Flutter 工程构建成 APK。
> 当前开发环境（110 容器 / 宿主机）**没有 Flutter / JDK / Android SDK**，
> 所以代码就绪但尚未出包，需按本文任选一种方式构建。

---

## 一、构建前必做（一次）

### 1. 内联 xterm.js

终端页面依赖 xterm.js，必须内联进 HTML 才能在 WebView 里离线运行。

```bash
cd app
bash scripts/fetch_xterm.sh
```

成功输出：

```
=== 下载 @xterm/xterm@5.5.0 ===
=== 下载 @xterm/addon-fit@0.10.0 ===
  内联 xterm.css (5 KB)
  内联 xterm.js (282 KB)
  内联 addon-fit.js (1 KB)
完成：js/terminal.html (297 KB)
```

> 已验证：xterm 5.5.0 可从 npmmirror 下载。
> 若 `terminal.html` 已被内联过，脚本会提示跳过 —— 重复运行不会膨胀文件。

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

### 终端页面白屏

1. 确认跑过 `bash scripts/fetch_xterm.sh`（`js/terminal.html` 应约 297KB）
2. 若只有几十 KB，说明还是模板没内联
3. 用 Chrome 打开 `chrome://inspect` 调试 WebView 看 console

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
| **APK 构建** | ⬜ **未做**（缺 Flutter/JDK/Android SDK） |
| 真机验证 | ⬜ 未做 |

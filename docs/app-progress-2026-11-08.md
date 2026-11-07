# PM 移动端 App — 进度归档

> 归档时间：2026-11-08
> 归档时仓库 HEAD：`53bc1a7`（已推送 GitLab）
> 后端线上版本：`1.0.25`（buildTime 2026-11-08 03:05:14）
> 下一步：**真机测试由用户明天推进**

---

## 一、一句话现状

**App 已能编译出包，登录页完成强化，后端已部署最新版；真机验证尚未开始，终端 WebSocket 输入存在已知缺陷。**

---

## 二、进度总览

| 模块 | 状态 | 说明 |
|---|---|---|
| Flutter 工程 | ✅ 完成 | 约 2300 行，`flutter analyze` 零问题 |
| 登录（服务器地址 / 账号 / 密码） | ✅ 完成 | 含测试连接、快捷地址、格式校验 |
| 切换服务器 | ✅ 完成 | 右上角账号菜单入口 |
| 文件管理 | ✅ 代码完成 | 浏览 / 编辑 / 增删改名 / 上传下载 |
| 终端 | ⚠️ **有缺陷** | 能连上、有 banner，但**输入不回显** |
| 构建环境（Win11） | ✅ 就绪 | Flutter 3.47.2 + JDK17 + SDK36 + NDK |
| APK 出包 | ✅ 完成 | `release/app/pm-0.2.0-debug-signed.apk`（57.7MB） |
| 后端部署 | ✅ 完成 | 已上线 `1.0.25`，配置保护完好 |
| **真机验证** | ⬜ **未开始** | **明天推进** |

---

## 三、本次完成的关键事项

### 3.1 修复 5 个构建阻塞问题（提交 `f16e803`）

仓库原状态在 Win11 上**根本编不过**：

| # | 问题 | 性质 |
|---|---|---|
| 1 | `session.dart` 用 `ChangeNotifier` 却没导入 `foundation`，且 `../api/` 引用同级目录错误 | 代码 bug |
| 2 | `_createSession()` 无任何按钮调用，空状态却提示「点右下角 + 新建」 | **UI bug** |
| 3 | `android/app/src/main/res/` **整个目录不存在** | 工程缺件 |
| 4 | `gradle/wrapper/`、`gradlew` 缺失 | 工程缺件 |
| 5 | Gradle 8.9 / AGP 8.7 / Kotlin 1.9.25 低于 Flutter 3.47.2 硬性下限 | 版本不兼容 |

版本下限（实测于 `DependencyVersionChecker.kt`）：

| 组件 | 必须 ≥ | 原值 | 已改为 |
|---|---|---|---|
| Gradle | 8.14.0 | 8.9.0 | **8.14.3** |
| AGP | 8.11.1 | 8.7.0 | **8.13.2** |
| Kotlin | 2.2.20 | 1.9.25 | **2.2.20** |

> ⚠️ 不要升到 AGP 9.x，会触发 `android.newDsl` 不兼容。

### 3.2 登录页强化（提交 `53bc1a7`）

**根因**：登录成功后 token 持久化，下次启动直接进 `HomePage`，**用户没有任何路径回到登录页改地址**。

| 能力 | 实现 |
|---|---|
| 测试连接 | `PmApi.probe()`，无需账号密码即可探测 |
| 地址格式校验 | 实时提示，示例 `192.168.2.110:8080` |
| 一键清空 | 地址栏按钮 |
| 常用地址 | 110 / 199 / pm.saibo.club 快捷 chip |
| 切换服务器 | 账号菜单入口，退出后地址保留 |
| 退出保留地址 | `Session.clear()` 不再清 `baseUrl` |

### 3.3 后端部署（版本 `1.0.25`）

- 从 `0f36722`（10-30）升级到 `1.0.25`（11-08）
- 进程重启，PID 258 → 15423
- 全链路回归通过（Login / AppInfo / CliSession / FileManager 全部 200）

---

## 四、部署架构（重要，排查时必看）

```
移动 App / 浏览器
     │
     ▼ HTTP
192.168.2.110:8088  ──►  Caddy (容器内)
     ├─ /            ──► admin 前端静态文件
     ├─ /admin/*     ──► 127.0.0.1:6200  (PM .NET 后端, Kestrel)
     ├─ /api/*       ──► 127.0.0.1:6200
     └─ /webterm/ws/ ──► 127.0.0.1:6200  (WebSocket, 需 Upgrade 头)
```

**后端就运行在 DSH 容器内部**，不是 110 宿主机上的独立容器。

| 项目 | 路径 |
|---|---|
| 后端程序 | `/opt/pm/api/` |
| 启动脚本 | `/opt/pm/start.sh`（`sh start.sh 6200`） |
| 进程日志 | `/opt/pm/api/run.log` |
| 会话 PTY 日志 | `/opt/pm/api/logs/cli/202611/session-<id>.log` |
| WebSocket 诊断 | `/opt/pm/api/logs/webterm-diag-YYYYMMDD.log` |
| **App 填的地址** | **`192.168.2.110:8088`**（不要填 6200，那是容器内端口） |

---

## 五、⚠️ 部署时必须知道的坑

### 5.1 `deploy-local.sh` / `build-release.sh` 在本环境不可直接用

- 这两个脚本依赖 **`rsync`，但容器里没有**（`rsync: command not found`）
- 脚本使用的路径是 `/opt/api`、`/opt/admin`，与实际 `/opt/pm/api` **不一致**
- **最危险**：仓库默认 `appsettings.json` 与线上差异巨大，直接覆盖会出事故

实测配置差异：

```
线上（必须保留）                        仓库默认（会覆盖）
ProductCn: PM 项目管理平台          →   项目管理系统
ProductEn: pm                      →   sbpm
TimeZone:  Asia/Shanghai           →   Asia/Ho_Chi_Minh  (差 1 小时!)
WebTerminal 调试日志: 已开启        →   关闭
```

**正确部署方式**（本次采用）：

```bash
# 1. 备份
BACKUP=/opt/pm/backup-$(date +%Y%m%d_%H%M%S); mkdir -p "$BACKUP"
cp -a /opt/pm/api/*.dll "$BACKUP"/

# 2. 构建
cd /root/saibo/apps/pm
export PATH=/usr/local/dotnet:$PATH DOTNET_ROOT=/usr/local/dotnet
VERSION="1.0.$(git rev-list --count HEAD)"
dotnet publish api/Sb.Apps.Pm.WebApi/Sb.Apps.Pm.WebApi.csproj \
  -c Release -o dist/api -p:Version=$VERSION \
  -p:IncludeSourceRevisionInInformationalVersion=false --nologo -v minimal

# 3. 删除产物中的配置（关键！）
rm -f dist/api/appsettings.json dist/api/appsettings.*.json

# 4. 用 tar 精确排除保护文件
cd dist/api && tar cf - \
  --exclude='appsettings.json' --exclude='appsettings.Development.json' \
  --exclude='installed.lock' --exclude='client.key' \
  --exclude='logs' --exclude='run.log' . | (cd /opt/pm/api && tar xf -)

# 5. 重启
cd /opt/pm && sh start.sh 6200
```

回滚备份：`/opt/pm/backup-20261108_030459`（88 个 DLL）

### 5.2 Windows 远程操作的两个坑

1. Windows 默认 shell 是 **PowerShell 5.1**，**不认** `cat > path` 重定向
2. PowerShell 5.1 会把 **无 BOM 的 UTF-8 中文脚本按 GBK 解析**，报 `missing terminator`
   → 传脚本请用 base64，且脚本内容**保持纯 ASCII**
3. **二进制文件（jar/png）不能用 `type` 走文本管道传输**，会损坏，必须 base64

### 5.3 构建环境网络

| 域名 | 状态 | 对策 |
|---|---|---|
| `maven.google.com` | ❌ 超时 | Gradle 加阿里云镜像 |
| `services.gradle.org` | ❌ 下载卡死 0 字节 | wrapper 走腾讯镜像 |
| `dl.google.com` | ✅ 通 | 用于 cmdline-tools |

---

## 六、❓ 已知缺陷：终端 WebSocket 输入不生效

### 现象

| 步骤 | 结果 |
|---|---|
| WebSocket 握手 | ✅ 101 |
| 收到 banner | ✅ `root@2da591ebc529:~#` |
| 发送 `echo XXX\r` | ❌ **无任何回显** |
| PTY 日志是否记录 | ❌ **一条都没有** |

### 已做的对照实验

| 写入方式 | PTY 日志是否记录 |
|---|---|
| 直接 `echo x > /dev/pts/N` | ✅ 立刻出现 |
| WebSocket Binary 帧 | ❌ 完全没出现 |

**结论**：PTY 本身是活的，直接写入有效；**问题在 WebSocket → PTY 这一段**。

### 重要说明（避免误导）

测试客户端是我手写的 WebSocket 实现（环境无 `ws` 库），**不能 100% 排除是测试端帧格式问题**。  
后端代码逻辑读下来是对的。**需要在真机上用 App 本体复测确认**。

### 排查线索（明天可以直接从这里入手）

```
WS 接收循环:  api/Sb.Apps.Pm.WebApi/WebTerminals/WebTerminalEndpoint.cs:373
Binary 分支:  同文件 line 412-415  → pty.SendInputAsync(...)
PTY 写入:     api/Sb.Apps.Pm.Services/WebTerminals/PtyManager.cs:259
原生 write:   api/Sb.Apps.Pm.Services/WebTerminals/NativePty.cs:166
```

建议第一步：在 `line 412` 分支加一条 `logger.LogInformation` 打日志，确认 Binary 帧是否真的到达。

诊断日志位置：`/opt/pm/api/logs/webterm-diag-*.log`

---

## 七、明天的测试清单

### 准备

1. 安装 **`release/app/pm-0.2.0-debug-signed.apk`**（57.7MB，debug 签名，可直接装）
   - SHA256: `1db6fa76139c2b53f002a69ba3502cb93145c0e24fa6bac4380320d6232ce773`
2. Win11 上的副本：`D:\projects\saibo\apps\pm\app\build\app\outputs\flutter-apk\app-release.apk`
3. 登录信息：服务器地址 **`192.168.2.110:8088`**，账号 `admin` / `111111`

### 逐项验证

- [ ] **登录**：地址框可输入、「测试连接」返回可达、能登录
- [ ] **切换服务器**：右上角账号菜单 →「切换服务器」→ 回到登录页且地址保留
- [ ] **文件管理**：目录浏览、面包屑、打开文件、编辑保存
- [ ] **终端**：连上后看 banner 是否出现，**重点看能不能输入命令**
- [ ] **软键盘**：弹出时输入框是否被遮挡
- [ ] **Tab 切换**：切到文件再切回终端，连接是否保持

### 如果终端输入确实打不了字

那就是后端确认有 bug，按第六节的线索修 `WebTerminalEndpoint.cs`。

---

## 八、产物与备份清单

| 类型 | 位置 |
|---|---|
| App 源码 | `/root/saibo/apps/pm/app/` |
| 登录页测试 | `app/test/login_page_test.dart` |
| 构建文档 | `app/docs/BUILD.md`（第九、十节为实测记录） |
| APK | `release/app/pm-0.2.0-debug-signed.apk` |
| 后端源码 | `/root/saibo/apps/pm/api/` |
| 部署备份 | `/opt/pm/backup-20261108_030459` |
| Win11 工程 | `D:\projects\saibo\apps\pm` |

---

## 九、仓库同步状态

| 远程 | 状态 |
|---|---|
| GitLab (`origin`) | ✅ 已同步到 `53bc1a7` |
| GitHub (`github`) | ⚠️ **未推送**（本机无 GitHub 凭据） |

若需要推 GitHub，需先配置凭据。

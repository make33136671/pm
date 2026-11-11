# PM 移动端（Android / iOS）

> PM 项目管理平台的手机 App。第一版只做两个模块：**终端** + **文件管理**。
> 技术栈：Flutter（一套代码同时出安卓和 iOS）+ **原生 xterm 终端渲染**（不再使用 WebView）。

---

## 一、功能范围

| 模块 | 能力 |
|---|---|
| **登录** | 填服务器地址 + 账号密码，**测试连接**、**常用地址一键填入**、地址格式校验，token 持久化，启动自动恢复登录态 |
| **切换服务器** | 右上角账号菜单 →「切换服务器」，退出后回到登录页改地址（退出登录会保留上次地址） |
| **终端** | 会话列表、新建会话、原生终端（xterm 渲染）、结束/删除会话、断开自动重连 |
| **终端快捷操作栏** | 与网页端一致：📎上传文件、命令输入框+发送、方向键 / Tab / ok / 继续 / 换行 / 空格 / 回车 |
| **文件管理** | 目录浏览、面包屑导航、文本编辑保存、新建文件/目录、重命名、删除、上传、下载 |

暂不包含：项目管理、需求、任务、文档、AI、Pipeline 等（后续迭代）。

---

## 二、为什么这样设计

### 终端用原生 xterm 渲染（2026-11 起）

早期版本用 WebView 内嵌 xterm.js，后来改为纯 Dart 的
[xterm](https://pub.dev/packages/xterm) 包原生渲染：

| 方案 | 取舍 |
|---|---|
| **原生 xterm**（当前） | 首帧快、内存低、软键盘与滚动手感原生；主题/字体/选中复制可与 App 统一；不受 WebView 内核版本影响 |
| WebView + xterm.js（已弃用） | 需要把 JS/CSS 内联进 HTML 才能离线跑，`loadHtmlString` 的 origin 是 `about:blank`，资源无法走相对路径；首帧慢、内存高 |

两者**共用同一套后端协议** `/webterm/ws/{id}?token=`，后端零改动。

> 切换代价：xterm 包为 2024 年的版本，若未来 Flutter 大版本升级需重新验证兼容性。

### 终端二进制帧必须按字节搬运

PTY 输出是**任意字节序列**，可能截断在多字节 UTF-8 字符中间。
`terminal_page.dart` 收到 Binary 帧后用 `utf8.decode(..., allowMalformed: true)`
兜底 —— 不要先 `latin1` 或强转，否则会出现 `�` 且拼不回来。

---

## 三、快速开始

### 1. 安装依赖

```bash
flutter pub get
```

### 2. 运行

```bash
# 连真机/模拟器
flutter run

# 或直接出 APK
flutter build apk --release
# 产物：build/app/outputs/flutter-apk/app-release.apk
```

> 终端已改为原生渲染，**不再需要下载/内联 xterm.js**。旧的 `js/` 与
> `scripts/fetch_xterm.sh` 已随 2026-11 改造移除。

### 4. 使用

App 启动后填：

| 字段 | 示例 |
|---|---|
| 服务器地址 | `192.168.10.199:8080`（不填协议默认 http） |
| 账号 | `admin` |
| 密码 | `111111` |

登录后底部两个 Tab：**终端** / **文件**。

---

## 四、后端接口依赖

App 完全复用现有后端，**后端无需任何改动**。

| 用途 | 接口 | 鉴权 |
|---|---|---|
| 登录 | `POST /admin/Auth/Login` | 无 |
| 校验 token | `GET /admin/App/Info` | JwtAdmin |
| 会话列表 | `GET /admin/CliSession/List` | JwtAdmin |
| 新建会话 | `POST /admin/CliSession/Create` | JwtAdmin |
| 结束会话 | `POST /admin/CliSession/Destroy` | JwtAdmin |
| 删除会话 | `POST /admin/CliSession/Del` | JwtAdmin |
| **终端 WebSocket** | `WS /webterm/ws/{id}?token=<jwt>` | query token |
| 默认路径 | `GET /admin/FileManager/DefaultPath` | JwtAdmin |
| 列目录 | `GET /admin/FileManager/List` | JwtAdmin |
| 读文件 | `GET /admin/FileManager/Read` | JwtAdmin |
| 存文件 | `POST /admin/FileManager/Save` | JwtAdmin |
| 新建/重命名/删除 | `POST /admin/FileManager/{Create,Rename,Delete}` | JwtAdmin |
| 上传 | `POST /admin/FileManager/Upload`（multipart） | JwtAdmin |
| 下载 | `GET /admin/FileManager/Download` | JwtAdmin |

### 协议要点

**终端 WebSocket 帧格式**（与网页端一致）：

```
client → server:
  Binary 帧          = PTY 输入（原样字节）
  Text 帧 JSON       = {"type":"resize","cols":120,"rows":30}
  Text 帧 JSON       = {"type":"ping","t":<时间戳>}

server → client:
  Binary 帧          = PTY 输出（原样字节，交给 xterm 解码）
  Text 帧 JSON       = {"type":"pong","t":<时间戳>}
  Text 帧 JSON       = {"type":"exit","code":0}
```

**关闭码语义**：

| code | 含义 | App 行为 |
|---|---|---|
| 1000/1001 | 正常关闭 | 不重连 |
| **4001** | **被同账号其他设备顶替** | 提示「已在别处打开」，不重连（避免两端互顶） |
| 其他 | 异常断开 | 指数退避自动重连（1s→2s→4s…上限10s，最多8次） |

---

## 五、目录结构

```
app/
├── lib/
│   ├── main.dart                  启动流程（恢复登录态 → 校验 token → 选页面）
│   ├── session.dart               全局会话（服务器地址 / token 持久化）
│   ├── api/pm_api.dart            API 客户端（Dio 封装 + 结果解包）
│   ├── models/
│   │   ├── auth.dart              ApiResult / LoginResult / UserInfo
│   │   └── webterm.dart           CliSession / FileEntry / DirectoryListing / FileContent
│   ├── pages/
│   │   ├── login_page.dart        登录
│   │   ├── home_page.dart         底部导航容器（终端 / 文件）
│   │   ├── session_list_page.dart 终端会话列表
│   │   ├── terminal_page.dart     原生终端（xterm 渲染 + WebSocket）
│   │   ├── file_manager_page.dart 文件浏览
│   │   └── file_edit_page.dart    文件编辑
│   └── theme/app_theme.dart       主题（Material 3，明暗两套）
├── test/
│   └── login_page_test.dart       登录页 widget 测试（含服务器地址输入）
└── android/                       Android 平台配置
```

---

## 六、关键实现说明

### 6.1 明文 HTTP 放行

内网 PM 多为 `http://`，Android 9+ 默认禁止明文流量。
`AndroidManifest.xml` 已设 `android:usesCleartextTraffic="true"`。

生产若全站 https，建议改为 false + `network_security_config` 白名单。

### 6.2 终端二进制帧

PTY 输出是**任意字节序列**，可能截断在多字节 UTF-8 字符中间。
原生实现里用 `utf8.decode(bytes, allowMalformed: true)` 兜底，
不要把 Binary 帧强转成 String —— 否则会出现 `�` 且拼不回来。

### 6.3 软键盘与尺寸

- `windowSoftInputMode="adjustResize"`：软键盘弹出时界面收缩而不是遮挡
- `Terminal.onResize` 回调 → 发送 `{"type":"resize"}` 通知后端 PTY

### 6.4 Tab 切换不断连

`HomePage` 用 `IndexedStack` 而非重建，切 Tab 时终端页保持存活，WebSocket 连接不受影响。

### 6.5 断线重连

| 关闭码 | 含义 | 行为 |
|---|---|---|
| 1000 / 1001 | 正常关闭 | 不重连，提示已关闭 |
| **4001** | 被同账号其他设备顶替 | 不重连（避免两端互顶） |
| 其他 | 异常断开 | 指数退避 1s→2s→4s→8s→10s，最多 8 次 |

另加 30s 应用层 ping 心跳，防止反向代理掐掉空闲连接。

### 6.6 快捷操作栏的文本框回车间隔（重要）

`_sendLine()` 把**文本和回车分两帧发、中间间隔 100ms**。

合并成 `text\r` 单帧发出时，PTY 端的 TUI 程序（Claude Code、vim、less 等）
会把整块识别成「粘贴」，`\r` 变成粘贴内容里的换行而不是「提交」。
分开发之后接收端才会把它当作独立的回车键。**与网页端行为完全一致。**

### 6.7 上传文件的路径

shell 与 pm 服务进程权限域隔离，不能把手机本地路径直接给终端。
所以走与网页端相同的链路：

```
选文件 → POST /admin/Upload/Direct → 拿到 absfileName(URL)
       → 生成 curl -fsSL '<url>' -o '<name>' 填进命令输入框
       → 用户回车，终端内把文件拉到当前目录
```

文件名中的单引号会转义成 `'\''`，避免拼出错误的 shell 命令。

### 6.8 文件保存并发保护

`Save` 接口带 `expectedHash`：若文件在读取后被他人修改，后端拒绝覆盖。
App 保存成功后立即重读一次刷新 hash，避免下次保存被误拦。

---

## 七、构建环境要求

| 工具 | 版本 |
|---|---|
| Flutter | 3.24+（Dart 3.5+） |
| JDK | 17 |
| Android SDK | compileSdk 36 / build-tools 36.0.0 |
| Gradle / AGP / Kotlin | ≥ 8.14.0 / ≥ 8.11.1 / ≥ 2.2.20（Flutter 3.47 硬性要求） |

### 目前环境状态

构建在 **Win11 工作站**（`192.168.2.100`）上完成，流程见 `docs/BUILD.md` 第九节。
本机（110 container）没有 Flutter / JDK / Android SDK。

---

## 八、后续计划

> 📌 **当前进度与待办见 [`../docs/app-progress-2026-11-08.md`](../docs/app-progress-2026-11-08.md)**

- [x] 首版 APK 构建（2026-11-07 在 Win11 出包，流程见 `docs/BUILD.md` 第九节）
- [x] 登录页服务器地址强化 + 切换服务器（2026-11-08）
- [x] **终端改用原生 xterm 渲染**，移除 WebView 与 js/ 内联产物（2026-11-11）
- [ ] **真机验证**（下次推进，清单见归档文档第七节）
- [ ] 终端手势：双指缩放字号、长按选择复制
- [ ] 文件管理：回收站查看与恢复、压缩解压
- [ ] 生物识别解锁（指纹/面容）替代每次输入密码
- [ ] 多服务器管理（同时连 110 和 199）

# PM 移动端（Android / iOS）

> PM 项目管理平台的手机 App。第一版只做两个模块：**终端** + **文件管理**。
> 技术栈：Flutter（一套代码同时出安卓和 iOS）+ WebView 内嵌 xterm.js 终端。

---

## 一、功能范围

| 模块 | 能力 |
|---|---|
| **登录** | 填服务器地址 + 账号密码，token 持久化，启动自动恢复登录态 |
| **终端** | 会话列表、新建会话、打开终端（WebView + xterm.js）、结束/删除会话 |
| **文件管理** | 目录浏览、面包屑导航、文本编辑保存、新建文件/目录、重命名、删除、上传、下载 |

暂不包含：项目管理、需求、任务、文档、AI、Pipeline 等（后续迭代）。

---

## 二、为什么这样设计

### 终端用 WebView + xterm.js，不自己实现原生终端

| 方案 | 取舍 |
|---|---|
| **WebView + xterm.js**（已选） | xterm.js 已完成 VT100/xterm 序列解析（颜色、光标、宽字符、鼠标），与网页端共用同一套后端协议 `/webterm/ws/{id}`，**后端零改动** |
| 原生自研 VT100 | 需要实现完整的 ANSI 转义序列解析、渲染、输入法处理，工作量大数倍且兼容性难保证 |

代价：终端是 WebView，首次加载比原生略慢（本地方案已内联资源，无网络请求）。

### xterm.js 资源必须内联进 HTML

WebView 用 `loadHtmlString` 加载页面时 origin 是 `about:blank`，
**无法用相对路径访问 Flutter assets 里的 js/css**。

所以构建前要把 xterm.js 内联进 `js/terminal.html`（脚本：`scripts/fetch_xterm.sh` + `scripts/inline_terminal.js`）。

> ⚠️ `js/terminal.html`、`xterm.js`、`xterm.css`、`addon-fit.js` **都是 .gitignore 里的生成物，不入库**。
> 新克隆的仓库里 `js/` 只有模板 `terminal.template.html`，**必须先生成再构建**，
> 否则 `flutter pub get` 会报 asset 缺失。
>
> **Windows 上没有 bash，`fetch_xterm.sh` 跑不了**，用 PowerShell 等价步骤（见 `docs/BUILD.md` 第九节）。

---

## 三、快速开始

### 1. 准备终端资源（只需一次）

```bash
cd app
bash scripts/fetch_xterm.sh
```

这会下载 xterm.js 并内联进 `js/terminal.html`，使其成为自包含文件。

### 2. 安装依赖

```bash
flutter pub get
```

### 3. 运行

```bash
# 连真机/模拟器
flutter run

# 或直接出 APK
flutter build apk --release
# 产物：build/app/outputs/flutter-apk/app-release.apk
```

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
│   │   ├── terminal_page.dart     WebView 终端
│   │   ├── file_manager_page.dart 文件浏览
│   │   └── file_edit_page.dart    文件编辑
│   └── theme/app_theme.dart       主题（Material 3，明暗两套）
├── js/
│   └── terminal.html              终端页面（构建后内联 xterm.js）
├── scripts/
│   ├── fetch_xterm.sh             下载 xterm.js + 内联
│   └── inline_terminal.js         内联脚本
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
所以全程用 `Uint8Array` 搬运，交给 xterm 自己解码 —— 不要先转字符串，否则会出现 `�` 且拼不回来。

### 6.3 软键盘与尺寸

- `windowSoftInputMode="adjustResize"`：软键盘弹出时 WebView 收缩而不是遮挡
- JS 侧监听 `resize` 事件 → `fit.fit()` 重算行列 → 通知后端 `resize`

### 6.4 Tab 切换不断连

`HomePage` 用 `IndexedStack` 而非重建，切 Tab 时终端 WebView 保持存活，连接不受影响。

### 6.5 文件保存并发保护

`Save` 接口带 `expectedHash`：若文件在读取后被他人修改，后端拒绝覆盖。
App 保存成功后立即重读一次刷新 hash，避免下次保存被误拦。

---

## 七、构建环境要求

| 工具 | 版本 |
|---|---|
| Flutter | 3.24+（Dart 3.5+） |
| JDK | 17 |
| Android SDK | compileSdk 35 / build-tools 35.x |
| Node.js | 仅用于 `fetch_xterm.sh` 下载 xterm.js（可选，也走 curl） |

### 目前环境状态

**本机（110 container）没有 Flutter / JDK / Android SDK**，
所以代码写完但**尚未构建出 APK**。构建方式见 `docs/BUILD.md`。

---

## 八、后续计划

- [x] 首版 APK 构建（2026-11-07 在 Win11 出包，流程见 `docs/BUILD.md` 第九节）
- [ ] 真机验证
- [ ] 终端手势：双指缩放字号、长按选择复制
- [ ] 文件管理：回收站查看与恢复、压缩解压
- [ ] 生物识别解锁（指纹/面容）替代每次输入密码
- [ ] 多服务器管理（同时连 110 和 199）

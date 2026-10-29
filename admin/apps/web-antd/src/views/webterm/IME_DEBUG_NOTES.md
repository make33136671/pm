# 网页终端 IME 输入修复 — 调试笔记

> 这份笔记记录了 webterm 移动端/桌面端中文 IME 输入兼容的完整修复历程,以及
> 围绕它建立的可观察性基础设施(浮层 + 后端日志 + 版本号)。
> 给以后回归 / 升级 xterm 5.x 时,能快速理解每条 path 的原因。

## 当前状态:已修复(2026-05-14, commit 93401a3,版本号 `v6-Cend-send-2026-05-14`)

所有已知 IME path 验证通过 — 桌面 PC 中文/英文/数字、iOS Safari 虚拟键盘、Android composition、物理键盘 全部正常。

线上 chunk: `https://pm.saibo.club/js/Terminal-Dxq-7Pt_.js`,包含
`v6-Cend-send-2026-05-14` 字面量可 grep 验证。

## 三条 IME path

不同 浏览器 + 输入法 组合走的 path 差异**很大**,这是反复绕弯的根源:

### Path A — 桌面 Chromium/Safari 中文 IME(有 composition)
```
keydown code=229          ← IME 占位 keydown
Cstart                    ← compositionstart
input "c"   insertCompositionText  ← 拼音进行中
input "ce"  insertCompositionText
input "测试" insertCompositionText  ← 候选词最后阶段
Cend                      ← compositionend (ev.data="测试")
onData "测试" composing=false  ← xterm 内部 _compositionHelper setTimeout(0) 重发
```
**关键差异**:**没有** `input "测试" insertFromComposition` 这一行(只有 `insertCompositionText`)。

### Path B — iOS Safari 中文输入(无 composition)
```
keydown code=0            ← iOS IME 占位,无 compositionstart
onData "测" composing=false   ← xterm _keyDown 把 event.key="测" 当字符 trigger 单字
input "测试" insertText        ← 真实 input event 带完整字符串
```
**关键差异**:**没有** compositionstart/end,xterm `_keyDown` 内 `evaluateKeyboardEvent` 把 `event.key` 当字符直接 `triggerDataEvent` → onData 单字。

### Path C — Android Chrome 中文(有 composition + insertFromComposition)
```
keydown 229 → Cstart → input "..." insertCompositionText → ...
input "你好" insertFromComposition  ← Android 有这一行
Cend
onData "你好" composing=false
```

## v6 综合修法(Terminal.vue 内)

1. **mock `_inputEvent` no-op** — prototype 替换,不依赖 capture 顺序,从根上断 xterm 自己的 input 事件处理
2. **mock `_keyPress` + `_handleAnyTextareaChanges` + `_keyDownSeen`** 兜底防 keypress charCode 中文码点 path
3. **`sendStr` 加 30ms 同 data dedup** — 同字符在 30ms 内只 send 一次,防多 path 重复
4. **`compositionend` handler 同步 `sendStr(ev.data)`** — 覆盖 Path A(桌面 IME)
5. **input handler 接管 `insertText / insertFromComposition / insertFromPaste / insertReplacementText`** — 覆盖 Path B/C 的完整字符串
6. **onData filter**:全 ASCII 放行(字母/数字/控制键/ESC 序列,来自 `_keyDown`)、非 ASCII 跳过(中文,来自 setTimeout 重发,已由其他 path 处理)
7. **keydown handler 只做 dlog**,不再 stop keyCode=0(onData filter 已覆盖)

代码见 `apps/web-antd/src/views/webterm/components/Terminal.vue` setup() 内。

## 调试基础设施

### 1. 浮层 (`?debug=1`)

URL 加 `?debug=1` 或 console 跑 `localStorage.setItem('webterm:debug','1')` 后刷新,
网页终端右上角出现绿字浮层,**第一行固定显示版本号**:

```
[v6-Cend-send-2026-05-14]
HH:mm:ss.xxx init v6-Cend-send-2026-05-14
HH:mm:ss.xxx mock _keyDownSeen=false (expect false)   ← mock 自检
HH:mm:ss.xxx ws-open v6-Cend-send-2026-05-14
HH:mm:ss.xxx keydown code=...
HH:mm:ss.xxx input data="..." type="..."
HH:mm:ss.xxx onData "..." composing=...
HH:mm:ss.xxx Cstart / Cend
```

最近 40 条事件循环显示,**移动端不用 copy**(见下面 §2)。

### 2. 后端日志镜像

`?debug=1` 启用后,每条 `dlog` 同步通过 WebSocket Text 帧 `{type:"clientlog",msg}`
推到 pm api 服务端,经 `WebTerminalEndpoint.cs` 用 `logger.LogInformation` 写到
server log:

```
[webterm/clientlog s={SessionId} u={Username}] {Msg}
```

调试时拉日志(用 framework LogViewer/Diag API,需要 MasterKey):

```bash
curl -sS \
  -H "X-Master-Key: <PM_MASTER_KEY>" \
  "https://pm.saibo.club/admin/LogViewer/Diag?contains=webterm/clientlog&level=info&tail=500" \
  | jq -r '"\(.["@t"]|sub(".[0-9]+Z$";"Z")) [s=\(.SessionId)] \(.Msg)"'
```

按 `contains=v6-Cend-send` 可以精确定位某版本部署后哪些 session 加载到了。

### 3. 版本号机制

`const WEBTERM_DEBUG_VERSION = 'v6-Cend-send-2026-05-14';` 在 Terminal.vue
顶部,每次部署 increment:
- 浮层顶部固定显示 `[v6-Cend-send-2026-05-14]` —— 用户一眼看版本
- ws.open 后 dlog 一次 `ws-open <version>` —— 后端日志可 grep
- chunk 里包含字符串字面量,`curl + grep` 可验证线上是哪一版

## 部署流程(MasterKey)

```bash
cd apps/pm/admin
export PATH="/usr/lib/node_modules/corepack/shims:$PATH"
pnpm run build:antd --force   # turbo cache 可能让代码变更不重 build,必须 --force

# 打包(zip 内必须直接是产物文件,不能有外层 admin/ 目录)
rm -rf apps/pm/dist/admin apps/pm/dist/admin.zip
mkdir -p apps/pm/dist/admin
cp -r apps/web-antd/dist/* apps/pm/dist/admin/
cd apps/pm/dist/admin && zip -qr ../admin.zip .

# 上传(framework AppController.UploadProject)
curl -X POST \
  -H "X-Master-Key: <PM_MASTER_KEY>" \
  -F "key=admin" \
  -F "file=@apps/pm/dist/admin.zip" \
  "https://pm.saibo.club/admin/App/UploadProject"
```

成功返回 `{"code":0,"message":"项目 Admin 已更新"}`。

### CF 缓存注意

公网 `https://pm.saibo.club` 走 Cloudflare,部署后浏览器 / CF 边缘可能仍命中
旧 chunk。验证手段:
- 内网直连 `http://192.168.10.242:6026/?debug=1` 绕开 CF
- 公网用无痕模式 + `?_=<timestamp>` 查询参数破缓存
- 看浮层顶部版本号是否对得上当前部署

### 后端协议改动

WS 协议在 `WebTerminalEndpoint.cs`:
- `{type:"resize",cols,rows}` — 终端尺寸变化(已有)
- `{type:"clientlog",msg}` — 前端 dlog 上报(本次新增)

后端改动后需要 dotnet build 后端 + 部署 api.zip(注意 framework 的
UploadProject 只支持前端项目,后端要 ssh 手动 stop/unzip/start)。

## 修复时间线(回溯弯路)

```
51f1149  只监听 compositionend(无效 - 部分设备根本没 compositionend)
23b710d  加 compositionupdate / textarea 三重 fallback(无效 - 根本不发 composition)
14a1d39  加 IME 调试 overlay                                      ← 关键基础设施
6a95452  调试日志加 input.data / inputType                       ← 拿到关键日志
a08f394  第一版 hack — mock _handleAnyTextareaChanges(猜错根因)
da915f1  root cause 是 _keyPress + mock _keyDownSeen             ← 部分设备成功
6b840df  fix(webterm): 修中文 IME 输入被前端自己丢弃
         da915f1 后,前端业务代码 setTimeout(100ms) 把 composing reset 延后,
         onData 命中 if(composing)return 提前丢字 — 改成同步 reset
feat(webterm): debug overlay 镜像到后端 logger                   ← 移动端不用 copy
fix(webterm): WS 协议加 clientlog type
fix(webterm): 顶部工具条加 sticky 防移动端被 scroll 走
dcab1fe  v4 接管移动端 input + keydown(0) stop + onData filter(部分设备 OK)
8e74dd5  v4r2 ws.open 后补 version dlog,版本号能进后端日志
63812a3  v5 mock _inputEvent + dedup 解决多路径重复
         (v4 capture 阶段 stopImmediatePropagation 来太晚 — xterm 在我们之前注册)
93401a3  v6 compositionend 直接发 ev.data
         (v5 漏了桌面 Chromium IME path — 它不发 insertFromComposition input)
```

## 维护建议

- **不要轻易移除 dlog 或 debug overlay** — 它们只在 `?debug=1` 时启用,生产无开销,
  下次 xterm 升级 / 浏览器更新 / IME 行为变化时,这套基础设施能 5 分钟定位问题
- **xterm 升级到 6.x 时**,本文档里的 mock(`_keyPress` / `_inputEvent` /
  `_handleAnyTextareaChanges` / `_keyDownSeen`)需要重新对照源码确认字段名
  是否还存在;mock 自检 dlog 会立即报告 `mock _keyDownSeen=true (expect false)`
- **每次改 Terminal.vue 内的 IME 逻辑,必须 bump `WEBTERM_DEBUG_VERSION`**,
  否则 turbo cache / 浏览器缓存 / CF 缓存可能让验证误判
- **`pnpm run build:antd --force`**:turbo 的 cache 命中判断不总是可靠,
  改了代码后强制 --force 才能稳

## 已修复边角

### 移动端拼音首字母重复(2026-06-15, `v9-ime-prefix-dedup`)

**现象**:手机拼音输入法直接对命令行输入、输入拼音后直接回车,终端首字母重复
(`ls` → `lls`)。桌面端不复现。

**根因**:即上面 §"已知边角" 描述的 iOS Path B —— `onData "l"`(xterm `_keyDown`
把首字母当字符单独发)+ `input "ls" insertText`(带完整串),两路相加 → 首字母重复。
桌面端 `compositionstart` 先于 `input`,`composing=true` 挡掉 input 分支,故不复现。

**修法**(Terminal.vue input handler):
1. **前缀 dedup** — `input` 的完整串若以刚由 `onData` 发出的 `lastSendData` 开头
   (且 100ms 内同源),只 send 差额部分。覆盖 iOS Path B(无 composition)。
2. **insertText 延后一个宏任务 + compositionstart 取消** — 覆盖另一种设备:首字母的
   `input(insertText)` 先于 `compositionstart` 触发的竞态,紧随的 compositionstart
   会取消这个待发送,组词内容统一由 compositionend 发出。

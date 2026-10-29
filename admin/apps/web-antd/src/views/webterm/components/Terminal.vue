<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, watch } from 'vue';

import { useAppConfig } from '@vben/hooks';
import { useAccessStore } from '@vben/stores';

import { FitAddon } from '@xterm/addon-fit';
import { WebLinksAddon } from '@xterm/addon-web-links';
import { Terminal } from '@xterm/xterm';
import '@xterm/xterm/css/xterm.css';

import { fetchSessionLog, getConfig } from '../api';

// 调试版本号 — 每次部署 increment,移动端浮层固定显示一行,便于确认拿到的是不是最新 chunk
// 部署后第一条 dlog 会通过 ws.send clientlog 上报,后端日志可按版本 grep
const WEBTERM_DEBUG_VERSION = 'v9-ime-prefix-dedup-2026-06-15';

// === 视觉配置(从后端 CliConfig 读)===
interface VisualConfig {
  fontFamily?: string;
  fontSize?: number;
  lineHeight?: number | string;
  letterSpacing?: number | string;
  theme?: string;
  customBgColor?: string;
  customFgColor?: string;
  customCursorColor?: string;
  cursorStyle?: string;
  cursorBlink?: boolean;
  xtermScrollback?: number;
}

const PRESET_THEMES: Record<
  string,
  { background: string; cursor: string; foreground: string }
> = {
  dark: { background: '#1e1e1e', foreground: '#ffffff', cursor: '#ffffff' },
  light: { background: '#ffffff', foreground: '#000000', cursor: '#000000' },
  'solarized-dark': {
    background: '#002b36',
    foreground: '#839496',
    cursor: '#93a1a1',
  },
  'solarized-light': {
    background: '#fdf6e3',
    foreground: '#586e75',
    cursor: '#586e75',
  },
  monokai: { background: '#272822', foreground: '#f8f8f2', cursor: '#f8f8f0' },
};

let visualCfg: null | VisualConfig = null;

async function loadVisualConfig() {
  try {
    visualCfg = (await getConfig()) as VisualConfig;
  } catch {
    visualCfg = null;
  }
}

function resolveTheme(cfg: null | VisualConfig) {
  const themeName = cfg?.theme || 'dark';
  if (themeName === 'custom') {
    return {
      background: cfg?.customBgColor || '#1e1e1e',
      foreground: cfg?.customFgColor || '#ffffff',
      cursor: cfg?.customCursorColor || '#ffffff',
    };
  }
  return PRESET_THEMES[themeName] || PRESET_THEMES.dark;
}

const props = defineProps<{
  // 只读模式:已结束会话回放日志,不连 WebSocket、不接受输入
  readonly?: boolean;
  sessionId: number;
}>();

const emit = defineEmits<{
  (e: 'closed', reason?: string): void;
}>();

const containerRef = ref<HTMLDivElement>();
let term: Terminal | null = null;
let fit: FitAddon | null = null;
let ws: WebSocket | null = null;
let resizeObserver: null | ResizeObserver = null;

// === 连接状态指示灯 ===
// connecting=连接中 / connected=正常(绿) / high=延迟高(黄) / closed=已断开(红)
type ConnState = 'closed' | 'connected' | 'connecting' | 'high';
const connState = ref<ConnState>('connecting');
const latencyMs = ref(-1); // -1 = 未知
// RTT 超过该阈值判为「延迟高」(黄灯)
const HIGH_LATENCY_MS = 300;
let pingTimer: null | ReturnType<typeof setInterval> = null;
let pendingPingAt = 0; // 本轮已发但未收到 pong 的 ping 时间戳;0=无在途 ping

function stopPing() {
  if (pingTimer) {
    clearInterval(pingTimer);
    pingTimer = null;
  }
  pendingPingAt = 0;
}

// 每秒发一次 ping;后端原样回 pong 后在 message 分支算 RTT。
function startPing() {
  stopPing();
  pingTimer = setInterval(() => {
    if (ws?.readyState === WebSocket.OPEN) {
      // 上一个 ping 仍未回 → 链路卡顿,先按「已等待时长」标黄,不等 pong
      if (pendingPingAt !== 0) {
        const waited = Date.now() - pendingPingAt;
        latencyMs.value = waited;
        connState.value = 'high';
        // 超过 3 秒没回 pong，判定链路卡顿（写 localStorage + HTTP 上报）
        if (waited > 3000) dlog(`pong 超时 已等待=${waited}ms ${context()}`);
      }
      pendingPingAt = Date.now();
      try {
        ws.send(JSON.stringify({ type: 'ping', t: pendingPingAt }));
      } catch {
        /* ws 状态变化忽略 */
      }
    } else if (!props.readonly) {
      connState.value = 'closed';
    }
  }, 1000);
}

// pong 回包:算 RTT,更新延迟与灯色
function onPong(t: number) {
  const rtt = Date.now() - (t || pendingPingAt || Date.now());
  latencyMs.value = rtt;
  pendingPingAt = 0;
  connState.value = rtt > HIGH_LATENCY_MS ? 'high' : 'connected';
}

// ==================== 自动重连 ====================
// 场景：公网走 Cloudflare 时，网络抖动/边缘节点切换会让 WebSocket 异常断开。
// 原实现断开即放弃，用户必须手动点重连，体验上就是「频繁断连」。
// 这里做指数退避自动重连，最大 8 次，期间灯变黄提示「重连中」。

let reconnectAttempt = 0;
// 主动销毁中（区分组件主动关闭与网络异常断开）
let destroying = false;
/** 本连接的建立时刻（用于算存活时长，断连诊断关键指标） */
let openedAt = 0;
// 连接代次：每次 setup() 自增。旧连接的异步事件迟到时（close/error 在新建连接之后才到），
// 只认当前代次的连接，避免「旧连接 close 事件误触发新一轮重连」造成重连风暴。
let wsGeneration = 0;
let reconnectTimer: null | ReturnType<typeof setTimeout> = null;
const MAX_RECONNECT = 8;

function clearReconnect() {
  if (reconnectTimer) {
    clearTimeout(reconnectTimer);
    reconnectTimer = null;
  }
}

function scheduleReconnect(code?: number) {
  clearReconnect();
  if (props.readonly) return;
  if (reconnectAttempt >= MAX_RECONNECT) {
    dlog(`重连放弃（已达 ${MAX_RECONNECT} 次）`);
    emit('closed', `连接已断开（重连 ${MAX_RECONNECT} 次失败）`);
    return;
  }

  reconnectAttempt += 1;
  const delay = Math.min(1000 * 2 ** (reconnectAttempt - 1), 10000);
  connState.value = 'high'; // 黄灯 = 重连中
  dlog(`断连 code=${code}，第 ${reconnectAttempt} 次重连将在 ${delay}ms 后`);

  reconnectTimer = setTimeout(() => {
    dlog(`执行第 ${reconnectAttempt} 次重连`);
    try {
      setup(props.sessionId);
    } catch (e) {
      dlog(`重连异常: ${String(e)}`);
    }
  }, delay);
}
// 重连:重建 ws(setup 内会先 destroy 再连,后端发 snapshot 还原缓冲区)
function reconnect() {
  if (props.readonly) return;
  connState.value = 'connecting';
  setup(props.sessionId);
}

const accessStore = useAccessStore();
const { apiURL } = useAppConfig(import.meta.env, import.meta.env.PROD);

// 调试 overlay —— URL 含 debug=1(query 或 hash 都行) 或 localStorage 开启
const debug = ref(
  /[?&]debug=1\b/.test(
    typeof window === 'undefined' ? '' : window.location.href,
  ) || localStorage.getItem('webterm:debug') === '1',
);
const debugLogs = ref<string[]>([]);
// ==================== 断连诊断日志（debug 版本核心） ====================
// 设计要点（见 docs/webterm/网页终端断连问题与调试日志.md §5.3）：
//   1. 断连类事件无条件写 localStorage 缓冲（不依赖 debug 开关），保留最近 80 条
//   2. 连接还活着 → 照旧 WS 推送
//   3. 连接已断 → navigator.sendBeacon 直接 POST /webterm/clientlog（不依赖 WS）
//   4. 下次 WS 建立成功 → 补传缓冲区历史

const LOGBUF_KEY = 'webterm:logbuf';
const LOGBUF_MAX = 80;
/** 断连/重连相关关键词：命中即无条件落盘 */
const CRITICAL_RE = /断连|ws-error|重连|关闭|close|error|unload/i;

function readBuf(): string[] {
  try {
    return JSON.parse(localStorage.getItem(LOGBUF_KEY) || '[]');
  } catch {
    return [];
  }
}

function writeBuf(lines: string[]) {
  try {
    localStorage.setItem(LOGBUF_KEY, JSON.stringify(lines.slice(-LOGBUF_MAX)));
  } catch {
    /* localStorage 满/禁用，忽略 */
  }
}

/** 采集断连上下文：一句话说清「是断网还是服务端关的、页面是否在后台」 */
function context(): string {
  const parts = [
    `online=${navigator.onLine}`,
    `vis=${document.visibilityState}`,
    `ua=${navigator.userAgent.slice(0, 60)}`,
  ];
  if (ws) {
    parts.push(`rs=${ws.readyState}`);
  }
  return parts.join(' ');
}

/** 不依赖 WS 的 HTTP 上报（sendBeacon 优先，页面卸载时也能发出） */
function postLog(lines: string[], sessionId?: number) {
  if (!lines.length) return;
  const payload = JSON.stringify({
    sessionId: sessionId ?? null,
    lines,
    ts: Date.now(),
    ctx: context(),
  });
  const url = `${apiURL}/webterm/clientlog?token=${encodeURIComponent(accessStore.accessToken || '')}`;

  try {
    if (typeof navigator.sendBeacon === 'function') {
      const blob = new Blob([payload], { type: 'application/json' });
      if (navigator.sendBeacon(url, blob)) return;
    }
  } catch {
    /* sendBeacon 失败，回退 fetch */
  }

  try {
    void fetch(url, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: payload,
      keepalive: true,
    });
  } catch {
    /* 彻底失败就只留 localStorage */
  }
}

function dlog(msg: string) {
  const ts = new Date().toISOString().slice(11, 23);
  const line = `${ts} ${msg}`;

  // 1. 关键事件无条件进本地缓冲（debug 关也记，否则线上永远看不到断连原因）
  if (CRITICAL_RE.test(msg)) {
    const buf = readBuf();
    buf.push(line);
    writeBuf(buf);
  }

  // 2. debug 浮层
  if (debug.value) {
    debugLogs.value.push(line);
    if (debugLogs.value.length > 40) debugLogs.value.shift();
  }

  // 3. 连接活着 → WS 推送；已断 → HTTP 上报（这是断连那一刻唯一能落盘的路）
  if (ws?.readyState === WebSocket.OPEN) {
    try {
      ws.send(JSON.stringify({ type: 'clientlog', msg: `${line} | ${context()}` }));
      return;
    } catch {
      /* 落到下面走 HTTP */
    }
  }
  postLog([`${line} | ${context()}`], props.sessionId);
}

/** 连接建立后补传本地积压的断连日志 */
function flushLogBuf() {
  const buf = readBuf();
  if (buf.length === 0) return;
  postLog(buf.slice(-LOGBUF_MAX), props.sessionId);
  writeBuf([]);
  dlog(`已补传 ${buf.length} 条历史断连日志`);
}

function buildWsUrl(sessionId: number, token: string): string {
  let base = apiURL || '';
  if (base.startsWith('http://') || base.startsWith('https://')) {
    base = base.replace(/^http/, 'ws');
  } else {
    const proto = location.protocol === 'https:' ? 'wss:' : 'ws:';
    base = `${proto}//${location.host}${base.startsWith('/') ? '' : '/'}${base}`;
  }
  base = base.replace(/\/admin\/?$/, '');
  return `${base}/webterm/ws/${sessionId}?token=${encodeURIComponent(token)}`;
}

function sendResize() {
  if (!fit || !term) return;
  fit.fit();
  // 只读回放无 ws —— fit 容器即可,不需要上报尺寸
  if (ws?.readyState === WebSocket.OPEN) {
    ws.send(
      JSON.stringify({
        cols: term.cols,
        rows: term.rows,
        type: 'resize',
      }),
    );
  }
}

function setup(sessionId: number) {
  if (!containerRef.value) return;
  destroying = false;
  // 复用还是重建?最简单的是销毁重建
  destroy();

  const cfg = visualCfg;
  term = new Terminal({
    cursorBlink: props.readonly ? false : (cfg?.cursorBlink ?? true),
    // 只读回放禁用输入 —— xterm 不接受键盘,光标不闪
    disableStdin: props.readonly ?? false,
    cursorStyle: ((cfg?.cursorStyle || 'block') as
      | 'bar'
      | 'block'
      | 'underline'),
    fontFamily:
      cfg?.fontFamily || 'Menlo, Consolas, "Liberation Mono", monospace',
    fontSize: cfg?.fontSize ?? 14,
    letterSpacing: cfg?.letterSpacing ? Number(cfg.letterSpacing) : 0,
    lineHeight: cfg?.lineHeight ? Number(cfg.lineHeight) : 1.2,
    scrollback: cfg?.xtermScrollback ?? 5000,
    theme: resolveTheme(cfg),
  });
  fit = new FitAddon();
  term.loadAddon(fit);
  term.loadAddon(new WebLinksAddon());
  term.open(containerRef.value);
  fit.fit();

  // 只读模式(已结束会话):不连 WebSocket,回放日志文件,只挂滚动相关监听
  if (props.readonly) {
    resizeObserver = new ResizeObserver(() => sendResize());
    resizeObserver.observe(containerRef.value);
    term.onScroll(() => updateAtBottom());
    replayLog();
    return;
  }

  const token = accessStore.accessToken || '';
  ws = new WebSocket(buildWsUrl(sessionId, token));
  // 当前代次：本连接的所有事件回调都先比对代次，迟到的旧事件直接忽略
  const gen = ++wsGeneration;
  const isStale = () => gen !== wsGeneration;
  ws.binaryType = 'arraybuffer';

  ws.addEventListener('open', () => {
    if (isStale()) return;
    connState.value = 'connected';
    latencyMs.value = -1;
    // 连上即重置重连计数，下次断连重新从 1 次开始退避
    reconnectAttempt = 0;
    clearReconnect();
    startPing();
    sendResize();
    term?.focus();
    // 版本号在 ws OPEN 后再 dlog 一次 — 这样能通过 clientlog 上报到后端
    // (setup() 早期那条 dlog 时 ws 还 CONNECTING,clientlog 只进浮层不入服务端日志)
    openedAt = Date.now();
    dlog(`ws-open ${WEBTERM_DEBUG_VERSION}`);
    // 上次断连时没能发出去的日志，这里补传
    flushLogBuf();
  });
  ws.addEventListener('message', (ev) => {
    if (isStale()) return;
    if (ev.data instanceof ArrayBuffer) {
      term?.write(new Uint8Array(ev.data));
    } else if (typeof ev.data === 'string') {
      try {
        const msg = JSON.parse(ev.data);
        if (msg.type === 'pong') {
          onPong(Number(msg.t));
          return;
        }
        if (msg.type === 'exit') emit('closed', `已退出 (code=${msg.code})`);
      } catch {
        // 不是 JSON,当文本输出
        term?.write(ev.data);
      }
    }
  });
  ws.addEventListener('close', (ev) => {
    // 迟到的旧连接 close 事件：新连接已建立，忽略，否则会触发重复重连
    if (isStale()) return;
    connState.value = 'closed';
    stopPing();
    if (destroying) return; // 主动销毁不重连
    const lifespan = Math.round((Date.now() - openedAt) / 1000);
    dlog(`断连 code=${code} 存活=${lifespan}s ${context()}`);
    const code = (ev as CloseEvent).code;
    // 被同账号另一连接顶替（后端 CloseCodes.Replaced = 4001）。
    // 不能自动抢回，否则两台设备会互相顶替形成无限重连风暴；
    // 这里明确告知用户，由用户决定是否夺回控制权。
    if (code === 4001) {
      dlog('被其他设备的同账号连接顶替');
      emit('closed', '已在其他设备/标签页打开该终端，连接被顶替');
      return;
    }
    // 服务端主动关闭（1000 正常关闭 / exit 帧已处理）不重连；异常断开自动重连
    const normal = code === 1000 || code === 1001;
    if (normal) {
      emit('closed', '连接已关闭');
      return;
    }
    scheduleReconnect(code);
  });
  ws.addEventListener('error', () => {
    // error 之后浏览器一定会再触发 close，重连逻辑统一放在 close 里，避免重复调度
    dlog('ws-error');
  });

  // 版本号 — 浮层显示
  dlog(`init ${WEBTERM_DEBUG_VERSION}`);

  // === xterm 5.5.0 移动端 IME 兼容 (v5) ===
  // 各路径 onData/输入来源:
  //   - 桌面英文字母 'x': _keyDown 内 evaluateKeyboardEvent → triggerDataEvent("x") (中文输入法
  //     IME 模式下,字母 keydown 不触发 input event,只走 _keyDown)
  //   - 桌面/iOS Composition 中文: _compositionHelper._finalizeComposition setTimeout(0)
  //     从 textarea substring → triggerDataEvent("测试")
  //   - iOS 直接 input(无 composition): input(insertText, "测试") → _inputEvent →
  //     triggerDataEvent("测试")
  //   - 控制键 (Enter/Tab/方向键/Backspace/Ctrl-x): _keyDown 处理 → triggerDataEvent
  //
  // 修法:
  //   1) mock _inputEvent no-op:断掉 xterm 处理 input 事件路径,所有字符走我们自己
  //      input handler (不依赖 capture 顺序 — prototype mock 一定生效)
  //   2) mock _keyPress / _handleAnyTextareaChanges / _keyDownSeen 兜底
  //   3) sendStr 加 30ms 同 data dedup,防 _keyDown 和 input handler 同时发同字符
  //   4) onData filter: 全 ASCII (单字字母/控制键/ESC 序列) 放行 — 来自 _keyDown
  //      含非 ASCII (中文) 跳过 — 由 input handler 或 _compositionHelper setTimeout
  //      处理,这里跳过避免重复
  try {
    (term as unknown as Record<string, unknown>)._keyPress = function (
      this: { _keyPressHandled: boolean },
    ) {
      this._keyPressHandled = false;
      return false;
    };
    (term as unknown as Record<string, unknown>)._handleAnyTextareaChanges =
      () => {};
    (term as unknown as Record<string, unknown>)._inputEvent = () => {};
    Object.defineProperty(term, '_keyDownSeen', {
      get() {
        return false;
      },
      set() {
        /* swallow */
      },
      configurable: true,
    });
    // 自检 mock 是否生效 — 写后立读,看 setter 是否真的被 swallow
    (term as unknown as Record<string, unknown>)._keyDownSeen = true;
    const seenVal = (term as unknown as { _keyDownSeen: unknown })
      ._keyDownSeen;
    dlog(`mock _keyDownSeen=${String(seenVal)} (expect false)`);
  } catch (e) {
    dlog(`mock failed: ${(e as Error).message}`);
  }

  let composing = false;
  let pasting = false; // paste 事件进行中，让 onData 放行非 ASCII（中文粘贴）
  // dedup: 30ms 内同 data 跳过(防 _keyDown 和 input handler 同时发同字符)
  // 人类敲键间隔 > 100ms,正常输入不被误杀
  let lastSendAt = 0;
  let lastSendData = '';
  const sendStr = (s: string) => {
    const now = Date.now();
    if (now - lastSendAt < 30 && s === lastSendData) return;
    lastSendAt = now;
    lastSendData = s;
    if (ws?.readyState === WebSocket.OPEN) {
      ws.send(new TextEncoder().encode(s));
    }
  };
  const helperTextarea = containerRef.value.querySelector(
    '.xterm-helper-textarea',
  ) as HTMLTextAreaElement | null;
  // 移动端拼音首字母重复修复:
  // iOS/部分安卓 IME 输入组词第一个字母时,事件顺序是 input(insertText,"l") 先于
  // compositionstart —— 此时 composing 仍为 false,insertText 分支会把首字母 "l" 直接
  // 发出;随后 compositionstart 置 composing=true,compositionend 再发完整 "ls",终端收到
  // "l"+"ls"=首字母重复。桌面端 compositionstart 先于 input 触发,故不复现。
  // 修法:insertText 延后一个宏任务再发,若紧随的 compositionstart 触发则取消(吞掉过早的
  // 首字母,组词内容统一由 compositionend 发出)。非组词输入仅晚 ~4ms,无感。
  let pendingInsertTimer: null | ReturnType<typeof setTimeout> = null;
  const cancelPendingInsert = () => {
    if (pendingInsertTimer) {
      clearTimeout(pendingInsertTimer);
      pendingInsertTimer = null;
    }
  };
  if (helperTextarea) {
    helperTextarea.addEventListener('compositionstart', () => {
      composing = true;
      cancelPendingInsert();
      dlog('Cstart');
    });
    helperTextarea.addEventListener('compositionend', (e: Event) => {
      const ce = e as CompositionEvent;
      dlog('Cend');
      composing = false;
      // 桌面 Chromium/Safari IME 不 fire input(insertFromComposition) 事件,
      // 只走 _compositionHelper setTimeout → onData(被 onData filter 跳过非 ASCII 拦掉)
      // → 中文输不上。这里直接用 ev.data 发,dedup 30ms 保证移动端 input handler
      // 也走 insertFromComposition 时不重复
      if (ce.data) {
        sendStr(ce.data);
      }
    });
    // 粘贴路径:paste → xterm onData(含 bracket paste mode 前缀) → sendStr
    // 不在 input(insertFromPaste) 再发一次，否则 bracket 版本和裸文本同时发出导致重复
    let pasteResetTimer: ReturnType<typeof setTimeout> | null = null;
    helperTextarea.addEventListener('paste', () => {
      dlog('paste-event');
      pasting = true;
      if (pasteResetTimer) clearTimeout(pasteResetTimer);
      pasteResetTimer = setTimeout(() => {
        pasting = false;
        pasteResetTimer = null;
      }, 100);
    });
    helperTextarea.addEventListener('input', (e: Event) => {
      const ie = e as InputEvent;
      dlog(`input data="${ie.data ?? ''}" type="${ie.inputType ?? ''}"`);
      if (ie.inputType === 'insertFromComposition' && ie.data) {
        sendStr(ie.data);
      } else if (
        !composing &&
        ie.data &&
        (ie.inputType === 'insertText' ||
          ie.inputType === 'insertReplacementText')
      ) {
        // insertFromPaste 不在这里处理 — 由 paste 事件 + onData 统一走，避免重复
        let data = ie.data;
        // iOS Path B(无 composition):xterm _keyDown 已把首字母经 onData 单独发出,
        // 这里 input 又带完整串(如 onData "l" + input "ls")→ 终端首字母重复 "lls"。
        // 去掉与刚发送内容重复的前缀,只发差额。100ms 内同源(同一次组词提交)才生效。
        if (
          lastSendData &&
          data.startsWith(lastSendData) &&
          Date.now() - lastSendAt < 100
        ) {
          data = data.slice(lastSendData.length);
        }
        if (!data) return;
        // 延后一个宏任务发送:若这是组词首字母(有 composition 的设备),
        // 紧随的 compositionstart 会取消它,避免与 compositionend 的完整串重复
        const payload = data;
        cancelPendingInsert();
        pendingInsertTimer = setTimeout(() => {
          pendingInsertTimer = null;
          sendStr(payload);
        }, 0);
      } else if (!composing && ie.inputType === 'deleteContentBackward') {
        sendStr('\x7f');
      }
    });
    helperTextarea.addEventListener('keydown', (e: Event) => {
      dlog(`keydown code=${(e as KeyboardEvent).keyCode}`);
    });
  }

  term.onData((data) => {
    dlog(`onData "${data.slice(0, 30)}" composing=${composing} pasting=${pasting}`);
    // 全 ASCII (含控制字符 / ESC 序列 / 字母数字符号) → send,来自 _keyDown
    // 含非 ASCII (中文等) → 通常跳过,由 input handler 或 _compositionHelper 处理
    // 例外:pasting=true 时是中文粘贴,需要放行（不走 input 路径）
    let allAscii = true;
    for (let i = 0; i < data.length; i++) {
      if (data.charCodeAt(i) >= 0x80) {
        allAscii = false;
        break;
      }
    }
    if (allAscii || pasting) {
      sendStr(data);
    }
  });

  resizeObserver = new ResizeObserver(() => sendResize());
  resizeObserver.observe(containerRef.value);

  // 终端滚动时更新「是否在底部」,驱动浮窗「回到底部」按钮高亮
  term.onScroll(() => updateAtBottom());
  updateAtBottom();
}

// 只读回放:拉取日志文件原始字节写入 xterm。日志内容含 ANSI 转义序列,
// 直接 write 即可还原颜色/光标,呈现效果与实时终端一致。
async function replayLog() {
  try {
    const buf = await fetchSessionLog(props.sessionId);
    if (!buf) {
      term?.write('\r\n  (该会话没有历史日志输出)\r\n');
      return;
    }
    let bytes = new Uint8Array(buf);
    const max = 3 * 1024 * 1024;
    if (bytes.length > max) {
      term?.write('  [日志较长,仅显示最后 3 MB]\r\n');
      bytes = bytes.slice(bytes.length - max);
    }
    term?.write(bytes);
    term?.scrollToBottom();
  } catch {
    term?.write('\r\n  (日志加载失败)\r\n');
  }
}

// focusTerminal=false 时不抢焦点 —— 供底部文本输入框发送后焦点留在输入框
function sendInput(data: string, focusTerminal = true) {
  if (ws?.readyState === WebSocket.OPEN) {
    ws.send(new TextEncoder().encode(data));
  }
  if (focusTerminal) term?.focus();
}

defineExpose({ connState, latencyMs, reconnect, sendInput });

// === 代码区滚动浮窗 ===
// 移动端 xterm 触摸滚动不灵,提供按钮辅助滚动终端缓冲区
const atBottom = ref(true);
function updateAtBottom() {
  if (!term) return;
  const buf = term.buffer.active;
  atBottom.value = buf.viewportY >= buf.baseY;
}
function termScrollTop() {
  term?.scrollToTop();
}
function termScrollPageUp() {
  term?.scrollPages(-1);
}
function termScrollPageDown() {
  term?.scrollPages(1);
}
function termScrollBottom() {
  term?.scrollToBottom();
}

function destroy() {
  destroying = true;
  stopPing();
  // 清掉待执行的重连，避免组件销毁后仍去新建 WebSocket
  clearReconnect();
  reconnectAttempt = 0;
  resizeObserver?.disconnect();
  resizeObserver = null;
  // 代次自增：让旧连接后续所有回调立即失效（迟到的 close 事件不再触发重连）
  wsGeneration += 1;
  const old = ws;
  ws = null;
  if (old) {
    try {
      // 1000 正常关闭：服务端看到这是主动断开，不会走「顶替」分支
      old.close(1000, 'client destroy');
    } catch {
      /* 已在关闭中 */
    }
  }
  term?.dispose();
  term = null;
  fit = null;
}

// 页面关闭/刷新/切后台时的断连归因（移动端切后台杀 WS 是最常见误判来源）
function onPageHide() {
  const lifespan = openedAt ? Math.round((Date.now() - openedAt) / 1000) : 0;
  dlog(`页面卸载 pagehide 存活=${lifespan}s ${context()}`);
  // sendBeacon 保证在卸载过程中也能发出
  postLog(
    [`${new Date().toISOString().slice(11, 23)} pagehide 存活=${lifespan}s ${context()}`],
    props.sessionId,
  );
}

function onVisibilityChange() {
  if (document.visibilityState === 'hidden') {
    dlog(`页面切后台 hidden ${context()}`);
  } else {
    dlog(`页面回前台 visible ${context()}`);
  }
}

function onOffline() {
  dlog(`网络断开 offline ${context()}`);
}

function onOnline() {
  dlog(`网络恢复 online ${context()}`);
}

onMounted(async () => {
  window.addEventListener('pagehide', onPageHide);
  document.addEventListener('visibilitychange', onVisibilityChange);
  window.addEventListener('offline', onOffline);
  window.addEventListener('online', onOnline);
  // 先拉视觉配置(字体/颜色/光标等);失败也照常用默认值起 xterm
  await loadVisualConfig();
  setup(props.sessionId);
});

onBeforeUnmount(() => {
  window.removeEventListener('pagehide', onPageHide);
  document.removeEventListener('visibilitychange', onVisibilityChange);
  window.removeEventListener('offline', onOffline);
  window.removeEventListener('online', onOnline);
  destroy();
});

watch(
  () => props.sessionId,
  (id) => setup(id),
);
</script>

<template>
  <div class="webterm-wrap">
    <div ref="containerRef" class="webterm-container" />
    <!-- 代码区滚动浮窗 -->
    <div class="webterm-scroller">
      <button
        class="webterm-scroller-btn"
        type="button"
        title="回到顶部"
        @click="termScrollTop"
      >
        ⤒
      </button>
      <button
        class="webterm-scroller-btn"
        type="button"
        title="向上翻一屏"
        @click="termScrollPageUp"
      >
        ▲
      </button>
      <button
        class="webterm-scroller-btn"
        type="button"
        title="向下翻一屏"
        @click="termScrollPageDown"
      >
        ▼
      </button>
      <button
        :class="['webterm-scroller-btn', { 'is-active': !atBottom }]"
        type="button"
        title="回到底部"
        @click="termScrollBottom"
      >
        ⤓
      </button>
    </div>
    <pre v-if="debug" class="webterm-debug">[{{ WEBTERM_DEBUG_VERSION }}]
{{ debugLogs.join('\n') }}</pre>
  </div>
</template>

<style scoped>
.webterm-wrap {
  position: relative;
  width: 100%;
  height: 100%;
}

.webterm-container {
  width: 100%;
  height: 100%;
  padding: 8px;
  background: #1e1e1e;
  overflow: hidden;
}

/* 代码区滚动浮窗 —— 叠在 xterm 固定深色画布上,与 .webterm-debug 同理用半透明深色,
   不走 antd 亮暗主题 token(终端画布颜色不随主题变化) */
.webterm-scroller {
  position: absolute;
  right: 10px;
  bottom: 10px;
  z-index: 9;
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.webterm-scroller-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 34px;
  height: 34px;
  padding: 0;
  font-size: 15px;
  line-height: 1;
  color: rgba(255, 255, 255, 0.85);
  cursor: pointer;
  background: rgba(40, 40, 40, 0.72);
  border: 1px solid rgba(255, 255, 255, 0.18);
  border-radius: 6px;
  transition: background 0.15s, color 0.15s;
}

.webterm-scroller-btn:hover {
  color: #fff;
  background: rgba(64, 64, 64, 0.92);
}

.webterm-scroller-btn:active {
  background: rgba(88, 88, 88, 0.95);
}

.webterm-scroller-btn.is-active {
  color: #fff;
  background: var(--ant-color-primary, #1677ff);
  border-color: transparent;
}

.webterm-debug {
  position: absolute;
  top: 6px;
  right: 6px;
  z-index: 10;
  max-width: 75%;
  max-height: 60%;
  margin: 0;
  padding: 6px 8px;
  overflow: auto;
  font-family: Menlo, Consolas, monospace;
  font-size: 10px;
  line-height: 1.35;
  color: #6cff8d;
  white-space: pre-wrap;
  word-break: break-all;
  background: rgba(0, 0, 0, 0.78);
  border: 1px solid rgba(255, 255, 255, 0.15);
  border-radius: 4px;
  pointer-events: auto;
}
</style>

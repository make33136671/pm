<script lang="ts" setup>
import { computed, nextTick, onBeforeUnmount, onMounted, ref } from 'vue';

import { Page } from '@vben/common-ui';

import {
  Badge,
  Button,
  Cascader,
  Empty,
  Form,
  FormItem,
  Input,
  List,
  ListItem,
  message,
  Modal,
  Popconfirm,
  Tag,
  Tooltip,
  Upload,
} from 'ant-design-vue';

import { directUploadApi } from '#/api/core/upload';

import {
  clearSessionLog,
  createSession,
  deleteSession,
  destroySession,
  downloadSessionLog,
  getProjectTree,
  getSessionList,
} from './api';
import Terminal from './components/Terminal.vue';

// 后端 enum CliSessionStatus { Running, Exited, Terminated, Reaped }
// 框架 JsonStringEnumConverter 序列化为字符串,前端按字符串比较。
const STATUS_RUNNING = 'Running';
const STATUS_EXITED = 'Exited';
const STATUS_TERMINATED = 'Terminated';
const STATUS_REAPED = 'Reaped';

interface CliSession {
  id: number;
  name: string;
  projectId?: number;
  project?: { id: number; name: string };
  ownerUsername: string;
  status: string;
  pid?: number;
  startedAt?: string;
  lastActiveAt?: string;
  exitedAt?: string;
  exitCode?: number;
  terminationReason?: string;
}

const sessions = ref<CliSession[]>([]);
const currentId = ref<null | number>(null);
const loading = ref(false);
let pollTimer: any = null;
// 首次加载(刷新页面)标记 —— 用于「有存活会话时默认隐藏列表」只在初次生效
let firstLoad = true;

// 移动端：顶部功能按钮区折叠状态
const toolbarActionsVisible = ref(false);

// 左侧会话列表折叠状态(移动设备可手动隐藏让出屏幕)
const SIDE_COLLAPSED_KEY = 'webterm:sideCollapsed';
const sideCollapsed = ref(localStorage.getItem(SIDE_COLLAPSED_KEY) === '1');
function toggleSide() {
  sideCollapsed.value = !sideCollapsed.value;
  localStorage.setItem(SIDE_COLLAPSED_KEY, sideCollapsed.value ? '1' : '0');
}

// 已结束会话的归档区是否展开 — 默认收起,放在侧边栏底部
const ARCHIVE_OPEN_KEY = 'webterm:archiveOpen';
const archiveOpen = ref(localStorage.getItem(ARCHIVE_OPEN_KEY) === '1');
function toggleArchive() {
  archiveOpen.value = !archiveOpen.value;
  localStorage.setItem(ARCHIVE_OPEN_KEY, archiveOpen.value ? '1' : '0');
}

// 终端组件 ref —— 用于"回车"按钮触发输入 + 读取连接状态指示灯
type ConnState = 'closed' | 'connected' | 'connecting' | 'high';
const terminalRef = ref<{
  connState: ConnState;
  latencyMs: number;
  reconnect: () => void;
  sendInput: (s: string, focusTerminal?: boolean) => void;
} | null>(null);

// 连接状态(绿/黄/红指示灯)。只读回放无 ws,统一当 connected 不显示灯。
const connState = computed<ConnState>(
  () => terminalRef.value?.connState ?? 'connecting',
);
const latencyMs = computed(() => terminalRef.value?.latencyMs ?? -1);
const connLabel = computed(() => {
  switch (connState.value) {
    case 'closed': {
      return '已断开';
    }
    case 'connected': {
      return latencyMs.value >= 0 ? `${latencyMs.value}ms` : '已连接';
    }
    case 'high': {
      return `延迟高 ${latencyMs.value}ms`;
    }
    default: {
      return '连接中';
    }
  }
});
// 快捷键按钮统一不抢终端焦点(focusTerminal=false):移动端聚焦 xterm 的
// helper-textarea 会弹出虚拟键盘,点按钮时不应触发键盘。需要打字时用底部
// 输入框或直接点终端区域。
function sendEnter() {
  terminalRef.value?.sendInput('\r', false);
}
function sendArrow(dir: 'up' | 'down' | 'left' | 'right') {
  const seq = { up: '\x1b[A', down: '\x1b[B', right: '\x1b[C', left: '\x1b[D' };
  terminalRef.value?.sendInput(seq[dir], false);
}

// ---- 底部聊天式输入区 ----
// 文本输入框:回车发送到终端;终端原生直接输入仍保留,两种方式并存
const inputText = ref('');

// 发送一行命令:文本和回车必须分两帧发、中间留间隔。
// 合并成 `text\r` 单帧发出时,PTY 端的 TUI 程序(如 Claude Code)会把
// 整块识别成「粘贴」,\r 当成粘贴内容里的换行而非「提交」。分开发并留
// 100ms 间隔,接收端才会把 \r 当成独立的回车键。
function sendLine(text: string) {
  // focusTerminal=false:焦点留在输入框,方便连续输入
  terminalRef.value?.sendInput(text, false);
  setTimeout(() => terminalRef.value?.sendInput('\r', false), 100);
}
function sendText() {
  const text = inputText.value;
  if (!text) return;
  sendLine(text);
  inputText.value = '';
}
function onInputEnter(e: KeyboardEvent) {
  // 中文输入法组词中的回车用于选词,不触发发送
  if (e.isComposing || e.keyCode === 229) return;
  e.preventDefault();
  sendText();
}

// 文件上传:走 framework 通用上传(/Upload/Direct),成功后把下载命令贴进输入框。
// shell 与 pm 系统进程权限域隔离,故不传文件系统路径,改贴 curl 命令(公网 URL),
// 用户回车即把文件拉到 shell 当前目录。
const uploading = ref(false);
async function handleUpload(opt: any) {
  uploading.value = true;
  try {
    const detail = await directUploadApi(opt.file as File);
    const safeName = String(detail.name).replaceAll("'", `'\\''`);
    const cmd = `curl -fsSL '${detail.absfileName}' -o '${safeName}'`;
    inputText.value = inputText.value ? `${inputText.value} ${cmd}` : cmd;
    opt.onSuccess?.({});
  } catch (error) {
    message.error('文件上传失败');
    opt.onError?.(error);
  } finally {
    uploading.value = false;
  }
}

// 点击会话进入。移动端窄屏自动隐藏列表让出空间(不持久化,刷新后仍默认展开)
function selectSession(id: number) {
  currentId.value = id;
  if (
    typeof window !== 'undefined' &&
    window.matchMedia('(max-width: 768px)').matches
  ) {
    sideCollapsed.value = true;
  }
}

async function loadList() {
  loading.value = true;
  try {
    const res = await getSessionList({ page: 1, pageSize: 100 });
    sessions.value = (res?.items ?? []) as CliSession[];
    if (currentId.value == null && sessions.value.length > 0) {
      const running = sessions.value.find((s) => s.status === STATUS_RUNNING);
      currentId.value = running?.id ?? sessions.value[0]?.id ?? null;
    }
    // 首次加载(刷新页面)后:若当前选中的是存活会话,默认隐藏左侧列表,让终端占满
    if (firstLoad) {
      firstLoad = false;
      const cur = sessions.value.find((s) => s.id === currentId.value);
      if (cur && cur.status === STATUS_RUNNING) {
        sideCollapsed.value = true;
      }
    }
  } finally {
    loading.value = false;
  }
}

const currentSession = computed(() =>
  sessions.value.find((s) => s.id === currentId.value),
);

// 主列表只显示运行中会话;已结束的进归档(按结束时间倒序,最近结束的在上)
const aliveSessions = computed(() =>
  sessions.value.filter((s) => s.status === STATUS_RUNNING),
);
const archivedSessions = computed(() =>
  sessions.value
    .filter((s) => s.status !== STATUS_RUNNING)
    .sort((a, b) => {
      const ta = a.exitedAt ?? a.lastActiveAt ?? '';
      const tb = b.exitedAt ?? b.lastActiveAt ?? '';
      return tb.localeCompare(ta);
    }),
);

// ---- 新建会话 ----
interface ProjectTreeNode {
  id: number;
  name: string;
  code?: string;
  computedLocalPath?: null | string;
  children?: ProjectTreeNode[];
}

const createOpen = ref(false);
const projectTree = ref<ProjectTreeNode[]>([]);
const createForm = ref({
  cwd: '',
  name: '',
  // Cascader 选中值: 主项目=[projectId],子项目=[projectId, subProjectId]
  selectedPath: [] as number[],
});

// Cascader options: [{ value, label, children?: [...] }]
const cascaderOptions = computed(() =>
  projectTree.value.map((p) => ({
    value: p.id,
    label: p.code ? `${p.name} (${p.code})` : p.name,
    children: (p.children ?? []).map((s) => ({
      value: s.id,
      label: s.code ? `${s.name} (${s.code})` : s.name,
    })),
  })),
);

function findProjectNode(projectId: number) {
  return projectTree.value.find((p) => p.id === projectId);
}
function findSubProjectNode(projectId: number, subId: number) {
  return findProjectNode(projectId)?.children?.find((s) => s.id === subId);
}

// Cascader 变化时自动填 name / cwd
function onProjectChange(value: number[]) {
  createForm.value.selectedPath = value ?? [];
  const time = new Date().toTimeString().slice(0, 8).replaceAll(':', '');
  if (!value || value.length === 0) {
    createForm.value.name = '';
    createForm.value.cwd = '';
    return;
  }
  const project = findProjectNode(value[0]!);
  if (!project) return;
  if (value.length === 2) {
    const sub = findSubProjectNode(value[0]!, value[1]!);
    if (sub) {
      createForm.value.name = `${project.name}/${sub.name}-${time}`;
      createForm.value.cwd = sub.computedLocalPath ?? '';
    }
  } else {
    createForm.value.name = `${project.name}-${time}`;
    createForm.value.cwd = project.computedLocalPath ?? '';
  }
}

async function openCreate() {
  createForm.value = { cwd: '', name: '', selectedPath: [] };
  createOpen.value = true;
  try {
    projectTree.value = (await getProjectTree()) ?? [];
  } catch {
    projectTree.value = [];
  }
}

async function submitCreate() {
  const [pid, sid] = createForm.value.selectedPath;
  const res = await createSession({
    cols: 120,
    cwd: createForm.value.cwd || undefined,
    name: createForm.value.name || undefined,
    projectId: pid,
    subProjectId: sid,
    rows: 30,
  });
  createOpen.value = false;
  await loadList();
  // res.id 来自后端
  if (res?.id) currentId.value = res.id;
}

// ---- 销毁/清日志/删除 ----
async function onDestroy(id: number) {
  await destroySession(id);
  await loadList();
}

async function onClearLog(id: number) {
  await clearSessionLog(id);
  await loadList();
}

async function onDelete(id: number) {
  await deleteSession(id);
  if (currentId.value === id) currentId.value = null;
  await loadList();
}

async function onDownloadLog(id: number) {
  try {
    await downloadSessionLog(id);
  } catch {
    message.error('日志下载失败');
  }
}

function onTerminalClosed(_reason?: string) {
  // 刷新一次列表,把 Status 更新过来
  loadList();
}

function statusBadgeStatus(s: string) {
  switch (s) {
    case STATUS_RUNNING: {
      return 'processing';
    }
    case STATUS_TERMINATED: {
      return 'error';
    }
    case STATUS_REAPED: {
      return 'warning';
    }
    default: {
      return 'default';
    }
  }
}

function statusLabel(s: string) {
  return (
    {
      [STATUS_EXITED]: '已退出',
      [STATUS_REAPED]: '已回收',
      [STATUS_RUNNING]: '运行中',
      [STATUS_TERMINATED]: '已终止',
    } as Record<string, string>
  )[s] || `状态 ${s}`;
}

// ---- 视口高度兜底 ----
// 基座外壳走 100vh / height:100% 链,iOS Safari 按「大视口」(含地址栏区域)算高,
// 比真正可视区高,导致整页可纵向滚动、把工具栏/输入栏顶出屏幕。这里用
// visualViewport 实测,把布局高度精确钉到「从自身顶部到可视区底部」,
// 顺带在虚拟键盘弹出(visualViewport 变矮)时自动收缩,保证输入栏始终可见。
const layoutRef = ref<HTMLElement>();
function applyViewportHeight() {
  const el = layoutRef.value;
  if (!el) return;
  const vv = window.visualViewport;
  const top = el.getBoundingClientRect().top;
  // 可视区底部在布局坐标系中的位置 = 视觉视口偏移 + 视觉视口高度
  const visibleBottom = vv ? vv.offsetTop + vv.height : window.innerHeight;
  el.style.height = `${Math.max(0, visibleBottom - top)}px`;
}

onMounted(() => {
  loadList();
  pollTimer = setInterval(loadList, 30_000);
  nextTick(applyViewportHeight);
  window.addEventListener('resize', applyViewportHeight);
  window.visualViewport?.addEventListener('resize', applyViewportHeight);
  window.visualViewport?.addEventListener('scroll', applyViewportHeight);
});
onBeforeUnmount(() => {
  if (pollTimer) clearInterval(pollTimer);
  window.removeEventListener('resize', applyViewportHeight);
  window.visualViewport?.removeEventListener('resize', applyViewportHeight);
  window.visualViewport?.removeEventListener('scroll', applyViewportHeight);
});
</script>

<template>
  <Page content-class="!p-0">
    <div ref="layoutRef" class="webterm-layout">
      <!-- 左侧会话列表 -->
      <div v-show="!sideCollapsed" class="webterm-sider">
        <div class="webterm-sider-header">
          <Button type="primary" block @click="openCreate">+ 新建会话</Button>
          <div class="webterm-sider-header-actions">
            <Tooltip title="刷新">
              <Button :loading="loading" @click="loadList">↻ 刷新</Button>
            </Tooltip>
            <Tooltip title="隐藏会话列表">
              <Button @click="toggleSide">« 隐藏</Button>
            </Tooltip>
          </div>
        </div>
        <List
          :data-source="aliveSessions"
          :locale="{ emptyText: '暂无活跃会话,点击 + 新建' }"
          class="webterm-session-list"
        >
          <template #renderItem="{ item }">
            <ListItem
              :class="['webterm-session-item', { active: item.id === currentId }]"
              @click="selectSession(item.id)"
            >
              <div class="webterm-session-row">
                <Badge :status="statusBadgeStatus(item.status) as any" />
                <span class="webterm-session-name">{{ item.name }}</span>
                <Tag v-if="item.project" color="blue">{{ item.project.name }}</Tag>
              </div>
              <div class="webterm-session-meta">
                <span>{{ statusLabel(item.status) }}</span>
                <span v-if="item.pid">· PID {{ item.pid }}</span>
              </div>
            </ListItem>
          </template>
        </List>
        <!-- 归档区:已结束会话 -->
        <div class="webterm-archive">
          <div class="webterm-archive-header" @click="toggleArchive">
            <span class="webterm-archive-caret">{{ archiveOpen ? '▼' : '▶' }}</span>
            <span class="webterm-archive-title">已结束</span>
            <span class="webterm-archive-count">({{ archivedSessions.length }})</span>
          </div>
          <div v-if="archiveOpen" class="webterm-archive-list">
            <div
              v-for="item in archivedSessions"
              :key="item.id"
              :class="['webterm-session-item', 'archived', { active: item.id === currentId }]"
              @click="selectSession(item.id)"
            >
              <div class="webterm-session-row">
                <Badge :status="statusBadgeStatus(item.status) as any" />
                <span class="webterm-session-name">{{ item.name }}</span>
              </div>
              <div class="webterm-session-meta">
                <span>{{ statusLabel(item.status) }}</span>
                <span v-if="item.project">· {{ item.project.name }}</span>
              </div>
            </div>
            <div v-if="archivedSessions.length === 0" class="webterm-archive-empty">
              暂无已结束会话
            </div>
          </div>
        </div>
      </div>

      <!-- 右侧主区 -->
      <div class="webterm-main">
        <div class="webterm-toolbar">
          <div class="webterm-toolbar-left">
            <!-- 移动端始终显示；桌面端仅侧边栏折叠时显示 -->
            <Tooltip :title="sideCollapsed ? '显示会话列表' : '隐藏会话列表'">
              <Button
                :class="['webterm-side-btn', { 'desktop-hidden': !sideCollapsed }]"
                @click="toggleSide"
              >{{ sideCollapsed ? '»' : '«' }}</Button>
            </Tooltip>
            <span v-if="currentSession" class="webterm-toolbar-title">
              {{ currentSession.name }}
              <Tag>{{ statusLabel(currentSession.status) }}</Tag>
            </span>
            <span v-else class="webterm-toolbar-title">未选择会话</span>
            <!-- 连接状态指示灯:绿=正常 黄=延迟高 红=断开;断开时旁显重连 -->
            <span
              v-if="currentSession && currentSession.status === STATUS_RUNNING"
              class="webterm-conn"
            >
              <Tooltip :title="connLabel">
                <span :class="['webterm-conn-dot', `is-${connState}`]" />
              </Tooltip>
              <span class="webterm-conn-text">{{ connLabel }}</span>
              <Button
                v-if="connState === 'closed'"
                size="small"
                type="primary"
                @click="terminalRef?.reconnect()"
              >
                重连
              </Button>
            </span>
            <!-- 移动端专用：展开/收起功能按钮 -->
            <Button
              v-if="currentSession"
              class="webterm-actions-toggle"
              @click="toolbarActionsVisible = !toolbarActionsVisible"
            >{{ toolbarActionsVisible ? '✕' : '⋮' }}</Button>
          </div>
          <div
            v-if="currentSession"
            :class="['webterm-toolbar-actions', { 'mobile-open': toolbarActionsVisible }]"
          >
            <Popconfirm
              v-if="currentSession.status === STATUS_RUNNING"
              title="确认销毁会话?(发 SIGTERM,2s 后 SIGKILL)"
              @confirm="onDestroy(currentSession.id)"
            >
              <Button danger>销毁</Button>
            </Popconfirm>
            <Popconfirm
              v-if="currentSession.status === STATUS_RUNNING"
              title="清空当前会话的输出日志?"
              @confirm="onClearLog(currentSession.id)"
            >
              <Button>清日志</Button>
            </Popconfirm>
            <Button @click="onDownloadLog(currentSession.id)">
              下载日志
            </Button>
            <Popconfirm
              title="删除会话记录?(同时删日志文件,不可恢复)"
              @confirm="onDelete(currentSession.id)"
            >
              <Button danger>删除</Button>
            </Popconfirm>
          </div>
        </div>
        <div class="webterm-body">
          <Terminal
            v-if="currentSession && currentSession.status === STATUS_RUNNING"
            ref="terminalRef"
            :key="`live-${currentSession.id}`"
            :session-id="currentSession.id"
            @closed="onTerminalClosed"
          />
          <Terminal
            v-else-if="currentSession"
            :key="`log-${currentSession.id}`"
            :session-id="currentSession.id"
            readonly
          />
          <Empty
            v-else
            description="左侧选择会话或点击 + 新建"
            class="webterm-empty"
          />
        </div>
        <div
          v-if="currentSession && currentSession.status === STATUS_RUNNING"
          class="webterm-inputbar"
        >
          <!-- 第一行:文件上传 + 文本输入框 + 发送 -->
          <div class="webterm-inputbar-main">
            <Upload
              :show-upload-list="false"
              :custom-request="handleUpload"
              :disabled="uploading"
            >
              <Tooltip title="上传文件 — 成功后把下载命令贴进输入框">
                <Button :loading="uploading" class="webterm-upload-btn">📎</Button>
              </Tooltip>
            </Upload>
            <Input
              v-model:value="inputText"
              class="webterm-input"
              placeholder="输入命令,回车发送到终端"
              allow-clear
              @keydown.enter="onInputEnter"
            />
            <Button type="primary" :disabled="!inputText" @click="sendText">
              发送
            </Button>
          </div>
          <!-- 第二行:方向键等快捷功能 -->
          <!-- @mousedown.prevent:阻止按钮抢焦点,避免点击时焦点跳动 / 触发虚拟键盘 -->
          <div class="webterm-inputbar-keys">
            <Button @mousedown.prevent @click="sendArrow('up')">↑</Button>
            <Button @mousedown.prevent @click="sendArrow('down')">↓</Button>
            <Button @mousedown.prevent @click="sendArrow('left')">←</Button>
            <Button @mousedown.prevent @click="sendArrow('right')">→</Button>
            <Button @mousedown.prevent @click="terminalRef?.sendInput('\t', false)">
              Tab
            </Button>
            <Button @mousedown.prevent @click="sendLine('ok')">ok</Button>
            <Button @mousedown.prevent @click="sendLine('继续')">继续</Button>
            <Button @mousedown.prevent @click="terminalRef?.sendInput('\n', false)">
              换行
            </Button>
            <Button @mousedown.prevent @click="terminalRef?.sendInput(' ', false)">
              空格
            </Button>
            <Button type="primary" @mousedown.prevent @click="sendEnter">
              ⏎ 回车
            </Button>
          </div>
        </div>
      </div>
    </div>

    <!-- 新建会话模态框 -->
    <Modal
      v-model:open="createOpen"
      title="新建会话"
      @ok="submitCreate"
    >
      <Form layout="vertical">
        <FormItem label="关联项目(可选)">
          <Cascader
            :value="createForm.selectedPath"
            :options="cascaderOptions"
            :change-on-select="true"
            placeholder="选主项目或下钻到子项目;留空表示无项目会话"
            allow-clear
            @change="onProjectChange"
          />
        </FormItem>
        <FormItem label="名称">
          <Input v-model:value="createForm.name" placeholder="不填默认 Session-时分秒" />
        </FormItem>
        <FormItem label="工作目录">
          <Input v-model:value="createForm.cwd" placeholder="留空使用全局默认 cwd" />
        </FormItem>
      </Form>
    </Modal>
  </Page>
</template>

<style scoped>
.webterm-layout {
  /* 锚定到 Page 外层 div（position:relative），完全绕开百分比高度链 */
  position: absolute;
  inset: 0;
  display: flex;
  overflow: hidden;
  gap: 12px;
}

.webterm-sider {
  display: flex;
  flex-direction: column;
  width: 280px;
  background: var(--ant-color-bg-container);
  border: 1px solid var(--ant-color-border-secondary);
  border-radius: 6px;
}

.webterm-sider-header {
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding: 12px;
  border-bottom: 1px solid var(--ant-color-split);
}

.webterm-sider-header-actions {
  display: flex;
  gap: 8px;
}

.webterm-sider-header-actions > * {
  flex: 1;
}

.webterm-session-list {
  flex: 1;
  overflow-y: auto;
}

.webterm-session-item {
  display: flex;
  flex-direction: column;
  align-items: stretch !important;
  padding: 12px 14px !important;
  cursor: pointer;
  border-bottom: 1px solid var(--ant-color-split);
  transition: background 0.15s;
}

.webterm-session-item:hover {
  background: var(--ant-color-fill-tertiary);
}

.webterm-session-item.active {
  background: var(--ant-color-primary-bg);
  box-shadow: inset 3px 0 0 var(--ant-color-primary);
}

.webterm-session-item.active .webterm-session-name {
  color: var(--ant-color-primary);
}

.webterm-session-row {
  display: flex;
  gap: 8px;
  align-items: center;
}

.webterm-session-name {
  flex: 1;
  font-weight: 500;
  font-size: 14px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.webterm-session-meta {
  margin-top: 6px;
  font-size: 12px;
  color: var(--ant-color-text-tertiary);
}

/* 归档区:已结束会话 */
.webterm-archive {
  border-top: 1px solid var(--ant-color-border-secondary);
  background: var(--ant-color-fill-quaternary);
}

.webterm-archive-header {
  display: flex;
  gap: 6px;
  align-items: center;
  padding: 10px 14px;
  cursor: pointer;
  user-select: none;
  font-size: 13px;
  color: var(--ant-color-text-secondary);
  transition: background 0.15s;
}

.webterm-archive-header:hover {
  background: var(--ant-color-fill-tertiary);
}

.webterm-archive-caret {
  font-size: 10px;
  width: 10px;
  display: inline-block;
}

.webterm-archive-title {
  flex: 1;
  font-weight: 500;
}

.webterm-archive-count {
  color: var(--ant-color-text-tertiary);
}

.webterm-archive-list {
  max-height: 40vh;
  overflow-y: auto;
  border-top: 1px solid var(--ant-color-split);
}

.webterm-archive-list .webterm-session-item.archived {
  opacity: 0.75;
}

.webterm-archive-empty {
  padding: 16px 14px;
  text-align: center;
  font-size: 12px;
  color: var(--ant-color-text-tertiary);
}

.webterm-main {
  display: flex;
  flex: 1;
  flex-direction: column;
  /* min-width/height:0 —— 允许 flex 子项收缩到内容尺寸以下,
     否则终端内容会把工具栏/输入栏撑出容器、整页出现滚动条 */
  min-width: 0;
  min-height: 0;
  background: var(--ant-color-bg-container);
  border: 1px solid var(--ant-color-border-secondary);
  border-radius: 6px;
  overflow: hidden;
}

.webterm-toolbar {
  flex-shrink: 0;
  position: sticky;
  top: 0;
  z-index: 5;
  display: flex;
  flex-wrap: wrap;
  justify-content: space-between;
  align-items: center;
  gap: 8px;
  padding: 10px 12px;
  background: var(--ant-color-bg-container);
  border-bottom: 1px solid var(--ant-color-split);
}

.webterm-toolbar-left {
  display: flex;
  align-items: center;
  gap: 8px;
  min-width: 0;
}

.webterm-toolbar-title {
  flex: 1;
  font-weight: 500;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  min-width: 0;
}

.webterm-toolbar-actions {
  display: flex;
  gap: 8px;
  flex-wrap: wrap;
}

/* 连接状态指示灯 —— 自绘元素用 shadcn :root token(自定义 div 用 --ant-color-* 会透明) */
.webterm-conn {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  flex-shrink: 0;
}

.webterm-conn-dot {
  width: 9px;
  height: 9px;
  border-radius: 50%;
  background: hsl(var(--muted-foreground));
  transition: background 0.2s;
}

.webterm-conn-dot.is-connected {
  background: hsl(var(--success));
  box-shadow: 0 0 0 3px hsl(var(--success) / 0.18);
}

.webterm-conn-dot.is-high {
  background: hsl(var(--warning));
  box-shadow: 0 0 0 3px hsl(var(--warning) / 0.18);
}

.webterm-conn-dot.is-closed {
  background: hsl(var(--destructive));
  box-shadow: 0 0 0 3px hsl(var(--destructive) / 0.18);
}

.webterm-conn-dot.is-connecting {
  background: hsl(var(--warning));
  animation: webterm-conn-blink 1s ease-in-out infinite;
}

@keyframes webterm-conn-blink {
  0%,
  100% {
    opacity: 1;
  }

  50% {
    opacity: 0.3;
  }
}

.webterm-conn-text {
  font-size: 12px;
  color: hsl(var(--muted-foreground));
}

/* 桌面端：侧边栏展开时隐藏切换按钮（侧边栏自己有隐藏按钮） */
.webterm-side-btn.desktop-hidden {
  display: none;
}

/* 移动端功能折叠按钮：桌面端不显示 */
.webterm-actions-toggle {
  display: none;
  flex-shrink: 0;
  margin-left: auto;
}

@media (max-width: 768px) {
  /* 移动端 toolbar-left 占满整行 */
  .webterm-toolbar-left {
    flex: 1;
  }

  /* 移动端：侧边栏切换按钮始终可见 */
  .webterm-side-btn.desktop-hidden {
    display: inline-flex;
  }

  /* 移动端：折叠按钮显示 */
  .webterm-actions-toggle {
    display: inline-flex;
  }

  /* 移动端：功能按钮区默认隐藏 */
  .webterm-toolbar-actions {
    display: none;
    width: 100%;
    padding-top: 8px;
    border-top: 1px solid var(--ant-color-split);
  }

  /* 移动端：功能按钮区展开 */
  .webterm-toolbar-actions.mobile-open {
    display: flex;
  }
}

.webterm-body {
  flex: 1;
  /* 同理:min-height:0 让终端区在剩余空间内收缩,内容超高时由 xterm 内部滚动,
     而不是把同级的输入栏顶出可视区 */
  min-height: 0;
  overflow: hidden;
}

/* 底部聊天式输入区 —— 始终钉在可视区最底部，键盘弹出时随容器缩小被顶上来 */
.webterm-inputbar {
  flex-shrink: 0; /* 不被终端内容挤压 */
  position: sticky;
  bottom: 0;
  z-index: 5;
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding: 10px 12px;
  background: var(--ant-color-bg-container);
  border-top: 1px solid var(--ant-color-split);
}

.webterm-inputbar-main {
  display: flex;
  gap: 8px;
  align-items: center;
}

.webterm-input {
  flex: 1;
}

.webterm-upload-btn {
  font-size: 16px;
}

.webterm-inputbar-keys {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}

.webterm-empty {
  margin-top: 64px;
}

.w-full {
  width: 100%;
}
</style>

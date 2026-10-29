<script lang="ts" setup>
import { nextTick, onMounted, ref } from 'vue';

import { Page } from '@vben/common-ui';

import {
  Button,
  Card,
  Empty,
  Input,
  Select,
  Spin,
  Tag,
  Tooltip,
} from 'ant-design-vue';

import { getList as getProjectList } from '../../project/api';
import { getSubProjectList } from '../../project/api';
import { postAiExecute, getAiTaskHistory } from '../api';

// ========== 项目选择 ==========

const projects = ref<any[]>([]);
const subProjects = ref<any[]>([]);
const selectedProjectId = ref<number | undefined>();
const selectedSubProjectId = ref<number | undefined>();

async function loadProjects() {
  const result: any = await getProjectList({ page: 1, pageSize: 200 });
  projects.value = (result?.items || []).map((p: any) => ({ label: p.name, value: p.id }));
}

async function handleProjectChange(val: number) {
  selectedProjectId.value = val;
  selectedSubProjectId.value = undefined;
  subProjects.value = [];
  if (val) {
    const result: any = await getSubProjectList({ projectId: val });
    subProjects.value = (result?.items || []).map((s: any) => ({ label: `${s.name} (${s.code || ''})`, value: s.id }));
  }
}

// ========== 任务类型 ==========

const taskTypes = [
  { key: 'chat', label: '自由对话', icon: '💬', desc: '项目相关的问答', color: '#1677ff' },
  { key: 'analyze', label: '分析代码', icon: '🔍', desc: '找Bug、性能、安全问题', color: '#faad14' },
  { key: 'generate', label: '生成代码', icon: '⚡', desc: '根据需求编写代码', color: '#52c41a' },
  { key: 'design', label: '设计功能', icon: '📐', desc: '输出架构和设计文档', color: '#722ed1' },
  { key: 'review', label: 'Review', icon: '👁', desc: '审查代码变更', color: '#13c2c2' },
];
const activeTaskType = ref('chat');

function selectTaskType(key: string) {
  activeTaskType.value = key;
  const t = taskTypes.find((t) => t.key === key);
  if (t && messages.value.length === 0) {
    inputValue.value = '';
  }
}

// ========== 对话 ==========

interface ChatMessage {
  role: 'user' | 'assistant';
  content: string;
  taskType?: string;
  codeChanges?: any[];
  loading?: boolean;
  error?: boolean;
  tokens?: { input: number; output: number };
  model?: string;
}

const messages = ref<ChatMessage[]>([]);
const inputValue = ref('');
const sending = ref(false);
const chatAreaRef = ref<HTMLElement>();

async function handleSend() {
  const instruction = inputValue.value?.trim();
  if (!instruction || sending.value) return;

  // 添加用户消息
  messages.value.push({ role: 'user', content: instruction, taskType: activeTaskType.value });
  inputValue.value = '';
  scrollToBottom();

  // 添加 AI loading 占位
  messages.value.push({ role: 'assistant', content: '', loading: true });
  sending.value = true;
  scrollToBottom();

  try {
    // 构建对话历史（排除当前的 loading 消息）
    const history = messages.value
      .filter((m) => !m.loading)
      .slice(0, -1) // 排除刚添加的用户消息（已包含在 instruction 中）
      .map((m) => ({ role: m.role, content: m.content }));

    const result: any = await postAiExecute({
      projectId: selectedProjectId.value || null,
      subProjectId: selectedSubProjectId.value || null,
      instruction,
      taskType: activeTaskType.value,
      conversationHistory: history.length > 1 ? history.slice(-10) : [], // 最近 10 条
    });

    // 替换 loading 消息
    const lastIdx = messages.value.length - 1;
    messages.value[lastIdx] = {
      role: 'assistant',
      content: result.content || '（无返回内容）',
      taskType: activeTaskType.value,
      codeChanges: result.codeChanges,
      tokens: { input: result.inputTokens || 0, output: result.outputTokens || 0 },
      model: result.modelUsed,
    };
  } catch (err: any) {
    const lastIdx = messages.value.length - 1;
    messages.value[lastIdx] = {
      role: 'assistant',
      content: err?.message || '请求失败',
      error: true,
    };
  } finally {
    sending.value = false;
    scrollToBottom();
  }
}

function scrollToBottom() {
  nextTick(() => {
    if (chatAreaRef.value) {
      chatAreaRef.value.scrollTop = chatAreaRef.value.scrollHeight;
    }
  });
}

function clearMessages() {
  messages.value = [];
}

// ========== 历史任务 ==========

const historyTasks = ref<any[]>([]);
const showHistory = ref(false);

async function loadHistory() {
  try {
    const result: any = await getAiTaskHistory({
      projectId: selectedProjectId.value,
      page: 1,
      pageSize: 20,
    });
    historyTasks.value = result?.items || [];
  } catch {
    historyTasks.value = [];
  }
}

function toggleHistory() {
  showHistory.value = !showHistory.value;
  if (showHistory.value) loadHistory();
}

// ========== 初始化 ==========

onMounted(() => {
  loadProjects();
});
</script>

<template>
  <Page auto-content-height>
    <div class="ai-chat-container">
      <!-- 左侧面板 -->
      <div class="ai-sidebar">
        <!-- 项目选择 -->
        <div class="sidebar-section">
          <div class="section-title">项目</div>
          <Select
            v-model:value="selectedProjectId"
            show-search
            :filter-option="(input: string, option: any) => option.label?.toLowerCase().includes(input.toLowerCase())"
            placeholder="选择项目"
            :options="projects"
            allow-clear
            style="width: 100%"
            @change="handleProjectChange"
          />
          <Select
            v-if="subProjects.length > 0"
            v-model:value="selectedSubProjectId"
            placeholder="选择子项目"
            :options="subProjects"
            allow-clear
            style="width: 100%; margin-top: 8px"
          />
        </div>

        <!-- 任务类型 -->
        <div class="sidebar-section">
          <div class="section-title">任务类型</div>
          <div class="task-type-list">
            <div
              v-for="t in taskTypes"
              :key="t.key"
              class="task-type-card"
              :class="{ active: activeTaskType === t.key }"
              :style="activeTaskType === t.key ? { borderColor: t.color } : {}"
              @click="selectTaskType(t.key)"
            >
              <span class="task-icon">{{ t.icon }}</span>
              <div class="task-info">
                <div class="task-label">{{ t.label }}</div>
                <div class="task-desc">{{ t.desc }}</div>
              </div>
            </div>
          </div>
        </div>

        <!-- 操作按钮 -->
        <div class="sidebar-section">
          <Button block @click="clearMessages" :disabled="messages.length === 0">清空对话</Button>
          <Button block style="margin-top: 8px" @click="toggleHistory">
            {{ showHistory ? '返回对话' : '历史任务' }}
          </Button>
        </div>
      </div>

      <!-- 右侧聊天区 -->
      <div class="ai-chat-main">
        <!-- 历史任务 -->
        <div v-if="showHistory" class="chat-history">
          <div class="history-title">历史任务</div>
          <Empty v-if="historyTasks.length === 0" description="暂无历史任务" />
          <div v-for="task in historyTasks" :key="task.id" class="history-item">
            <div class="history-header">
              <Tag :color="task.status === 'Completed' ? 'green' : task.status === 'Failed' ? 'red' : 'blue'">
                {{ task.status }}
              </Tag>
              <Tag>{{ task.taskType }}</Tag>
              <span class="history-time">{{ new Date(task.createdTime).toLocaleString('zh-CN') }}</span>
            </div>
            <div v-if="task.inputData" class="history-content">{{ tryParseInstruction(task.inputData) }}</div>
            <div v-if="task.tokensUsed" class="history-tokens">Token: {{ task.tokensUsed }}</div>
          </div>
        </div>

        <!-- 对话区域 -->
        <template v-if="!showHistory">
          <div ref="chatAreaRef" class="chat-messages">
            <div v-if="messages.length === 0" class="chat-welcome">
              <div class="welcome-icon">🤖</div>
              <div class="welcome-title">AI 助手</div>
              <div class="welcome-desc">
                选择任务类型，输入指令开始对话。
                {{ selectedProjectId ? '' : '建议先在左侧选择项目以获得更好的上下文。' }}
              </div>
            </div>

            <div v-for="(msg, idx) in messages" :key="idx" class="chat-message" :class="msg.role">
              <div class="message-bubble" :class="{ error: msg.error }">
                <Spin v-if="msg.loading" size="small" />
                <template v-else>
                  <div v-if="msg.role === 'user'" class="message-text">{{ msg.content }}</div>
                  <div v-else class="message-text markdown-body" v-html="renderMarkdown(msg.content)"></div>
                  <!-- 代码变更列表 -->
                  <div v-if="msg.codeChanges?.length" class="code-changes">
                    <div class="changes-title">代码变更 ({{ msg.codeChanges.length }} 个文件)</div>
                    <div v-for="(change, ci) in msg.codeChanges" :key="ci" class="change-item">
                      <Tag :color="change.action === 'create' ? 'green' : change.action === 'delete' ? 'red' : 'blue'">
                        {{ change.action }}
                      </Tag>
                      <code>{{ change.filePath }}</code>
                      <span v-if="change.description" class="change-desc">{{ change.description }}</span>
                    </div>
                  </div>
                  <!-- Token 信息 -->
                  <div v-if="msg.tokens && (msg.tokens.input || msg.tokens.output)" class="message-meta">
                    <span v-if="msg.model">{{ msg.model }}</span>
                    <span>Token: {{ msg.tokens.input + msg.tokens.output }}</span>
                  </div>
                </template>
              </div>
            </div>
          </div>

          <!-- 输入区 -->
          <div class="chat-input-area">
            <div class="input-row">
              <Input.TextArea
                v-model:value="inputValue"
                :auto-size="{ minRows: 1, maxRows: 5 }"
                placeholder="输入指令，按 Enter 发送..."
                @press-enter="(e: KeyboardEvent) => { if (!e.shiftKey) { e.preventDefault(); handleSend(); } }"
              />
              <Button type="primary" :loading="sending" :disabled="!inputValue?.trim()" @click="handleSend">
                发送
              </Button>
            </div>
            <div class="input-hint">
              <Tag :color="taskTypes.find(t => t.key === activeTaskType)?.color">
                {{ taskTypes.find(t => t.key === activeTaskType)?.icon }}
                {{ taskTypes.find(t => t.key === activeTaskType)?.label }}
              </Tag>
              <span v-if="selectedProjectId">项目已选择</span>
              <span>Shift+Enter 换行</span>
            </div>
          </div>
        </template>
      </div>
    </div>
  </Page>
</template>

<script lang="ts">
// 简易 Markdown 渲染（将代码块和基本格式转为 HTML）
function renderMarkdown(text: string): string {
  if (!text) return '';
  return text
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    // 代码块
    .replace(/```(\w*)\n([\s\S]*?)```/g, '<pre><code class="language-$1">$2</code></pre>')
    // 行内代码
    .replace(/`([^`]+)`/g, '<code>$1</code>')
    // 标题
    .replace(/^### (.+)$/gm, '<h4>$1</h4>')
    .replace(/^## (.+)$/gm, '<h3>$1</h3>')
    .replace(/^# (.+)$/gm, '<h2>$1</h2>')
    // 粗体
    .replace(/\*\*(.+?)\*\*/g, '<strong>$1</strong>')
    // 列表
    .replace(/^- (.+)$/gm, '<li>$1</li>')
    .replace(/^(\d+)\. (.+)$/gm, '<li>$2</li>')
    // 分隔线
    .replace(/^---$/gm, '<hr/>')
    // 换行
    .replace(/\n/g, '<br/>');
}

function tryParseInstruction(inputData: string): string {
  try {
    const data = JSON.parse(inputData);
    return data.instruction || inputData;
  } catch {
    return inputData;
  }
}

export default {
  methods: { renderMarkdown, tryParseInstruction },
};
</script>

<style scoped>
.ai-chat-container {
  display: flex;
  gap: 0;
  height: calc(100vh - 120px);
}

.ai-sidebar {
  display: flex;
  flex-shrink: 0;
  flex-direction: column;
  gap: 16px;
  width: 260px;
  padding: 16px;
  overflow-y: auto;
  border-right: 1px solid var(--ant-color-border);
}

.sidebar-section {
  display: flex;
  flex-direction: column;
}

.section-title {
  margin-bottom: 8px;
  font-size: 12px;
  font-weight: 600;
  color: var(--ant-color-text-tertiary);
  text-transform: uppercase;
  letter-spacing: 1px;
}

.task-type-list {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.task-type-card {
  display: flex;
  gap: 10px;
  align-items: center;
  padding: 10px 12px;
  cursor: pointer;
  border: 1px solid var(--ant-color-border);
  border-radius: 8px;
  transition: all 0.2s;
}

.task-type-card:hover {
  background: var(--ant-color-fill-quaternary);
}

.task-type-card.active {
  background: var(--ant-color-primary-bg);
  border-width: 2px;
}

.task-icon {
  flex-shrink: 0;
  font-size: 20px;
}

.task-info {
  flex: 1;
  min-width: 0;
}

.task-label {
  font-size: 14px;
  font-weight: 500;
  color: var(--ant-color-text);
}

.task-desc {
  font-size: 11px;
  color: var(--ant-color-text-tertiary);
}

/* 聊天主区域 */
.ai-chat-main {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-width: 0;
}

.chat-messages {
  display: flex;
  flex: 1;
  flex-direction: column;
  gap: 16px;
  padding: 20px;
  overflow-y: auto;
}

.chat-welcome {
  margin: auto;
  color: var(--ant-color-text-tertiary);
  text-align: center;
}

.welcome-icon {
  margin-bottom: 12px;
  font-size: 48px;
}

.welcome-title {
  margin-bottom: 8px;
  font-size: 20px;
  font-weight: 600;
  color: var(--ant-color-text);
}

.welcome-desc {
  max-width: 400px;
  font-size: 14px;
}

/* 消息 */
.chat-message {
  display: flex;
}

.chat-message.user {
  justify-content: flex-end;
}

.chat-message.assistant {
  justify-content: flex-start;
}

.message-bubble {
  max-width: 80%;
  padding: 12px 16px;
  font-size: 14px;
  line-height: 1.6;
  border-radius: 12px;
}

.chat-message.user .message-bubble {
  color: #fff;
  background: var(--ant-color-primary);
  border-bottom-right-radius: 4px;
}

.chat-message.assistant .message-bubble {
  color: var(--ant-color-text);
  background: var(--ant-color-fill-quaternary);
  border: 1px solid var(--ant-color-border);
  border-bottom-left-radius: 4px;
}

.message-bubble.error {
  color: var(--ant-color-error);
  background: var(--ant-color-error-bg);
  border-color: var(--ant-color-error-border);
}

.message-text :deep(pre) {
  padding: 12px;
  margin: 8px 0;
  overflow-x: auto;
  background: var(--ant-color-bg-elevated);
  border: 1px solid var(--ant-color-border);
  border-radius: 6px;
}

.message-text :deep(code) {
  font-family: Menlo, Monaco, 'Courier New', monospace;
  font-size: 13px;
}

.message-text :deep(h2), .message-text :deep(h3), .message-text :deep(h4) {
  margin: 12px 0 6px;
  font-weight: 600;
}

.message-meta {
  display: flex;
  gap: 8px;
  margin-top: 8px;
  font-size: 11px;
  color: var(--ant-color-text-quaternary);
}

/* 代码变更 */
.code-changes {
  padding-top: 8px;
  margin-top: 12px;
  border-top: 1px solid var(--ant-color-border-secondary);
}

.changes-title {
  margin-bottom: 6px;
  font-size: 13px;
  font-weight: 500;
}

.change-item {
  display: flex;
  gap: 6px;
  align-items: center;
  padding: 4px 0;
  font-size: 13px;
}

.change-item code {
  font-family: monospace;
  color: var(--ant-color-primary);
}

.change-desc {
  font-size: 12px;
  color: var(--ant-color-text-tertiary);
}

/* 输入区 */
.chat-input-area {
  padding: 12px 20px;
  background: var(--ant-color-bg-container);
  border-top: 1px solid var(--ant-color-border);
}

.input-row {
  display: flex;
  gap: 8px;
  align-items: flex-end;
}

.input-row :deep(.ant-input) {
  border-radius: 8px;
}

.input-hint {
  display: flex;
  gap: 12px;
  align-items: center;
  margin-top: 6px;
  font-size: 12px;
  color: var(--ant-color-text-quaternary);
}

/* 历史 */
.chat-history {
  flex: 1;
  padding: 20px;
  overflow-y: auto;
}

.history-title {
  margin-bottom: 16px;
  font-size: 16px;
  font-weight: 600;
}

.history-item {
  padding: 12px;
  margin-bottom: 8px;
  border: 1px solid var(--ant-color-border);
  border-radius: 8px;
}

.history-header {
  display: flex;
  gap: 6px;
  align-items: center;
  margin-bottom: 6px;
}

.history-time {
  margin-left: auto;
  font-size: 12px;
  color: var(--ant-color-text-tertiary);
}

.history-content {
  overflow: hidden;
  text-overflow: ellipsis;
  font-size: 13px;
  color: var(--ant-color-text-secondary);
  white-space: nowrap;
}

.history-tokens {
  margin-top: 4px;
  font-size: 11px;
  color: var(--ant-color-text-quaternary);
}
</style>

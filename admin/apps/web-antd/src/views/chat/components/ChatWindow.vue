<script lang="ts" setup>
import { ref, computed, onMounted, nextTick, watch } from 'vue';
import { Spin, Empty, Tag, Button, Input, Checkbox, Space, message } from 'ant-design-vue';
import { getMessageList, postMessageSend, postMessageExportMd, postMessageAnalyzeSelected } from '../api';
import BatchImportModal from './BatchImportModal.vue';
import LottieSticker from './LottieSticker.vue';

const props = withDefaults(defineProps<{
  groupBindingId?: number;
  projectId?: number;
  height?: string;
  sendable?: boolean;
}>(), {
  height: '500px',
  sendable: true,
});

const inputText = ref('');
const sending = ref(false);
const selectMode = ref(false);
const selectedIds = ref<Set<number>>(new Set());
const analyzing = ref(false);
const exporting = ref(false);

const selectedCount = computed(() => selectedIds.value.size);
const avatarErrors = ref(new Set<string>());
const batchModalOpen = ref(false);

interface MediaItem {
  id: number;
  mimeType: string;
  fileName: string;
  fileSize: number;
}

interface MessageItem {
  id: number;
  senderName: string;
  senderId: string;
  content: string;
  messageType: string;
  createdTime: string;
  mediaList?: MediaItem[];
  groupBinding?: { groupName: string };
}

const messages = ref<MessageItem[]>([]);
const loading = ref(false);
const hasMore = ref(true);
const page = ref(1);
const pageSize = 30;
const chatAreaRef = ref<HTMLElement>();

async function loadMessages(append = false) {
  if (loading.value) return;
  loading.value = true;
  try {
    const params: any = { page: page.value, pageSize, field: 'id', order: 'descend' };
    if (props.groupBindingId) params.groupBindingId = props.groupBindingId;
    const res: any = await getMessageList(params);
    const items: MessageItem[] = res?.items || [];

    if (append) {
      // 加载更多（旧消息在上面）
      messages.value = [...items.reverse(), ...messages.value];
    } else {
      messages.value = items.reverse();
      await nextTick();
      scrollToBottom();
    }
    hasMore.value = items.length === pageSize;
  } finally {
    loading.value = false;
  }
}

function loadMore() {
  page.value++;
  loadMessages(true);
}

function scrollToBottom() {
  if (chatAreaRef.value) {
    chatAreaRef.value.scrollTop = chatAreaRef.value.scrollHeight;
  }
}

function formatTime(dateStr: string) {
  if (!dateStr) return '';
  const d = new Date(dateStr);
  const now = new Date();
  const isToday = d.toDateString() === now.toDateString();
  if (isToday) return d.toLocaleTimeString('zh-CN', { hour: '2-digit', minute: '2-digit' });
  return d.toLocaleDateString('zh-CN', { month: '2-digit', day: '2-digit' }) + ' ' +
         d.toLocaleTimeString('zh-CN', { hour: '2-digit', minute: '2-digit' });
}

function getAvatarColor(name: string) {
  const colors = ['#1890ff', '#52c41a', '#fa8c16', '#eb2f96', '#722ed1', '#13c2c2', '#f5222d', '#2f54eb'];
  let hash = 0;
  for (let i = 0; i < (name || '').length; i++) hash = name.charCodeAt(i) + ((hash << 5) - hash);
  return colors[Math.abs(hash) % colors.length];
}

function getAvatarLetter(name: string) {
  return (name || '?').charAt(0).toUpperCase();
}

function getMediaUrl(mediaId: number) {
  return `/api/chat/media/${mediaId}`;
}

// 初始加载 & 监听参数变化
onMounted(() => loadMessages());
watch(() => [props.groupBindingId, props.projectId], () => {
  page.value = 1;
  loadMessages();
});

async function handleSend() {
  if (!inputText.value.trim()) return;
  if (!props.groupBindingId) { message.warning('请先选择群组'); return; }
  sending.value = true;
  try {
    await postMessageSend(props.groupBindingId, inputText.value.trim());
    inputText.value = '';
    // 刷新消息列表
    page.value = 1;
    await loadMessages();
  } catch (e: any) {
    message.error(e?.message || '发送失败');
  } finally {
    sending.value = false;
  }
}

function handleKeyDown(e: KeyboardEvent) {
  if (e.key === 'Enter' && !e.shiftKey) {
    e.preventDefault();
    handleSend();
  }
}

function toggleSelect(id: number) {
  const s = new Set(selectedIds.value);
  if (s.has(id)) s.delete(id); else s.add(id);
  selectedIds.value = s;
}

function toggleSelectAll() {
  if (selectedIds.value.size === messages.value.length) {
    selectedIds.value = new Set();
  } else {
    selectedIds.value = new Set(messages.value.map(m => m.id));
  }
}

function exitSelectMode() {
  selectMode.value = false;
  selectedIds.value = new Set();
}

async function handleAnalyzeSelected() {
  if (selectedIds.value.size === 0) { message.warning('请先选择消息'); return; }
  analyzing.value = true;
  try {
    const ids = Array.from(selectedIds.value).join(',');
    const res: any = await postMessageAnalyzeSelected(ids);
    message.success(res?.message || '分析完成');
    exitSelectMode();
  } finally { analyzing.value = false; }
}

function handleCreateBatch() {
  if (selectedIds.value.size === 0) { message.warning('请先选择消息'); return; }
  batchModalOpen.value = true;
}

function onBatchSuccess() {
  exitSelectMode();
}

async function handleExportMd() {
  if (selectedIds.value.size === 0) { message.warning('请先选择消息'); return; }
  exporting.value = true;
  try {
    const ids = Array.from(selectedIds.value).join(',');
    const blob = await postMessageExportMd(ids);
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `chat-export-${new Date().toISOString().slice(0, 10)}.md`;
    a.click();
    URL.revokeObjectURL(url);
    message.success('导出成功');
  } finally { exporting.value = false; }
}

// 暴露刷新方法
defineExpose({ refresh: () => { page.value = 1; loadMessages(); } });
</script>

<template>
  <div class="chat-window" :style="{ height }">
    <!-- 操作栏 -->
    <div class="chat-toolbar">
      <template v-if="selectMode">
        <Space>
          <span style="font-size: 13px; color: var(--ant-color-text-secondary);">已选 {{ selectedCount }} 条</span>
          <Button size="small" @click="toggleSelectAll">{{ selectedIds.size === messages.length ? '取消全选' : '全选' }}</Button>
          <Button size="small" type="primary" :loading="analyzing" @click="handleAnalyzeSelected">AI 分析</Button>
          <Button size="small" @click="handleCreateBatch">导入批次</Button>
          <Button size="small" :loading="exporting" @click="handleExportMd">导出 MD</Button>
          <Button size="small" @click="exitSelectMode">取消</Button>
        </Space>
      </template>
      <template v-else>
        <Button size="small" @click="selectMode = true">批量选择</Button>
      </template>
    </div>

    <!-- 消息区 -->
    <div ref="chatAreaRef" class="chat-messages">
      <!-- 加载更多 -->
      <div v-if="hasMore" class="chat-load-more">
        <Button size="small" :loading="loading" @click="loadMore">加载更多</Button>
      </div>

      <Empty v-if="!loading && messages.length === 0" description="暂无消息" style="padding: 40px 0;" />

      <!-- 消息列表 -->
      <div v-for="msg in messages" :key="msg.id"
           :class="['chat-msg', { 'chat-msg-self': msg.senderId?.startsWith('admin:'), 'chat-msg-selected': selectedIds.has(msg.id) }]"
           @click="selectMode && toggleSelect(msg.id)">
        <!-- 选择框 -->
        <Checkbox v-if="selectMode" :checked="selectedIds.has(msg.id)" class="chat-checkbox"
                  @click.stop @change="toggleSelect(msg.id)" />

        <!-- 头像 -->
        <div class="chat-avatar" :style="{ background: avatarErrors.has(msg.senderId) ? getAvatarColor(msg.senderName) : 'transparent' }">
          <img v-if="!avatarErrors.has(msg.senderId)" :src="`/api/chat/avatar/${msg.senderId}`"
               class="chat-avatar-img" @error="avatarErrors.add(msg.senderId)" />
          <span v-else>{{ getAvatarLetter(msg.senderName) }}</span>
        </div>

        <div class="chat-body">
          <!-- 名称 + 时间 -->
          <div class="chat-meta">
            <span class="chat-name">{{ msg.senderName }}</span>
            <span class="chat-time">{{ formatTime(msg.createdTime) }}</span>
            <Tag v-if="msg.groupBinding?.groupName" size="small" style="margin-left: 4px;">{{ msg.groupBinding.groupName }}</Tag>
          </div>

          <!-- 内容 -->
          <div class="chat-bubble">
            <!-- 文本 -->
            <div v-if="msg.messageType === 'Text'" class="chat-text">{{ msg.content }}</div>

            <!-- 图片 / 贴纸 -->
            <template v-else-if="msg.mediaList?.length">
              <template v-for="media in msg.mediaList" :key="media.id">
                <LottieSticker v-if="media.mimeType === 'application/x-tgsticker'"
                               :src="getMediaUrl(media.id)" />
                <img v-else-if="media.mimeType?.startsWith('image/')"
                     :src="getMediaUrl(media.id)"
                     class="chat-media-img"
                     @click="window.open(getMediaUrl(media.id), '_blank')" />
                <video v-else-if="media.mimeType?.startsWith('video/')"
                       :src="getMediaUrl(media.id)"
                       class="chat-media-video"
                       autoplay loop muted playsinline />
                <a v-else :href="getMediaUrl(media.id)" target="_blank" class="chat-file-link">
                  📎 {{ media.fileName || '文件' }} ({{ (media.fileSize / 1024).toFixed(1) }}KB)
                </a>
              </template>
              <div v-if="msg.content" class="chat-text" style="margin-top: 4px;">{{ msg.content }}</div>
            </template>

            <!-- 其他类型无媒体 -->
            <div v-else class="chat-text">[{{ msg.messageType }}]</div>
          </div>
        </div>
      </div>
    </div>

    <!-- 发送栏 -->
    <div v-if="sendable && groupBindingId" class="chat-input-bar">
      <Input.TextArea v-model:value="inputText" placeholder="输入消息，Enter 发送，Shift+Enter 换行"
                      :auto-size="{ minRows: 1, maxRows: 4 }" @keydown="handleKeyDown" />
      <Button type="primary" :loading="sending" :disabled="!inputText?.trim()" @click="handleSend">发送</Button>
    </div>

    <!-- 刷新按钮 -->
    <div class="chat-refresh-btn" @click="page = 1; loadMessages()">
      <svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
        <polyline points="23 4 23 10 17 10" /><polyline points="1 20 1 14 7 14" />
        <path d="M3.51 9a9 9 0 0 1 14.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0 0 20.49 15" />
      </svg>
    </div>

    <!-- 加载遮罩 -->
    <div v-if="loading && messages.length === 0" class="chat-loading">
      <Spin />
    </div>

    <!-- 批次导入弹窗 -->
    <BatchImportModal v-model:open="batchModalOpen"
                      :message-ids="Array.from(selectedIds)"
                      :project-id="projectId"
                      :group-binding-id="groupBindingId"
                      @success="onBatchSuccess" />
  </div>
</template>

<style scoped>
.chat-window {
  position: relative;
  display: flex;
  flex-direction: column;
  overflow: hidden;
  background: var(--ant-color-bg-container);
  border: 1px solid var(--ant-color-border);
  border-radius: 8px;
}

.chat-toolbar {
  display: flex;
  align-items: center;
  min-height: 38px;
  padding: 6px 16px;
  background: var(--ant-color-bg-container);
  border-bottom: 1px solid var(--ant-color-border);
}

.chat-messages {
  flex: 1;
  padding: 12px 16px;
  overflow-y: auto;
}

.chat-load-more {
  padding: 8px 0 12px;
  text-align: center;
}

.chat-refresh-btn {
  position: absolute;
  right: 16px;
  bottom: 60px;
  z-index: 10;
  display: flex;
  align-items: center;
  justify-content: center;
  width: 36px;
  height: 36px;
  color: #fff;
  cursor: pointer;
  background: var(--ant-color-primary);
  border-radius: 50%;
  box-shadow: 0 2px 8px rgb(0 0 0 / 15%);
  opacity: 0.8;
  transition: transform 0.2s, opacity 0.2s;
}

.chat-refresh-btn:hover {
  opacity: 1;
  transform: scale(1.1);
}

.chat-msg {
  display: flex;
  gap: 8px;
  margin-bottom: 12px;
}

.chat-avatar {
  display: flex;
  flex-shrink: 0;
  align-items: center;
  justify-content: center;
  width: 32px;
  min-width: 32px;
  height: 32px;
  min-height: 32px;
  margin-top: 2px;
  font-size: 14px;
  font-weight: 600;
  line-height: 1;
  color: #fff;
  border-radius: 50%;
}

.chat-avatar-img {
  width: 100%;
  height: 100%;
  object-fit: cover;
  border-radius: 50%;
}

.chat-body {
  flex: 1;
  min-width: 0;
}

.chat-meta {
  display: flex;
  gap: 6px;
  align-items: center;
  margin-bottom: 2px;
}

.chat-name {
  font-size: 13px;
  font-weight: 500;
  color: var(--ant-color-text);
}

.chat-time {
  font-size: 11px;
  color: var(--ant-color-text-quaternary);
}

.chat-bubble {
  display: inline-block;
  max-width: 80%;
  padding: 8px 12px;
  overflow-wrap: break-word;
  background: var(--ant-color-fill-tertiary);
  border-radius: 0 12px 12px;
}

/* 自己发的消息：右对齐 */
.chat-msg-self {
  flex-direction: row-reverse;
}

.chat-msg-self .chat-body {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
}

.chat-msg-self .chat-meta {
  flex-direction: row-reverse;
}

.chat-msg-self .chat-bubble {
  background: var(--ant-color-primary-bg);
  border-radius: 12px 0 12px 12px;
}

.chat-msg-selected {
  padding: 4px 8px;
  margin-right: -8px;
  margin-left: -8px;
  background: var(--ant-color-primary-bg);
  border-radius: 8px;
}

.chat-checkbox {
  flex-shrink: 0;
  margin-top: 6px;
}

.chat-text {
  font-size: 14px;
  line-height: 1.5;
  color: var(--ant-color-text);
  white-space: pre-wrap;
}

.chat-media-img {
  display: block;
  max-width: 260px;
  max-height: 260px;
  cursor: pointer;
  border-radius: 8px;
}

.chat-media-video {
  display: block;
  max-width: 260px;
  max-height: 260px;
  border-radius: 8px;
}

.chat-file-link {
  display: inline-block;
  padding: 4px 0;
  color: var(--ant-color-primary);
}

.chat-input-bar {
  display: flex;
  gap: 8px;
  align-items: flex-end;
  padding: 10px 16px;
  background: var(--ant-color-bg-container);
  border-top: 1px solid var(--ant-color-border);
}

.chat-input-bar :deep(.ant-input) {
  resize: none;
}

.chat-loading {
  position: absolute;
  inset: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  background: var(--ant-color-bg-container);
}
</style>

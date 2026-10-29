<script lang="ts" setup>
import { ref, watch } from 'vue';
import { Drawer, Select, Tag } from 'ant-design-vue';
import ChatMessages from './ChatMessages.vue';
import ChatInput from './ChatInput.vue';
import { useChat } from './useChat';
import { api } from '#/api/request';

const { state, toggle, close, send, stop, clearMessages } = useChat();

// ---- 项目列表 ----
const projectOptions = ref<{ label: string; value: number }[]>([]);
const projectLoading = ref(false);

async function loadProjects() {
  if (projectOptions.value.length > 0) return;
  projectLoading.value = true;
  try {
    const result: any = await api.get('/Project/List', { params: { page: 1, pageSize: 200 } });
    const items = result?.items || [];
    projectOptions.value = items.map((p: any) => ({ label: p.name, value: p.id }));
  } catch {
    projectOptions.value = [];
  } finally {
    projectLoading.value = false;
  }
}

// ---- 需求列表（依赖项目） ----
const requirementOptions = ref<{ label: string; value: number }[]>([]);
const requirementLoading = ref(false);

async function loadRequirements(projectId: number) {
  requirementLoading.value = true;
  try {
    const result: any = await api.get('/Requirement/List', {
      params: { projectId, page: 1, pageSize: 200 },
    });
    const items = result?.items || [];
    requirementOptions.value = items.map((r: any) => ({
      label: `[${r.type}] ${r.title}`,
      value: r.id,
    }));
  } catch {
    requirementOptions.value = [];
  } finally {
    requirementLoading.value = false;
  }
}

// ---- 文档列表（依赖项目） ----
const documentOptions = ref<{ label: string; value: number }[]>([]);
const documentLoading = ref(false);

async function loadDocuments(projectId: number) {
  documentLoading.value = true;
  try {
    const result: any = await api.get('/ProjectDocument/List', {
      params: { projectId, page: 1, pageSize: 200 },
    });
    const items = result?.items || [];
    documentOptions.value = items.map((d: any) => ({
      label: d.title,
      value: d.id,
    }));
  } catch {
    documentOptions.value = [];
  } finally {
    documentLoading.value = false;
  }
}

// ---- 选择事件 ----

function onProjectChange(val: number | undefined) {
  const opt = projectOptions.value.find((p) => p.value === val);
  state.context.projectId = val;
  state.context.projectName = opt?.label;
  // 切换项目时清空需求和文档
  state.context.requirementId = undefined;
  state.context.requirementTitle = undefined;
  state.context.documentId = undefined;
  state.context.documentTitle = undefined;
  requirementOptions.value = [];
  documentOptions.value = [];
  if (val) {
    loadRequirements(val);
    loadDocuments(val);
  }
}

function onRequirementChange(val: number | undefined) {
  const opt = requirementOptions.value.find((r) => r.value === val);
  state.context.requirementId = val;
  state.context.requirementTitle = opt?.label;
}

function onDocumentChange(val: number | undefined) {
  const opt = documentOptions.value.find((d) => d.value === val);
  state.context.documentId = val;
  state.context.documentTitle = opt?.label;
}

// 打开 drawer 时加载项目列表
watch(() => state.visible, (visible) => {
  if (visible) loadProjects();
});
</script>

<template>
  <!-- Chat drawer -->
  <Drawer
    :open="state.visible"
    :width="480"
    :mask="false"
    :closable="true"
    title="Claude 助手"
    placement="right"
    class="claude-drawer"
    @close="close"
  >
    <template #extra>
      <button class="clear-btn" @click="clearMessages">清空</button>
    </template>

    <div class="chat-container">
      <!-- 上下文选择区 -->
      <div class="context-selectors">
        <Select
          :value="state.context.projectId"
          :options="projectOptions"
          :loading="projectLoading"
          placeholder="选择项目"
          allow-clear
          show-search
          :filter-option="(input: string, option: any) => option.label.toLowerCase().includes(input.toLowerCase())"
          size="small"
          style="flex: 1"
          @change="onProjectChange"
        />
        <Select
          v-if="state.context.projectId"
          :value="state.context.requirementId"
          :options="requirementOptions"
          :loading="requirementLoading"
          placeholder="选择需求"
          allow-clear
          show-search
          :filter-option="(input: string, option: any) => option.label.toLowerCase().includes(input.toLowerCase())"
          size="small"
          style="flex: 1"
          @change="onRequirementChange"
        />
        <Select
          v-if="state.context.projectId"
          :value="state.context.documentId"
          :options="documentOptions"
          :loading="documentLoading"
          placeholder="选择文档"
          allow-clear
          show-search
          :filter-option="(input: string, option: any) => option.label.toLowerCase().includes(input.toLowerCase())"
          size="small"
          style="flex: 1"
          @change="onDocumentChange"
        />
      </div>

      <!-- 已选上下文标签 -->
      <div v-if="state.context.projectName || state.context.requirementTitle || state.context.documentTitle" class="context-bar">
        <Tag color="purple" v-if="state.context.projectName">{{ state.context.projectName }}</Tag>
        <Tag color="blue" v-if="state.context.requirementTitle">{{ state.context.requirementTitle }}</Tag>
        <Tag color="cyan" v-if="state.context.documentTitle">{{ state.context.documentTitle }}</Tag>
      </div>

      <ChatMessages :messages="state.messages" />
      <ChatInput :loading="state.loading" @send="send" @stop="stop" />
    </div>
  </Drawer>
</template>

<style scoped>
.clear-btn {
  padding: 2px 10px;
  font-size: 13px;
  color: #666;
  cursor: pointer;
  background: transparent;
  border: 1px solid #d9d9d9;
  border-radius: 4px;
}

html.dark .clear-btn {
  color: #aaa;
  border-color: #424242;
}

.clear-btn:hover {
  color: #ff4d4f;
  border-color: #ff4d4f;
}

.chat-container {
  display: flex;
  flex-direction: column;
  height: 100%;
  margin: -24px;
}

.context-selectors {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  padding: 8px 12px;
  background: #fafafa;
  border-bottom: 1px solid #f0f0f0;
}

html.dark .context-selectors {
  background: #1d1d1d;
  border-bottom-color: #303030;
}

.context-bar {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
  padding: 4px 12px;
  border-bottom: 1px solid #f0f0f0;
}

html.dark .context-bar {
  border-bottom-color: #303030;
}
</style>

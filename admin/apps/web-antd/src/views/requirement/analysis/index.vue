<script lang="ts" setup>
import { ref, computed, onMounted } from 'vue';

import { Page } from '@vben/common-ui';
import { usePreferences } from '@vben/preferences';

import {
  Tag,
  Space,
  Button,
  Popconfirm,
  Modal,
  Drawer,
  Card,
  List,
  ListItem,
  ListItemMeta,
  Empty,
  Spin,
  Select,
  Form,
  FormItem,
  Input,
  Pagination,
  message,
} from 'ant-design-vue';
import { MdPreview, MdEditor } from 'md-editor-v3';
import 'md-editor-v3/lib/style.css';
import 'md-editor-v3/lib/preview.css';

import {
  getAnalysisBatchList,
  getStagingList,
  postEditStaging,
  postConfirmSingle,
  postConfirmAll,
  postRejectSingle,
  postRestoreSingle,
  postDiscardBatch,
} from '../api';
import { getList as getProjectList } from '../../project/api';
import { requirementTypeLabels, complexityLabels } from '#/utils/enum-labels';

const { isDark } = usePreferences();
const mdTheme = computed(() => (isDark.value ? 'dark' : 'light'));

// ===== Batch Status =====
const batchStatusLabels: Record<string, string> = {
  Pending: '待审核',
  Partial: '部分确认',
  Confirmed: '已确认',
  Discarded: '已丢弃',
};
const batchStatusColors: Record<string, string> = {
  Pending: 'orange',
  Partial: 'blue',
  Confirmed: 'green',
  Discarded: 'default',
};
const typeColors: Record<string, string> = {
  Feature: 'blue',
  Bugfix: 'red',
  Improvement: 'green',
  Task: 'orange',
  Research: 'purple',
};
const typeOptions = Object.entries(requirementTypeLabels).map(([value, label]) => ({
  value,
  label,
}));

// ===== State =====
const projectList = ref<any[]>([]);
const filterProjectId = ref<number | undefined>();

const batchList = ref<any[]>([]);
const batchLoading = ref(false);
const batchPage = ref(1);
const batchTotal = ref(0);

const selectedBatchId = ref('');
const selectedBatch = ref<any>(null);
const stagingList = ref<any[]>([]);
const stagingLoading = ref(false);

const editVisible = ref(false);
const complexityColorMap: Record<string, string> = {
  Low: 'green',
  Medium: 'blue',
  High: 'orange',
  VeryHigh: 'red',
};
const complexityOptions = Object.entries(complexityLabels).map(([value, label]) => ({ value, label }));
const editForm = ref({ id: 0, title: '', content: '', type: 'Feature', complexity: 'Medium' });
const editSubmitting = ref(false);

// ===== MD Preview =====
const mdPreviewVisible = ref(false);
const mdPreviewContent = ref('');
const mdPreviewTitle = ref('需求文档预览');

const requirementStatusLabels: Record<string, string> = {
  Draft: '草稿', Todo: '待办', InProgress: '进行中',
  Testing: '测试中', Done: '已完成', Closed: '已关闭',
};
const requirementSourceLabels: Record<string, string> = {
  Manual: '手动', Telegram: 'Telegram', AI: 'AI', Git: 'Git',
};

function handleViewMd(req: any) {
  const lines = [
    `# ${req.title}`,
    '',
    '| 属性 | 值 |',
    '|------|------|',
    `| 类型 | ${requirementTypeLabels[req.type] || req.type} |`,
    `| 状态 | ${requirementStatusLabels[req.status] || req.status || '-'} |`,
    `| 来源 | ${requirementSourceLabels[req.source] || req.source || '-'} |`,
    `| 负责人 | ${req.assignee || '-'} |`,
    `| 预估工时 | ${req.estimatedHours ?? '-'} |`,
    `| 截止日期 | ${req.dueDate ? new Date(req.dueDate).toLocaleDateString('zh-CN') : '-'} |`,
    '',
    '## 需求描述',
    '',
    req.content || '',
  ];
  mdPreviewContent.value = lines.join('\n');
  mdPreviewTitle.value = req.title;
  mdPreviewVisible.value = true;
}

const noPending = computed(() => {
  if (!stagingList.value.length) return true;
  return stagingList.value.filter((r) => !r.isDeleted && !r.isConfirmed).length === 0;
});

// ===== Pending counts per project =====
const pendingByProject = ref<Record<number, number>>({});

// ===== Load Projects =====
async function loadProjects() {
  try {
    const res: any = await getProjectList({ pageSize: 999 });
    const raw = res?.items || res || [];
    projectList.value = raw.map((p: any) => ({
      value: p.id,
      label: p.name,
    }));
  } catch {}
}

const projectOptions = computed(() =>
  projectList.value.map((p) => {
    const count = pendingByProject.value[p.value] || 0;
    return {
      value: p.value,
      label: count > 0 ? `${p.label}（${count}条待审）` : p.label,
    };
  }),
);

async function loadPendingCounts() {
  try {
    const res: any = await getAnalysisBatchList({ pageSize: 999 });
    const allBatches = res?.items || res || [];
    const counts: Record<number, number> = {};
    for (const b of allBatches) {
      if (b.status === 'Pending' || b.status === 'Partial') {
        const pending = b.requirementCount - (b.confirmedCount || 0) - (b.deletedCount || 0);
        if (pending > 0) counts[b.projectId] = (counts[b.projectId] || 0) + pending;
      }
    }
    pendingByProject.value = counts;
  } catch {}
}

// ===== Load Batches =====
async function loadBatches() {
  batchLoading.value = true;
  try {
    const res: any = await getAnalysisBatchList({
      projectId: filterProjectId.value,
      page: batchPage.value,
      pageSize: 20,
    });
    batchList.value = res?.items || res || [];
    batchTotal.value = res?.total || batchList.value.length;

    // 自动匹配项目：如果未选项目且有批次，取第一个批次的项目
    if (!filterProjectId.value && batchList.value.length > 0) {
      const firstBatch = batchList.value[0];
      if (firstBatch.projectId) {
        filterProjectId.value = firstBatch.projectId;
      }
    }
  } catch {
    batchList.value = [];
  } finally {
    batchLoading.value = false;
  }
}

// ===== Select Batch =====
async function selectBatch(batch: any) {
  selectedBatchId.value = batch.batchId;
  selectedBatch.value = batch;

  // 自动同步项目筛选
  if (batch.projectId && filterProjectId.value !== batch.projectId) {
    filterProjectId.value = batch.projectId;
  }

  stagingLoading.value = true;
  try {
    const list: any = await getStagingList(batch.batchId);
    stagingList.value = Array.isArray(list) ? list : [];
  } catch {
    stagingList.value = [];
  } finally {
    stagingLoading.value = false;
  }
}

// ===== Actions =====
async function handleConfirmSingle(id: number) {
  await postConfirmSingle(id);
  message.success('已确认');
  await refreshCurrent();
}

async function handleConfirmAll() {
  await postConfirmAll(selectedBatchId.value);
  message.success('已全部确认');
  await refreshCurrent();
}

async function handleReject(id: number) {
  await postRejectSingle(id);
  message.success('已移除');
  await selectBatch(selectedBatch.value);
}

async function handleRestore(id: number) {
  await postRestoreSingle(id);
  message.success('已恢复');
  await selectBatch(selectedBatch.value);
}

async function handleDiscardBatch() {
  await postDiscardBatch(selectedBatchId.value);
  message.success('批次已丢弃');
  selectedBatchId.value = '';
  selectedBatch.value = null;
  stagingList.value = [];
  await loadBatches();
  await loadPendingCounts();
}

function handleEdit(req: any) {
  editForm.value = {
    id: req.id,
    title: req.title,
    content: req.content || '',
    type: req.type,
    complexity: req.complexity || 'Medium',
  };
  editVisible.value = true;
}

async function handleEditSubmit() {
  editSubmitting.value = true;
  try {
    await postEditStaging(editForm.value);
    message.success('更新成功');
    editVisible.value = false;
    await selectBatch(selectedBatch.value);
  } finally {
    editSubmitting.value = false;
  }
}

async function refreshCurrent() {
  await selectBatch(selectedBatch.value);
  await loadBatches();
  await loadPendingCounts();
}

function formatTime(t: string) {
  if (!t) return '';
  return new Date(t).toLocaleString('zh-CN', {
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
  });
}

onMounted(async () => {
  await loadProjects();
  await loadPendingCounts();
  await loadBatches();
});
</script>

<template>
  <Page :show-header="false" content-class="h-full">
    <div class="analysis-layout">
      <!-- Left: Batch List -->
      <Card class="batch-panel" :body-style="{ padding: '12px', flex: 1, overflow: 'auto' }">
        <template #title>
          <span style="font-size: 14px">分析批次</span>
        </template>
        <template #extra>
          <Select
            v-model:value="filterProjectId"
            :options="projectOptions"
            placeholder="全部项目"
            allow-clear
            style="width: 200px"
            size="small"
            @change="() => { batchPage = 1; loadBatches(); }"
          />
        </template>

        <Spin :spinning="batchLoading">
          <List
            :data-source="batchList"
            :locale="{ emptyText: '暂无分析批次' }"
            size="small"
          >
            <template #renderItem="{ item }">
              <ListItem
                class="batch-item"
                :class="{ 'batch-item-active': selectedBatchId === item.batchId }"
                @click="selectBatch(item)"
              >
                <ListItemMeta>
                  <template #title>
                    <div style="display: flex; gap: 6px; align-items: center">
                      <span style="flex: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap">
                        {{ item.batchLabel || item.batchId }}
                      </span>
                      <Tag :color="batchStatusColors[item.status]" style="margin: 0">
                        {{ batchStatusLabels[item.status] || item.status }}
                      </Tag>
                    </div>
                  </template>
                  <template #description>
                    <Space :size="12" class="batch-desc-text">
                      <span>{{ item.projectName || '未关联' }}</span>
                      <span>{{ item.requirementCount }}条</span>
                      <span v-if="item.confirmedCount > 0">已确认{{ item.confirmedCount }}</span>
                      <span>{{ formatTime(item.createdTime) }}</span>
                    </Space>
                  </template>
                </ListItemMeta>
              </ListItem>
            </template>
          </List>
        </Spin>

        <div v-if="batchTotal > 20" style=" padding-top: 8px;text-align: center">
          <Pagination
            v-model:current="batchPage"
            :total="batchTotal"
            :page-size="20"
            size="small"
            simple
            @change="loadBatches"
          />
        </div>
      </Card>

      <!-- Right: Batch Detail -->
      <Card class="detail-panel" :body-style="{ padding: '16px', flex: 1, overflow: 'auto' }">
        <template #title>
          <Space v-if="selectedBatch">
            <span style="font-size: 14px">{{ selectedBatch.batchLabel || selectedBatch.batchId }}</span>
            <Tag :color="batchStatusColors[selectedBatch.status]">
              {{ batchStatusLabels[selectedBatch.status] }}
            </Tag>
          </Space>
          <span class="batch-desc-text" style="font-size: 14px">请选择批次</span>
        </template>
        <template #extra>
          <Space v-if="selectedBatch && selectedBatch.status !== 'Confirmed' && selectedBatch.status !== 'Discarded'">
            <Button type="primary" :disabled="noPending" @click="handleConfirmAll">
              全部确认
            </Button>
            <Popconfirm title="确定丢弃整批？不可恢复" @confirm="handleDiscardBatch">
              <Button danger>丢弃批次</Button>
            </Popconfirm>
          </Space>
        </template>

        <Empty v-if="!selectedBatchId" description="请从左侧选择一个分析批次" />

        <Spin v-else :spinning="stagingLoading">
          <div v-if="selectedBatch?.batchDescription" class="batch-description">
            {{ selectedBatch.batchDescription }}
          </div>

          <Empty v-if="!stagingList.length" description="该批次没有需求" />

          <div v-for="req in stagingList" :key="req.id" class="req-card" :class="{ 'req-deleted': req.isDeleted, 'req-confirmed': req.isConfirmed }">
            <div class="req-header">
              <div class="req-title">
                <Tag :color="typeColors[req.type]" style="margin-right: 6px">
                  {{ requirementTypeLabels[req.type] || req.type }}
                </Tag>
                <Tag v-if="req.complexity" :color="complexityColorMap[req.complexity] || 'default'" style="margin-right: 6px">
                  {{ complexityLabels[req.complexity] || req.complexity }}
                </Tag>
                <span :style="{ textDecoration: req.isDeleted ? 'line-through' : 'none' }">
                  {{ req.title }}
                </span>
              </div>
              <Space :size="4">
                <Button size="small" @click="handleViewMd(req)">文档</Button>
                <template v-if="!req.isConfirmed">
                  <Button size="small" @click="handleEdit(req)">编辑</Button>
                  <Button
                    v-if="!req.isDeleted"
                    size="small"
                    type="primary"
                    @click="handleConfirmSingle(req.id)"
                  >确认</Button>
                  <Popconfirm v-if="!req.isDeleted" title="确定移除？" @confirm="handleReject(req.id)">
                    <Button size="small" danger>移除</Button>
                  </Popconfirm>
                  <Button v-if="req.isDeleted" size="small" @click="handleRestore(req.id)">恢复</Button>
                </template>
                <Tag v-else color="green">已确认</Tag>
              </Space>
            </div>
            <div v-if="req.content" class="req-content">
              <MdPreview :model-value="req.content" :theme="mdTheme" />
            </div>
          </div>
        </Spin>
      </Card>
    </div>

    <!-- Edit Modal -->
    <Modal
      v-model:open="editVisible"
      title="编辑暂存需求"
      width="720px"
      :confirm-loading="editSubmitting"
      @ok="handleEditSubmit"
    >
      <Form layout="vertical">
        <FormItem label="标题">
          <Input v-model:value="editForm.title" />
        </FormItem>
        <FormItem label="类型">
          <Select v-model:value="editForm.type" :options="typeOptions" style="width: 200px" />
        </FormItem>
        <FormItem label="复杂度">
          <Select v-model:value="editForm.complexity" :options="complexityOptions" style="width: 200px" />
        </FormItem>
        <FormItem label="描述">
          <MdEditor v-model="editForm.content" :preview="false" :theme="mdTheme" style="height: 260px" />
        </FormItem>
      </Form>
    </Modal>

    <!-- MD Preview Drawer -->
    <Drawer
      v-model:open="mdPreviewVisible"
      :title="mdPreviewTitle"
      placement="right"
      :width="720"
    >
      <MdPreview :model-value="mdPreviewContent" :theme="mdTheme" />
    </Drawer>
  </Page>
</template>

<style scoped>
.analysis-layout {
  display: flex;
  gap: 16px;
  height: calc(100vh - 120px);
}

.batch-panel {
  display: flex;
  flex-shrink: 0;
  flex-direction: column;
  width: 380px;
}

.detail-panel {
  display: flex;
  flex: 1;
  flex-direction: column;
  overflow: hidden;
}

.batch-item {
  cursor: pointer;
  border-radius: 6px;
  transition: background 0.2s;
}

.batch-item:hover {
  background: rgb(0 0 0 / 4%);
}

.batch-item-active {
  background: rgb(22 119 255 / 10%) !important;
}

.batch-desc-text {
  font-size: 12px;
  color: rgb(0 0 0 / 45%);
}

.batch-description {
  padding: 12px;
  margin-bottom: 16px;
  font-size: 13px;
  color: rgb(0 0 0 / 65%);
  background: rgb(0 0 0 / 4%);
  border-radius: 6px;
}

.req-card {
  padding: 12px 16px;
  margin-bottom: 12px;
  border: 1px solid rgb(0 0 0 / 8%);
  border-radius: 8px;
  transition: all 0.2s;
}

.req-card:hover {
  border-color: rgb(0 0 0 / 15%);
}

.req-deleted {
  background: rgb(0 0 0 / 2%);
  opacity: 0.5;
}

.req-confirmed {
  background: rgb(183 235 143 / 10%);
  border-color: #b7eb8f;
}

.req-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 8px;
}

.req-title {
  flex: 1;
  margin-right: 12px;
  overflow: hidden;
  text-overflow: ellipsis;
  font-weight: 500;
  white-space: nowrap;
}

.req-content {
  max-height: 200px;
  padding: 4px 12px;
  overflow-y: auto;
  font-size: 13px;
  background: rgb(0 0 0 / 4%);
  border-radius: 6px;
}

.req-content :deep(.md-editor-preview-wrapper),
.req-content :deep(.md-editor),
.req-content :deep(.md-editor-preview) {
  padding: 0;
  font-size: 13px;
  background: transparent !important;
}

</style>

<style>
/* Dark mode overrides (unscoped to match html.dark) */
.dark .batch-item:hover {
  background: rgb(255 255 255 / 8%) !important;
}

.dark .batch-item-active {
  background: rgb(22 119 255 / 20%) !important;
}

.dark .batch-desc-text {
  color: rgb(255 255 255 / 45%) !important;
}

.dark .batch-description {
  color: rgb(255 255 255 / 65%) !important;
  background: rgb(255 255 255 / 8%) !important;
}

.dark .req-card {
  border-color: rgb(255 255 255 / 12%) !important;
}

.dark .req-card:hover {
  border-color: rgb(255 255 255 / 20%) !important;
}

.dark .req-content {
  background: rgb(255 255 255 / 6%) !important;
}

.dark .req-deleted {
  background: rgb(255 255 255 / 4%) !important;
}

.dark .req-confirmed {
  background: rgb(183 235 143 / 8%) !important;
  border-color: #3c6e15 !important;
}
</style>

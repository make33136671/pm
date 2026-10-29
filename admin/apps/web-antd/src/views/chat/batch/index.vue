<script lang="ts" setup>
import type { VxeGridProps } from '#/adapter/vxe-table';
import type { VbenFormProps } from '#/adapter/form';

import { ref } from 'vue';

import { Page } from '@vben/common-ui';

import { Tag, Space, Button, Popconfirm, Drawer, message } from 'ant-design-vue';

import BatchDetail from './detail.vue';

import { useVbenVxeGrid } from '#/adapter/vxe-table';

import { getBatchList, postBatchAnalyze, postBatchExportMd, postBatchImportMd, postBatchDel } from '../api';
import { getList as getProjectList } from '../../project/api';

const batchStatusLabels: Record<string, string> = {
  Pending: '待分析', Running: '分析中', Completed: '已完成', Failed: '失败',
};
const batchStatusColors: Record<string, string> = {
  Pending: 'default', Running: 'processing', Completed: 'green', Failed: 'red',
};

const analyzingId = ref<number>(0);
const detailVisible = ref(false);
const detailBatchId = ref<number>(0);

const formOptions: VbenFormProps = {
  schema: [
    {
      label: '项目',
      fieldName: 'projectId',
      component: 'ApiSelect',
      componentProps: {
        placeholder: '请选择项目',
        api: getProjectList,
        afterFetch: (data: any) => data.items.map((item: any) => ({ label: item.name, value: item.id })),
        allowClear: true,
        showSearch: true,
        filterOption: true,
      },
    },
    {
      label: '状态',
      fieldName: 'status',
      component: 'Select',
      componentProps: {
        placeholder: '请选择',
        options: Object.entries(batchStatusLabels).map(([value, label]) => ({ label, value })),
        allowClear: true,
      },
    },
  ],
  wrapperClass: 'grid-cols-2',
  showCollapseButton: false,
  submitOnChange: false,
  submitOnEnter: true,
};

const gridOptions: VxeGridProps = {
  columns: [
    { field: 'id', title: 'Id', width: 50, visible: false },
    { field: 'title', title: '标题 / 项目', minWidth: 200, slots: { default: 'titleProject' } },
    { field: 'messageCount', title: '统计 / 状态', minWidth: 260, slots: { default: 'stats' } },
    { field: 'createdTime', title: '时间', width: 170, slots: { default: 'createdTime' } },
    { field: 'action', fixed: 'right', slots: { default: 'action' }, title: '操作', width: 180, align: 'center' },
  ],
  height: 'auto',
  showOverflow: false,
  keepSource: true,
  pagerConfig: {},
  proxyConfig: {
    ajax: {
      query: async ({ page }, formValues) => {
        return await getBatchList({ page: page.currentPage, pageSize: page.pageSize, ...formValues });
      },
    },
  },
  toolbarConfig: { refresh: true, custom: true },
};

const [Grid, gridApi] = useVbenVxeGrid({ formOptions, gridOptions });

async function handleAnalyze(id: number) {
  analyzingId.value = id;
  try {
    const res: any = await postBatchAnalyze(id);
    message.success(res?.message || '分析完成');
    gridApi.query();
  } catch (e: any) {
    message.error(e?.message || '分析失败');
  } finally {
    analyzingId.value = 0;
  }
}

function handleDetail(id: number) {
  detailBatchId.value = id;
  detailVisible.value = true;
}

async function handleExportMd(id: number) {
  try {
    const blob = await postBatchExportMd(id);
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `batch-${id}-export-${new Date().toISOString().slice(0, 10)}.md`;
    a.click();
    URL.revokeObjectURL(url);
    message.success('导出成功');
  } catch (e: any) {
    message.error(e?.message || '导出失败');
  }
}

const importFileInput = ref<HTMLInputElement>();
const importBatchId = ref(0);

function triggerImportMd(id: number) {
  importBatchId.value = id;
  importFileInput.value?.click();
}

async function onImportFileChange(e: Event) {
  const file = (e.target as HTMLInputElement).files?.[0];
  if (!file || !importBatchId.value) return;
  try {
    await postBatchImportMd(importBatchId.value, file);
    gridApi.query();
  } catch (err: any) {
    message.error(err?.message || '导入失败');
  }
  // 清空 input 以便重复选择同文件
  if (importFileInput.value) importFileInput.value.value = '';
}

function handleDel(id: number) {
  postBatchDel(id).then(() => { message.success('删除成功'); gridApi.query(); });
}

function formatDateTime(dateStr: string) {
  if (!dateStr) return '';
  const d = new Date(dateStr);
  return `${d.getFullYear()}/${d.getMonth() + 1}/${d.getDate()} ${d.toTimeString().slice(0, 8)}`;
}
</script>

<template>
  <Page auto-content-height>
    <Grid>
      <template #titleProject="{ row }">
        <div>{{ row.title }}</div>
        <div v-if="row.projectName" style="font-size: 12px; color: var(--ant-color-text-tertiary);">{{ row.projectName }}</div>
      </template>
      <template #stats="{ row }">
        <div style="display: flex; flex-wrap: wrap; gap: 4px 12px; align-items: center;">
          <span>消息 {{ row.messageCount }}</span>
          <span>需求 {{ row.requirementCount }}</span>
          <span>任务 {{ row.taskCount }}</span>
          <Tag :color="batchStatusColors[row.status] || 'default'" style="margin: 0;">{{ batchStatusLabels[row.status] || row.status }}</Tag>
        </div>
        <div v-if="row.resultSummary" style=" margin-top: 4px;font-size: 12px; color: var(--ant-color-text-tertiary);">{{ row.resultSummary }}</div>
      </template>
      <template #createdTime="{ row }">
        <div style="font-size: 12px; color: var(--ant-color-text-tertiary);">导入: {{ formatDateTime(row.createdTime) }}</div>
        <div v-if="row.completedTime" style="font-size: 12px; color: var(--ant-color-text-tertiary);">完成: {{ formatDateTime(row.completedTime) }}</div>
      </template>
      <template #action="{ row }">
        <Space wrap>
          <Button v-if="row.status === 'Pending' || row.status === 'Failed'"
                  type="primary"
                  :loading="analyzingId === row.id"
                  @click="handleAnalyze(row.id)">分析</Button>
          <Button @click="handleDetail(row.id)">详情</Button>
          <Button @click="handleExportMd(row.id)">导出 MD</Button>
          <Button @click="triggerImportMd(row.id)">导入 MD</Button>
          <Popconfirm title="确定删除？" @confirm="handleDel(row.id)">
            <Button danger>删除</Button>
          </Popconfirm>
        </Space>
      </template>
    </Grid>

    <input ref="importFileInput" type="file" accept=".md" style="display: none" @change="onImportFileChange" />

    <Drawer v-model:open="detailVisible" title="消息 — 需求 关联" width="90%" :destroy-on-close="true" :body-style="{ padding: 0, height: 'calc(100vh - 55px)' }">
      <BatchDetail v-if="detailBatchId" :batch-id="detailBatchId" @close="detailVisible = false" />
    </Drawer>
  </Page>
</template>

<script lang="ts" setup>
import type { VxeGridProps } from '#/adapter/vxe-table';
import type { VbenFormProps } from '#/adapter/form';

import { Page } from '@vben/common-ui';

import { Tag } from 'ant-design-vue';

import { useVbenVxeGrid } from '#/adapter/vxe-table';

import { pipelineStatusLabels, toOptions } from '#/utils/enum-labels';

import { getPipelineList, getRepoList } from '../api';

interface RowType {
  id: number;
  pipelineId: string;
  branch: string;
  commitSha: string;
  status: string;
  duration: number;
  startedAt: string;
  webUrl: string;
}

const statusOptions = toOptions(pipelineStatusLabels);

const statusColorMap: Record<string, string> = {
  Success: 'green',
  Failed: 'red',
  Running: 'processing',
  Pending: 'default',
  Cancelled: 'orange',
};

const formOptions: VbenFormProps = {
  schema: [
    {
      label: '仓库',
      fieldName: 'repositoryId',
      component: 'ApiSelect',
      componentProps: {
        placeholder: '请选择仓库',
        api: getRepoList,
        afterFetch: (data: any) => {
          return data.items.map((item: any) => ({
            label: item.repoUrl || item.repoFullName || `Repo #${item.id}`,
            value: item.id,
          }));
        },
        allowClear: true,
        filterOption: true,
        showSearch: true,
      },
    },
    {
      label: '状态',
      fieldName: 'status',
      component: 'Select',
      componentProps: {
        placeholder: '请选择状态',
        options: statusOptions,
        allowClear: true,
      },
    },
  ],
  wrapperClass: 'grid-cols-2',
  showCollapseButton: false,
  submitOnChange: false,
  submitOnEnter: true,
};

const gridOptions: VxeGridProps<RowType> = {
  columns: [
    { fixed: 'left', field: 'id', title: 'Id', width: 50 },
    { field: 'pipelineId', title: 'Pipeline ID', width: 120 },
    { field: 'branch', title: '分支', width: 150 },
    {
      field: 'commitSha',
      title: 'Commit',
      width: 110,
      slots: { default: 'commitSha' },
    },
    {
      field: 'status',
      title: '状态',
      width: 110,
      slots: { default: 'status' },
    },
    {
      field: 'duration',
      title: '耗时',
      width: 100,
      slots: { default: 'duration' },
    },
    {
      field: 'startedAt',
      title: '开始时间',
      width: 170,
      slots: { default: 'startedAt' },
    },
    {
      field: 'webUrl',
      title: '链接',
      minWidth: 150,
      slots: { default: 'webUrl' },
    },
  ],
  height: 'auto',
  keepSource: true,
  pagerConfig: {},
  proxyConfig: {
    ajax: {
      query: async ({ page }, formValues) => {
        return await getPipelineList({
          page: page.currentPage,
          pageSize: page.pageSize,
          ...formValues,
        });
      },
    },
  },
  showOverflow: false,
  rowConfig: {
    isHover: true,
  },
  toolbarConfig: {
    refresh: true,
    custom: true,
  },
};

const [Grid] = useVbenVxeGrid({
  formOptions,
  gridOptions,
});

function formatDuration(seconds: number) {
  if (!seconds && seconds !== 0) return '-';
  return `${seconds}s`;
}

function formatDateTime(dateStr: string) {
  if (!dateStr) return '-';
  return new Date(dateStr).toLocaleString('zh-CN');
}
</script>

<template>
  <Page auto-content-height>
    <Grid>
      <template #commitSha="{ row }">
        <span style="font-family: monospace">
          {{ row.commitSha ? row.commitSha.substring(0, 8) : '-' }}
        </span>
      </template>

      <template #status="{ row }">
        <Tag :color="statusColorMap[row.status] || 'default'">
          {{ pipelineStatusLabels[row.status] || row.status }}
        </Tag>
      </template>

      <template #duration="{ row }">
        {{ formatDuration(row.duration) }}
      </template>

      <template #startedAt="{ row }">
        {{ formatDateTime(row.startedAt) }}
      </template>

      <template #webUrl="{ row }">
        <a
          v-if="row.webUrl"
          :href="row.webUrl"
          target="_blank"
          style="color: #1890ff"
        >
          查看
        </a>
        <span v-else style="color: #999">-</span>
      </template>
    </Grid>
  </Page>
</template>

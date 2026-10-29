<script lang="ts" setup>
import type { VxeGridProps } from '#/adapter/vxe-table';

import { ref } from 'vue';

import { Page } from '@vben/common-ui';

import { Tag, Space, Button, Popconfirm, Switch, message } from 'ant-design-vue';

import { useVbenVxeGrid } from '#/adapter/vxe-table';

import { getGroupList, postGroupEdit, postGroupDel } from '../api';
import GroupBindModal from '../components/GroupBindModal.vue';

const bindingStatusLabels: Record<string, string> = { Pending: '待绑定', Active: '已绑定', Disabled: '已禁用' };
const bindingStatusColors: Record<string, string> = { Pending: 'orange', Active: 'green', Disabled: 'default' };

interface RowType {
  id: number;
  accountId: number;
  projectId: number;
  platformGroupId: string;
  groupName: string;
  bindCode: string;
  aiEnabled: boolean;
  aiRealtimeEnabled: boolean;
  status: string;
  createdTime: string;
  account?: { accountName: string };
}

const addModalOpen = ref(false);

const gridOptions: VxeGridProps<RowType> = {
  columns: [
    { field: 'id', title: 'Id', width: 50 },
    { field: 'groupName', title: '群组名称', minWidth: 150 },
    { field: 'account.accountName', title: '账号', width: 120 },
    { field: 'project.name', title: '项目', width: 120 },
    { field: 'bindCode', title: '绑定命令', width: 200, slots: { default: 'bindCode' } },
    { field: 'status', title: '状态', width: 80, slots: { default: 'status' } },
    { field: 'aiEnabled', title: 'AI分析', width: 80, slots: { default: 'aiEnabled' } },
    { field: 'aiRealtimeEnabled', title: '实时', width: 70, slots: { default: 'aiRealtime' } },
    { field: 'createdTime', title: '创建时间', width: 170, slots: { default: 'createdTime' } },
    { field: 'action', fixed: 'right', slots: { default: 'action' }, title: '操作', width: 130, align: 'center' },
  ],
  height: 'auto',
  keepSource: true,
  pagerConfig: {},
  proxyConfig: {
    ajax: {
      query: async ({ page }) => {
        return await getGroupList({ page: page.currentPage, pageSize: page.pageSize });
      },
    },
  },
  toolbarConfig: { refresh: true, custom: true },
};

const [Grid, gridApi] = useVbenVxeGrid({ gridOptions });

function copyBindCommand(code: string) {
  navigator.clipboard.writeText(`/bind ${code}`).then(() => message.success('已复制'));
}

function handleDel(id: number) {
  postGroupDel(id).then(() => { message.success('删除成功'); gridApi.query(); });
}

function toggleAi(row: RowType) {
  postGroupEdit({ id: row.id, accountId: row.accountId, projectId: row.projectId, aiEnabled: !row.aiEnabled, aiRealtimeEnabled: row.aiRealtimeEnabled, status: row.status })
    .then(() => { gridApi.query(); });
}

function toggleRealtime(row: RowType) {
  postGroupEdit({ id: row.id, accountId: row.accountId, projectId: row.projectId, aiEnabled: row.aiEnabled, aiRealtimeEnabled: !row.aiRealtimeEnabled, status: row.status })
    .then(() => { gridApi.query(); });
}

function formatDateTime(dateStr: string) {
  if (!dateStr) return '-';
  return new Date(dateStr).toLocaleString('zh-CN');
}
</script>

<template>
  <Page auto-content-height>
    <Grid>
      <template #toolbar-tools>
        <Button type="primary" @click="addModalOpen = true">新增绑定</Button>
      </template>
      <template #bindCode="{ row }">
        <span v-if="row.bindCode" style="display: inline-flex; gap: 6px; align-items: center;">
          <Tag color="blue" style="margin: 0;">/bind {{ row.bindCode }}</Tag>
          <Button size="small" @click="copyBindCommand(row.bindCode)">复制</Button>
        </span>
        <span v-else>-</span>
      </template>
      <template #status="{ row }">
        <Tag :color="bindingStatusColors[row.status] || 'default'">{{ bindingStatusLabels[row.status] || row.status }}</Tag>
      </template>
      <template #aiEnabled="{ row }">
        <Switch :checked="row.aiEnabled" size="small" @change="toggleAi(row)" />
      </template>
      <template #aiRealtime="{ row }">
        <Switch :checked="row.aiRealtimeEnabled" size="small" @change="toggleRealtime(row)" />
      </template>
      <template #createdTime="{ row }">{{ formatDateTime(row.createdTime) }}</template>
      <template #action="{ row }">
        <Space wrap>
          <Popconfirm title="确定删除？" @confirm="handleDel(row.id)">
            <Button danger>删除</Button>
          </Popconfirm>
        </Space>
      </template>
    </Grid>

    <GroupBindModal v-model:open="addModalOpen" @success="gridApi.query()" />
  </Page>
</template>

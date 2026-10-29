<script lang="ts" setup>
import type { VxeGridProps } from '#/adapter/vxe-table';

import { ref } from 'vue';

import { Page } from '@vben/common-ui';

import { Tag, Space, Button, Popconfirm, message } from 'ant-design-vue';

import { useVbenVxeGrid } from '#/adapter/vxe-table';

import { getUserList, postUserDel } from '../api';
import UserBindModal from '../components/UserBindModal.vue';

const roleLabels: Record<string, string> = { Admin: '管理员', TeamMember: '团队成员', Client: '客户' };
const roleColors: Record<string, string> = { Admin: 'red', TeamMember: 'blue', Client: 'green' };
const statusLabels: Record<string, string> = { Active: '已绑定', PendingVerify: '待验证', Disabled: '已禁用' };
const statusColors: Record<string, string> = { Active: 'green', PendingVerify: 'orange', Disabled: 'default' };

interface RowType {
  id: number;
  platformType: string;
  platformUserId: string;
  platformUserName: string;
  projectId: number;
  projectName: string;
  adminId: number | null;
  adminName: string;
  role: string;
  status: string;
  createdTime: string;
}

const bindModalOpen = ref(false);

const gridOptions: VxeGridProps<RowType> = {
  columns: [
    { field: 'id', title: 'Id', width: 50, visible: false },
    { field: 'platformUserName', title: '平台用户名', minWidth: 120 },
    { field: 'platformType', title: '平台', width: 80 },
    { field: 'projectName', title: '项目', minWidth: 120 },
    { field: 'role', title: '角色', width: 90, slots: { default: 'role' } },
    { field: 'adminName', title: '系统用户', minWidth: 100, slots: { default: 'adminName' } },
    { field: 'status', title: '状态', width: 80, slots: { default: 'status' } },
    { field: 'createdTime', title: '创建时间', width: 170, slots: { default: 'createdTime' } },
    { field: 'action', fixed: 'right', slots: { default: 'action' }, title: '操作', width: 130, align: 'center' },
  ],
  height: 'auto',
  keepSource: true,
  pagerConfig: {},
  proxyConfig: {
    ajax: {
      query: async ({ page }) => {
        return await getUserList({ page: page.currentPage, pageSize: page.pageSize });
      },
    },
  },
  toolbarConfig: { refresh: true, custom: true },
};

const [Grid, gridApi] = useVbenVxeGrid({ gridOptions });

function handleDel(id: number) {
  postUserDel(id).then(() => { message.success('删除成功'); gridApi.query(); });
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
        <Button type="primary" @click="bindModalOpen = true">绑定 Telegram</Button>
      </template>
      <template #role="{ row }">
        <Tag :color="roleColors[row.role] || 'default'">{{ roleLabels[row.role] || row.role }}</Tag>
      </template>
      <template #adminName="{ row }">{{ row.adminName || '-' }}</template>
      <template #status="{ row }">
        <Tag :color="statusColors[row.status] || 'default'">{{ statusLabels[row.status] || row.status }}</Tag>
      </template>
      <template #createdTime="{ row }">{{ formatDateTime(row.createdTime) }}</template>
      <template #action="{ row }">
        <Space wrap>
          <Popconfirm title="确定解绑？" @confirm="handleDel(row.id)">
            <Button danger>解绑</Button>
          </Popconfirm>
        </Space>
      </template>
    </Grid>

    <UserBindModal v-model:open="bindModalOpen" @success="gridApi.query()" />
  </Page>
</template>

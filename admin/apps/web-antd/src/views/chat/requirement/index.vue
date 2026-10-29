<script lang="ts" setup>
import type { VxeGridProps } from '#/adapter/vxe-table';

import { ref } from 'vue';

import { Page } from '@vben/common-ui';

import { Tag, Space, Button, Popconfirm, Modal, Select, message } from 'ant-design-vue';

import { useVbenVxeGrid } from '#/adapter/vxe-table';

import {
  getRequirementList, postRequirementConfirm, postRequirementReject,
  postRequirementDel, postRequirementAnalyze, postRequirementSendToGroup,
  postBatchAnalyze, getGroupList,
} from '../api';

const statusLabels: Record<string, string> = {
  Draft: '草稿', PendingConfirm: '待确认', Confirmed: '已确认', Rejected: '已拒绝',
};
const statusColors: Record<string, string> = {
  Draft: 'default', PendingConfirm: 'orange', Confirmed: 'green', Rejected: 'red',
};

interface RowType {
  id: number;
  groupBindingId: number;
  projectId: number;
  title: string;
  description: string;
  status: string;
  confirmedTime: string;
  createdTime: string;
}

const analyzeModalVisible = ref(false);
const analyzeGroupId = ref<number>();
const analyzing = ref(false);
const sendingToGroup = ref(false);
const groupOptions = ref<any[]>([]);

const gridOptions: VxeGridProps<RowType> = {
  columns: [
    { field: 'id', title: 'Id', width: 50 },
    { field: 'title', title: '需求标题', minWidth: 200 },
    { field: 'description', title: '描述', minWidth: 200, showOverflow: 'tooltip' },
    { field: 'sourceMessageIds', title: '来源消息', width: 120, slots: { default: 'sourceIds' } },
    { field: 'projectId', title: '项目', width: 60 },
    { field: 'status', title: '状态', width: 80, slots: { default: 'status' } },
    { field: 'createdTime', title: '提取时间', width: 170, slots: { default: 'createdTime' } },
    { field: 'action', fixed: 'right', slots: { default: 'action' }, title: '操作', width: 180, align: 'center' },
  ],
  height: 'auto',
  keepSource: true,
  pagerConfig: {},
  proxyConfig: {
    ajax: {
      query: async ({ page }) => {
        return await getRequirementList({ page: page.currentPage, pageSize: page.pageSize });
      },
    },
  },
  toolbarConfig: { refresh: true, custom: true },
};

const [Grid, gridApi] = useVbenVxeGrid({ gridOptions });

function handleConfirm(id: number) {
  postRequirementConfirm(id, 1).then(() => { message.success('已确认'); gridApi.query(); });
}

function handleReject(id: number) {
  postRequirementReject(id).then(() => { message.success('已拒绝'); gridApi.query(); });
}

function handleDel(id: number) {
  postRequirementDel(id).then(() => { message.success('删除成功'); gridApi.query(); });
}

const batchAnalyzing = ref(false);
async function handleBatchAnalyze(id: number) {
  batchAnalyzing.value = true;
  try {
    const res: any = await postBatchAnalyze(id);
    message.success(res?.message || '分析完成');
    gridApi.query();
  } finally { batchAnalyzing.value = false; }
}

async function showAnalyzeModal() {
  analyzeGroupId.value = undefined;
  const res: any = await getGroupList({ pageSize: 200 });
  groupOptions.value = (res?.items || []).map((g: any) => ({
    label: `${g.groupName || '未命名'} (${g.account?.accountName || ''})`,
    value: g.id,
  }));
  analyzeModalVisible.value = true;
}

async function handleAnalyze() {
  if (!analyzeGroupId.value) { message.error('请输入群组绑定 ID'); return; }
  analyzing.value = true;
  try {
    const res: any = await postRequirementAnalyze(analyzeGroupId.value);
    message.success(res?.message || '分析完成');
    analyzeModalVisible.value = false;
    gridApi.query();
  } finally {
    analyzing.value = false;
  }
}

async function handleSendToGroup(groupBindingId: number) {
  sendingToGroup.value = true;
  try {
    await postRequirementSendToGroup(groupBindingId);
    message.success('已发送到群组');
    gridApi.query();
  } finally {
    sendingToGroup.value = false;
  }
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
        <Space>
          <Button type="primary" @click="showAnalyzeModal">整理需求</Button>
        </Space>
      </template>
      <template #sourceIds="{ row }">
        <span v-if="row.sourceMessageIds" style="font-size: 12px; color: var(--ant-color-text-secondary);">
          {{ JSON.parse(row.sourceMessageIds).map((id: number) => `#${id}`).join(' ') }}
        </span>
        <span v-else>-</span>
      </template>
      <template #status="{ row }">
        <Tag :color="statusColors[row.status] || 'default'">{{ statusLabels[row.status] || row.status }}</Tag>
      </template>
      <template #createdTime="{ row }">{{ formatDateTime(row.createdTime) }}</template>
      <template #action="{ row }">
        <Space wrap>
          <Button v-if="row.status === 'Draft' || row.status === 'PendingConfirm'"
                  type="primary" @click="handleConfirm(row.id)">确认</Button>
          <Button v-if="row.status === 'Draft' || row.status === 'PendingConfirm'"
                  @click="handleReject(row.id)">拒绝</Button>
          <Button v-if="row.status === 'Draft' && row.sourceMessageIds"
                  type="primary" ghost :loading="batchAnalyzing" @click="handleBatchAnalyze(row.id)">AI 接单</Button>
          <Button v-if="row.status === 'Draft'"
                  :loading="sendingToGroup" @click="handleSendToGroup(row.groupBindingId)">发到群</Button>
          <Popconfirm title="确定删除？" @confirm="handleDel(row.id)">
            <Button danger>删除</Button>
          </Popconfirm>
        </Space>
      </template>
    </Grid>

    <Modal v-model:open="analyzeModalVisible" title="整理需求 — AI 分析" @ok="handleAnalyze"
           :confirm-loading="analyzing" width="400px">
      <p>选择群组，AI 将分析该群组所有未处理的消息并提取需求。</p>
      <Select v-model:value="analyzeGroupId" placeholder="选择群组" style="width: 100%;"
              show-search option-filter-prop="label" :options="groupOptions" />
    </Modal>
  </Page>
</template>

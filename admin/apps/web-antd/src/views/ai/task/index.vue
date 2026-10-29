<script lang="ts" setup>
import type { VxeGridProps } from '#/adapter/vxe-table';
import type { VbenFormProps } from '#/adapter/form';

import { ref } from 'vue';

import { Page } from '@vben/common-ui';

import { Tag, Space, Button, Popconfirm, Modal, message } from 'ant-design-vue';

import { useVbenVxeGrid } from '#/adapter/vxe-table';
import { useVbenForm } from '#/adapter/form';

import { getTaskList, getTaskDetail, postTaskCreate, postTaskCancel } from '../api';
import { getList as getProjectList } from '../../project/api';
import { aiTaskStatusLabels, aiTaskTypeLabels, toOptions } from '#/utils/enum-labels';

interface RowType {
  id: number;
  taskType: string;
  status: string;
  aiModel: string;
  tokensUsed: number;
  operatorName: string;
  createdTime: string;
  finishedAt: string;
}

const detailVisible = ref(false);
const detailData = ref<any>(null);
const modalVisible = ref(false);

const statusColorMap: Record<string, string> = { Pending: 'default', Running: 'processing', Completed: 'green', Failed: 'red' };

const formOptions: VbenFormProps = {
  schema: [
    {
      label: '类型',
      fieldName: 'taskType',
      component: 'Select',
      componentProps: {
        placeholder: '全部类型',
        options: toOptions(aiTaskTypeLabels),
        allowClear: true,
      },
    },
    {
      label: '状态',
      fieldName: 'status',
      component: 'Select',
      componentProps: {
        placeholder: '全部状态',
        options: toOptions(aiTaskStatusLabels),
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
    { field: 'id', title: 'Id', width: 50 },
    { field: 'taskType', title: '类型', width: 110, slots: { default: 'taskType' } },
    { field: 'status', title: '状态', width: 80, slots: { default: 'status' } },
    { field: 'aiModel', title: 'AI模型', width: 120 },
    { field: 'tokensUsed', title: 'Token', width: 80 },
    { field: 'operatorName', title: '操作人', width: 100 },
    { field: 'createdTime', title: '创建时间', width: 170, slots: { default: 'createdTime' } },
    { field: 'action', fixed: 'right', slots: { default: 'action' }, title: '操作', width: 130, align: 'center' },
  ],
  height: 'auto',
  keepSource: true,
  pagerConfig: {},
  proxyConfig: {
    ajax: {
      query: async ({ page }, formValues) => {
        return await getTaskList({ page: page.currentPage, pageSize: page.pageSize, ...formValues });
      },
    },
  },
  toolbarConfig: { refresh: true, custom: true },
};

const [Grid, gridApi] = useVbenVxeGrid({ formOptions, gridOptions });

const [ModalForm, modalFormApi] = useVbenForm({
  commonConfig: { colon: true, componentProps: { class: 'w-full' } },
  handleSubmit: onCreateSubmit,
  layout: 'horizontal',
  schema: [
    {
      fieldName: 'taskType',
      component: 'Select',
      label: '任务类型',
      rules: 'required',
      componentProps: {
        options: toOptions(aiTaskTypeLabels),
      },
    },
    {
      fieldName: 'projectId',
      component: 'ApiSelect',
      label: '项目',
      componentProps: {
        placeholder: '关联项目(可选)',
        api: getProjectList,
        afterFetch: (data: any) => data.items.map((item: any) => ({ label: item.name, value: item.id })),
        allowClear: true,
        filterOption: true,
        showSearch: true,
      },
    },
    { fieldName: 'inputData', component: 'Textarea', label: '输入数据', componentProps: { rows: 4, placeholder: 'JSON格式' } },
  ],
  wrapperClass: 'grid-cols-1',
});

function handleCreate() {
  modalFormApi.resetForm();
  modalVisible.value = true;
}

async function handleView(id: number) {
  detailData.value = await getTaskDetail(id);
  detailVisible.value = true;
}

function handleCancel(id: number) {
  postTaskCancel(id).then(() => { message.success('已取消'); gridApi.query(); });
}

function onCreateSubmit(values: Record<string, any>) {
  postTaskCreate(values).then(() => { message.success('任务已创建'); modalVisible.value = false; gridApi.query(); });
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
        <Button type="primary" @click="handleCreate">创建任务</Button>
      </template>

      <template #taskType="{ row }">
        <Tag color="blue">{{ aiTaskTypeLabels[row.taskType] || row.taskType }}</Tag>
      </template>
      <template #status="{ row }">
        <Tag :color="statusColorMap[row.status] || 'default'">{{ aiTaskStatusLabels[row.status] || row.status }}</Tag>
      </template>
      <template #createdTime="{ row }">{{ formatDateTime(row.createdTime) }}</template>

      <template #action="{ row }">
        <Space wrap>
          <Button @click="handleView(row.id)">详情</Button>
          <Popconfirm v-if="row.status === 'Pending' || row.status === 'Running'" title="确定取消？" @confirm="handleCancel(row.id)">
            <Button danger>取消</Button>
          </Popconfirm>
        </Space>
      </template>
    </Grid>

    <Modal v-model:open="modalVisible" title="创建 AI 任务" :footer="null" :destroy-on-close="false" width="600px">
      <ModalForm />
    </Modal>

    <Modal v-model:open="detailVisible" title="任务详情" :footer="null" width="700px">
      <div v-if="detailData">
        <p><b>类型:</b> {{ aiTaskTypeLabels[detailData.taskType] || detailData.taskType }}</p>
        <p><b>状态:</b> {{ aiTaskStatusLabels[detailData.status] || detailData.status }}</p>
        <p><b>AI模型:</b> {{ detailData.aiModel || '-' }}</p>
        <p><b>Token消耗:</b> {{ detailData.tokensUsed || '-' }}</p>
        <p><b>创建时间:</b> {{ formatDateTime(detailData.createdTime) }}</p>
        <p><b>完成时间:</b> {{ formatDateTime(detailData.finishedAt) }}</p>
        <div v-if="detailData.errorMessage">
          <p><b>错误信息:</b></p>
          <pre style=" padding: 12px;background: #fff2f0; border-radius: 4px;">{{ detailData.errorMessage }}</pre>
        </div>
        <div v-if="detailData.inputData">
          <p><b>输入数据:</b></p>
          <pre style=" max-height: 200px; padding: 12px; overflow: auto;background: #f5f5f5; border-radius: 4px;">{{ detailData.inputData }}</pre>
        </div>
        <div v-if="detailData.outputData">
          <p><b>输出结果:</b></p>
          <pre style=" max-height: 200px; padding: 12px; overflow: auto;background: #f6ffed; border-radius: 4px;">{{ detailData.outputData }}</pre>
        </div>
      </div>
    </Modal>
  </Page>
</template>

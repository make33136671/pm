<script lang="ts" setup>
import type { VxeGridProps } from '#/adapter/vxe-table';
import type { VbenFormProps } from '#/adapter/form';

import { ref } from 'vue';

import { Page } from '@vben/common-ui';

import { Tag, Space, Button, Popconfirm, Modal, message, Input } from 'ant-design-vue';

import { useVbenVxeGrid } from '#/adapter/vxe-table';
import { useVbenForm } from '#/adapter/form';

import { releaseStatusLabels, approvalStatusLabels, deployMethodLabels, toOptions } from '#/utils/enum-labels';

import {
  getList,
  postCreate,
  postApprove,
  postReject,
  postDeploy,
  postRollback,
} from '../api';
import { getList as getProjectList } from '../../project/api';

interface RowType {
  id: number;
  version: string;
  project: { name: string };
  environmentId: number;
  status: string;
  approvalStatus: string;
  deployMethod: string;
  operatorName: string;
  createdTime: string;
}

const modalVisible = ref(false);
const commentVisible = ref(false);
const commentAction = ref<'approve' | 'reject'>('approve');
const commentTargetId = ref<number>(0);
const commentText = ref('');

const statusOptions = toOptions(releaseStatusLabels);

const statusColorMap: Record<string, string> = {
  Pending: 'default',
  Approved: 'blue',
  Deploying: 'processing',
  Success: 'green',
  Failed: 'red',
  Rollback: 'orange',
};

const approvalColorMap: Record<string, string> = {
  Pending: 'default',
  Approved: 'green',
  Rejected: 'red',
};

const deployMethodOptions = toOptions(deployMethodLabels);

const formOptions: VbenFormProps = {
  schema: [
    {
      label: '项目',
      fieldName: 'projectId',
      component: 'ApiSelect',
      componentProps: {
        placeholder: '请选择项目',
        api: getProjectList,
        afterFetch: (data: any) => {
          return data.items.map((item: any) => ({
            label: item.name,
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
    { field: 'version', title: '版本号', width: 130 },
    { field: 'project.name', title: '项目', width: 140 },
    { field: 'environmentId', title: '环境ID', width: 80 },
    {
      field: 'status',
      title: '状态',
      width: 110,
      slots: { default: 'status' },
    },
    {
      field: 'approvalStatus',
      title: '审批状态',
      width: 110,
      slots: { default: 'approvalStatus' },
    },
    { field: 'deployMethod', title: '部署方式', width: 100 },
    { field: 'operatorName', title: '操作人', width: 100 },
    {
      field: 'createdTime',
      title: '创建时间',
      width: 170,
      slots: { default: 'createdTime' },
    },
    {
      field: 'action',
      fixed: 'right',
      align: 'center',
      slots: { default: 'action' },
      title: '操作',
      width: 180,
    },
  ],
  height: 'auto',
  keepSource: true,
  pagerConfig: {},
  proxyConfig: {
    ajax: {
      query: async ({ page }, formValues) => {
        return await getList({
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

const [Grid, gridApi] = useVbenVxeGrid({
  formOptions,
  gridOptions,
});

const [ModalForm, modalFormApi] = useVbenForm({
  commonConfig: {
    colon: true,
    componentProps: {
      class: 'w-full',
    },
  },
  handleSubmit: onModalSubmit,
  layout: 'horizontal',
  schema: [
    {
      fieldName: 'projectId',
      component: 'ApiSelect',
      label: '项目',
      rules: 'required',
      componentProps: {
        placeholder: '请选择项目',
        api: getProjectList,
        afterFetch: (data: any) => {
          return data.items.map((item: any) => ({
            label: item.name,
            value: item.id,
          }));
        },
        allowClear: true,
        filterOption: true,
        showSearch: true,
      },
    },
    {
      fieldName: 'environmentId',
      component: 'InputNumber',
      label: '环境ID',
      rules: 'required',
      componentProps: { placeholder: '请输入环境ID', min: 1 },
    },
    {
      fieldName: 'version',
      component: 'Input',
      label: '版本号',
      rules: 'required',
      componentProps: { placeholder: '请输入版本号' },
    },
    {
      fieldName: 'releaseNote',
      component: 'Textarea',
      label: '发布说明',
      componentProps: {
        placeholder: '请输入发布说明',
        rows: 4,
      },
    },
    {
      fieldName: 'deployMethod',
      component: 'Select',
      label: '部署方式',
      componentProps: {
        placeholder: '请选择部署方式',
        options: deployMethodOptions,
      },
    },
  ],
  wrapperClass: 'grid-cols-1',
});

function handleAdd() {
  modalFormApi.resetForm();
  modalVisible.value = true;
}

function onModalSubmit(values: Record<string, any>) {
  postCreate(values).then(() => {
    message.success('发布已创建');
    modalVisible.value = false;
    gridApi.query();
  });
}

function handleModalCancel() {
  modalVisible.value = false;
}

function openComment(action: 'approve' | 'reject', id: number) {
  commentAction.value = action;
  commentTargetId.value = id;
  commentText.value = '';
  commentVisible.value = true;
}

function handleCommentOk() {
  const fn =
    commentAction.value === 'approve' ? postApprove : postReject;
  fn(commentTargetId.value, commentText.value).then(() => {
    message.success(commentAction.value === 'approve' ? '已审批通过' : '已驳回');
    commentVisible.value = false;
    gridApi.query();
  });
}

function handleDeploy(id: number) {
  postDeploy(id).then(() => {
    message.success('部署已触发');
    gridApi.query();
  });
}

function handleRollback(id: number) {
  postRollback(id).then(() => {
    message.success('回滚已触发');
    gridApi.query();
  });
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
        <Button type="primary" @click="handleAdd">新建发布</Button>
      </template>

      <template #status="{ row }">
        <Tag :color="statusColorMap[row.status] || 'default'">
          {{ releaseStatusLabels[row.status] || row.status }}
        </Tag>
      </template>

      <template #approvalStatus="{ row }">
        <Tag :color="approvalColorMap[row.approvalStatus] || 'default'">
          {{ approvalStatusLabels[row.approvalStatus] || row.approvalStatus }}
        </Tag>
      </template>

      <template #createdTime="{ row }">
        {{ formatDateTime(row.createdTime) }}
      </template>

      <template #action="{ row }">
        <Space wrap>
          <Button
            v-if="row.status === 'Pending'"
            @click="openComment('approve', row.id)"
          >
            审批
          </Button>
          <Button
            v-if="row.status === 'Pending'"
            danger
            @click="openComment('reject', row.id)"
          >
            驳回
          </Button>
          <Popconfirm
            v-if="row.status === 'Approved'"
            title="确定要部署此版本吗？"
            @confirm="handleDeploy(row.id)"
          >
            <Button>部署</Button>
          </Popconfirm>
          <Popconfirm
            v-if="row.status === 'Success'"
            title="确定要回滚此版本吗？"
            @confirm="handleRollback(row.id)"
          >
            <Button danger>回滚</Button>
          </Popconfirm>
        </Space>
      </template>
    </Grid>

    <Modal
      v-model:open="modalVisible"
      title="新建发布"
      :footer="null"
      :destroy-on-close="false"
      width="600px"
      @cancel="handleModalCancel"
    >
      <ModalForm />
    </Modal>

    <Modal
      v-model:open="commentVisible"
      :title="commentAction === 'approve' ? '审批通过' : '驳回'"
      @ok="handleCommentOk"
      @cancel="commentVisible = false"
    >
      <div style="padding: 12px 0">
        <Input.TextArea
          v-model:value="commentText"
          :rows="3"
          placeholder="请输入审批意见（可选）"
        />
      </div>
    </Modal>
  </Page>
</template>

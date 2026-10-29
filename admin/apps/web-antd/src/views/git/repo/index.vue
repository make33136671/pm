<script lang="ts" setup>
import type { VxeGridProps } from '#/adapter/vxe-table';
import type { VbenFormProps } from '#/adapter/form';

import { ref } from 'vue';

import { Page } from '@vben/common-ui';

import { Tag, Space, Button, Popconfirm, Modal, message } from 'ant-design-vue';

import { useVbenVxeGrid } from '#/adapter/vxe-table';
import { useVbenForm } from '#/adapter/form';

import {
  getRepoList,
  getRepoDetail,
  postRepoEdit,
  postRepoDel,
  postRepoSync,
} from '../api';
import { getList as getProjectList } from '../../project/api';

interface RowType {
  id: number;
  repoUrl: string;
  platform: string;
  defaultBranch: string;
  syncStatus: string;
  lastSyncTime: string;
}

const modalVisible = ref(false);
const modalTitle = ref('添加仓库');
const editId = ref<number>(0);

const syncStatusColorMap: Record<string, string> = {
  Idle: 'default',
  Syncing: 'processing',
  Error: 'red',
};

const platformColorMap: Record<string, string> = {
  GitLab: 'orange',
  GitHub: 'default',
};

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
      label: '关键词',
      fieldName: 'keyword',
      component: 'Input',
      componentProps: { placeholder: '请输入关键词' },
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
    { field: 'repoUrl', title: '仓库地址', minWidth: 280 },
    {
      field: 'platform',
      title: '平台',
      width: 100,
      slots: { default: 'platform' },
    },
    { field: 'defaultBranch', title: '默认分支', width: 120 },
    {
      field: 'syncStatus',
      title: '同步状态',
      width: 110,
      slots: { default: 'syncStatus' },
    },
    {
      field: 'lastSyncTime',
      title: '最后同步',
      width: 170,
      slots: { default: 'lastSyncTime' },
    },
    {
      field: 'action',
      fixed: 'right',
      slots: { default: 'action' },
      title: '操作',
      width: 130,
      align: 'center',
    },
  ],
  height: 'auto',
  keepSource: true,
  pagerConfig: {},
  proxyConfig: {
    ajax: {
      query: async ({ page }, formValues) => {
        return await getRepoList({
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

const platformOptions = [
  { label: 'GitLab', value: 'GitLab' },
  { label: 'GitHub', value: 'GitHub' },
];

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
      fieldName: 'platform',
      component: 'Select',
      label: '平台',
      componentProps: {
        placeholder: '请选择平台',
        options: platformOptions,
      },
    },
    {
      fieldName: 'repoUrl',
      component: 'Input',
      label: '仓库地址',
      rules: 'required',
      componentProps: { placeholder: '请输入仓库地址' },
    },
    {
      fieldName: 'repoFullName',
      component: 'Input',
      label: '仓库全名',
      componentProps: { placeholder: '如: group/project' },
    },
    {
      fieldName: 'repoId',
      component: 'Input',
      label: '仓库ID',
      componentProps: { placeholder: '远端仓库ID' },
    },
    {
      fieldName: 'accessToken',
      component: 'InputPassword',
      label: 'Access Token',
      componentProps: { placeholder: '请输入访问令牌' },
    },
    {
      fieldName: 'defaultBranch',
      component: 'Input',
      label: '默认分支',
      defaultValue: 'main',
      componentProps: { placeholder: '默认分支' },
    },
  ],
  wrapperClass: 'grid-cols-1',
});

function handleAdd() {
  editId.value = 0;
  modalTitle.value = '添加仓库';
  modalFormApi.resetForm();
  modalVisible.value = true;
}

async function handleEdit(id: number) {
  editId.value = id;
  modalTitle.value = '编辑仓库';
  modalFormApi.resetForm();
  const detail = await getRepoDetail(id);
  modalFormApi.setValues(detail);
  modalVisible.value = true;
}

function handleDel(id: number) {
  postRepoDel(id).then(() => {
    message.success('删除成功');
    gridApi.query();
  });
}

function handleSync(id: number) {
  postRepoSync(id).then(() => {
    message.success('同步已触发');
    gridApi.query();
  });
}

function onModalSubmit(values: Record<string, any>) {
  if (editId.value > 0) {
    values.id = editId.value;
  }
  postRepoEdit(values).then(() => {
    message.success('保存成功');
    modalVisible.value = false;
    gridApi.query();
  });
}

function handleModalCancel() {
  modalVisible.value = false;
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
        <Button type="primary" @click="handleAdd">添加仓库</Button>
      </template>

      <template #platform="{ row }">
        <Tag :color="platformColorMap[row.platform] || 'default'">
          {{ row.platform }}
        </Tag>
      </template>

      <template #syncStatus="{ row }">
        <Tag :color="syncStatusColorMap[row.syncStatus] || 'default'">
          {{ row.syncStatus }}
        </Tag>
      </template>

      <template #lastSyncTime="{ row }">
        {{ formatDateTime(row.lastSyncTime) }}
      </template>

      <template #action="{ row }">
        <Space wrap>
          <Button @click="handleSync(row.id)">同步</Button>
          <Button @click="handleEdit(row.id)">编辑</Button>
          <Popconfirm
            title="确定要删除该仓库吗？"
            @confirm="handleDel(row.id)"
          >
            <Button danger>删除</Button>
          </Popconfirm>
        </Space>
      </template>
    </Grid>

    <Modal
      v-model:open="modalVisible"
      :title="modalTitle"
      :footer="null"
      :destroy-on-close="false"
      width="600px"
      @cancel="handleModalCancel"
    >
      <ModalForm />
    </Modal>
  </Page>
</template>

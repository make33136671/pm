<script lang="ts" setup>
import type { VxeGridProps } from '#/adapter/vxe-table';
import type { VbenFormProps } from '#/adapter/form';

import { ref } from 'vue';

import { Page } from '@vben/common-ui';

import { Tag, Space, Button, Popconfirm, Modal, message } from 'ant-design-vue';

import { useVbenVxeGrid } from '#/adapter/vxe-table';
import { useVbenForm } from '#/adapter/form';

import { getList, getDetail, postEdit, postDel, postExecute } from './api';
import { getList as getProjectList } from '../project/api';
import { toOptions } from '#/utils/enum-labels';

const migrationStatusLabels: Record<string, string> = {
  Pending: '待执行',
  Executed: '已执行',
  Failed: '失败',
  Rollback: '已回滚',
};

interface RowType {
  id: number;
  version: string;
  title: string;
  project: { name: string };
  status: string;
  executedBy: string;
  executedAt: string;
  createdTime: string;
}

const modalVisible = ref(false);
const modalTitle = ref('新增脚本');
const editId = ref<number>(0);

const statusOptions = toOptions(migrationStatusLabels);

const statusColorMap: Record<string, string> = {
  Pending: 'default',
  Executed: 'green',
  Failed: 'red',
  Rollback: 'orange',
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
    { field: 'title', title: '标题', minWidth: 200 },
    { field: 'project.name', title: '项目', width: 140 },
    {
      field: 'status',
      title: '状态',
      width: 110,
      slots: { default: 'status' },
    },
    { field: 'executedBy', title: '执行人', width: 100 },
    {
      field: 'executedAt',
      title: '执行时间',
      width: 170,
      slots: { default: 'executedAt' },
    },
    {
      field: 'createdTime',
      title: '创建时间',
      width: 170,
      slots: { default: 'createdTime' },
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
      fieldName: 'version',
      component: 'Input',
      label: '版本号',
      rules: 'required',
      componentProps: { placeholder: '请输入版本号' },
    },
    {
      fieldName: 'title',
      component: 'Input',
      label: '标题',
      rules: 'required',
      componentProps: { placeholder: '请输入标题' },
    },
    {
      fieldName: 'sqlContent',
      component: 'Textarea',
      label: 'SQL内容',
      rules: 'required',
      componentProps: {
        placeholder: '请输入SQL脚本内容',
        rows: 10,
      },
    },
    {
      fieldName: 'rollbackSql',
      component: 'Textarea',
      label: '回滚SQL',
      componentProps: {
        placeholder: '请输入回滚SQL脚本',
        rows: 6,
      },
    },
    {
      fieldName: 'remark',
      component: 'Input',
      label: '备注',
      componentProps: { placeholder: '请输入备注' },
    },
  ],
  wrapperClass: 'grid-cols-1',
});

function handleAdd() {
  editId.value = 0;
  modalTitle.value = '新增脚本';
  modalFormApi.resetForm();
  modalVisible.value = true;
}

async function handleEdit(id: number) {
  editId.value = id;
  modalTitle.value = '编辑脚本';
  modalFormApi.resetForm();
  const detail = await getDetail(id);
  modalFormApi.setValues(detail);
  modalVisible.value = true;
}

function handleDel(id: number) {
  postDel(id).then(() => {
    message.success('删除成功');
    gridApi.query();
  });
}

function handleExecute(id: number) {
  postExecute(id).then(() => {
    message.success('执行成功');
    gridApi.query();
  });
}

function onModalSubmit(values: Record<string, any>) {
  if (editId.value > 0) {
    values.id = editId.value;
  }
  postEdit(values).then(() => {
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
        <Button type="primary" @click="handleAdd">新增脚本</Button>
      </template>

      <template #status="{ row }">
        <Tag :color="statusColorMap[row.status] || 'default'">
          {{ migrationStatusLabels[row.status] || row.status }}
        </Tag>
      </template>

      <template #executedAt="{ row }">
        {{ formatDateTime(row.executedAt) }}
      </template>

      <template #createdTime="{ row }">
        {{ formatDateTime(row.createdTime) }}
      </template>

      <template #action="{ row }">
        <Space wrap>
          <Popconfirm
            v-if="row.status === 'Pending'"
            title="确定执行此脚本？"
            @confirm="handleExecute(row.id)"
          >
            <Button>执行</Button>
          </Popconfirm>
          <Button @click="handleEdit(row.id)">编辑</Button>
          <Popconfirm
            title="确定要删除该脚本吗？"
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
      width="700px"
      @cancel="handleModalCancel"
    >
      <ModalForm />
    </Modal>
  </Page>
</template>

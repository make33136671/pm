<script lang="ts" setup>
import type { VxeGridProps } from '#/adapter/vxe-table';
import type { VbenFormProps } from '#/adapter/form';

import { ref } from 'vue';

import { Page } from '@vben/common-ui';

import { Tag, Space, Button, Popconfirm, Modal, message } from 'ant-design-vue';

import { useVbenVxeGrid } from '#/adapter/vxe-table';
import { useVbenForm } from '#/adapter/form';
import { generatedByLabels } from '#/utils/enum-labels';

import { getList, getDetail, postEdit, postDel } from './api';
import { getList as getProjectList } from '../project/api';

interface RowType {
  id: number;
  version: string;
  project: { name: string };
  generatedBy: string;
  publishedAt: string;
  createdTime: string;
}

const modalVisible = ref(false);
const modalTitle = ref('新增变更日志');
const editId = ref<number>(0);

const generatedByColorMap: Record<string, string> = {
  Manual: 'blue',
  Auto: 'green',
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
    { field: 'version', title: '版本号', width: 130 },
    { field: 'project.name', title: '项目', width: 140 },
    {
      field: 'generatedBy',
      title: '生成方式',
      width: 110,
      slots: { default: 'generatedBy' },
    },
    {
      field: 'publishedAt',
      title: '发布时间',
      width: 170,
      slots: { default: 'publishedAt' },
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
      fieldName: 'content',
      component: 'Textarea',
      label: '内容',
      rules: 'required',
      componentProps: {
        placeholder: '请输入变更内容',
        rows: 10,
      },
    },
    {
      fieldName: 'publishedAt',
      component: 'DatePicker',
      label: '发布时间',
      componentProps: {
        placeholder: '请选择发布时间',
        style: { width: '100%' },
      },
    },
  ],
  wrapperClass: 'grid-cols-1',
});

function handleAdd() {
  editId.value = 0;
  modalTitle.value = '新增变更日志';
  modalFormApi.resetForm();
  modalVisible.value = true;
}

async function handleEdit(id: number) {
  editId.value = id;
  modalTitle.value = '编辑变更日志';
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
        <Button type="primary" @click="handleAdd">新增变更日志</Button>
      </template>

      <template #generatedBy="{ row }">
        <Tag :color="generatedByColorMap[row.generatedBy] || 'default'">
          {{ generatedByLabels[row.generatedBy] || row.generatedBy }}
        </Tag>
      </template>

      <template #publishedAt="{ row }">
        {{ formatDateTime(row.publishedAt) }}
      </template>

      <template #createdTime="{ row }">
        {{ formatDateTime(row.createdTime) }}
      </template>

      <template #action="{ row }">
        <Space wrap>
          <Button @click="handleEdit(row.id)">编辑</Button>
          <Popconfirm
            title="确定要删除该记录吗？"
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

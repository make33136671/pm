<script lang="ts" setup>
import type { VxeGridProps } from '#/adapter/vxe-table';
import type { VbenFormProps } from '#/adapter/form';

import { ref } from 'vue';

import { Page } from '@vben/common-ui';

import { Tag, Space, Button, Popconfirm, Modal, message } from 'ant-design-vue';

import { useVbenVxeGrid } from '#/adapter/vxe-table';
import { useVbenForm } from '#/adapter/form';

import { getMemoryList, getMemoryDetail, postMemoryEdit, postMemoryDel } from '../api';
import { getList as getProjectList } from '../../project/api';

interface RowType {
  id: number;
  title: string;
  scope: string;
  category: string;
  importance: number;
  source: string;
  accessCount: number;
  createdTime: string;
}

const modalVisible = ref(false);
const modalTitle = ref('新增 AI 记忆');
const editId = ref<number>(0);

const scopeColorMap: Record<string, string> = { Global: 'blue', Project: 'green', Permanent: 'red' };
const scopeLabel: Record<string, string> = { Global: '全局', Project: '项目', Permanent: '永久' };

const formOptions: VbenFormProps = {
  schema: [
    {
      label: '范围',
      fieldName: 'scope',
      component: 'Select',
      componentProps: {
        placeholder: '全部范围',
        options: Object.entries(scopeLabel).map(([value, label]) => ({ label, value })),
        allowClear: true,
      },
    },
    { label: '关键词', fieldName: 'keyword', component: 'Input', componentProps: { placeholder: '搜索标题/内容/标签' } },
  ],
  wrapperClass: 'grid-cols-2',
  showCollapseButton: false,
  submitOnChange: false,
  submitOnEnter: true,
};

const gridOptions: VxeGridProps<RowType> = {
  columns: [
    { field: 'id', title: 'Id', width: 50 },
    { field: 'title', title: '标题', minWidth: 200 },
    { field: 'scope', title: '范围', width: 80, slots: { default: 'scope' } },
    { field: 'category', title: '分类', width: 100 },
    { field: 'importance', title: '重要度', width: 80 },
    { field: 'source', title: '来源', width: 80 },
    { field: 'accessCount', title: '访问次数', width: 80 },
    { field: 'createdTime', title: '创建时间', width: 170, slots: { default: 'createdTime' } },
    { field: 'action', fixed: 'right', align: 'center', slots: { default: 'action' }, title: '操作', width: 130 },
  ],
  height: 'auto',
  keepSource: true,
  pagerConfig: {},
  proxyConfig: {
    ajax: {
      query: async ({ page }, formValues) => {
        return await getMemoryList({ page: page.currentPage, pageSize: page.pageSize, ...formValues });
      },
    },
  },
  toolbarConfig: { refresh: true, custom: true },
};

const [Grid, gridApi] = useVbenVxeGrid({ formOptions, gridOptions });

const [ModalForm, modalFormApi] = useVbenForm({
  commonConfig: { colon: true, componentProps: { class: 'w-full' } },
  handleSubmit: onModalSubmit,
  layout: 'horizontal',
  schema: [
    { fieldName: 'title', component: 'Input', label: '标题', rules: 'required' },
    { fieldName: 'content', component: 'Textarea', label: '内容', rules: 'required', componentProps: { rows: 6 } },
    {
      fieldName: 'scope',
      component: 'Select',
      label: '范围',
      rules: 'required',
      defaultValue: 'Global',
      componentProps: {
        options: Object.entries(scopeLabel).map(([value, label]) => ({ label, value })),
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
    { fieldName: 'category', component: 'Input', label: '分类', componentProps: { placeholder: '架构/偏好/规范/经验' } },
    {
      fieldName: 'importance',
      component: 'InputNumber',
      label: '重要度',
      defaultValue: 5,
      componentProps: { min: 1, max: 10 },
    },
    { fieldName: 'tags', component: 'Input', label: '标签', componentProps: { placeholder: '逗号分隔' } },
  ],
  wrapperClass: 'grid-cols-1',
});

function handleAdd() {
  editId.value = 0;
  modalTitle.value = '新增 AI 记忆';
  modalFormApi.resetForm();
  modalVisible.value = true;
}

async function handleEdit(id: number) {
  editId.value = id;
  modalTitle.value = '编辑 AI 记忆';
  modalFormApi.resetForm();
  const detail = await getMemoryDetail(id);
  modalFormApi.setValues(detail);
  modalVisible.value = true;
}

function handleDel(id: number) {
  postMemoryDel(id).then(() => { message.success('删除成功'); gridApi.query(); });
}

function onModalSubmit(values: Record<string, any>) {
  if (editId.value > 0) values.id = editId.value;
  postMemoryEdit(values).then(() => { message.success('保存成功'); modalVisible.value = false; gridApi.query(); });
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
        <Button type="primary" @click="handleAdd">新增记忆</Button>
      </template>
      <template #scope="{ row }">
        <Tag :color="scopeColorMap[row.scope] || 'default'">{{ scopeLabel[row.scope] || row.scope }}</Tag>
      </template>
      <template #createdTime="{ row }">{{ formatDateTime(row.createdTime) }}</template>
      <template #action="{ row }">
        <Space wrap>
          <Button @click="handleEdit(row.id)">编辑</Button>
          <Popconfirm title="确定删除？" @confirm="handleDel(row.id)">
            <Button danger>删除</Button>
          </Popconfirm>
        </Space>
      </template>
    </Grid>

    <Modal v-model:open="modalVisible" :title="modalTitle" :footer="null" :destroy-on-close="false" width="700px">
      <ModalForm />
    </Modal>
  </Page>
</template>

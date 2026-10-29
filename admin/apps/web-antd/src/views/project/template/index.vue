<script lang="ts" setup>
import type { VxeGridProps } from '#/adapter/vxe-table';
import type { VbenFormProps } from '#/adapter/form';

import { ref, computed } from 'vue';

import { Page } from '@vben/common-ui';

import { Tag, Space, Button, Popconfirm, Modal, message } from 'ant-design-vue';
import { MdEditor, MdPreview } from 'md-editor-v3';
import 'md-editor-v3/lib/style.css';
import 'md-editor-v3/lib/preview.css';
import { usePreferences } from '@vben/preferences';

import { useVbenVxeGrid } from '#/adapter/vxe-table';
import { useVbenForm } from '#/adapter/form';

import {
  getTemplateList,
  getTemplateDetail,
  postTemplateAdd,
  postTemplateEdit,
  postTemplateDel,
} from '../api';

const { isDark } = usePreferences();
const mdTheme = computed(() => isDark.value ? 'dark' : 'light');

interface RowType {
  id: number;
  name: string;
  description: string;
  techStack: string;
  defaultEnvs: string;
}

const modalVisible = ref(false);
const modalTitle = ref('新增模板');
const editId = ref<number>(0);
const editorContent = ref('');

// 预览
const previewVisible = ref(false);
const previewTitle = ref('');
const previewContent = ref('');

const formOptions: VbenFormProps = {
  schema: [
    {
      label: '模板名称',
      fieldName: 'name',
      component: 'Input',
      componentProps: { placeholder: '请输入模板名称' },
    },
  ],
  wrapperClass: 'grid-cols-1',
  showCollapseButton: false,
  submitOnChange: false,
  submitOnEnter: true,
};

const gridOptions: VxeGridProps<RowType> = {
  columns: [
    { fixed: 'left', field: 'id', title: 'Id', width: 60 },
    { field: 'name', title: '模板名称', minWidth: 150 },
    { field: 'techStack', title: '技术栈', width: 180 },
    {
      field: 'defaultEnvs',
      title: '默认环境',
      width: 200,
      slots: { default: 'defaultEnvs' },
    },
    {
      field: 'action',
      fixed: 'right',
      align: 'center',
      slots: { default: 'action' },
      title: '操作',
      width: 130,
    },
  ],
  height: 'auto',
  keepSource: true,
  pagerConfig: {},
  proxyConfig: {
    ajax: {
      query: async ({ page }, formValues) => {
        return await getTemplateList({
          page: page.currentPage,
          pageSize: page.pageSize,
          ...formValues,
        });
      },
    },
  },
  showOverflow: false,
  rowConfig: { isHover: true },
  toolbarConfig: { refresh: true, custom: true },
};

const [Grid, gridApi] = useVbenVxeGrid({ formOptions, gridOptions });

const [ModalForm, modalFormApi] = useVbenForm({
  commonConfig: {
    colon: true,
    componentProps: { class: 'w-full' },
  },
  handleSubmit: onModalSubmit,
  layout: 'horizontal',
  schema: [
    {
      fieldName: 'name',
      component: 'Input',
      label: '模板名称',
      rules: 'required',
      componentProps: { placeholder: '请输入模板名称' },
    },
    {
      fieldName: 'techStack',
      component: 'Input',
      label: '技术栈',
      componentProps: { placeholder: '请输入技术栈' },
    },
    {
      fieldName: 'defaultEnvs',
      component: 'Input',
      label: '默认环境',
      defaultValue: 'dev,test,prod',
      componentProps: { placeholder: '如: dev,test,prod' },
    },
  ],
  wrapperClass: 'grid-cols-1',
});

function handleAdd() {
  editId.value = 0;
  modalTitle.value = '新增模板';
  modalFormApi.resetForm();
  editorContent.value = '';
  modalVisible.value = true;
}

async function handleEdit(id: number) {
  editId.value = id;
  modalTitle.value = '编辑模板';
  modalFormApi.resetForm();
  const detail: any = await getTemplateDetail(id);
  modalFormApi.setValues(detail);
  editorContent.value = detail.description || '';
  modalVisible.value = true;
}

function handleDel(id: number) {
  postTemplateDel(id).then(() => {
    message.success('删除成功');
    gridApi.query();
  });
}

async function handlePreview(row: any) {
  previewTitle.value = row.name;
  if (row.description) {
    previewContent.value = row.description;
  } else {
    const detail: any = await getTemplateDetail(row.id);
    previewContent.value = detail?.description || '暂无文档';
  }
  previewVisible.value = true;
}

function onModalSubmit(values: Record<string, any>) {
  values.description = editorContent.value;
  if (editId.value > 0) {
    values.id = editId.value;
    postTemplateEdit(values).then(() => {
      message.success('保存成功');
      modalVisible.value = false;
      gridApi.query();
    });
  } else {
    postTemplateAdd(values).then(() => {
      message.success('新增成功');
      modalVisible.value = false;
      gridApi.query();
    });
  }
}
</script>

<template>
  <Page auto-content-height>
    <Grid>
      <template #toolbar-tools>
        <Button type="primary" @click="handleAdd">新增模板</Button>
      </template>

      <template #defaultEnvs="{ row }">
        <template v-if="row.defaultEnvs">
          <Tag v-for="(env, index) in row.defaultEnvs.split(',')" :key="index" color="blue" style="margin-bottom: 2px">
            {{ env.trim() }}
          </Tag>
        </template>
        <span v-else style="color: #999">-</span>
      </template>

      <template #action="{ row }">
        <Space wrap>
          <Button @click="handlePreview(row)">文档</Button>
          <Button type="primary" @click="handleEdit(row.id)">编辑</Button>
          <Popconfirm title="确定要删除该模板吗？" @confirm="handleDel(row.id)">
            <Button danger>删除</Button>
          </Popconfirm>
        </Space>
      </template>
    </Grid>

    <!-- 编辑弹窗 -->
    <Modal v-model:open="modalVisible" :title="modalTitle" :footer="null" :destroy-on-close="false" width="850px">
      <ModalForm />
      <div style="margin-top: 12px;">
        <div style="margin-bottom: 8px; font-weight: 500;">模板文档 (Markdown)</div>
        <MdEditor v-model="editorContent" :theme="mdTheme" :preview="false" style="height: 400px;" />
      </div>
    </Modal>

    <!-- 文档预览弹窗 -->
    <Modal v-model:open="previewVisible" :title="previewTitle + ' - 模板文档'" width="820px" :footer="null">
      <div style="max-height: 70vh; padding: 4px 0; overflow-y: auto;">
        <div class="template-doc-body" :class="{ dark: isDark }">
          <MdPreview :model-value="previewContent || '暂无文档'" :theme="mdTheme" />
        </div>
      </div>
      <div style="margin-top: 14px; text-align: right;">
        <Button @click="previewVisible = false">关闭</Button>
      </div>
    </Modal>
  </Page>
</template>

<style scoped>
.template-doc-body {
  background: #f6f8fa;
  border: 1px solid #e5e7eb;
  border-radius: 8px;
}

.template-doc-body.dark {
  background: #161b22;
  border-color: #30363d;
}
</style>

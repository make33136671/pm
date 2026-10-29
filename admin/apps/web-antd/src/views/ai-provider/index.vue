<script lang="ts" setup>
import type { VxeGridProps } from '#/adapter/vxe-table';
import type { VbenFormProps } from '#/adapter/form';

import { ref } from 'vue';

import { Page } from '@vben/common-ui';

import { Tag, Space, Button, Popconfirm, Modal, Form, Input, Select, Switch, message } from 'ant-design-vue';

import { useVbenVxeGrid } from '#/adapter/vxe-table';

import { getList, postEdit, postDel, getAdapterTypes } from './api';
import { aiProviderStatusLabels, toOptions } from '#/utils/enum-labels';

// 适配器类型列表
const adapterTypes = ref<any[]>([]);
getAdapterTypes().then((res: any) => {
  adapterTypes.value = res || [];
});

const statusOptions = toOptions(aiProviderStatusLabels);

const formOptions: VbenFormProps = {
  schema: [
    {
      label: '名称',
      fieldName: 'keyword',
      component: 'Input',
      componentProps: { placeholder: '请输入名称' },
    },
  ],
  wrapperClass: 'grid-cols-2',
  showCollapseButton: false,
  submitOnChange: false,
  submitOnEnter: true,
};

const gridOptions: VxeGridProps = {
  columns: [
    { field: 'id', title: 'Id', width: 50 },
    { field: 'name', title: '名称' },
    { field: 'providerType', title: '类型', width: 120, slots: { default: 'providerType' } },
    { field: 'modelName', title: '模型', width: 160 },
    { field: 'isDefault', title: '默认', width: 70, slots: { default: 'isDefault' } },
    { field: 'status', title: '状态', width: 80, slots: { default: 'status' } },
    { field: 'action', fixed: 'right', slots: { default: 'action' }, title: '操作', width: 130, align: 'center' },
  ],
  height: 'auto',
  keepSource: true,
  pagerConfig: {},
  proxyConfig: {
    ajax: {
      query: async ({ page }, formValues) => {
        return await getList({ page: page.currentPage, pageSize: page.pageSize, keyword: formValues?.keyword });
      },
    },
  },
  showOverflow: false,
  rowConfig: { isHover: true },
  toolbarConfig: { refresh: true, custom: true },
};

const [Grid, gridApi] = useVbenVxeGrid({ formOptions, gridOptions });

// 编辑弹窗
const modalVisible = ref(false);
const editForm = ref<any>({});

const handleAdd = () => {
  editForm.value = { status: 'Active', isDefault: false };
  modalVisible.value = true;
};

const handleEdit = (row: any) => {
  editForm.value = { ...row };
  modalVisible.value = true;
};

const handleSave = async () => {
  await postEdit(editForm.value);
  message.success('保存成功');
  modalVisible.value = false;
  gridApi.query();
};

const handleDel = (id: number) => {
  postDel(id).then(() => gridApi.query());
};

const getAdapterInfo = (type: string) => {
  return adapterTypes.value.find((a: any) => a.providerType === type);
};
</script>

<template>
  <Page auto-content-height>
    <Grid>
      <template #toolbar-tools>
        <Button type="primary" @click="handleAdd">新增提供商</Button>
      </template>

      <template #providerType="{ row }">
        <Tag color="blue">{{ row.providerType }}</Tag>
        <Tag v-if="getAdapterInfo(row.providerType)?.supportStream" color="green" style="margin-left: 4px;">流式</Tag>
      </template>

      <template #isDefault="{ row }">
        <Tag v-if="row.isDefault" color="green">是</Tag>
        <span v-else style="color: #999;">-</span>
      </template>

      <template #status="{ row }">
        <Tag :color="row.status === 'Active' ? 'green' : 'default'">
          {{ aiProviderStatusLabels[row.status] || row.status }}
        </Tag>
      </template>

      <template #action="{ row }">
        <Space wrap>
          <Button type="primary" @click="handleEdit(row)">编辑</Button>
          <Popconfirm title="确定删除吗？" @confirm="handleDel(row.id)">
            <Button type="primary" danger>删除</Button>
          </Popconfirm>
        </Space>
      </template>
    </Grid>

    <Modal v-model:open="modalVisible" :title="editForm.id ? '编辑提供商' : '新增提供商'" @ok="handleSave" width="520px">
      <Form layout="vertical" style="margin-top: 16px;">
        <Form.Item label="名称" required>
          <Input v-model:value="editForm.name" placeholder="如：我的 Claude" />
        </Form.Item>
        <Form.Item label="提供商类型" required>
          <Select v-model:value="editForm.providerType" placeholder="请选择">
            <Select.Option v-for="adapter in adapterTypes" :key="adapter.providerType" :value="adapter.providerType">
              {{ adapter.displayName }} {{ adapter.supportStream ? '(流式)' : '' }}
            </Select.Option>
          </Select>
        </Form.Item>
        <Form.Item label="API 地址">
          <Input v-model:value="editForm.endpoint" placeholder="https://api.example.com" />
        </Form.Item>
        <Form.Item label="API Key">
          <Input.Password v-model:value="editForm.apiKey" placeholder="API Key" />
        </Form.Item>
        <Form.Item label="模型名称">
          <Input v-model:value="editForm.modelName" placeholder="如：claude-sonnet-4-20250514" />
        </Form.Item>
        <Form.Item label="额外配置 (JSON)">
          <Input.TextArea v-model:value="editForm.extraConfig" placeholder='如：{"temperature": 0.3}' :rows="2" />
        </Form.Item>
        <Form.Item label="状态">
          <Select v-model:value="editForm.status" :options="statusOptions" />
        </Form.Item>
        <Form.Item label="设为默认">
          <Switch v-model:checked="editForm.isDefault" />
        </Form.Item>
      </Form>
    </Modal>
  </Page>
</template>

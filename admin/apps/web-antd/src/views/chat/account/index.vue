<script lang="ts" setup>
import type { VxeGridProps } from '#/adapter/vxe-table';

import { ref } from 'vue';

import { Page } from '@vben/common-ui';

import { Tag, Space, Button, Popconfirm, Modal, message, Input } from 'ant-design-vue';

import { useVbenVxeGrid } from '#/adapter/vxe-table';
import { useVbenForm } from '#/adapter/form';

import {
  getAccountList, getAccountDetail, postAccountEdit, postAccountDel,
  postAccountConnect, postAccountDisconnect, postSetWebhook, postDeleteWebhook,
} from '../api';
import { commonStatusLabels, toOptions } from '#/utils/enum-labels';

const accountTypeLabels: Record<string, string> = { Bot: 'Bot 机器人', Personal: '个人账号' };
const envLabels: Record<string, string> = { All: '所有环境', Development: '开发', Production: '生产' };
const envColors: Record<string, string> = { All: 'default', Development: 'orange', Production: 'green' };

interface RowType {
  id: number;
  platformType: string;
  accountType: string;
  accountName: string;
  status: string;
  createdTime: string;
}

const modalVisible = ref(false);
const modalTitle = ref('新增账号');
const editId = ref<number>(0);
const webhookModalVisible = ref(false);
const webhookAccountId = ref(0);
const webhookUrl = ref('');

const gridOptions: VxeGridProps<RowType> = {
  columns: [
    { field: 'id', title: 'Id', width: 50 },
    { field: 'accountName', title: '名称', minWidth: 150 },
    { field: 'platformType', title: '平台', width: 100 },
    { field: 'accountType', title: '类型', width: 100, slots: { default: 'accountType' } },
    { field: 'env', title: '环境', width: 80, slots: { default: 'env' } },
    { field: 'connected', title: '加载状态', width: 100, slots: { default: 'connected' } },
    { field: 'status', title: '状态', width: 80, slots: { default: 'status' } },
    { field: 'createdTime', title: '创建时间', width: 170, slots: { default: 'createdTime' } },
    { field: 'action', fixed: 'right', slots: { default: 'action' }, title: '操作', width: 180, align: 'center' },
  ],
  height: 'auto',
  keepSource: true,
  pagerConfig: {},
  proxyConfig: {
    ajax: {
      query: async ({ page }) => {
        return await getAccountList({ page: page.currentPage, pageSize: page.pageSize });
      },
    },
  },
  toolbarConfig: { refresh: true, custom: true },
};

const [Grid, gridApi] = useVbenVxeGrid({ gridOptions });

const [ModalForm, modalFormApi] = useVbenForm({
  commonConfig: { colon: true, componentProps: { class: 'w-full' } },
  handleSubmit: onModalSubmit,
  layout: 'horizontal',
  schema: [
    { fieldName: 'accountName', component: 'Input', label: '名称', rules: 'required' },
    { fieldName: 'platformType', component: 'Input', label: '平台', defaultValue: 'Telegram' },
    {
      fieldName: 'accountType', component: 'Select', label: '类型', defaultValue: 'Bot',
      componentProps: { options: toOptions(accountTypeLabels) },
    },
    {
      fieldName: 'env', component: 'Select', label: '环境', defaultValue: 'All',
      componentProps: { options: toOptions(envLabels) },
    },
    { fieldName: 'connectionConfig', component: 'Textarea', label: '连接参数',
      componentProps: { placeholder: 'Bot: {"BotToken":"xxx"}\n个人号: {"ApiId":"xxx","ApiHash":"xxx","Phone":"xxx"}', rows: 4 } },
    {
      fieldName: 'status', component: 'Select', label: '状态', defaultValue: 'Active',
      componentProps: { options: toOptions(commonStatusLabels) },
    },
  ],
  wrapperClass: 'grid-cols-1',
});

function handleAdd() {
  editId.value = 0;
  modalTitle.value = '新增账号';
  modalFormApi.resetForm();
  modalVisible.value = true;
}

async function handleEdit(id: number) {
  editId.value = id;
  modalTitle.value = '编辑账号';
  modalFormApi.resetForm();
  const detail = await getAccountDetail(id);
  modalFormApi.setValues({ ...detail, connectionConfig: '' });
  modalVisible.value = true;
}

function handleDel(id: number) {
  postAccountDel(id).then(() => { message.success('删除成功'); gridApi.query(); });
}

function onModalSubmit(values: Record<string, any>) {
  if (editId.value > 0) values.id = editId.value;
  postAccountEdit(values).then(() => { message.success('保存成功'); modalVisible.value = false; gridApi.query(); });
}

function handleConnect(id: number) {
  postAccountConnect(id).then(() => { message.success('连接成功'); }).catch(e => message.error(e.message));
}

function handleDisconnect(id: number) {
  postAccountDisconnect(id).then(() => { message.success('已断开'); });
}

function showWebhookModal(id: number) {
  webhookAccountId.value = id;
  webhookUrl.value = '';
  webhookModalVisible.value = true;
}

function handleSetWebhook() {
  if (!webhookUrl.value) { message.error('请输入 Webhook URL'); return; }
  postSetWebhook(webhookAccountId.value, webhookUrl.value)
    .then(() => { message.success('Webhook 设置成功'); webhookModalVisible.value = false; });
}

function handleDeleteWebhook(id: number) {
  postDeleteWebhook(id).then(() => { message.success('Webhook 已删除'); });
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
        <Button type="primary" @click="handleAdd">新增账号</Button>
      </template>
      <template #accountType="{ row }">
        <Tag :color="row.accountType === 'Bot' ? 'blue' : 'purple'">{{ accountTypeLabels[row.accountType] || row.accountType }}</Tag>
      </template>
      <template #env="{ row }">
        <Tag :color="envColors[row.env] || 'default'">{{ envLabels[row.env] || row.env }}</Tag>
      </template>
      <template #connected="{ row }">
        <Tag :color="row.connected ? 'green' : 'default'">{{ row.connected ? '已加载' : '未加载' }}</Tag>
      </template>
      <template #status="{ row }">
        <Tag :color="row.status === 'Active' ? 'green' : 'default'">{{ commonStatusLabels[row.status] || row.status }}</Tag>
      </template>
      <template #createdTime="{ row }">{{ formatDateTime(row.createdTime) }}</template>
      <template #action="{ row }">
        <Space wrap>
          <Button @click="handleEdit(row.id)">编辑</Button>
          <Button type="primary" @click="handleConnect(row.id)">连接</Button>
          <Button @click="handleDisconnect(row.id)">断开</Button>
          <Button v-if="row.accountType === 'Bot'" @click="showWebhookModal(row.id)">Webhook</Button>
          <Popconfirm title="确定删除？" @confirm="handleDel(row.id)">
            <Button danger>删除</Button>
          </Popconfirm>
        </Space>
      </template>
    </Grid>

    <Modal v-model:open="modalVisible" :title="modalTitle" :footer="null" :destroy-on-close="false" width="600px">
      <ModalForm />
    </Modal>

    <Modal v-model:open="webhookModalVisible" title="设置 Webhook" @ok="handleSetWebhook" width="500px">
      <Input v-model:value="webhookUrl" placeholder="https://your-domain.com/api/webhook/telegram/{accountId}" />
      <div style="margin-top: 8px;">
        <Button size="small" danger @click="handleDeleteWebhook(webhookAccountId)">删除 Webhook</Button>
      </div>
    </Modal>
  </Page>
</template>

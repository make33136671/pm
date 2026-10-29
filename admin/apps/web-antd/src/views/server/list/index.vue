<script lang="ts" setup>
import type { VbenFormProps } from '#/adapter/form';
import type { VxeGridProps } from '#/adapter/vxe-table';

import { ref, h } from 'vue';

import { Page } from '@vben/common-ui';

import {
  Tag,
  Button,
  Popconfirm,
  Modal,
  Form,
  FormItem,
  Input,
  InputNumber,
  Select,
  DatePicker,
  Switch,
  Collapse,
  CollapsePanel,
  Textarea,
  message,
  Space,
} from 'ant-design-vue';

import { useVbenVxeGrid } from '#/adapter/vxe-table';

import {
  getList,
  postAdd,
  postEdit,
  postDel,
  postDiscover,
  postTestConnection,
} from '../api';

import {
  serverStatusLabels,
  serverTypeLabels,
  databaseTypeLabels,
  paymentCycleLabels,
  toOptions,
  t,
} from '#/utils/enum-labels';

// ========== 服务器列表 ==========

const formOptions: VbenFormProps = {
  schema: [
    {
      label: '关键字',
      fieldName: 'keyword',
      component: 'Input',
      componentProps: { placeholder: '名称/IP' },
    },
    {
      label: '状态',
      fieldName: 'status',
      component: 'Select',
      componentProps: {
        placeholder: '全部',
        options: toOptions(serverStatusLabels),
        allowClear: true,
      },
    },
  ],
  wrapperClass: 'grid-cols-2',
  showCollapseButton: false,
  submitOnChange: false,
  submitOnEnter: true,
};

const gridOptions: VxeGridProps = {
  columns: [
    { field: 'id', title: 'Id', width: 60 },
    { field: 'name', title: '名称', minWidth: 120 },
    { field: 'host', title: '地址', minWidth: 140 },
    { field: 'sshPort', title: 'SSH端口', width: 80 },
    { field: 'serverType', title: '类型', width: 100, slots: { default: 'serverType' } },
    { field: 'status', title: '状态', width: 80, slots: { default: 'status' } },
    { field: 'provider', title: '提供商', width: 100 },
    { field: 'expiryDate', title: '到期时间', width: 110, slots: { default: 'expiryDate' } },
    { field: 'dbType', title: '数据库', width: 90, slots: { default: 'dbType' } },
    { field: 'btPanelUrl', title: '宝塔', width: 80, slots: { default: 'btPanel' } },
    { field: 'createdTime', title: '创建时间', width: 160 },
    { field: 'action', title: '操作', width: 180, fixed: 'right', align: 'center', slots: { default: 'action' } },
  ],
  height: 'auto',
  pagerConfig: {},
  proxyConfig: {
    ajax: {
      query: async ({ page }, formValues) => {
        return await getList({ page: page.currentPage, pageSize: page.pageSize, ...formValues });
      },
    },
  },
  rowConfig: { isHover: true },
  toolbarConfig: { refresh: true, custom: true },
};

const [Grid, gridApi] = useVbenVxeGrid({ formOptions, gridOptions });

// ========== 表单弹窗 ==========

const modalVisible = ref(false);
const modalTitle = ref('新增服务器');
const formData = ref<any>({});
const formLoading = ref(false);

function handleAdd() {
  formData.value = { sshPort: 22, serverType: 'Cloud', status: 'Active' };
  modalTitle.value = '新增服务器';
  modalVisible.value = true;
}

function handleEdit(row: any) {
  formData.value = { ...row };
  modalTitle.value = '编辑服务器';
  modalVisible.value = true;
}

async function handleSave() {
  formLoading.value = true;
  try {
    const fn = formData.value.id ? postEdit : postAdd;
    const res: any = await fn(formData.value);
    if (res.code === 0) {
      message.success(formData.value.id ? '更新成功' : '创建成功');
      modalVisible.value = false;
      gridApi.query();
    } else {
      message.error(res.message);
    }
  } finally {
    formLoading.value = false;
  }
}

function handleDel(id: number) {
  postDel(id).then(() => {
    message.success('删除成功');
    gridApi.query();
  });
}

async function handleDiscover(row: any) {
  const res: any = await postDiscover(row.id);
  if (res.code === 0) {
    message.success('探测完成');
    gridApi.query();
  } else {
    message.error(res.message);
  }
}

async function handleTestConnection(row: any) {
  const res: any = await postTestConnection(row.id);
  if (res.code === 0) {
    const data = res.data;
    const lines = Object.entries(data).map(([k, v]) => `${k}: ${v}`).join('\n');
    Modal.info({ title: '连通性测试结果', content: h('pre', lines) });
  } else {
    message.error(res.message);
  }
}

function formatDate(val: string) {
  return val ? val.split('T')[0] : '-';
}
</script>

<template>
  <Page auto-content-height>
    <Grid>
      <template #toolbar-tools>
        <Button type="primary" @click="handleAdd">新增服务器</Button>
      </template>

      <template #serverType="{ row }">
        <Tag>{{ t(serverTypeLabels, row.serverType) }}</Tag>
      </template>

      <template #status="{ row }">
        <Tag :color="row.status === 'Active' ? 'success' : row.status === 'Disabled' ? 'default' : 'error'">
          {{ t(serverStatusLabels, row.status) }}
        </Tag>
      </template>

      <template #expiryDate="{ row }">
        {{ formatDate(row.expiryDate) }}
      </template>

      <template #dbType="{ row }">
        {{ row.dbType ? t(databaseTypeLabels, row.dbType) : '-' }}
      </template>

      <template #btPanel="{ row }">
        <Tag v-if="row.btPanelUrl" color="blue">已配</Tag>
        <span v-else>-</span>
      </template>

      <template #action="{ row }">
        <Space wrap>
          <Button type="link" @click="handleEdit(row)">编辑</Button>
          <Button type="link" @click="handleDiscover(row)" :disabled="!row.btPanelUrl">探测</Button>
          <Button type="link" @click="handleTestConnection(row)">测试</Button>
          <Popconfirm title="确认删除?" @confirm="handleDel(row.id)">
            <Button type="link" danger>删除</Button>
          </Popconfirm>
        </Space>
      </template>
    </Grid>

    <!-- 服务器表单弹窗 -->
    <Modal v-model:open="modalVisible" :title="modalTitle" width="700px" @ok="handleSave" :confirmLoading="formLoading">
      <Form :model="formData" layout="vertical" style="max-height: 65vh; padding-right: 8px; overflow-y: auto;">
        <Collapse :default-active-key="['basic', 'ops', 'ssh']" ghost>
          <CollapsePanel key="basic" header="基本信息">
            <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 0 16px;">
              <FormItem label="名称" required>
                <Input v-model:value="formData.name" placeholder="如：测试服" />
              </FormItem>
              <FormItem label="状态">
                <Select v-model:value="formData.status" :options="toOptions(serverStatusLabels)" />
              </FormItem>
              <FormItem label="类型">
                <Select v-model:value="formData.serverType" :options="toOptions(serverTypeLabels)" />
              </FormItem>
              <FormItem label="虚拟机">
                <Switch v-model:checked="formData.isVirtual" />
              </FormItem>
              <FormItem label="宿主物理机" v-if="formData.isVirtual">
                <Input v-model:value="formData.physicalHost" placeholder="宿主机标识" />
              </FormItem>
              <FormItem label="标签">
                <Input v-model:value="formData.tags" placeholder="逗号分隔" />
              </FormItem>
            </div>
            <FormItem label="描述">
              <Textarea v-model:value="formData.description" :rows="2" />
            </FormItem>
          </CollapsePanel>

          <CollapsePanel key="ops" header="运营信息">
            <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 0 16px;">
              <FormItem label="提供商">
                <Input v-model:value="formData.provider" placeholder="如：阿里云" />
              </FormItem>
              <FormItem label="提供商客服">
                <Input v-model:value="formData.providerContact" placeholder="联系方式" />
              </FormItem>
              <FormItem label="价格">
                <InputNumber v-model:value="formData.price" :precision="2" :min="0" style="width: 100%" placeholder="元" />
              </FormItem>
              <FormItem label="支付周期">
                <Select v-model:value="formData.paymentCycle" :options="toOptions(paymentCycleLabels)" allowClear placeholder="请选择" />
              </FormItem>
              <FormItem label="到期时间">
                <Input v-model:value="formData.expiryDate" type="date" />
              </FormItem>
            </div>
          </CollapsePanel>

          <CollapsePanel key="ssh" header="SSH 连接">
            <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 0 16px;">
              <FormItem label="地址" required>
                <Input v-model:value="formData.host" placeholder="IP 或域名" />
              </FormItem>
              <FormItem label="SSH 端口">
                <InputNumber v-model:value="formData.sshPort" :min="1" :max="65535" style="width: 100%" />
              </FormItem>
              <FormItem label="用户名">
                <Input v-model:value="formData.sshUsername" placeholder="root" />
              </FormItem>
              <FormItem label="密码">
                <Input.Password v-model:value="formData.sshPassword" placeholder="留空不修改" />
              </FormItem>
            </div>
          </CollapsePanel>

          <CollapsePanel key="bt" header="宝塔面板">
            <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 0 16px;">
              <FormItem label="面板地址">
                <Input v-model:value="formData.btPanelUrl" placeholder="http://ip:port" />
              </FormItem>
              <FormItem label="API Key">
                <Input.Password v-model:value="formData.btApiKey" placeholder="留空不修改" />
              </FormItem>
              <FormItem label="用户名">
                <Input v-model:value="formData.btUsername" />
              </FormItem>
              <FormItem label="密码">
                <Input.Password v-model:value="formData.btPassword" placeholder="留空不修改" />
              </FormItem>
            </div>
          </CollapsePanel>

          <CollapsePanel key="db" header="数据库">
            <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 0 16px;">
              <FormItem label="类型">
                <Select v-model:value="formData.dbType" :options="toOptions(databaseTypeLabels)" allowClear placeholder="请选择" />
              </FormItem>
              <FormItem label="版本">
                <Input v-model:value="formData.dbVersion" placeholder="如：8.0.35" />
              </FormItem>
              <FormItem label="地址">
                <Input v-model:value="formData.dbHost" placeholder="127.0.0.1" />
              </FormItem>
              <FormItem label="端口">
                <InputNumber v-model:value="formData.dbPort" :min="1" style="width: 100%" placeholder="3306" />
              </FormItem>
              <FormItem label="Root 用户名">
                <Input v-model:value="formData.dbRootUsername" placeholder="root" />
              </FormItem>
              <FormItem label="Root 密码">
                <Input.Password v-model:value="formData.dbRootPassword" placeholder="留空不修改" />
              </FormItem>
            </div>
          </CollapsePanel>

          <CollapsePanel key="ftp" header="FTP">
            <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 0 16px;">
              <FormItem label="地址">
                <Input v-model:value="formData.ftpHost" placeholder="同服务器地址" />
              </FormItem>
              <FormItem label="端口">
                <InputNumber v-model:value="formData.ftpPort" :min="1" style="width: 100%" placeholder="21" />
              </FormItem>
              <FormItem label="用户名">
                <Input v-model:value="formData.ftpUsername" />
              </FormItem>
              <FormItem label="密码">
                <Input.Password v-model:value="formData.ftpPassword" placeholder="留空不修改" />
              </FormItem>
            </div>
          </CollapsePanel>
        </Collapse>
      </Form>
    </Modal>
  </Page>
</template>

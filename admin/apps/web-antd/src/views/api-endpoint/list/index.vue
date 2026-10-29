<script lang="ts" setup>
import { ref, onMounted } from 'vue';

import { Page } from '@vben/common-ui';
import {
  Table, Tag, Button, Space, Select, Input, Modal, message,
  Card, Popconfirm,
} from 'ant-design-vue';

import { getList, postEdit, postDel, saveRelations, getRelations } from '../api';
import { getList as getProjectList } from '../../project/api';
import { getList as getFeatureModuleList } from '../../feature-module/api';

// ---- 筛选 ----
const projectId = ref<number | undefined>(undefined);
const sideFilter = ref<string | undefined>(undefined);
const statusFilter = ref<string | undefined>(undefined);
const keyword = ref('');

const loading = ref(false);
const dataList = ref<any[]>([]);
const projectOptions = ref<any[]>([]);
const featureOptions = ref<any[]>([]);

const sideOptions = [
  { label: '后端接口', value: 'Api' },
  { label: 'Admin 调用', value: 'Admin' },
  { label: 'H5 调用', value: 'H5' },
];
const methodOptions = [
  { label: 'GET', value: 'GET' },
  { label: 'POST', value: 'POST' },
  { label: 'PUT', value: 'PUT' },
  { label: 'DELETE', value: 'DELETE' },
  { label: 'PATCH', value: 'PATCH' },
];
const statusOptions = [
  { label: '规划中', value: 'Planned' },
  { label: '开发中', value: 'Developing' },
  { label: '已完成', value: 'Done' },
  { label: '已废弃', value: 'Deprecated' },
];

const sideColor: Record<string, string> = { Api: 'blue', Admin: 'purple', H5: 'cyan' };
const methodColor: Record<string, string> = { GET: 'green', POST: 'blue', PUT: 'orange', DELETE: 'red', PATCH: 'gold' };
const statusColor: Record<string, string> = { Planned: 'default', Developing: 'processing', Done: 'success', Deprecated: 'error' };
const sideLabel: Record<string, string> = { Api: '后端接口', Admin: 'Admin 调用', H5: 'H5 调用' };
const statusLabel: Record<string, string> = { Planned: '规划中', Developing: '开发中', Done: '已完成', Deprecated: '已废弃' };

onMounted(async () => {
  const proj: any = await getProjectList({ pageSize: 200 });
  projectOptions.value = (proj.items || []).map((p: any) => ({ label: p.name, value: p.id }));
  if (projectOptions.value.length > 0) {
    projectId.value = projectOptions.value[0].value;
    await loadFeatures();
    await loadData();
  }
});

async function loadFeatures() {
  if (!projectId.value) { featureOptions.value = []; return; }
  const res: any = await getFeatureModuleList(projectId.value);
  const items = Array.isArray(res) ? res : (res?.items || []);
  featureOptions.value = items.map((m: any) => ({ label: m.name, value: m.id }));
}

async function loadData() {
  if (!projectId.value) { message.warning('请先选择项目'); return; }
  loading.value = true;
  try {
    const res: any = await getList({
      projectId: projectId.value,
      side: sideFilter.value,
      status: statusFilter.value,
      keyword: keyword.value || undefined,
      pageSize: 200,
    });
    dataList.value = res?.items || [];
  } finally {
    loading.value = false;
  }
}

async function onProjectChange() {
  await loadFeatures();
  await loadData();
}

// ---- 新增/编辑 ----
const formVisible = ref(false);
const formSaving = ref(false);
const formData = ref<any>({});
const formModuleIds = ref<number[]>([]);

function handleAdd() {
  if (!projectId.value) { message.warning('请先选择项目'); return; }
  formData.value = { projectId: projectId.value, name: '', path: '', method: 'GET', side: 'Api', status: 'Planned', description: '' };
  formModuleIds.value = [];
  formVisible.value = true;
}

async function handleEdit(row: any) {
  formData.value = { ...row };
  const rels: any = await getRelations(row.id);
  formModuleIds.value = Array.isArray(rels) ? rels : (row.featureModuleIds || []);
  formVisible.value = true;
}

async function handleFormSubmit() {
  if (!formData.value.name?.trim()) { message.warning('请输入接口名称'); return; }
  if (!formData.value.path?.trim()) { message.warning('请输入接口路径'); return; }
  formSaving.value = true;
  try {
    const result: any = await postEdit(formData.value);
    const epId = formData.value.id || result?.id;
    if (epId) await saveRelations(epId, formModuleIds.value);
    formVisible.value = false;
    await loadData();
  } finally {
    formSaving.value = false;
  }
}

async function handleDel(id: number) {
  await postDel(id);
  await loadData();
}

const columns = [
  { title: '名称', dataIndex: 'name', width: 180, ellipsis: true },
  { title: '方法', dataIndex: 'method', width: 80 },
  { title: '路径', dataIndex: 'path', ellipsis: true },
  { title: '端', dataIndex: 'side', width: 110 },
  { title: '状态', dataIndex: 'status', width: 90 },
  { title: '关联功能', dataIndex: 'featureModuleIds', width: 160 },
  { title: '操作', key: 'action', width: 160 },
];

function featureNames(ids: number[]) {
  if (!ids || ids.length === 0) return [];
  return ids.map((id) => featureOptions.value.find((f) => f.value === id)?.label).filter(Boolean);
}
</script>

<template>
  <Page auto-content-height>
    <Card style="margin-bottom: 12px;">
      <Space wrap>
        <Select v-model:value="projectId" :options="projectOptions" placeholder="选择项目"
          style="width: 200px;" @change="onProjectChange" show-search :filter-option="(i:string,o:any)=>o.label.includes(i)" />
        <Select v-model:value="sideFilter" :options="sideOptions" placeholder="全部端" allow-clear style="width: 130px;" @change="loadData" />
        <Select v-model:value="statusFilter" :options="statusOptions" placeholder="全部状态" allow-clear style="width: 130px;" @change="loadData" />
        <Input v-model:value="keyword" placeholder="名称/路径关键词" style="width: 200px;" @press-enter="loadData" allow-clear />
        <Button type="primary" :loading="loading" @click="loadData">查询</Button>
        <Button type="primary" @click="handleAdd">新增接口</Button>
      </Space>
    </Card>

    <Table :columns="columns" :data-source="dataList" :loading="loading" row-key="id"
      :pagination="{ pageSize: 20, showSizeChanger: true }" size="middle">
      <template #bodyCell="{ column, record }">
        <template v-if="column.dataIndex === 'method'">
          <Tag :color="methodColor[record.method] || 'default'">{{ record.method }}</Tag>
        </template>
        <template v-else-if="column.dataIndex === 'path'">
          <span style="font-family: monospace; font-size: 13px;">{{ record.path }}</span>
        </template>
        <template v-else-if="column.dataIndex === 'side'">
          <Tag :color="sideColor[record.side] || 'default'">{{ sideLabel[record.side] || record.side }}</Tag>
        </template>
        <template v-else-if="column.dataIndex === 'status'">
          <Tag :color="statusColor[record.status] || 'default'">{{ statusLabel[record.status] || record.status }}</Tag>
        </template>
        <template v-else-if="column.dataIndex === 'featureModuleIds'">
          <Space wrap :size="[4, 4]">
            <Tag v-for="(n, i) in featureNames(record.featureModuleIds)" :key="i" color="geekblue" style="margin: 0;">{{ n }}</Tag>
            <span v-if="!record.featureModuleIds || record.featureModuleIds.length === 0" style="color: var(--ant-color-text-tertiary);">—</span>
          </Space>
        </template>
        <template v-else-if="column.key === 'action'">
          <Space wrap>
            <Button type="primary" @click="handleEdit(record)">编辑</Button>
            <Popconfirm title="确定删除吗？" @confirm="handleDel(record.id)">
              <Button type="primary" danger>删除</Button>
            </Popconfirm>
          </Space>
        </template>
      </template>
    </Table>

    <!-- 新增/编辑弹窗 -->
    <Modal v-model:open="formVisible" :title="formData.id ? '编辑接口' : '新增接口'"
      :confirm-loading="formSaving" @ok="handleFormSubmit" width="600px">
      <div style="display: flex; flex-direction: column; gap: 14px; padding: 12px 0;">
        <div>
          <div style="margin-bottom: 4px; font-weight: 500;">接口名称 <span style="color: var(--ant-color-error);">*</span></div>
          <Input v-model:value="formData.name" placeholder="如：获取任务列表" />
        </div>
        <div style="display: flex; gap: 12px;">
          <div style="width: 120px;">
            <div style="margin-bottom: 4px; font-weight: 500;">方法</div>
            <Select v-model:value="formData.method" :options="methodOptions" style="width: 100%;" />
          </div>
          <div style="flex: 1;">
            <div style="margin-bottom: 4px; font-weight: 500;">路径 <span style="color: var(--ant-color-error);">*</span></div>
            <Input v-model:value="formData.path" placeholder="如：/admin/TaskItem/List" />
          </div>
        </div>
        <div style="display: flex; gap: 12px;">
          <div style="flex: 1;">
            <div style="margin-bottom: 4px; font-weight: 500;">归属端</div>
            <Select v-model:value="formData.side" :options="sideOptions" style="width: 100%;" />
          </div>
          <div style="flex: 1;">
            <div style="margin-bottom: 4px; font-weight: 500;">状态</div>
            <Select v-model:value="formData.status" :options="statusOptions" style="width: 100%;" />
          </div>
        </div>
        <div>
          <div style="margin-bottom: 4px; font-weight: 500;">关联功能模块</div>
          <Select v-model:value="formModuleIds" :options="featureOptions" mode="multiple"
            placeholder="选择该接口服务的功能模块（可多选）" style="width: 100%;" allow-clear
            :filter-option="(i:string,o:any)=>o.label.includes(i)" />
        </div>
        <div>
          <div style="margin-bottom: 4px; font-weight: 500;">调用说明 / 参数</div>
          <Input.TextArea v-model:value="formData.description" :rows="4" placeholder="请求参数、返回说明、调用方式等" />
        </div>
      </div>
    </Modal>
  </Page>
</template>

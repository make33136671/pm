<script lang="ts" setup>
import type { VxeGridProps } from '#/adapter/vxe-table';
import type { VbenFormProps } from '#/adapter/form';

import { ref, onMounted } from 'vue';

import { Page } from '@vben/common-ui';

import { Tag, Space, Button, Popconfirm, Modal, Input, Select, DatePicker, message } from 'ant-design-vue';

import { useVbenVxeGrid } from '#/adapter/vxe-table';

import { getList, postEdit, postDel } from './api';
import { getList as getProjectList } from '../project/api';
import { customerStatusLabels, toOptions } from '#/utils/enum-labels';

const statusOptions = toOptions(customerStatusLabels);
const statusColorMap: Record<string, string> = {
  Active: 'green',
  Expired: 'orange',
  Terminated: 'default',
};

const formOptions: VbenFormProps = {
  schema: [
    {
      label: '客户名称',
      fieldName: 'keyword',
      component: 'Input',
      componentProps: { placeholder: '搜索客户名称/产品名' },
    },
    {
      label: '项目',
      fieldName: 'projectId',
      component: 'ApiSelect',
      componentProps: {
        placeholder: '请选择项目',
        api: getProjectList,
        afterFetch: (data: any) => data.items.map((item: any) => ({ label: item.name, value: item.id })),
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

interface RowType {
  id: number;
  projectId: number;
  name: string;
  productName: string;
  contact: string;
  branchName: string;
  prodServer: string;
  testServer: string;
  expireDate: string;
  status: string;
  remark: string;
  project?: { name: string };
}

const gridOptions: VxeGridProps<RowType> = {
  columns: [
    { field: 'id', title: 'Id', width: 50, visible: false },
    { field: 'name', title: '客户名称', minWidth: 120, slots: { default: 'name' } },
    { field: 'productName', title: '产品名', minWidth: 120 },
    { field: 'branchName', title: '代码分支', width: 140, slots: { default: 'branch' } },
    { field: 'prodServer', title: '生产服务器', minWidth: 140 },
    { field: 'testServer', title: '测试服务器', minWidth: 140 },
    { field: 'expireDate', title: '到期时间', width: 110, slots: { default: 'expire' } },
    { field: 'status', title: '状态', width: 90, slots: { default: 'status' } },
    { field: 'action', fixed: 'right', slots: { default: 'action' }, title: '操作', width: 160 },
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
  rowConfig: { isHover: true },
  toolbarConfig: { refresh: true, custom: true },
};

const [Grid, gridApi] = useVbenVxeGrid({ formOptions, gridOptions });

// ========== 新增/编辑 ==========

const modalVisible = ref(false);
const modalTitle = ref('');
const formData = ref<any>({});
const saving = ref(false);

function handleAdd() {
  modalTitle.value = '新增客户';
  formData.value = { status: 'Active' };
  modalVisible.value = true;
}

function handleEdit(row: any) {
  modalTitle.value = '编辑客户';
  formData.value = { ...row };
  modalVisible.value = true;
}

async function handleSubmit() {
  if (!formData.value.name?.trim()) return message.warning('请输入客户名称');
  if (!formData.value.projectId) return message.warning('请选择所属项目');
  saving.value = true;
  try {
    await postEdit(formData.value);
    message.success('保存成功');
    modalVisible.value = false;
    gridApi.query();
  } finally {
    saving.value = false;
  }
}

function handleDel(id: number) {
  postDel(id).then(() => gridApi.query());
}

// ========== 项目列表（弹窗用）==========

const projectOptions = ref<any[]>([]);

onMounted(async () => {
  const res: any = await getProjectList({ page: 1, pageSize: 200 });
  projectOptions.value = (res?.items || []).map((p: any) => ({ label: p.name, value: p.id }));
});
</script>

<template>
  <Page auto-content-height>
    <Grid>
      <template #toolbar-tools>
        <Button type="primary" @click="handleAdd">新增客户</Button>
      </template>

      <template #name="{ row }">
        <div>
          <div style="font-weight: 500;">{{ row.name }}</div>
          <div v-if="row.contact" style="font-size: 12px; color: #999;">{{ row.contact }}</div>
        </div>
      </template>

      <template #branch="{ row }">
        <Tag v-if="row.branchName" color="blue">{{ row.branchName }}</Tag>
        <span v-else style="color: #ccc;">-</span>
      </template>

      <template #expire="{ row }">
        <span v-if="row.expireDate">{{ new Date(row.expireDate).toLocaleDateString('zh-CN') }}</span>
        <span v-else style="color: #ccc;">-</span>
      </template>

      <template #status="{ row }">
        <Tag :color="statusColorMap[row.status] || 'default'">
          {{ customerStatusLabels[row.status] || row.status }}
        </Tag>
      </template>

      <template #action="{ row }">
        <Space>
          <Button type="primary" @click="handleEdit(row)">编辑</Button>
          <Popconfirm title="确定删除吗？" @confirm="handleDel(row.id)">
            <Button type="primary" danger>删除</Button>
          </Popconfirm>
        </Space>
      </template>
    </Grid>

    <!-- 新增/编辑弹窗 -->
    <Modal v-model:open="modalVisible" :title="modalTitle" :confirm-loading="saving" @ok="handleSubmit" width="600px">
      <div style=" display: flex; flex-direction: column; gap: 16px;padding: 16px 0;">
        <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 16px;">
          <div>
            <div style="margin-bottom: 4px; font-weight: 500;">客户名称 <span style="color: red;">*</span></div>
            <Input v-model:value="formData.name" placeholder="客户名称" />
          </div>
          <div>
            <div style="margin-bottom: 4px; font-weight: 500;">所属项目 <span style="color: red;">*</span></div>
            <Select v-model:value="formData.projectId" placeholder="请选择项目" style="width: 100%;" show-search :filter-option="(input: string, option: any) => option.label?.toLowerCase().includes(input.toLowerCase())" :options="projectOptions" />
          </div>
        </div>
        <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 16px;">
          <div>
            <div style="margin-bottom: 4px; font-weight: 500;">产品名</div>
            <Input v-model:value="formData.productName" placeholder="客户方的产品名称" />
          </div>
          <div>
            <div style="margin-bottom: 4px; font-weight: 500;">联系方式</div>
            <Input v-model:value="formData.contact" placeholder="联系方式" />
          </div>
        </div>
        <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 16px;">
          <div>
            <div style="margin-bottom: 4px; font-weight: 500;">代码分支</div>
            <Input v-model:value="formData.branchName" placeholder="如 customer/xxx" />
          </div>
          <div>
            <div style="margin-bottom: 4px; font-weight: 500;">状态</div>
            <Select v-model:value="formData.status" :options="statusOptions" style="width: 100%;" />
          </div>
        </div>
        <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 16px;">
          <div>
            <div style="margin-bottom: 4px; font-weight: 500;">生产服务器</div>
            <Input v-model:value="formData.prodServer" placeholder="生产环境服务器地址" />
          </div>
          <div>
            <div style="margin-bottom: 4px; font-weight: 500;">测试服务器</div>
            <Input v-model:value="formData.testServer" placeholder="测试环境服务器地址" />
          </div>
        </div>
        <div>
          <div style="margin-bottom: 4px; font-weight: 500;">到期时间</div>
          <DatePicker v-model:value="formData.expireDate" style="width: 100%;" placeholder="选择到期时间" />
        </div>
        <div>
          <div style="margin-bottom: 4px; font-weight: 500;">备注</div>
          <Input.TextArea v-model:value="formData.remark" :rows="2" placeholder="备注信息" />
        </div>
      </div>
    </Modal>
  </Page>
</template>

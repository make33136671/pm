<script lang="ts" setup>
import type { VxeGridProps } from '#/adapter/vxe-table';
import type { VbenFormProps } from '#/adapter/form';

import { ref, reactive } from 'vue';
import { useRouter } from 'vue-router';

import { Page } from '@vben/common-ui';

import { Tag, Space, Button, Input, Popconfirm, Modal, Card, Badge, Drawer, List as AList, ListItem as AListItem, ListItemMeta as AListItemMeta, message } from 'ant-design-vue';

import { useVbenVxeGrid } from '#/adapter/vxe-table';

import { getList, postDel, getAiParseSearchIntent } from '../api';
import { getList as getProjectList, getSubProjectList } from '../../project/api';
import { getList as getCustomerList } from '../../project-customer/api';
import { postGenerateFromRequirements, postConfirmCreate } from '../../task-item/api';
import { requirementStatusLabels, requirementTypeLabels, requirementSourceLabels, complexityLabels, taskItemTypeLabels, toOptions } from '#/utils/enum-labels';
import DocPreview from '#/components/DocPreview/index.vue';
import RequirementInfoCell from '#/components/business/RequirementInfoCell.vue';

const router = useRouter();
const docRef = ref<InstanceType<typeof DocPreview>>();

interface RowType {
  id: number;
  projectId: number;
  title: string;
  projectName: string;
  type: string;
  status: string;
  parentId: number | null;
  childCount: number;
  assignee: string;
  dueDate: string;
  source: string;
  project?: { name: string };
}

const statusOptions = toOptions(requirementStatusLabels);
const typeOptions = toOptions(requirementTypeLabels);
const sourceOptions = toOptions(requirementSourceLabels);
const statusColorMap: Record<string, string> = {
  Draft: 'default',
  Todo: 'blue',
  InProgress: 'processing',
  Testing: 'orange',
  Done: 'green',
  Closed: 'default',
};

const typeColorMap: Record<string, string> = {
  Feature: 'blue',
  Bugfix: 'red',
  Improvement: 'green',
  Task: 'default',
  Research: 'purple',
};

const complexityColorMap: Record<string, string> = {
  Low: 'green',
  Medium: 'blue',
  High: 'orange',
  VeryHigh: 'red',
};

const formOptions: VbenFormProps = {
  schema: [
    {
      label: '标题',
      fieldName: 'title',
      component: 'Input',
      componentProps: { placeholder: '请输入标题' },
    },
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
      label: '子项目',
      fieldName: 'subProjectId',
      component: 'ApiSelect',
      componentProps: {
        placeholder: '请选择子项目',
        api: getSubProjectList,
        afterFetch: (data: any) => (data?.items || []).map((s: any) => ({ label: s.name, value: s.id })),
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
    {
      label: '类型',
      fieldName: 'type',
      component: 'Select',
      componentProps: {
        placeholder: '请选择类型',
        options: typeOptions,
        allowClear: true,
      },
    },
    {
      label: '来源',
      fieldName: 'source',
      component: 'Select',
      componentProps: {
        placeholder: '请选择来源',
        options: sourceOptions,
        allowClear: true,
      },
    },
    {
      label: '客户',
      fieldName: 'customerId',
      component: 'ApiSelect',
      componentProps: {
        placeholder: '全部 / 通用需求',
        api: getCustomerList,
        afterFetch: (data: any) => {
          const items = (data?.items || []).map((c: any) => ({ label: c.name, value: c.id }));
          return [{ label: '通用需求', value: 0 }, ...items];
        },
        allowClear: true,
        filterOption: true,
        showSearch: true,
      },
    },
    {
      label: '分析批次',
      fieldName: 'batchId',
      component: 'ApiSelect',
      componentProps: {
        placeholder: '按分析批次筛选',
        api: () => import('../api').then(m => m.getAnalysisBatchList({ pageSize: 100 })),
        afterFetch: (data: any) => {
          const items = data?.items || data || [];
          return items.map((b: any) => ({
            label: `${b.batchLabel || b.batchId}（${b.requirementCount}条）`,
            value: b.batchId,
          }));
        },
        allowClear: true,
        filterOption: true,
        showSearch: true,
      },
    },
  ],
  wrapperClass: 'grid-cols-3',
  showCollapseButton: true,
  collapsed: true, // 默认收起，只占一行
  submitOnChange: false,
  submitOnEnter: true,
};

const gridOptions: VxeGridProps<RowType> = {
  checkboxConfig: { highlight: true },
  columns: [
    { type: 'checkbox', width: 40 },
    { fixed: 'left', field: 'id', title: 'Id', width: 50, visible: false },
    {
      field: 'title',
      title: '信息',
      slots: { default: 'info' },
      minWidth: 280,
    },
    {
      field: 'type',
      title: '状态/类型',
      slots: { default: 'status' },
      width: 220,  // #69 列宽收窄
      minWidth: 180,
    },
    {
      field: 'source',
      title: '来源',
      width: 90,   // #69 来源列收窄
      slots: { default: 'source' },
    },
    {
      field: 'action',
      fixed: 'right',
      align: 'center',
      slots: { default: 'action' },
      title: '操作',
      width: 260,  // #71 操作列调整
      minWidth: 200,
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
    height: 130, // 操作列 8 个按钮 wrap 成最多 3 行，行高容下避免溢出
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

const handleAdd = () => {
  router.push({ path: '/project/requirement/form' });
};

const handleEdit = (id: number) => {
  router.push({ path: '/project/requirement/form', query: { id } });
};

const handleDel = (id: number) => {
  postDel(id).then(() => gridApi.query());
};

// ========== 补充需求 ==========

const handleAddChild = (parentId: number) => {
  router.push({ path: '/project/requirement/form', query: { parentId } });
};

// ========== 查看子需求 ==========

const childDrawerVisible = ref(false);
const childDrawerTitle = ref('');
const childList = ref<any[]>([]);
const childLoading = ref(false);

const handleViewChildren = async (row: any) => {
  childDrawerTitle.value = `子需求 — ${row.title}`;
  childDrawerVisible.value = true;
  childLoading.value = true;
  try {
    const res: any = await getList({ parentId: row.id, pageSize: 100 });
    childList.value = res?.items || [];
  } finally {
    childLoading.value = false;
  }
};

// ========== 生成任务 ==========

const genTaskVisible = ref(false);
const genTaskPreview = ref<any[]>([]);
const genTaskLoading = ref(false);

const handleGenTask = async () => {
  const records = gridApi.grid?.getCheckboxRecords() || [];
  if (records.length === 0) return message.warning('请先勾选需求');
  const requirementIds = records.map((r: any) => r.id);
  const projectId = records[0]?.projectId;
  if (!projectId) return message.warning('未找到项目信息');

  genTaskLoading.value = true;
  try {
    const res: any = await postGenerateFromRequirements({ requirementIds, projectId });
    genTaskPreview.value = Array.isArray(res) ? res : [];
    genTaskVisible.value = true;
  } finally {
    genTaskLoading.value = false;
  }
};

const handleConfirmGenTask = async () => {
  if (genTaskPreview.value.length === 0) return;
  await postConfirmCreate(genTaskPreview.value);
  message.success(`已创建 ${genTaskPreview.value.length} 个任务`);
  genTaskVisible.value = false;
  genTaskPreview.value = [];
};

// 单条生成任务
// AI 搜索(#18): 自然语言 → 结构化过滤条件 → 触发列表筛选
const aiSearchQuery = ref('');
const aiSearchLoading = ref(false);

const handleAiSearch = async () => {
  if (!aiSearchQuery.value.trim()) return;
  aiSearchLoading.value = true;
  try {
    const intent: any = await getAiParseSearchIntent(aiSearchQuery.value, 'requirement');
    // 将解析结果填入筛选表单并刷新
    const patch: Record<string, any> = {};
    if (intent?.keyword) patch.title = intent.keyword;
    if (intent?.type) patch.type = intent.type.split('|')[0];
    if (intent?.status) patch.status = intent.status.split('|')[0];
    if (intent?.complexity) patch.complexity = intent.complexity;
    await gridApi.formApi?.setValues(patch);
    gridApi.query();
  } finally {
    aiSearchLoading.value = false; }
};

const handleGenTaskSingle = async (row: any) => {
  if (!row.projectId) return message.warning('未找到项目信息');
  genTaskLoading.value = true;
  try {
    const res: any = await postGenerateFromRequirements({ requirementIds: [row.id], projectId: row.projectId });
    genTaskPreview.value = Array.isArray(res) ? res : [];
    genTaskVisible.value = true;
  } finally {
    genTaskLoading.value = false;
  }
};
</script>

<template>
  <Page auto-content-height>
    <Grid>
      <template #toolbar-tools>
        <Space wrap>
          <Input.Search
            v-model:value="aiSearchQuery"
            placeholder="AI 搜索：如「未完成的功能需求」"
            style="width: 260px"
            :loading="aiSearchLoading"
            enter-button="✨"
            @search="handleAiSearch"
          />
          <Button type="primary" @click="handleAdd">新增需求</Button>
          <Button :loading="genTaskLoading" @click="handleGenTask">生成任务</Button>
        </Space>
      </template>

      <template #info="{ row }">
        <div>
          <RequirementInfoCell :record="row" group="info" show-project click-title @click-title="handleEdit" />
          <div style=" display: flex; flex-wrap: wrap; gap: 6px; align-items: center;padding: 2px 12px;">
            <Tag v-if="row.parentId" color="orange" size="small">
              补充 #{{ row.parentId }}
            </Tag>
            <a
              v-if="row.childCount > 0"
              style=" font-size: 12px;cursor: pointer;"
              @click="handleViewChildren(row)"
            >
              <Badge :count="row.childCount" :overflow-count="99" style="margin-right: 4px;" />
              <span>条补充需求</span>
            </a>
            <Tag v-if="row.customer" color="gold" size="small">
              {{ row.customer.name }}
            </Tag>
            <Tag v-for="fm in (row.featureModules || [])" :key="fm.id" color="cyan" size="small">
              {{ fm.name }}
            </Tag>
          </div>
        </div>
      </template>

      <template #status="{ row }">
        <RequirementInfoCell :record="row" group="status" />
      </template>

      <template #source="{ row }">
        <div>
          <Tag v-if="row.source === 'AI'" color="purple">AI</Tag>
          <Tag v-else-if="row.source === 'Telegram'" color="cyan">TG</Tag>
          <Tag v-else-if="row.source === 'Hermes'" color="gold">🤖 Hermes</Tag>
          <Tag v-else-if="row.source === 'Git'" color="geekblue">Git</Tag>
          <Tag v-else color="default">{{ requirementSourceLabels[row.source] || '手动' }}</Tag>
          <div v-if="row.sourceRef" style=" margin-top: 2px;font-size: 11px; color: #999; word-break: break-all;">
            {{ row.sourceRef }}
          </div>
        </div>
      </template>

      <template #parent="{ row }">
        <a
          v-if="row.parentId"
          style="color: #1890ff; cursor: pointer;"
          @click="handleEdit(row.parentId)"
        >
          #{{ row.parentId }}
        </a>
        <span v-else style="color: #ccc;">—</span>
      </template>

      <template #action="{ row }">
        <Space wrap>
          <Button type="primary" @click="handleEdit(row.id)">编辑</Button>
          <Button @click="handleAddChild(row.id)">补充</Button>
          <Button @click="docRef?.openPreview(row.id, row.title)">预览</Button>
          <Button @click="docRef?.download(row.id, row.title)">下载</Button>
          <Button @click="docRef?.copyLink(row.id)">链接</Button>
          <Button @click="docRef?.openHistory(row.id, row.title)">版本</Button>
          <Button @click="handleGenTaskSingle(row)">生成任务</Button>
          <Popconfirm title="确定删除吗？" @confirm="handleDel(row.id)">
            <Button type="primary" danger>删除</Button>
          </Popconfirm>
        </Space>
      </template>
    </Grid>

    <DocPreview ref="docRef" source-type="Requirement" />

    <!-- 子需求抽屉 -->
    <Drawer
      v-model:open="childDrawerVisible"
      :title="childDrawerTitle"
      width="500"
      placement="right"
    >
      <AList
        :loading="childLoading"
        :data-source="childList"
        item-layout="vertical"
        size="small"
      >
        <template #renderItem="{ item }">
          <AListItem>
            <AListItemMeta>
              <template #title>
                <a style="cursor: pointer;" @click="handleEdit(item.id)">
                  #{{ item.id }} {{ item.title }}
                </a>
              </template>
              <template #description>
                <Space>
                  <Tag :color="statusColorMap[item.status] || 'default'" size="small">
                    {{ requirementStatusLabels[item.status] || item.status }}
                  </Tag>
                  <Tag :color="typeColorMap[item.type] || 'default'" size="small">
                    {{ requirementTypeLabels[item.type] || item.type }}
                  </Tag>
                  <Tag v-if="item.complexity" :color="complexityColorMap[item.complexity] || 'default'" size="small">
                    {{ complexityLabels[item.complexity] || item.complexity }}
                  </Tag>
                  <span v-if="item.assignee" style="font-size: 12px; color: #999;">{{ item.assignee }}</span>
                </Space>
              </template>
            </AListItemMeta>
          </AListItem>
        </template>
        <template #header>
          <div style="display: flex; justify-content: flex-end;">
            <Button type="primary" size="small" @click="childDrawerVisible = false; handleAddChild(childList[0]?.parentId || 0)">
              继续补充
            </Button>
          </div>
        </template>
      </AList>
    </Drawer>

    <!-- 生成任务预览弹窗 -->
    <Modal v-model:open="genTaskVisible" title="生成任务预览" width="600px" :footer="null">
      <div style="margin-bottom: 12px; font-weight: 500;">将创建 {{ genTaskPreview.length }} 个任务：</div>
      <div style="max-height: 400px; overflow-y: auto;">
        <Card v-for="(task, index) in genTaskPreview" :key="index" size="small" style="margin-bottom: 8px;">
          <div style="font-weight: 500;">{{ task.title }}</div>
          <div style="margin-top: 4px;">
            <Tag color="blue">{{ taskItemTypeLabels[task.type] || task.type }}</Tag>
          </div>
        </Card>
      </div>
      <div v-if="genTaskPreview.length === 0" style=" padding: 40px 0; color: #999;text-align: center;">无可生成的任务</div>
      <div style=" display: flex; gap: 8px; justify-content: flex-end;margin-top: 16px;">
        <Button @click="genTaskVisible = false">取消</Button>
        <Button type="primary" @click="handleConfirmGenTask" :disabled="genTaskPreview.length === 0">确认创建</Button>
      </div>
    </Modal>
  </Page>
</template>

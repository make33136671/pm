<script lang="ts" setup>
import { onMounted, ref, computed } from 'vue';
import { useRoute, useRouter } from 'vue-router';

import { Button, Card, Alert, Tag, Modal, Spin, message } from 'ant-design-vue';
import { ref as vRef } from 'vue';
import { usePreferences } from '@vben/preferences';

import { useVbenForm } from '#/adapter/form';
import { MdEditor } from 'md-editor-v3';
import 'md-editor-v3/lib/style.css';

import { getDetail, postEdit, getList, postAiSplitTasks, postAiConfirmTasks } from '../api';
import { getList as getProjectList } from '../../project/api';
import { getList as getFeatureModuleList, saveRelations, getRelations } from '../../feature-module/api';
import { getList as getCustomerList } from '../../project-customer/api';
import { requirementStatusLabels, requirementTypeLabels, requirementSourceLabels, complexityLabels, toOptions } from '#/utils/enum-labels';
import { uploadFileApi } from '#/api/core/admin';

const router = useRouter();
const route = useRoute();

const id = ref<number>(0);
const parentId = ref<number>(0);
const parentInfo = ref<any>(null);
const editorContent = ref('');
const featureModuleIds = ref<number[]>([]);
const { isDark } = usePreferences();
const mdTheme = computed(() => isDark.value ? 'dark' : 'light');

onMounted(async () => {
  id.value = Number(route.query.id) || 0;
  parentId.value = Number(route.query.parentId) || 0;

  if (id.value > 0) {
    // 编辑模式
    const result: any = await getDetail(id.value);
    baseFormApi.setValues(result);
    editorContent.value = result.content || '';
    if (result.parent) {
      parentInfo.value = result.parent;
    }
    // 加载关联的功能模块并回填
    const rels: any = await getRelations(id.value);
    featureModuleIds.value = Array.isArray(rels) ? rels : [];
    baseFormApi.setValues({ featureModuleIds: featureModuleIds.value });
  } else if (parentId.value > 0) {
    // 新建补充需求 — 加载父需求信息并继承项目
    const result: any = await getDetail(parentId.value);
    parentInfo.value = { id: result.id, title: result.title, status: result.status, type: result.type };
    baseFormApi.setValues({ projectId: result.projectId });
  }
});

const statusOptions = toOptions(requirementStatusLabels);
const typeOptions = toOptions(requirementTypeLabels);
const complexityOptions = toOptions(complexityLabels);
const sourceOptions = toOptions(requirementSourceLabels);

const statusColorMap: Record<string, string> = {
  Draft: 'default', Todo: 'blue', InProgress: 'processing',
  Testing: 'orange', Done: 'green', Closed: 'default',
};

const [BaseForm, baseFormApi] = useVbenForm({
  commonConfig: {
    colon: true,
    componentProps: { class: 'w-full' },
  },
  handleSubmit: onSubmit,
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
        afterFetch: (data: any) => data.items.map((item: any) => ({ label: item.name, value: item.id })),
        allowClear: true,
        filterOption: true,
        showSearch: true,
      },
    },
    {
      fieldName: 'title',
      component: 'Input',
      label: '标题',
      rules: 'required',
      componentProps: { placeholder: '请输入需求标题' },
    },
    {
      fieldName: 'type',
      component: 'Select',
      label: '类型',
      rules: 'required',
      defaultValue: 'Feature',
      componentProps: { placeholder: '请选择类型', options: typeOptions },
    },
    {
      fieldName: 'complexity',
      component: 'Select',
      label: '复杂度',
      defaultValue: 'Medium',
      componentProps: { placeholder: '请选择复杂度', options: complexityOptions },
    },
    {
      fieldName: 'status',
      component: 'Select',
      label: '状态',
      rules: 'required',
      defaultValue: 'Draft',
      componentProps: { placeholder: '请选择状态', options: statusOptions },
    },
    {
      fieldName: 'source',
      component: 'Select',
      label: '来源',
      defaultValue: 'Manual',
      componentProps: { placeholder: '请选择来源', options: sourceOptions },
    },
    {
      fieldName: 'customerId',
      component: 'ApiSelect',
      label: '客户',
      dependencies: {
        triggerFields: ['projectId'],
        show: (values: any) => !!values.projectId,
      },
      componentProps: {
        placeholder: '通用需求留空，客户专属请选择',
        api: getCustomerList,
        beforeFetch: (params: any) => {
          const projectId = baseFormApi.getValues()?.projectId;
          return projectId ? { projectId } : params;
        },
        afterFetch: (data: any) => {
          const items = data?.items || [];
          return items.map((item: any) => ({
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
      fieldName: 'assignee',
      component: 'Input',
      label: '负责人',
      componentProps: { placeholder: '请输入负责人' },
    },
    {
      fieldName: 'estimatedHours',
      component: 'InputNumber',
      label: '预估工时',
      componentProps: { placeholder: '请输入预估工时', min: 0, style: { width: '100%' } },
    },
    {
      fieldName: 'dueDate',
      component: 'DatePicker',
      label: '截止日期',
      componentProps: { placeholder: '请选择截止日期', style: { width: '100%' } },
    },
    {
      fieldName: 'parentId',
      component: 'ApiSelect',
      label: '父需求',
      dependencies: {
        triggerFields: ['projectId'],
        show: (values: any) => {
          return !!values.projectId;
        },
      },
      componentProps: {
        placeholder: '请选择父需求（可选）',
        api: getList,
        afterFetch: (data: any) => {
          return (data.items || [])
            .filter((item: any) => item.id !== id.value)
            .map((item: any) => ({
              label: `#${item.id} ${item.title}`,
              value: item.id,
            }));
        },
        allowClear: true,
        filterOption: true,
        showSearch: true,
      },
    },
    {
      fieldName: 'featureModuleIds',
      component: 'ApiSelect',
      label: '功能模块',
      dependencies: {
        triggerFields: ['projectId'],
        show: (values: any) => {
          return !!values.projectId;
        },
      },
      componentProps: {
        placeholder: '请选择关联的功能模块（可选）',
        mode: 'multiple',
        api: getFeatureModuleList,
        beforeFetch: (params: any) => {
          const projectId = baseFormApi.getValues()?.projectId;
          return projectId || params;
        },
        afterFetch: (data: any) => {
          const items = Array.isArray(data) ? data : (data?.items || []);
          return items.map((item: any) => ({
            label: item.name,
            value: item.id,
          }));
        },
        allowClear: true,
        filterOption: true,
        showSearch: true,
      },
    },
  ],
  wrapperClass: 'grid-cols-1 md:grid-cols-2 lg:grid-cols-3',
});

async function handleUploadImg(
  files: File[],
  callback: (urls: string[]) => void,
) {
  const urls: string[] = [];
  for (const file of files) {
    try {
      const result = await uploadFileApi(file);
      if (result) {
        const url =
          typeof result === 'string'
            ? result
            : (result as any).absfileName || (result as any).fileName;
        if (url) urls.push(url);
      }
    } catch (e) {
      console.error('图片上传失败', e);
    }
  }
  callback(urls);
}

// AI 补充内容(#10): 用 AI 基于标题/描述丰富 content
const aiEnriching = vRef(false);
async function onAiEnrich() {
  const vals = await baseFormApi.getValues();
  if (!vals.title) { message.warning('请先填写需求标题'); return; }
  aiEnriching.value = true;
  try {
    // 调 AI Chat 接口补充内容
    const res: any = await import('#/api/request').then(m =>
      m.api.post('/Ai/Chat', {
        projectId: vals.projectId,
        instruction: `请根据需求标题"${vals.title}"，补充完整的需求描述，包括背景、目标、验收条件。已有描述：${editorContent.value || '(无)'}`,
      })
    );
    if (res?.content) editorContent.value = res.content;
  } finally { aiEnriching.value = false; }
}

// AI 分解任务(#9): 将当前需求拆解为子任务
const splitVisible = vRef(false);
const splitBatchId = vRef('');
const splitTasks = vRef<any[]>([]);
const splitLoading = vRef(false);

async function onAiSplitTasks() {
  if (!id.value) { message.warning('请先保存需求'); return; }
  splitLoading.value = true;
  splitVisible.value = true;
  splitTasks.value = [];
  try {
    const res: any = await postAiSplitTasks(id.value);
    splitBatchId.value = res?.batchId || '';
    // 获取暂存任务列表
    if (splitBatchId.value) {
      const tasks: any = await import('#/api/request').then(m =>
        m.api.get('/Ai/TaskHistory', { params: { page: 1, pageSize: 50 } })
      );
      // 直接用 batchId 查询
      const list: any = await import('#/api/request').then(m =>
        m.api.get('/TaskItem/List', { params: { batchId: splitBatchId.value, pageSize: 50 } })
      );
      splitTasks.value = (list as any)?.items || [];
    }
  } finally { splitLoading.value = false; }
}

async function confirmSplitTasks() {
  if (!splitBatchId.value) return;
  await postAiConfirmTasks(splitBatchId.value);
  splitVisible.value = false;
}

async function onSubmit(values: Record<string, any>) {
  values.content = editorContent.value;
  const moduleIds = values.featureModuleIds || [];
  delete values.featureModuleIds;
  if (id.value > 0) {
    values.id = id.value;
  }
  // 新建补充需求时携带 parentId
  if (parentId.value > 0 && !id.value) {
    values.parentId = parentId.value;
  }
  const result: any = await postEdit(values);
  // 保存功能模块关联
  const reqId = id.value || result?.id;
  if (reqId && moduleIds.length > 0) {
    await saveRelations(reqId, moduleIds);
  } else if (reqId && moduleIds.length === 0 && id.value > 0) {
    // 编辑时清空了关联
    await saveRelations(reqId, []);
  }
  message.success('保存成功');
  router.back();
}
</script>

<template>
  <Card :title="parentInfo && !id ? '补充需求' : '需求'" style="margin: 10px">
    <Alert
      v-if="parentInfo"
      style="margin-bottom: 16px;"
      type="info"
      show-icon
    >
      <template #message>
        <span>上级需求：</span>
        <Tag :color="statusColorMap[parentInfo.status] || 'default'">
          {{ requirementStatusLabels[parentInfo.status] || parentInfo.status }}
        </Tag>
        <a @click="router.push({ path: '/project/requirement/form', query: { id: parentInfo.id } })">
          #{{ parentInfo.id }} {{ parentInfo.title }}
        </a>
      </template>
    </Alert>
    <BaseForm />
    <div style="margin-top: 16px;">
      <div style="margin-bottom: 8px; display: flex; align-items: center; gap: 8px;">
        <span style="font-weight: 500;">描述 (Markdown)</span>
        <Button size="small" :loading="aiEnriching" @click="onAiEnrich">✨ AI 补充内容</Button>
        <Button v-if="id" size="small" type="primary" ghost @click="onAiSplitTasks">🔀 AI 分解任务</Button>
      </div>
      <MdEditor v-model="editorContent" :theme="mdTheme" :preview="false" style="height: 350px;" :onUploadImg="handleUploadImg" />
    </div>

    <!-- AI 分解任务预览弹窗 -->
    <Modal v-model:open="splitVisible" title="AI 分解任务预览" width="640px"
      ok-text="确认创建" cancel-text="取消" @ok="confirmSplitTasks">
      <Spin :spinning="splitLoading">
        <div v-if="!splitLoading && splitTasks.length === 0" style="padding: 24px; text-align: center; color: var(--ant-color-text-secondary);">
          暂无任务（AI 正在生成或未返回结果）
        </div>
        <div v-for="t in splitTasks" :key="t.id" style="padding: 8px 0; border-bottom: 1px solid var(--ant-color-split);">
          <div style="font-weight: 500;">{{ t.title }}</div>
          <div style="font-size: 12px; color: var(--ant-color-text-secondary); margin-top: 2px;">{{ t.content }}</div>
        </div>
      </Spin>
    </Modal>
  </Card>
</template>

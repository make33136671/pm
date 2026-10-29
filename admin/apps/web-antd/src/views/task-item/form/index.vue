<script lang="ts" setup>
import { onMounted, ref, computed } from 'vue';
import { useRoute, useRouter } from 'vue-router';

import { Button, Card, message, Switch } from 'ant-design-vue';
import { usePreferences } from '@vben/preferences';

import { useVbenForm } from '#/adapter/form';
import { MdEditor } from 'md-editor-v3';
import 'md-editor-v3/lib/style.css';

import { getDetail, postEdit } from '../api';
import { getList as getProjectList } from '../../project/api';
import { getList as getRequirementList } from '../../requirement/api';
import {
  taskItemTypeLabels, taskItemExecModeLabels, complexityLabels, toOptions,
} from '#/utils/enum-labels';

const router = useRouter();
const route = useRoute();

const id = ref<number>(0);
const editorContent = ref('');
const autoAssign = ref(false);
const { isDark } = usePreferences();
const mdTheme = computed(() => isDark.value ? 'dark' : 'light');

onMounted(async () => {
  id.value = Number(route.query.id) || 0;
  if (id.value > 0) {
    getDetail(id.value).then((result: any) => {
      baseFormApi.setValues(result);
      editorContent.value = result.content || '';
      autoAssign.value = !!result.autoAssign;
    });
  }
});

const typeOptions = toOptions(taskItemTypeLabels);
const complexityOptions = toOptions(complexityLabels);
const execModeOptions = toOptions(taskItemExecModeLabels);

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
        showSearch: true,
        filterOption: true,
      },
    },
    {
      fieldName: 'title',
      component: 'Input',
      label: '标题',
      rules: 'required',
      componentProps: { placeholder: '请输入任务标题' },
    },
    {
      fieldName: 'requirementId',
      component: 'ApiSelect',
      label: '关联需求',
      componentProps: {
        placeholder: '请选择关联需求（可选）',
        api: getRequirementList,
        afterFetch: (data: any) => data.items.map((item: any) => ({ label: item.title, value: item.id })),
        allowClear: true,
        showSearch: true,
        filterOption: true,
      },
    },
    {
      fieldName: 'type',
      component: 'Select',
      label: '类型',
      rules: 'required',
      defaultValue: 'Development',
      componentProps: { options: typeOptions },
    },
    {
      fieldName: 'complexity',
      component: 'Select',
      label: '复杂度',
      defaultValue: 'Medium',
      componentProps: { options: complexityOptions },
    },
    {
      fieldName: 'execMode',
      component: 'Select',
      label: '执行模式',
      defaultValue: 'Api',
      componentProps: { options: execModeOptions },
    },
    {
      fieldName: 'assignee',
      component: 'Input',
      label: '负责人',
      componentProps: { placeholder: '请输入负责人' },
    },
    {
      fieldName: 'maxRetryCount',
      component: 'InputNumber',
      label: '最大重试次数',
      defaultValue: 3,
      componentProps: { min: 1, max: 20, style: { width: '100%' } },
    },
    {
      fieldName: 'timeoutSeconds',
      component: 'InputNumber',
      label: '超时时间(秒)',
      defaultValue: 300,
      componentProps: { min: 30, max: 86400, style: { width: '100%' } },
    },
    {
      fieldName: 'estimatedHours',
      component: 'InputNumber',
      label: '预估工时',
      componentProps: { min: 0, placeholder: '小时', style: { width: '100%' } },
    },
    {
      fieldName: 'dueDate',
      component: 'DatePicker',
      label: '截止日期',
      componentProps: { placeholder: '请选择截止日期', style: { width: '100%' } },
    },
  ],
  wrapperClass: 'grid-cols-1 md:grid-cols-2 lg:grid-cols-3',
});

// AI 补充内容(#11)
const aiEnriching = ref(false);
async function onAiEnrich() {
  const vals = await baseFormApi.getValues();
  if (!vals.title) { message.warning('请先填写任务标题'); return; }
  aiEnriching.value = true;
  try {
    const res: any = await import('#/api/request').then(m =>
      m.api.post('/Ai/Chat', {
        projectId: vals.projectId,
        instruction: `请根据任务标题"${vals.title}"，补充完整的任务描述，包括目标、实现要点、验收标准。已有描述：${editorContent.value || '(无)'}`,
      })
    );
    if (res?.content) editorContent.value = res.content;
  } finally { aiEnriching.value = false; }
}

function onSubmit(values: Record<string, any>) {
  values.content = editorContent.value;
  values.autoAssign = autoAssign.value;
  if (id.value > 0) {
    values.id = id.value;
  }
  postEdit(values).then(() => {
    message.success('保存成功');
    router.back();
  });
}
</script>

<template>
  <Card title="任务" style="margin: 10px">
    <BaseForm />
    <div style="display: flex; align-items: center; gap: 10px; margin: 0 0 16px 0; padding: 10px 16px; background: var(--ant-color-fill-quaternary); border-radius: 6px; border: 1px solid var(--ant-color-border);">
      <Switch v-model:checked="autoAssign" checked-children="自动派发" un-checked-children="手动" />
      <span style="font-size: 13px; color: var(--ant-color-text-secondary);">
        开启后创建任务时自动推送给 Hermes Agent 接单执行
      </span>
    </div>
    <div style="margin-top: 16px;">
      <div style="margin-bottom: 8px; display: flex; align-items: center; gap: 8px;">
        <span style="font-weight: 500;">描述 (Markdown)</span>
        <Button size="small" :loading="aiEnriching" @click="onAiEnrich">✨ AI 补充内容</Button>
      </div>
      <MdEditor v-model="editorContent" :theme="mdTheme" :preview="false" style="height: 350px;" />
    </div>
  </Card>
</template>

<script lang="ts" setup>
import { ref } from 'vue';

import { Page } from '@vben/common-ui';

import { Tag, Space, Button, Popconfirm, Modal, Switch, InputNumber, message, Card, Table } from 'ant-design-vue';

import { useVbenForm } from '#/adapter/form';

import {
  getPipelineByGroup, postPipelineToggleEnabled, postPipelineToggleRealtime,
  postPipelineAddStep, postPipelineEditStep, postPipelineDeleteStep,
} from '../api';

const stepTypeLabels: Record<string, string> = {
  Filter: '废话过滤', RequirementExtract: '需求提取',
  ImageRecognition: '图像识别', AudioTranscription: '音频转文字',
};

const groupBindingId = ref<number>();
const pipeline = ref<any>(null);
const steps = ref<any[]>([]);
const loading = ref(false);
const stepModalVisible = ref(false);
const stepEditId = ref(0);

const stepColumns = [
  { title: '顺序', dataIndex: 'orderNo', width: 60 },
  { title: '类型', dataIndex: 'stepType', key: 'stepType' },
  { title: 'AI 提供商 ID', dataIndex: 'aiProviderId', width: 110 },
  { title: '启用', dataIndex: 'enabled', key: 'enabled' },
  { title: '操作', key: 'action', width: 130 },
];

const [StepForm, stepFormApi] = useVbenForm({
  commonConfig: { colon: true, componentProps: { class: 'w-full' } },
  handleSubmit: onStepSubmit,
  layout: 'horizontal',
  schema: [
    {
      fieldName: 'stepType', component: 'Select', label: '类型', rules: 'required',
      componentProps: {
        options: Object.entries(stepTypeLabels).map(([value, label]) => ({ label, value })),
      },
    },
    { fieldName: 'aiProviderId', component: 'InputNumber', label: 'AI 提供商 ID', rules: 'required' },
    { fieldName: 'orderNo', component: 'InputNumber', label: '顺序', defaultValue: 0 },
    {
      fieldName: 'enabled', component: 'RadioGroup', label: '启用', defaultValue: true,
      componentProps: { options: [{ label: '是', value: true }, { label: '否', value: false }] },
    },
    { fieldName: 'config', component: 'Textarea', label: '配置 JSON', componentProps: { rows: 3 } },
  ],
  wrapperClass: 'grid-cols-1',
});

async function loadPipeline() {
  if (!groupBindingId.value) { message.error('请输入群组绑定 ID'); return; }
  loading.value = true;
  try {
    const res: any = await getPipelineByGroup(groupBindingId.value);
    pipeline.value = res.pipeline;
    steps.value = res.steps || [];
  } catch {
    pipeline.value = null;
    steps.value = [];
  } finally {
    loading.value = false;
  }
}

async function toggleEnabled(val: boolean) {
  if (!pipeline.value) return;
  await postPipelineToggleEnabled(pipeline.value.id, val);
  pipeline.value.enabled = val;
}

async function toggleRealtime(val: boolean) {
  if (!pipeline.value) return;
  await postPipelineToggleRealtime(pipeline.value.id, val);
  pipeline.value.realtimeEnabled = val;
}

function showAddStep() {
  stepEditId.value = 0;
  stepFormApi.resetForm();
  if (pipeline.value) {
    stepFormApi.setValues({ pipelineId: pipeline.value.id });
  }
  stepModalVisible.value = true;
}

function showEditStep(step: any) {
  stepEditId.value = step.id;
  stepFormApi.resetForm();
  stepFormApi.setValues(step);
  stepModalVisible.value = true;
}

function onStepSubmit(values: Record<string, any>) {
  if (!pipeline.value) return;
  values.pipelineId = pipeline.value.id;

  const action = stepEditId.value > 0
    ? postPipelineEditStep({ ...values, id: stepEditId.value })
    : postPipelineAddStep(values);

  action.then(() => {
    message.success('保存成功');
    stepModalVisible.value = false;
    loadPipeline();
  });
}

function handleDeleteStep(stepId: number) {
  postPipelineDeleteStep(stepId).then(() => { message.success('删除成功'); loadPipeline(); });
}
</script>

<template>
  <Page auto-content-height>
    <Card title="AI 管线配置" style="margin-bottom: 16px;">
      <Space style="margin-bottom: 16px;">
        <InputNumber v-model:value="groupBindingId" placeholder="群组绑定 ID" style="width: 200px;" />
        <Button type="primary" :loading="loading" @click="loadPipeline">加载配置</Button>
      </Space>

      <div v-if="pipeline">
        <Space style="margin-bottom: 16px;">
          <span>AI 分析:</span>
          <Switch :checked="pipeline.enabled" @change="(val: any) => toggleEnabled(val as boolean)" />
          <span style="margin-left: 16px;">实时分析:</span>
          <Switch :checked="pipeline.realtimeEnabled" @change="(val: any) => toggleRealtime(val as boolean)" />
        </Space>

        <div style=" display: flex; align-items: center; justify-content: space-between;margin-bottom: 12px;">
          <h4 style="margin: 0;">管线步骤</h4>
          <Button type="primary" size="small" @click="showAddStep">添加步骤</Button>
        </div>

        <Table :columns="stepColumns" :data-source="steps" :pagination="false" size="small" row-key="id">
          <template #bodyCell="{ column, record }">
            <template v-if="column.key === 'stepType'">
              <Tag color="blue">{{ stepTypeLabels[record.stepType] || record.stepType }}</Tag>
            </template>
            <template v-if="column.key === 'enabled'">
              <Tag :color="record.enabled ? 'green' : 'default'">{{ record.enabled ? '是' : '否' }}</Tag>
            </template>
            <template v-if="column.key === 'action'">
              <Space wrap>
                <Button @click="showEditStep(record)">编辑</Button>
                <Popconfirm title="确定删除？" @confirm="handleDeleteStep(record.id)">
                  <Button danger>删除</Button>
                </Popconfirm>
              </Space>
            </template>
          </template>
        </Table>
      </div>

      <div v-else-if="!loading" style="color: #999;">请输入群组绑定 ID 并加载配置</div>
    </Card>

    <Modal v-model:open="stepModalVisible" :title="stepEditId > 0 ? '编辑步骤' : '添加步骤'"
           :footer="null" :destroy-on-close="false" width="500px">
      <StepForm />
    </Modal>
  </Page>
</template>

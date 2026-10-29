<script lang="ts" setup>
import { ref, watch } from 'vue';
import { Modal, Select, Input, Tag, Divider, message } from 'ant-design-vue';
import { getList as getProjectList } from '../../project/api';
import { getBatchList, getGroupList, postBatchCreate, postBatchImport } from '../api';

const props = defineProps<{
  open: boolean;
  messageIds: number[];
  projectId?: number;
  groupBindingId?: number;
}>();

const emit = defineEmits<{
  (e: 'update:open', value: boolean): void;
  (e: 'success'): void;
}>();

const projects = ref<any[]>([]);
const batches = ref<any[]>([]);
const selectedProjectId = ref<number>();
const selectedBatchId = ref<number | 'new'>('new');
const newBatchTitle = ref('');
const loading = ref(false);

watch(() => props.open, async (val) => {
  if (!val) return;
  selectedBatchId.value = 'new';

  const projRes = await getProjectList({ pageSize: 200 });
  projects.value = projRes?.items || [];

  // 自动选项目：有传入就用传入的，没有就选第一个
  selectedProjectId.value = props.projectId || projects.value[0]?.id;

  // 生成默认批次名：项目名-群组名-时间
  await generateBatchTitle();

  if (selectedProjectId.value) await loadBatches();
});

async function generateBatchTitle() {
  const projectName = projects.value.find((p: any) => p.id === selectedProjectId.value)?.name || '';
  let groupName = '';
  if (props.groupBindingId) {
    const groupRes: any = await getGroupList({ pageSize: 200 });
    const group = (groupRes?.items || []).find((g: any) => g.id === props.groupBindingId);
    groupName = group?.groupName || '';
  }
  const time = new Date().toLocaleString('zh-CN', { month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit' });
  const parts = [projectName, groupName, time].filter(Boolean);
  newBatchTitle.value = parts.join('-');
}

watch(selectedProjectId, () => {
  if (selectedProjectId.value) {
    loadBatches();
    generateBatchTitle();
  }
});

async function loadBatches() {
  if (!selectedProjectId.value) return;
  const res: any = await getBatchList({ projectId: selectedProjectId.value, pageSize: 100 });
  batches.value = res?.items || [];
}

async function handleSubmit() {
  if (!selectedProjectId.value) { message.error('请选择项目'); return; }
  if (props.messageIds.length === 0) { message.error('没有选中的消息'); return; }

  loading.value = true;
  try {
    let batchId: number;

    if (selectedBatchId.value === 'new') {
      if (!newBatchTitle.value.trim()) { message.error('请输入批次名称'); loading.value = false; return; }
      const res: any = await postBatchCreate(
        selectedProjectId.value,
        props.groupBindingId || null,
        newBatchTitle.value.trim(),
      );
      batchId = res?.id;
    } else {
      batchId = selectedBatchId.value as number;
    }

    // 导入消息
    const ids = props.messageIds.join(',');
    const importRes: any = await postBatchImport(batchId, ids);
    message.success(importRes?.message || '导入成功');

    emit('update:open', false);
    emit('success');
  } finally {
    loading.value = false;
  }
}
</script>

<template>
  <Modal :open="open" :title="selectedBatchId === 'new' ? '新建批次并导入消息' : '导入消息到批次'" :confirm-loading="loading"
         @update:open="emit('update:open', $event)" @ok="handleSubmit" width="480px" ok-text="导入">
    <div style="display: grid; gap: 16px; padding: 12px 0;">
      <!-- 待导入消息 -->
      <div>
        <div style="margin-bottom: 6px; font-weight: 500;">待导入消息</div>
        <div style=" padding: 8px 12px; font-size: 13px;background: var(--ant-color-fill-tertiary); border-radius: 6px;">
          共 <b>{{ messageIds.length }}</b> 条
          <span style=" margin-left: 8px;color: var(--ant-color-text-quaternary);">
            {{ messageIds.slice(0, 10).map(id => `#${id}`).join(' ') }}{{ messageIds.length > 10 ? ' ...' : '' }}
          </span>
        </div>
      </div>

      <!-- 选择项目 -->
      <div>
        <div style="margin-bottom: 6px; font-weight: 500;">项目</div>
        <Select v-model:value="selectedProjectId" placeholder="选择项目" style="width: 100%"
                show-search option-filter-prop="label">
          <Select.Option v-for="p in projects" :key="p.id" :value="p.id" :label="p.name">{{ p.name }}</Select.Option>
        </Select>
      </div>

      <!-- 选择批次 -->
      <div>
        <div style="margin-bottom: 6px; font-weight: 500;">批次</div>
        <Select v-model:value="selectedBatchId" placeholder="选择批次" style="width: 100%">
          <Select.Option value="new">
            <span style="color: var(--ant-color-primary);">+ 新建批次</span>
          </Select.Option>
          <Divider v-if="batches.length > 0" style="margin: 4px 0;" />
          <Select.Option v-for="b in batches" :key="b.id" :value="b.id">
            {{ b.title }}
            <Tag size="small" style="margin-left: 6px;">{{ b.messageCount }} 消息</Tag>
            <Tag size="small">{{ b.requirementCount }} 需求</Tag>
            <Tag v-if="b.status !== 'Pending'" size="small" :color="b.status === 'Completed' ? 'green' : b.status === 'Running' ? 'blue' : 'default'">{{ b.status }}</Tag>
          </Select.Option>
        </Select>
      </div>

      <!-- 新建批次名称 -->
      <div v-if="selectedBatchId === 'new'">
        <div style="margin-bottom: 6px; font-weight: 500;">批次名称</div>
        <Input v-model:value="newBatchTitle" placeholder="输入批次名称" />
      </div>
    </div>
  </Modal>
</template>

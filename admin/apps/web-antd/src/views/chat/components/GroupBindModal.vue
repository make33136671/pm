<script lang="ts" setup>
import { ref, watch } from 'vue';
import { Modal, Select, Input, message } from 'ant-design-vue';
import { getAccountList, postGroupAdd } from '../api';
import { getList as getProjectList } from '../../project/api';

const props = defineProps<{
  open: boolean;
  projectId?: number; // 预设项目 ID（从项目详情页打开时传入）
}>();

const emit = defineEmits<{
  (e: 'update:open', value: boolean): void;
  (e: 'success'): void;
}>();

const accounts = ref<any[]>([]);
const projects = ref<any[]>([]);
const form = ref({ accountId: null as number | null, projectId: null as number | null, groupName: '' });
const loading = ref(false);

watch(() => props.open, async (val) => {
  if (!val) return;
  form.value = { accountId: null, projectId: props.projectId || null, groupName: '' };

  const [accRes, projRes] = await Promise.all([
    getAccountList({ pageSize: 100 }),
    getProjectList({ pageSize: 200 }),
  ]);
  accounts.value = (accRes?.items || []).filter((a: any) => a.accountType === 'Bot');
  projects.value = projRes?.items || [];

  if (accounts.value.length > 0 && !form.value.accountId) {
    form.value.accountId = accounts.value[0].id;
  }
});

async function handleOk() {
  if (!form.value.accountId) { message.error('请选择 Bot 账号'); return; }
  if (!form.value.projectId) { message.error('请选择项目'); return; }

  loading.value = true;
  try {
    const res: any = await postGroupAdd(form.value);
    message.success(`绑定已创建! 绑定码: ${res?.bindCode || ''}`);
    emit('update:open', false);
    emit('success');
  } finally {
    loading.value = false;
  }
}
</script>

<template>
  <Modal :open="open" title="新增群组绑定" :confirm-loading="loading"
         @update:open="emit('update:open', $event)" @ok="handleOk" width="450px">
    <div style="display: grid; gap: 12px; padding: 12px 0;">
      <div>
        <div style="margin-bottom: 4px; font-weight: 500;">Bot 账号</div>
        <Select v-model:value="form.accountId" placeholder="选择 Bot 账号" style="width: 100%">
          <Select.Option v-for="a in accounts" :key="a.id" :value="a.id">{{ a.accountName }}</Select.Option>
        </Select>
      </div>
      <div>
        <div style="margin-bottom: 4px; font-weight: 500;">关联项目</div>
        <Select v-model:value="form.projectId" placeholder="选择项目" style="width: 100%"
                show-search option-filter-prop="label">
          <Select.Option v-for="p in projects" :key="p.id" :value="p.id" :label="p.name">{{ p.name }}</Select.Option>
        </Select>
      </div>
      <div>
        <div style="margin-bottom: 4px;">群组名称（可选）</div>
        <Input v-model:value="form.groupName" placeholder="绑定后会自动获取" />
      </div>
    </div>
  </Modal>
</template>

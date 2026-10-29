<script lang="ts" setup>
import { ref, watch, computed } from 'vue';
import { Modal, Select, Space, Button, message } from 'ant-design-vue';
import { useUserStore } from '@vben/stores';
import { postGenerateVerifyCode } from '../api';
import { getList as getProjectList } from '../../project/api';

const props = defineProps<{
  open: boolean;
  projectId?: number;
}>();

const emit = defineEmits<{
  (e: 'update:open', value: boolean): void;
  (e: 'success'): void;
}>();

const projects = ref<any[]>([]);
const adminList = ref<any[]>([]);
const bindMode = ref<'self' | 'other'>('self');
const bindProjectId = ref<number>();
const bindAdminId = ref<number>();
const verifyResult = ref<any>(null);
const isAdmin = ref(false);

const userStore = useUserStore();
const currentUser = computed(() => userStore.userInfo);

watch(() => props.open, async (val) => {
  if (!val) return;
  bindMode.value = 'self';
  bindAdminId.value = undefined;
  bindProjectId.value = props.projectId;
  verifyResult.value = null;

  const projRes = await getProjectList({ pageSize: 200 });
  projects.value = projRes?.items || [];

  // 判断是否管理员
  const user = currentUser.value as any;
  isAdmin.value = user?.roleName === '超级管理员' || user?.roles?.includes('admin');

  if (isAdmin.value) {
    try {
      const mod = await import('../../admin/api');
      const res: any = await mod.getParentList();
      adminList.value = res?.items || res || [];
    } catch { adminList.value = []; }
  }
});

async function handleSubmit() {
  if (!bindProjectId.value) { message.error('请选择项目'); return; }

  let targetAdminId: number;
  if (bindMode.value === 'self') {
    const user = currentUser.value as any;
    targetAdminId = user?.userId || user?.id;
    if (!targetAdminId) { message.error('无法获取当前用户ID'); return; }
  } else {
    if (!bindAdminId.value) { message.error('请选择用户'); return; }
    targetAdminId = bindAdminId.value;
  }

  const res: any = await postGenerateVerifyCode(targetAdminId, bindProjectId.value);
  verifyResult.value = res;
}

function handleClose() {
  verifyResult.value = null;
  emit('update:open', false);
  emit('success');
}
</script>

<template>
  <Modal :open="open" title="绑定 Telegram 账号" :footer="null" width="480px"
         @update:open="emit('update:open', $event)">
    <!-- 表单 -->
    <div v-if="!verifyResult" style="padding: 12px 0;">
      <div style="margin-bottom: 16px;">
        <div style="margin-bottom: 8px; font-weight: 500;">关联项目</div>
        <Select v-model:value="bindProjectId" placeholder="选择项目" style="width: 100%"
                show-search option-filter-prop="label">
          <Select.Option v-for="p in projects" :key="p.id" :value="p.id" :label="p.name">{{ p.name }}</Select.Option>
        </Select>
      </div>

      <div style="margin-bottom: 16px;">
        <div style="margin-bottom: 8px; font-weight: 500;">绑定对象</div>
        <Space>
          <Button :type="bindMode === 'self' ? 'primary' : 'default'" @click="bindMode = 'self'">绑定自己</Button>
          <Button v-if="isAdmin" :type="bindMode === 'other' ? 'primary' : 'default'" @click="bindMode = 'other'">绑定其他用户</Button>
        </Space>
      </div>

      <div v-if="bindMode === 'other'" style="margin-bottom: 16px;">
        <div style="margin-bottom: 8px;">选择用户</div>
        <Select v-model:value="bindAdminId" placeholder="请选择要绑定的用户" style="width: 100%;"
                show-search option-filter-prop="label">
          <Select.Option v-for="admin in adminList" :key="admin.id" :value="admin.id" :label="admin.name">
            {{ admin.name }} ({{ admin.username || admin.account }})
          </Select.Option>
        </Select>
      </div>

      <Button type="primary" block @click="handleSubmit">生成验证码</Button>
    </div>

    <!-- 验证码结果 -->
    <div v-else style="padding: 20px 0; text-align: center;">
      <div style=" margin-bottom: 16px;font-size: 48px; font-weight: bold; color: #1890ff; letter-spacing: 8px;">
        {{ verifyResult.code }}
      </div>
      <div style=" margin-bottom: 20px;color: var(--ant-color-text-secondary);">
        请在 Telegram 中私聊 Bot，发送以下命令完成绑定：
      </div>
      <div style=" padding: 12px; margin-bottom: 16px; font-family: monospace; font-size: 16px;background: var(--ant-color-fill-tertiary); border-radius: 6px;">
        /verify {{ verifyResult.code }}
      </div>
      <div style=" font-size: 12px;color: var(--ant-color-text-quaternary);">验证码 5 分钟内有效</div>
      <div style="margin-top: 16px;">
        <Space>
          <Button @click="verifyResult = null">重新生成</Button>
          <Button type="primary" @click="handleClose">完成</Button>
        </Space>
      </div>
    </div>
  </Modal>
</template>

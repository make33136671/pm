<script lang="ts" setup>
import { ref } from 'vue';

import { Page } from '@vben/common-ui';

import { Select, Space } from 'ant-design-vue';

import { getGroupList } from '../api';
import ChatWindow from '../components/ChatWindow.vue';

interface SourceOption {
  value: number;
  label: string;
  type: 'all' | 'private' | 'group';
}

const sources = ref<SourceOption[]>([]);
const selectedSource = ref<number | undefined>();
const chatRef = ref<InstanceType<typeof ChatWindow>>();

// 计算传给 ChatWindow 的 groupBindingId
function getGroupBindingId(): number | undefined {
  if (selectedSource.value === undefined) return undefined; // 全部
  if (selectedSource.value === 0) return 0; // 私聊（后端 0 = GroupBindingId is null）
  return selectedSource.value; // 群组 ID
}

async function loadSources() {
  const res: any = await getGroupList({ pageSize: 200 });
  const groups = res?.items || [];

  const list: SourceOption[] = [
    { value: 0, label: '私聊消息', type: 'private' },
  ];
  for (const g of groups) {
    list.push({ value: g.id, label: g.groupName || `群组#${g.id}`, type: 'group' });
  }
  sources.value = list;
}

function onSourceChange() {
  chatRef.value?.refresh();
}

loadSources();
</script>

<template>
  <Page auto-content-height>
    <div style="display: flex; flex-direction: column; height: 100%;">
      <!-- 筛选栏 -->
      <Space style="margin-bottom: 12px;">
        <Select v-model:value="selectedSource" placeholder="全部消息" allow-clear
                style="width: 280px;" show-search option-filter-prop="label"
                @change="onSourceChange">
          <Select.Option v-for="s in sources" :key="s.value" :value="s.value" :label="s.label">
            <span v-if="s.type === 'private'" style="color: #722ed1;">{{ s.label }}</span>
            <span v-else>{{ s.label }}</span>
          </Select.Option>
        </Select>
      </Space>

      <!-- 聊天窗口 -->
      <ChatWindow ref="chatRef" :group-binding-id="getGroupBindingId()" height="calc(100vh - 200px)" />
    </div>
  </Page>
</template>

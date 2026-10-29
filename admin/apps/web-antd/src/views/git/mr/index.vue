<script lang="ts" setup>
import { ref, onMounted } from 'vue';
import { Page } from '@vben/common-ui';
import { Table, Tag, Button, Space, InputNumber, Select, message, Card } from 'ant-design-vue';
import { api } from '#/api/request';
import { postAiCodeReview } from '#/views/task-item/api';

// GitLab 项目选择
const gitlabProjectId = ref<number | null>(null);
const mrState = ref<string>('opened');
const loading = ref(false);
const aiReviewLoading = ref<Record<number, boolean>>({});
const mrList = ref<any[]>([]);

const stateOptions = [
  { label: '开启中', value: 'opened' },
  { label: '已合并', value: 'merged' },
  { label: '已关闭', value: 'closed' },
  { label: '全部', value: 'all' },
];

const loadMrs = async () => {
  if (!gitlabProjectId.value) { message.warning('请输入 GitLab Project ID'); return; }
  loading.value = true;
  try {
    const res: any = await api.get('/GitLab/MergeRequests', {
      params: { projectId: gitlabProjectId.value, state: mrState.value, perPage: 50 },
    });
    mrList.value = Array.isArray(res) ? res : [];
  } finally {
    loading.value = false;
  }
};

const handleAiReview = async (mr: any) => {
  if (!gitlabProjectId.value) return;
  aiReviewLoading.value[mr.iid] = true;
  try {
    await postAiCodeReview({ gitlabProjectId: gitlabProjectId.value, mrIid: mr.iid });
    message.success(`MR !${mr.iid} AI Review 完成，结果已写入 MR 评论`);
  } finally {
    aiReviewLoading.value[mr.iid] = false;
  }
};

const stateColorMap: Record<string, string> = {
  opened: 'processing',
  merged: 'success',
  closed: 'default',
};

const columns = [
  { title: '编号', dataIndex: 'iid', width: 70, customRender: ({ text }: any) => `!${text}` },
  { title: '标题', dataIndex: 'title', ellipsis: true },
  { title: '状态', dataIndex: 'state', width: 90, slots: { customRender: 'state' } },
  { title: '源分支', dataIndex: 'sourceBranch', width: 150, ellipsis: true },
  { title: '目标分支', dataIndex: 'targetBranch', width: 100 },
  { title: '作者', dataIndex: 'author', width: 100 },
  { title: '创建时间', dataIndex: 'createdAt', width: 160, ellipsis: true },
  { title: '操作', key: 'action', width: 130, slots: { customRender: 'action' } },
];
</script>

<template>
  <Page auto-content-height>
    <Card title="Merge Request 列表" style="margin-bottom: 16px;">
      <Space>
        <span>GitLab Project ID：</span>
        <InputNumber v-model:value="gitlabProjectId" placeholder="Project ID" :min="1" style="width: 140px;" />
        <Select v-model:value="mrState" :options="stateOptions" style="width: 120px;" />
        <Button type="primary" :loading="loading" @click="loadMrs">加载 MR 列表</Button>
      </Space>
    </Card>

    <Table
      :columns="columns"
      :data-source="mrList"
      :loading="loading"
      row-key="iid"
      :pagination="{ pageSize: 20 }"
      size="small"
    >
      <template #bodyCell="{ column, record }">
        <template v-if="column.dataIndex === 'state'">
          <Tag :color="stateColorMap[record.state] || 'default'">{{ record.state }}</Tag>
        </template>
        <template v-else-if="column.key === 'action'">
          <Space wrap>
            <Button
              type="primary"
              :loading="aiReviewLoading[record.iid]"
              @click="handleAiReview(record)"
            >
              🤖 AI Review
            </Button>
            <Button :href="record.webUrl" target="_blank">查看</Button>
          </Space>
        </template>
      </template>
    </Table>
  </Page>
</template>

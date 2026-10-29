<script lang="ts" setup>
import { onMounted, ref, nextTick } from 'vue';
import { useRoute } from 'vue-router';

import { Page } from '@vben/common-ui';

import {
  Button,
  Card,
  Checkbox,
  Descriptions,
  DescriptionsItem,
  Empty,
  Input,
  message,
  Modal,
  Popconfirm,
  Progress,
  Select,
  Space,
  Spin,
  Table,
  Tag,
  Tree,
  Tooltip,
} from 'ant-design-vue';

import { getTree, getDetail, postAdd, postEdit, postDel, postSyncMap, postGenerateRequirements, postConfirmBatch, postDiscardBatch } from './api';
import { getAvailable as getAiProviderAvailable } from '../ai-provider/api';
import { getList as getProjectList } from '../project/api';

import {
  requirementStatusLabels,
  requirementTypeLabels,
} from '#/utils/enum-labels';

const route = useRoute();

// ========== 项目选择 ==========

const projects = ref<any[]>([]);
const selectedProjectId = ref<number | undefined>();

async function loadProjects() {
  const result: any = await getProjectList({ page: 1, pageSize: 200 });
  projects.value = (result?.items || []).map((p: any) => ({ label: p.name, value: p.id }));

  // 从路由参数获取默认项目
  const qid = Number(route.query.projectId);
  if (qid) {
    selectedProjectId.value = qid;
    loadTree();
  }
}

function handleProjectChange(val: number) {
  selectedProjectId.value = val;
  selectedNodeId.value = undefined;
  nodeDetail.value = null;
  if (val) loadTree();
  else treeData.value = [];
}

// ========== 树 ==========

const treeData = ref<any[]>([]);
const treeLoading = ref(false);
const selectedNodeId = ref<number | undefined>();
const expandedKeys = ref<number[]>([]);

const statusColorMap: Record<string, string> = {
  '规划中': 'default', '开发中': 'blue', '已上线': 'green', '部分上线': 'orange',
};

async function loadTree() {
  if (!selectedProjectId.value) return;
  treeLoading.value = true;
  try {
    const result: any = await getTree(selectedProjectId.value);
    treeData.value = convertToAntTree(Array.isArray(result) ? result : []);
    // 默认展开前两级
    expandedKeys.value = treeData.value.map((n: any) => n.key);
  } finally {
    treeLoading.value = false;
  }
}

function convertToAntTree(nodes: any[]): any[] {
  return nodes.map((n) => ({
    key: n.id,
    title: n.name,
    status: n.status,
    requirementCount: n.requirementCount,
    description: n.description,
    children: n.children?.length > 0 ? convertToAntTree(n.children) : undefined,
    raw: n,
  }));
}

function handleTreeSelect(keys: number[]) {
  if (keys.length > 0) {
    selectedNodeId.value = keys[0];
    loadNodeDetail(keys[0]!);
  }
}

// ========== 节点详情 ==========

const nodeDetail = ref<any>(null);
const detailLoading = ref(false);

const reqStatusColorMap: Record<string, string> = {
  Draft: 'default', Todo: 'blue', InProgress: 'processing', Testing: 'orange', Done: 'success', Closed: 'default',
};

async function loadNodeDetail(id: number) {
  detailLoading.value = true;
  try {
    nodeDetail.value = await getDetail(id);
  } finally {
    detailLoading.value = false;
  }
}

// ========== 新增/编辑 ==========

// 功能模块手动状态选项（留空=自动派生）
const featureStatusOptions = [
  { label: '规划中', value: 'Planning' },
  { label: '开发中', value: 'InProgress' },
  { label: '已上线', value: 'Done' },
  { label: '已废弃', value: 'Deprecated' },
];

const formVisible = ref(false);
const formTitle = ref('');
const formData = ref<any>({});
const formSaving = ref(false);

function handleAddRoot() {
  if (!selectedProjectId.value) return message.warning('请先选择项目');
  formTitle.value = '新增根模块';
  formData.value = { projectId: selectedProjectId.value, parentId: null, name: '', description: '' };
  formVisible.value = true;
}

function handleAddChild(parentId: number) {
  formTitle.value = '新增子模块';
  formData.value = { projectId: selectedProjectId.value, parentId, name: '', description: '' };
  formVisible.value = true;
}

function handleEditNode(node: any) {
  formTitle.value = '编辑模块';
  formData.value = {
    id: node.id, projectId: node.projectId, parentId: node.parentId,
    name: node.name, description: node.description,
    status: node.statusManual ?? undefined,  // 回填手动状态（null=自动派生，显示空）
  };
  formVisible.value = true;
}

async function handleFormSubmit() {
  if (!formData.value.name?.trim()) return message.warning('请输入模块名称');
  formSaving.value = true;
  try {
    if (formData.value.id) {
      await postEdit(formData.value);
    } else {
      await postAdd(formData.value);
    }
    formVisible.value = false;
    await loadTree();
    if (selectedNodeId.value) loadNodeDetail(selectedNodeId.value);
  } finally {
    formSaving.value = false;
  }
}

async function handleDeleteNode(id: number) {
  await postDel(id);
  selectedNodeId.value = undefined;
  nodeDetail.value = null;
  await loadTree();
}

// ========== 同步导图 ==========

const syncing = ref(false);
async function handleSyncMap() {
  if (!selectedProjectId.value) return;
  syncing.value = true;
  try {
    await postSyncMap(selectedProjectId.value);
    message.success('功能地图同步成功');
  } finally {
    syncing.value = false;
  }
}

// ========== Checkbox 选择 ==========

const checkedKeys = ref<number[]>([]);

// ========== AI 生成需求 ==========

const aiGenVisible = ref(false);
const aiProviders = ref<any[]>([]);
const selectedAiProviderId = ref<number | undefined>();
const aiFriendly = ref(false);
const aiGenerating = ref(false);
const aiProgress = ref({ current: 0, total: 0, moduleName: '' });
const aiStreamText = ref('');
const aiBatchId = ref('');
const aiGenResult = ref<any[]>([]);
const aiGenStep = ref<'config' | 'generating' | 'preview'>('config');

function handleOpenAiGen() {
  if (checkedKeys.value.length === 0) return message.warning('请先勾选功能模块');
  aiGenStep.value = 'config';
  aiStreamText.value = '';
  aiGenResult.value = [];
  aiGenVisible.value = true;
  getAiProviderAvailable().then((res: any) => {
    aiProviders.value = res || [];
    const def = aiProviders.value.find((p: any) => p.isDefault);
    if (def) selectedAiProviderId.value = def.id;
  });
}

async function handleStartGenerate() {
  if (!selectedAiProviderId.value) return message.warning('请选择 AI 提供商');
  const provider = aiProviders.value.find((p: any) => p.id === selectedAiProviderId.value);

  if (provider?.supportStream) {
    // SSE 流式模式
    aiGenStep.value = 'generating';
    aiGenerating.value = true;
    aiProgress.value = { current: 0, total: checkedKeys.value.length, moduleName: '' };
    aiStreamText.value = '';

    const params = new URLSearchParams({
      featureModuleIds: checkedKeys.value.join(','),
      aiProviderId: String(selectedAiProviderId.value),
      aiFriendly: String(aiFriendly.value),
    });
    const baseUrl = (import.meta as any).env.VITE_GLOB_API_URL || '';
    const token = localStorage.getItem('accessToken') || '';
    const eventSource = new EventSource(`${baseUrl}/admin/FeatureModule/StreamGenerateRequirements?${params}&token=${token}`);

    eventSource.addEventListener('progress', (e: any) => {
      const data = JSON.parse(e.data);
      aiProgress.value = { current: data.current, total: data.total, moduleName: data.moduleName };
    });

    eventSource.addEventListener('chunk', (e: any) => {
      const data = JSON.parse(e.data);
      aiStreamText.value += data.text;
    });

    eventSource.addEventListener('requirement', (e: any) => {
      const data = JSON.parse(e.data);
      aiGenResult.value.push(data);
    });

    eventSource.addEventListener('done', (e: any) => {
      const data = JSON.parse(e.data);
      aiBatchId.value = data.batchId;
      aiGenerating.value = false;
      aiGenStep.value = 'preview';
      eventSource.close();
    });

    eventSource.addEventListener('error', () => {
      aiGenerating.value = false;
      eventSource.close();
    });
  } else {
    // 非流式模式
    aiGenStep.value = 'generating';
    aiGenerating.value = true;
    try {
      const res: any = await postGenerateRequirements({
        featureModuleIds: checkedKeys.value,
        aiProviderId: selectedAiProviderId.value,
        aiFriendly: aiFriendly.value,
      });
      aiBatchId.value = res.batchId;
      aiGenResult.value = res.requirements || [];
      aiGenStep.value = 'preview';
    } catch (err: any) {
      message.error(err.message || '生成失败');
      aiGenStep.value = 'config';
    } finally {
      aiGenerating.value = false;
    }
  }
}

async function handleConfirmBatch() {
  if (!aiBatchId.value) return;
  await postConfirmBatch(aiBatchId.value);
  message.success('需求已确认');
  aiGenVisible.value = false;
  checkedKeys.value = [];
  checkable.value = false;
  loadTree();
}

async function handleDiscardBatch() {
  if (!aiBatchId.value) return;
  await postDiscardBatch(aiBatchId.value);
  message.info('已丢弃');
  aiGenVisible.value = false;
}

// ========== 初始化 ==========

onMounted(() => loadProjects());
</script>

<template>
  <Page auto-content-height>
    <div class="fm-container">
      <!-- 左侧：树 -->
      <div class="fm-tree-panel">
        <div class="fm-toolbar">
          <Select
            v-model:value="selectedProjectId"
            show-search
            :filter-option="(input: string, option: any) => option.label?.toLowerCase().includes(input.toLowerCase())"
            placeholder="选择项目"
            :options="projects"
            style="flex: 1"
            @change="handleProjectChange"
          />
        </div>

        <div class="fm-tree-actions">
          <Button size="small" type="primary" @click="handleAddRoot" :disabled="!selectedProjectId">新增根模块</Button>
          <Button size="small" :loading="syncing" @click="handleSyncMap" :disabled="!selectedProjectId">同步导图</Button>
          <Button size="small" type="primary" @click="handleOpenAiGen" :disabled="checkedKeys.length === 0">
            AI 生成需求{{ checkedKeys.length > 0 ? ` (${checkedKeys.length})` : '' }}
          </Button>
        </div>

        <Spin :spinning="treeLoading">
          <Tree
            v-if="treeData.length > 0"
            :tree-data="treeData"
            :expanded-keys="expandedKeys"
            :selected-keys="selectedNodeId ? [selectedNodeId] : []"
            checkable
            v-model:checked-keys="checkedKeys"
            @expand="(keys: number[]) => expandedKeys = keys"
            @select="handleTreeSelect"
            block-node
          >
            <template #title="{ title, status, requirementCount }">
              <div class="tree-node-title">
                <span>{{ title }}</span>
                <span class="tree-node-meta">
                  <Tag :color="statusColorMap[status] || 'default'" size="small" style=" margin: 0;font-size: 10px">{{ status }}</Tag>
                  <span v-if="requirementCount" class="req-count">{{ requirementCount }}</span>
                </span>
              </div>
            </template>
          </Tree>
          <Empty v-else-if="!treeLoading && selectedProjectId" description="暂无功能模块" style="padding: 40px 0" />
          <Empty v-else-if="!treeLoading" description="请先选择项目" style="padding: 40px 0" />
        </Spin>
      </div>

      <!-- 右侧：详情 -->
      <div class="fm-detail-panel">
        <Spin :spinning="detailLoading">
          <template v-if="nodeDetail">
            <Card size="small">
              <template #title>
                <div style="display: flex; align-items: center; justify-content: space-between">
                  <div style="display: flex; gap: 8px; align-items: center">
                    <span style="font-size: 16px; font-weight: 600">{{ nodeDetail.name }}</span>
                    <Tag :color="statusColorMap[nodeDetail.status] || 'default'">{{ nodeDetail.status }}</Tag>
                  </div>
                  <Space>
                    <Button size="small" @click="handleAddChild(nodeDetail.id)">新增子模块</Button>
                    <Button size="small" @click="handleEditNode(nodeDetail)">编辑</Button>
                    <Popconfirm title="确定删除？子节点也会一并删除" @confirm="handleDeleteNode(nodeDetail.id)">
                      <Button size="small" danger>删除</Button>
                    </Popconfirm>
                  </Space>
                </div>
              </template>

              <div v-if="nodeDetail.description" style=" margin-bottom: 16px;color: var(--ant-color-text-secondary)">
                {{ nodeDetail.description }}
              </div>

              <Descriptions :column="3" size="small" bordered>
                <DescriptionsItem label="状态">
                  <Tag :color="statusColorMap[nodeDetail.status] || 'default'">{{ nodeDetail.status }}</Tag>
                </DescriptionsItem>
                <DescriptionsItem label="关联需求数">{{ nodeDetail.requirementCount }}</DescriptionsItem>
                <DescriptionsItem label="创建时间">{{ new Date(nodeDetail.createdTime).toLocaleDateString('zh-CN') }}</DescriptionsItem>
              </Descriptions>
            </Card>

            <!-- 关联需求列表 -->
            <Card size="small" title="关联需求" style="margin-top: 12px">
              <Table
                v-if="nodeDetail.requirements?.length > 0"
                :data-source="nodeDetail.requirements"
                :pagination="false"
                row-key="id"
                size="small"
              >
                <Table.Column title="标题" data-index="title" key="title" ellipsis />
                <Table.Column title="类型" key="type" width="80">
                  <template #default="{ record }">
                    <Tag>{{ requirementTypeLabels[record.type] || record.type }}</Tag>
                  </template>
                </Table.Column>
                <Table.Column title="状态" key="status" width="90">
                  <template #default="{ record }">
                    <Tag :color="reqStatusColorMap[record.status] || 'default'">{{ requirementStatusLabels[record.status] || record.status }}</Tag>
                  </template>
                </Table.Column>
                <Table.Column title="负责人" data-index="assignee" key="assignee" width="90" />
              </Table>
              <Empty v-else description="暂无关联需求" />
            </Card>
          </template>

          <div v-else class="fm-empty-detail">
            <Empty description="选择左侧节点查看详情" />
          </div>
        </Spin>
      </div>
    </div>

    <!-- 新增/编辑弹窗 -->
    <Modal v-model:open="formVisible" :title="formTitle" :confirm-loading="formSaving" @ok="handleFormSubmit" width="500px">
      <div style=" display: flex; flex-direction: column; gap: 16px;padding: 16px 0">
        <div>
          <div style="margin-bottom: 4px; font-weight: 500">名称 <span style="color: red">*</span></div>
          <Input v-model:value="formData.name" placeholder="功能模块名称" />
        </div>
        <div>
          <div style="margin-bottom: 4px; font-weight: 500">描述</div>
          <Input.TextArea v-model:value="formData.description" :rows="3" placeholder="简要描述该模块的功能" />
        </div>
        <div>
          <div style="margin-bottom: 4px; font-weight: 500">实现状态</div>
          <Select v-model:value="formData.status" :options="featureStatusOptions" allow-clear
            placeholder="留空 = 按关联需求自动派生" style="width: 100%" />
          <div style="margin-top: 4px; font-size: 12px; color: var(--ant-color-text-tertiary);">
            留空时状态由关联需求自动计算；手动选择则锁定为该状态
          </div>
        </div>
      </div>
    </Modal>

    <!-- AI 生成需求弹窗 -->
    <Modal v-model:open="aiGenVisible" title="AI 生成需求" :footer="null" width="700px" :maskClosable="false">
      <!-- 步骤1: 配置 -->
      <div v-if="aiGenStep === 'config'" style=" display: flex; flex-direction: column; gap: 16px;padding: 16px 0;">
        <div>
          <div style="margin-bottom: 4px; font-weight: 500">已选功能模块</div>
          <div style="color: #666;">{{ checkedKeys.length }} 个模块</div>
        </div>
        <div>
          <div style="margin-bottom: 4px; font-weight: 500">AI 提供商</div>
          <Select v-model:value="selectedAiProviderId" placeholder="请选择 AI 提供商" style="width: 100%">
            <Select.Option v-for="p in aiProviders" :key="p.id" :value="p.id">
              {{ p.name }} ({{ p.providerType }}) {{ p.supportStream ? '- 流式' : '' }}
            </Select.Option>
          </Select>
        </div>
        <div>
          <Checkbox v-model:checked="aiFriendly">生成 AI 友好型需求</Checkbox>
          <div style=" margin-top: 4px;font-size: 12px; color: #999;">AI 友好型需求描述更结构化，便于 AI Agent 直接理解和执行</div>
        </div>
        <Button type="primary" @click="handleStartGenerate" :disabled="!selectedAiProviderId">开始生成</Button>
      </div>

      <!-- 步骤2: 生成中 -->
      <div v-if="aiGenStep === 'generating'" style="padding: 16px 0;">
        <div style="margin-bottom: 12px;">
          <Progress :percent="aiProgress.total > 0 ? Math.round(aiProgress.current / aiProgress.total * 100) : 0" />
          <div style="margin-top: 4px; color: #666;">
            正在分析: {{ aiProgress.moduleName }} ({{ aiProgress.current }}/{{ aiProgress.total }})
          </div>
        </div>
        <div v-if="aiStreamText" style="max-height: 300px; padding: 12px; overflow-y: auto; font-family: monospace; font-size: 13px; white-space: pre-wrap; background: #f5f5f5; border-radius: 6px;">
          {{ aiStreamText }}
        </div>
        <Spin v-else tip="AI 正在分析中..." style="display: block; padding: 40px 0; text-align: center;" />
      </div>

      <!-- 步骤3: 预览确认 -->
      <div v-if="aiGenStep === 'preview'" style="padding: 16px 0;">
        <div style="margin-bottom: 12px; font-weight: 500;">生成了 {{ aiGenResult.length }} 条需求，请确认：</div>
        <div style="max-height: 400px; overflow-y: auto;">
          <Card v-for="(req, index) in aiGenResult" :key="index" size="small" style="margin-bottom: 8px;">
            <div style="font-weight: 500;">{{ req.title }}</div>
            <div v-if="req.content" style="margin-top: 4px; font-size: 13px; color: #666; white-space: pre-wrap;">{{ req.content?.substring(0, 200) }}{{ req.content?.length > 200 ? '...' : '' }}</div>
            <div style="margin-top: 4px;">
              <Tag color="blue">{{ req.type }}</Tag>
            </div>
          </Card>
        </div>
        <div style=" display: flex; gap: 8px; justify-content: flex-end;margin-top: 16px;">
          <Button @click="handleDiscardBatch" danger>丢弃全部</Button>
          <Button type="primary" @click="handleConfirmBatch">全部确认</Button>
        </div>
      </div>
    </Modal>
  </Page>
</template>

<style scoped>
.fm-container {
  display: flex;
  gap: 0;
  height: calc(100vh - 120px);
}

.fm-tree-panel {
  display: flex;
  flex-shrink: 0;
  flex-direction: column;
  width: 360px;
  overflow: hidden;
  border-right: 1px solid var(--ant-color-border);
}

.fm-toolbar {
  padding: 12px;
  border-bottom: 1px solid var(--ant-color-border-secondary);
}

.fm-tree-actions {
  display: flex;
  gap: 8px;
  padding: 8px 12px;
  border-bottom: 1px solid var(--ant-color-border-secondary);
}

.fm-tree-panel :deep(.ant-tree) {
  flex: 1;
  padding: 8px;
  overflow-y: auto;
}

.fm-detail-panel {
  flex: 1;
  padding: 16px;
  overflow-y: auto;
}

.fm-empty-detail {
  display: flex;
  align-items: center;
  justify-content: center;
  height: 100%;
}

.tree-node-title {
  display: flex;
  align-items: center;
  justify-content: space-between;
  width: 100%;
  padding-right: 8px;
}

.tree-node-meta {
  display: flex;
  gap: 4px;
  align-items: center;
}

.req-count {
  padding: 0 6px;
  font-size: 11px;
  line-height: 18px;
  color: #fff;
  background: var(--ant-color-primary);
  border-radius: 10px;
}
</style>

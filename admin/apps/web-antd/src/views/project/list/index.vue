<script lang="ts" setup>
import { onMounted, onBeforeUnmount, ref, nextTick, watch } from 'vue';
import { useRouter } from 'vue-router';

import { Page } from '@vben/common-ui';
import { usePreferences } from '@vben/preferences';

import {
  Tag,
  Space,
  Button,
  Popconfirm,
  Input,
  Select,
  Progress,
  Spin,
  Pagination,
  Tooltip,
  Modal,
  Table,
  Empty,
} from 'ant-design-vue';

import {
  projectStatusLabels,
  requirementStatusLabels,
  requirementTypeLabels,
  toOptions,
} from '#/utils/enum-labels';

import { getListWithStats, postDel } from '../api';
import {
  getList as getMindMapList,
  getDetail as getMindMapDetail,
} from '../../mindmap/api';
import { getList as getRequirementList } from '../../requirement/api';
import ProjectTimeBar from '#/components/ProjectTimeBar/index.vue';

const router = useRouter();
const { isDark } = usePreferences();

// ========== 列表筛选 & 数据 ==========

const keyword = ref('');
const filterStatus = ref<string | undefined>(undefined);
const statusOptions = toOptions(projectStatusLabels);

const loading = ref(false);
const projects = ref<any[]>([]);
const total = ref(0);
const page = ref(1);
const pageSize = ref(12);

const statusColorMap: Record<string, string> = {
  Planning: 'blue',
  Active: 'green',
  OnHold: 'orange',
  Completed: 'cyan',
  Archived: 'default',
};

async function loadData() {
  loading.value = true;
  try {
    const result: any = await getListWithStats({
      keyword: keyword.value || undefined,
      status: filterStatus.value || undefined,
      page: page.value,
      pageSize: pageSize.value,
    });
    projects.value = result?.items || [];
    total.value = result?.total || 0;
  } finally {
    loading.value = false;
  }
}

function handleSearch() {
  page.value = 1;
  loadData();
}

function handlePageChange(p: number, ps: number) {
  page.value = p;
  pageSize.value = ps;
  loadData();
}

function handleAdd() {
  router.push({ path: '/project/form' });
}
function handleDetail(id: number) {
  router.push({ path: '/project/detail', query: { id } });
}
function handleEdit(id: number) {
  router.push({ path: '/project/form', query: { id } });
}
function handleGit(id: number) {
  router.push({ path: '/project/detail', query: { id, tab: 'git' } });
}
async function handleDel(id: number) {
  await postDel(id);
  loadData();
}

function formatDate(dateStr: string) {
  if (!dateStr) return '-';
  return new Date(dateStr).toLocaleDateString('zh-CN');
}
function timeAgo(dateStr: string) {
  if (!dateStr) return '';
  const diff = Date.now() - new Date(dateStr).getTime();
  const minutes = Math.floor(diff / 60000);
  if (minutes < 1) return '刚刚';
  if (minutes < 60) return `${minutes}分钟前`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours}小时前`;
  const days = Math.floor(hours / 24);
  if (days < 30) return `${days}天前`;
  return formatDate(dateStr);
}
function reqPercent(stats: any) {
  if (!stats || stats.reqTotal === 0) return 0;
  return Math.round((stats.reqDone / stats.reqTotal) * 100);
}

// ========== 思维导图弹窗 ==========

const mindMapVisible = ref(false);
const mindMapProjectName = ref('');
const mindMapList = ref<any[]>([]);
const mindMapLoading = ref(false);
const mindMapActiveId = ref<number>(0);
const mindMapContainer = ref<HTMLElement>();
let mindMapInstance: any = null;

async function openMindMapModal(projectId: number, projectName: string) {
  mindMapProjectName.value = projectName;
  mindMapList.value = [];
  mindMapActiveId.value = 0;
  mindMapVisible.value = true;
  mindMapLoading.value = true;

  try {
    const result: any = await getMindMapList({ projectId, page: 1, pageSize: 100 });
    mindMapList.value = result?.items || [];
    if (mindMapList.value.length > 0) {
      // 等待 DOM 渲染完成后再加载导图
      await nextTick();
      await nextTick();
      await loadMindMapById(mindMapList.value[0]!.id);
    }
  } finally {
    mindMapLoading.value = false;
  }
}

async function selectMindMap(id: number) {
  if (mindMapActiveId.value === id) return;
  mindMapLoading.value = true;
  try {
    await loadMindMapById(id);
  } finally {
    mindMapLoading.value = false;
  }
}

async function loadMindMapById(id: number) {
  mindMapActiveId.value = id;
  const detail: any = await getMindMapDetail(id);
  // 确保 DOM 已挂载
  await nextTick();
  // 等待 Modal 动画完成、容器有实际尺寸
  await new Promise((resolve) => setTimeout(resolve, 350));
  initMindMap(detail);
}

function convertLegacyData(data: any): any {
  if (data?.data?.text !== undefined) {
    stripNodeColors(data);
    return data;
  }
  if (data?.root) return convertNode(data.root);
  if (data?.text !== undefined) return convertNode(data);
  return { data: { text: '中心主题' }, children: [] };
}

// 递归清除节点中嵌入的颜色样式，让主题配置生效
function stripNodeColors(node: any) {
  if (node?.data) {
    delete node.data.fillColor;
    delete node.data.color;
    delete node.data.borderColor;
    delete node.data.fontWeight;
    delete node.data.fontSize;
  }
  if (Array.isArray(node?.children)) {
    node.children.forEach(stripNodeColors);
  }
}

function convertNode(node: any): any {
  const result: any = { data: { text: node.text || '' }, children: [] };
  if (node.status) result.data.tag = [node.status];
  if (Array.isArray(node.children)) {
    result.children = node.children.map(convertNode);
  }
  return result;
}

async function initMindMap(detail: any) {
  if (!mindMapContainer.value) return;
  destroyMindMap();

  let mapData: any;
  try {
    const parsed = typeof detail.mapData === 'string' ? JSON.parse(detail.mapData) : detail.mapData;
    mapData = convertLegacyData(parsed);
  } catch {
    mapData = { data: { text: detail.title || '中心主题' }, children: [] };
  }

  const dark = isDark.value;
  const themeConfig = dark
    ? {
        backgroundColor: '#141414',
        lineColor: '#5bb59a',
        lineWidth: 2,
        root: { fillColor: '#177a56', color: '#ffffff', borderColor: '#1fa06e', borderWidth: 0, fontSize: 18, fontWeight: 'bold' },
        second: { fillColor: '#262626', color: '#f0f0f0', borderColor: '#5bb59a', borderWidth: 1, fontSize: 15, marginX: 80, marginY: 50 },
        node: { fillColor: '#1f1f1f', color: '#d9d9d9', borderColor: '#434343', borderWidth: 1, fontSize: 14, marginX: 40, marginY: 22 },
      }
    : {
        backgroundColor: '#ffffff',
        lineColor: '#409e85',
        lineWidth: 2,
        root: { fillColor: '#409e85', color: '#ffffff', borderColor: 'transparent', borderWidth: 0, fontSize: 18, fontWeight: 'bold' },
        second: { fillColor: '#f0f7f4', color: '#1a1a1a', borderColor: '#409e85', borderWidth: 1, fontSize: 15, marginX: 80, marginY: 50 },
        node: { fillColor: '#f5f5f5', color: '#262626', borderColor: '#d9d9d9', borderWidth: 1, fontSize: 14, marginX: 40, marginY: 22 },
      };

  const MindMap = (await import('simple-mind-map')).default;
  mindMapInstance = new MindMap({
    el: mindMapContainer.value,
    data: mapData,
    readonly: true,
    layout: 'logicalStructure',
    theme: 'default',
    themeConfig,
    scaleRatio: 0.1,
    mousewheelAction: 'zoom',
    enableFreeDrag: false,
  });
}

function destroyMindMap() {
  if (mindMapInstance) {
    mindMapInstance.destroy();
    mindMapInstance = null;
  }
}

function closeMindMapModal() {
  destroyMindMap();
  mindMapVisible.value = false;
}

watch(isDark, () => {
  if (mindMapInstance && mindMapActiveId.value) {
    getMindMapDetail(mindMapActiveId.value).then(initMindMap);
  }
});

// ========== 需求弹窗 ==========

const reqVisible = ref(false);
const reqProjectName = ref('');
const reqList = ref<any[]>([]);
const reqLoading = ref(false);

const reqStatusColorMap: Record<string, string> = {
  Draft: 'default', Todo: 'blue', InProgress: 'processing', Testing: 'orange', Done: 'success', Closed: 'default',
};
const reqProgressMap: Record<string, number> = {
  Draft: 0, Todo: 0, InProgress: 40, Testing: 75, Done: 100, Closed: 100,
};

const reqColumns = [
  { title: '标题', dataIndex: 'title', key: 'title', ellipsis: true },
  { title: '类型', dataIndex: 'type', key: 'type', width: 90 },
  { title: '状态', dataIndex: 'status', key: 'status', width: 90 },
  { title: '负责人', dataIndex: 'assignee', key: 'assignee', width: 90 },
  { title: '进度', key: 'progress', width: 150 },
  { title: '截止日期', dataIndex: 'dueDate', key: 'dueDate', width: 110 },
];

async function openReqModal(projectId: number, projectName: string) {
  reqProjectName.value = projectName;
  reqList.value = [];
  reqVisible.value = true;
  reqLoading.value = true;
  try {
    const result: any = await getRequirementList({ projectId, page: 1, pageSize: 200 });
    reqList.value = result?.items || [];
  } finally {
    reqLoading.value = false;
  }
}

// ========== Lifecycle ==========

onMounted(() => loadData());
onBeforeUnmount(() => destroyMindMap());
</script>

<template>
  <Page auto-content-height>
    <div class="project-list-page">
      <!-- 顶部筛选栏 -->
      <div class="filter-bar">
        <div class="filter-left">
          <Input
            v-model:value="keyword"
            placeholder="搜索项目名称 / 编码"
            style="width: 240px"
            allow-clear
            @press-enter="handleSearch"
          />
          <Select
            v-model:value="filterStatus"
            placeholder="状态"
            :options="statusOptions"
            allow-clear
            style="width: 130px"
            @change="handleSearch"
          />
          <Button type="primary" @click="handleSearch">搜索</Button>
        </div>
        <div class="filter-right">
          <Button type="primary" @click="handleAdd">新增项目</Button>
        </div>
      </div>

      <!-- 项目卡片列表 -->
      <Spin :spinning="loading">
        <div v-if="projects.length === 0 && !loading" class="empty-state">
          暂无项目数据
        </div>
        <div class="project-grid">
          <div
            v-for="item in projects"
            :key="item.id"
            class="project-card"
            @click="handleDetail(item.id)"
          >
            <!-- 卡片头部 -->
            <div class="card-header">
              <div class="card-title-row">
                <img
                  v-if="item.logoUrl"
                  :src="item.logoUrl"
                  alt="logo"
                  class="card-logo"
                />
                <div class="card-title">{{ item.name }}</div>
                <div class="card-tags-row">
                  <Tag :color="statusColorMap[item.status] || 'default'" size="small">
                    {{ projectStatusLabels[item.status] || item.status }}
                  </Tag>
                </div>
              </div>
              <div class="card-code">{{ item.code }}</div>
            </div>

            <!-- 标签 -->
            <div v-if="item.tags" class="card-tags">
              <Tag
                v-for="(tag, index) in item.tags.split(',')"
                :key="index"
                style="margin-bottom: 2px"
              >
                {{ tag.trim() }}
              </Tag>
            </div>

            <!-- 模板标签 -->
            <div v-if="item.templateName" class="card-info-row">
              <span class="info-chip template">{{ item.templateName }}</span>
            </div>

            <!-- 统计概要 -->
            <div class="card-stats">
              <!-- 需求进度 -->
              <div class="stat-section">
                <div class="stat-label">需求进度</div>
                <div class="stat-content">
                  <Progress
                    :percent="reqPercent(item.stats)"
                    :stroke-color="reqPercent(item.stats) >= 100 ? '#52c41a' : '#1677ff'"
                    size="small"
                    style="flex: 1; margin-right: 8px"
                  />
                  <span class="stat-numbers">
                    <span class="stat-done">{{ item.stats?.reqDone || 0 }}</span>
                    <span class="stat-divider">/</span>
                    <span>{{ item.stats?.reqTotal || 0 }}</span>
                  </span>
                </div>
              </div>

              <!-- 任务进度 -->
              <div class="stat-section">
                <div class="stat-label">任务进度</div>
                <div class="stat-content">
                  <Progress
                    :percent="item.stats?.taskTotal > 0 ? Math.round((item.stats.taskDone / item.stats.taskTotal) * 100) : 0"
                    :stroke-color="item.stats?.taskTotal > 0 && item.stats.taskDone >= item.stats.taskTotal ? '#52c41a' : '#722ed1'"
                    size="small"
                    style="flex: 1; margin-right: 8px"
                  />
                  <span class="stat-numbers">
                    <span class="stat-done">{{ item.stats?.taskDone || 0 }}</span>
                    <span class="stat-divider">/</span>
                    <span>{{ item.stats?.taskTotal || 0 }}</span>
                  </span>
                </div>
              </div>

              <!-- 项目时间进度 -->
              <div v-if="item.dueDate" class="stat-section">
                <div class="stat-label">项目时间</div>
                <ProjectTimeBar :start-date="item.startDate" :created-time="item.createdTime" :due-date="item.dueDate" />
              </div>

              <!-- 指标行 -->
              <div class="stat-metrics">
                <Tooltip title="Git 仓库">
                  <div class="metric-item" @click.stop="handleGit(item.id)">
                    <span class="metric-icon">📦</span>
                    <span class="metric-value">{{ item.stats?.gitRepoCount || 0 }}</span>
                    <span class="metric-label">仓库</span>
                  </div>
                </Tooltip>
                <Tooltip title="思维导图">
                  <div class="metric-item" @click.stop="openMindMapModal(item.id, item.name)">
                    <span class="metric-icon">🧠</span>
                    <span class="metric-value">{{ item.stats?.mindMapCount || 0 }}</span>
                    <span class="metric-label">导图</span>
                  </div>
                </Tooltip>
                <Tooltip title="功能模块">
                  <div class="metric-item">
                    <span class="metric-icon">🧩</span>
                    <span class="metric-value">{{ item.stats?.featureModuleCount || 0 }}</span>
                    <span class="metric-label">功能</span>
                  </div>
                </Tooltip>
                <Tooltip title="需求">
                  <div class="metric-item" @click.stop="openReqModal(item.id, item.name)">
                    <span class="metric-icon">📋</span>
                    <span class="metric-value">{{ item.stats?.reqTotal || 0 }}</span>
                    <span class="metric-label">需求</span>
                  </div>
                </Tooltip>
                <Tooltip title="任务">
                  <div class="metric-item">
                    <span class="metric-icon">✅</span>
                    <span class="metric-value">{{ item.stats?.taskTotal || 0 }}</span>
                    <span class="metric-label">任务</span>
                  </div>
                </Tooltip>
                <Tooltip v-if="item.stats?.lastGitActivity" title="最近Git活动">
                  <div class="metric-item">
                    <span class="metric-icon">⏱</span>
                    <span class="metric-value time-value">{{ timeAgo(item.stats.lastGitActivity) }}</span>
                  </div>
                </Tooltip>
              </div>
            </div>

            <!-- 日期 & 操作 -->
            <div class="card-footer">
              <div class="card-dates">
                <span v-if="item.startDate || item.dueDate" class="date-range">
                  {{ formatDate(item.startDate) }} ~ {{ formatDate(item.dueDate) }}
                </span>
              </div>
              <div class="card-actions" @click.stop>
                <Space :size="4">
                  <Tooltip title="思维导图">
                    <Button size="small" @click="openMindMapModal(item.id, item.name)">导图</Button>
                  </Tooltip>
                  <Tooltip title="需求列表">
                    <Button size="small" @click="openReqModal(item.id, item.name)">需求</Button>
                  </Tooltip>
                  <Button size="small" @click="handleEdit(item.id)">编辑</Button>
                  <Popconfirm
                    title="确定要删除该项目吗？"
                    @confirm="handleDel(item.id)"
                  >
                    <Button danger size="small">删除</Button>
                  </Popconfirm>
                </Space>
              </div>
            </div>
          </div>
        </div>
      </Spin>

      <!-- 分页 -->
      <div v-if="total > 0" class="pagination-bar">
        <Pagination
          :current="page"
          :page-size="pageSize"
          :total="total"
          :page-size-options="['12', '24', '48']"
          show-size-changer
          show-quick-jumper
          :show-total="(t: number) => `共 ${t} 个项目`"
          @change="handlePageChange"
        />
      </div>
    </div>

    <!-- ========== 思维导图弹窗 ========== -->
    <Modal
      :open="mindMapVisible"
      :title="`${mindMapProjectName} - 思维导图`"
      :footer="null"
      width="90vw"
      wrap-class-name="mindmap-modal-wrap"
      :destroy-on-close="false"
      @cancel="closeMindMapModal"
    >
      <div class="mindmap-modal-body">
        <Spin :spinning="mindMapLoading" style=" display: flex; flex-direction: column;height: 100%">
          <!-- 导图选择 tabs -->
          <div v-if="mindMapList.length > 0" class="mindmap-tabs">
            <div
              v-for="item in mindMapList"
              :key="item.id"
              class="mindmap-tab-item"
              :class="{ active: mindMapActiveId === item.id }"
              @click="selectMindMap(item.id)"
            >
              <span>{{ item.title }}</span>
              <Tag v-if="item.isMain" color="red" style="margin-left: 4px; font-size: 10px">主</Tag>
            </div>
          </div>
          <!-- 导图渲染区域 -->
          <div v-if="mindMapList.length > 0" ref="mindMapContainer" class="mindmap-render-area"></div>
          <Empty v-if="!mindMapLoading && mindMapList.length === 0" description="该项目暂无思维导图" style="margin: auto" />
        </Spin>
      </div>
    </Modal>

    <!-- ========== 需求弹窗 ========== -->
    <Modal
      v-model:open="reqVisible"
      :title="`${reqProjectName} - 需求列表`"
      :footer="null"
      width="1000px"
      :style="{ top: '5vh' }"
      :body-style="{ padding: '16px', maxHeight: '80vh', overflow: 'auto' }"
    >
      <Spin :spinning="reqLoading">
        <Table
          v-if="reqList.length > 0"
          :columns="reqColumns"
          :data-source="reqList"
          :pagination="reqList.length > 20 ? { pageSize: 20 } : false"
          row-key="id"
          size="middle"
        >
          <template #bodyCell="{ column, record }">
            <template v-if="column.key === 'type'">
              <Tag>{{ requirementTypeLabels[record.type] || record.type }}</Tag>
            </template>
            <template v-else-if="column.key === 'status'">
              <Tag :color="reqStatusColorMap[record.status] || 'default'">
                {{ requirementStatusLabels[record.status] || record.status }}
              </Tag>
            </template>
            <template v-else-if="column.key === 'progress'">
              <Progress
                :percent="reqProgressMap[record.status] ?? 0"
                :stroke-color="record.status === 'Done' || record.status === 'Closed' ? '#52c41a' : '#1890ff'"
                size="small"
              />
            </template>
            <template v-else-if="column.key === 'dueDate'">
              {{ formatDate(record.dueDate) }}
            </template>
          </template>
        </Table>
        <Empty v-else description="该项目暂无需求" />
      </Spin>
    </Modal>
  </Page>
</template>

<style scoped>
.project-list-page {
  padding: 0 4px;
}

.filter-bar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 16px 20px;
  margin-bottom: 20px;
  background: var(--ant-color-bg-container);
  border: 1px solid var(--ant-color-border);
  border-radius: 8px;
  box-shadow: 0 1px 4px rgb(0 0 0 / 8%);
}

.filter-left {
  display: flex;
  gap: 12px;
  align-items: center;
}

.project-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(420px, 1fr));
  gap: 16px;
}

.project-card {
  display: flex;
  flex-direction: column;
  gap: 12px;
  padding: 20px;
  cursor: pointer;
  background: var(--ant-color-bg-container);
  border: 1px solid #d9d9d9;
  border-radius: 10px;
  box-shadow: 0 2px 8px rgb(0 0 0 / 10%);
  transition: all 0.2s ease;
}

.project-card:hover {
  border-color: var(--ant-color-primary);
  box-shadow: 0 6px 20px rgb(0 0 0 / 15%);
  transform: translateY(-2px);
}

.card-header {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.card-title-row {
  display: flex;
  gap: 8px;
  align-items: flex-start;
  justify-content: space-between;
}

.card-logo {
  flex-shrink: 0;
  width: 28px;
  height: 28px;
  object-fit: contain;
  border-radius: 6px;
}

.card-title {
  flex: 1;
  overflow: hidden;
  text-overflow: ellipsis;
  font-size: 16px;
  font-weight: 600;
  line-height: 1.4;
  color: var(--ant-color-text);
  white-space: nowrap;
}

.card-tags-row {
  display: flex;
  flex-shrink: 0;
  gap: 4px;
}

.card-code {
  font-family: monospace;
  font-size: 12px;
  color: var(--ant-color-text-tertiary);
}

.card-desc {
  overflow: hidden;
  text-overflow: ellipsis;
  font-size: 13px;
  color: var(--ant-color-text-secondary);
  white-space: nowrap;
}

.card-tags {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
}

.card-info-row {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  margin-bottom: 10px;
}

.info-chip {
  padding: 2px 8px;
  font-size: 12px;
  line-height: 20px;
  border-radius: 4px;
}

.info-chip.template {
  color: #1677ff;
  background: rgb(22 119 255 / 10%);
  border: 1px solid rgb(22 119 255 / 20%);
}

.info-chip.feature {
  color: #722ed1;
  background: rgb(114 46 209 / 10%);
  border: 1px solid rgb(114 46 209 / 20%);
}

.card-stats {
  display: flex;
  flex-direction: column;
  gap: 10px;
  padding: 12px;
  background: #f7f8fa;
  border: 1px solid #e8e8e8;
  border-radius: 8px;
}

.stat-section .stat-label {
  margin-bottom: 4px;
  font-size: 12px;
  color: var(--ant-color-text-tertiary);
}

.stat-content {
  display: flex;
  align-items: center;
}

.stat-numbers {
  font-size: 13px;
  color: var(--ant-color-text-secondary);
  white-space: nowrap;
}

.stat-done {
  font-weight: 600;
  color: var(--ant-color-success);
}

.stat-divider {
  margin: 0 2px;
  color: var(--ant-color-text-quaternary);
}

.stat-detail {
  display: flex;
  gap: 8px;
  margin-top: 4px;
}


.stat-chip {
  padding: 1px 8px;
  font-size: 11px;
  white-space: nowrap;
  border-radius: 10px;
}

.stat-chip.in-progress {
  color: var(--ant-color-primary);
  background: var(--ant-color-primary-bg);
}

.stat-chip.testing {
  color: var(--ant-color-warning);
  background: var(--ant-color-warning-bg);
}

.stat-metrics {
  display: flex;
  flex-wrap: nowrap;
  gap: 16px;
  overflow-x: auto;
  scrollbar-width: none;
  -ms-overflow-style: none;
}

.stat-metrics::-webkit-scrollbar {
  display: none;
}

.metric-item {
  display: flex;
  flex-shrink: 0;
  gap: 4px;
  align-items: center;
  padding: 2px 6px;
  white-space: nowrap;
  cursor: pointer;
  border-radius: 4px;
  transition: background 0.2s;
}

.metric-item:hover {
  background: var(--ant-color-fill-secondary);
}

.metric-icon {
  font-size: 14px;
}

.metric-value {
  font-size: 14px;
  font-weight: 600;
  color: var(--ant-color-text);
}

.metric-value.time-value {
  font-size: 12px;
  font-weight: 400;
  color: var(--ant-color-text-secondary);
}

.metric-label {
  font-size: 11px;
  color: var(--ant-color-text-tertiary);
}

.card-footer {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding-top: 8px;
  margin-top: auto;
  border-top: 1px solid #e8e8e8;
}

.card-dates {
  font-size: 12px;
  color: var(--ant-color-text-tertiary);
}

.card-actions {
  display: flex;
  gap: 4px;
}

.empty-state {
  padding: 80px 0;
  font-size: 14px;
  color: var(--ant-color-text-tertiary);
  text-align: center;
}

.pagination-bar {
  display: flex;
  justify-content: flex-end;
  padding: 12px 0;
  margin-top: 20px;
}

/* ========== 思维导图弹窗 ========== */

.mindmap-modal-body {
  display: flex;
  flex-direction: column;
  height: 85vh;
}

.mindmap-modal-body :deep(.ant-spin-nested-loading),
.mindmap-modal-body :deep(.ant-spin-container) {
  display: flex;
  flex: 1;
  flex-direction: column;
  height: 100%;
}

.mindmap-tabs {
  display: flex;
  flex-shrink: 0;
  gap: 0;
  padding: 0 16px;
  overflow-x: auto;
  background: var(--ant-color-bg-container);
  border-bottom: 1px solid var(--ant-color-border-secondary);
}

.mindmap-tab-item {
  display: flex;
  align-items: center;
  padding: 10px 16px;
  font-size: 14px;
  color: var(--ant-color-text-secondary);
  white-space: nowrap;
  cursor: pointer;
  border-bottom: 2px solid transparent;
  transition: all 0.2s;
}

.mindmap-tab-item:hover {
  color: var(--ant-color-primary);
}

.mindmap-tab-item.active {
  font-weight: 500;
  color: var(--ant-color-primary);
  border-bottom-color: var(--ant-color-primary);
}

.mindmap-render-area {
  flex: 1;
  width: 100%;
  min-height: 400px;
  overflow: hidden;
}
</style>

<style>
.mindmap-modal-wrap .ant-modal {
  top: 2vh !important;
  padding-bottom: 0;
}

.mindmap-modal-wrap .ant-modal-body {
  padding: 0 !important;
}

/* 暗黑模式适配 */
html.dark .project-card {
  background: var(--ant-color-bg-elevated, #1f1f1f);
  border-color: var(--ant-color-border, #424242);
  box-shadow: 0 1px 4px rgb(0 0 0 / 30%);
}

html.dark .project-card:hover {
  border-color: var(--ant-color-primary);
  box-shadow: 0 6px 20px rgb(0 0 0 / 50%);
}

html.dark .filter-bar {
  background: var(--ant-color-bg-elevated, #1f1f1f);
  border-color: var(--ant-color-border, #424242);
  box-shadow: 0 1px 4px rgb(0 0 0 / 20%);
}

html.dark .card-stats {
  background: rgb(255 255 255 / 4%);
  border-color: rgb(255 255 255 / 8%);
}

html.dark .card-footer {
  border-top-color: rgb(255 255 255 / 8%);
}
</style>

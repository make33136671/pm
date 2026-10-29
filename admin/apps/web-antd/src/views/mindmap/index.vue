<script lang="ts" setup>
import type { VxeGridProps } from '#/adapter/vxe-table';
import type { VbenFormProps } from '#/adapter/form';

import { ref, nextTick, onBeforeUnmount, watch } from 'vue';

import { Page } from '@vben/common-ui';
import { usePreferences } from '@vben/preferences';

import {
  Tag,
  Space,
  Button,
  Popconfirm,
  Modal,
  message,
  Input,
  Tooltip,
} from 'ant-design-vue';

import { useVbenVxeGrid } from '#/adapter/vxe-table';
import { useVbenForm } from '#/adapter/form';
import { generatedByLabels } from '#/utils/enum-labels';

import {
  getList,
  getDetail,
  postAdd,
  postEdit,
  postDel,
  postSyncMain,
} from './api';
import { getList as getProjectList } from '../project/api';

const { isDark } = usePreferences();

interface RowType {
  id: number;
  title: string;
  type: string;
  isMain: boolean;
  project: { name: string };
  generatedBy: string;
  createdTime: string;
}

// ========== 列表 ==========

const typeColorMap: Record<string, string> = {
  Requirement: 'blue',
  CodeStructure: 'green',
  ModuleDependency: 'orange',
};
const typeLabelMap: Record<string, string> = {
  Requirement: '需求导图',
  CodeStructure: '代码结构',
  ModuleDependency: '模块依赖',
};

const formOptions: VbenFormProps = {
  schema: [
    {
      label: '项目',
      fieldName: 'projectId',
      component: 'ApiSelect',
      componentProps: {
        placeholder: '请选择项目',
        api: getProjectList,
        afterFetch: (data: any) =>
          data.items.map((item: any) => ({
            label: item.name,
            value: item.id,
          })),
        allowClear: true,
        filterOption: true,
        showSearch: true,
      },
    },
    {
      label: '类型',
      fieldName: 'type',
      component: 'Select',
      componentProps: {
        placeholder: '全部类型',
        options: [
          { label: '需求导图', value: 'Requirement' },
          { label: '代码结构', value: 'CodeStructure' },
          { label: '模块依赖', value: 'ModuleDependency' },
        ],
        allowClear: true,
      },
    },
  ],
  wrapperClass: 'grid-cols-2',
  showCollapseButton: false,
  submitOnChange: false,
  submitOnEnter: true,
};

const gridOptions: VxeGridProps<RowType> = {
  columns: [
    { field: 'id', title: 'Id', width: 50 },
    {
      field: 'title',
      title: '标题',
      minWidth: 200,
      slots: { default: 'title' },
    },
    { field: 'project.name', title: '项目', width: 140 },
    {
      field: 'type',
      title: '类型',
      width: 110,
      slots: { default: 'type' },
    },
    {
      field: 'generatedBy',
      title: '生成方式',
      width: 100,
      slots: { default: 'generatedBy' },
    },
    {
      field: 'createdTime',
      title: '创建时间',
      width: 170,
      slots: { default: 'createdTime' },
    },
    {
      field: 'action',
      fixed: 'right',
      align: 'center',
      slots: { default: 'action' },
      title: '操作',
      width: 130,
    },
  ],
  height: 'auto',
  keepSource: true,
  pagerConfig: {},
  proxyConfig: {
    ajax: {
      query: async ({ page }, formValues) => {
        return await getList({
          page: page.currentPage,
          pageSize: page.pageSize,
          ...formValues,
        });
      },
    },
  },
  toolbarConfig: { refresh: true, custom: true },
};

const [Grid, gridApi] = useVbenVxeGrid({ formOptions, gridOptions });

// ========== 新增弹窗 ==========

const addModalVisible = ref(false);

const [AddForm, addFormApi] = useVbenForm({
  commonConfig: { colon: true, componentProps: { class: 'w-full' } },
  handleSubmit: onAddSubmit,
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
        afterFetch: (data: any) =>
          data.items.map((item: any) => ({
            label: item.name,
            value: item.id,
          })),
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
      componentProps: { placeholder: '请输入标题' },
    },
  ],
  wrapperClass: 'grid-cols-1',
});

async function onAddSubmit(values: Record<string, any>) {
  const defaultData = {
    data: { text: values.title || '中心主题' },
    children: [],
  };
  const res: any = await postAdd({
    ...values,
    mapData: JSON.stringify(defaultData),
  });
  addModalVisible.value = false;
  message.success('创建成功');
  openEditor(res.id, false);
  gridApi.query();
}

function handleAdd() {
  addFormApi.resetForm();
  addModalVisible.value = true;
}

// ========== 思维导图编辑器 ==========

const editorVisible = ref(false);
const editorTitle = ref('');
const editorReadonly = ref(false);
const editorRecordId = ref(0);
const editorRecord = ref<any>(null);
const editorIsMain = ref(false);
const mindMapContainer = ref<HTMLElement>();
let mindMapInstance: any = null;

// 进度统计
const progressStats = ref({ total: 0, done: 0, percent: 0 });

function convertLegacyData(data: any): any {
  if (data?.data?.text !== undefined) return data;
  if (data?.root) return convertNode(data.root);
  if (data?.text !== undefined) return convertNode(data);
  return { data: { text: '中心主题' }, children: [] };
}

function convertNode(node: any): any {
  const result: any = {
    data: { text: node.text || '' },
    children: [],
  };
  if (node.status) result.data.tag = [node.status];
  if (Array.isArray(node.children)) {
    result.children = node.children.map(convertNode);
  }
  return result;
}

function calcProgress(data: any) {
  let total = 0;
  let done = 0;
  function walk(node: any, depth: number) {
    if (depth > 0) {
      // 叶子节点或第2层以下节点算作功能项
      const tags: string[] = node?.data?.tag || [];
      const isDone =
        tags.includes('已完成') ||
        tags.includes('Done') ||
        tags.includes('Closed');
      total++;
      if (isDone) done++;
    }
    if (Array.isArray(node.children)) {
      node.children.forEach((c: any) => walk(c, depth + 1));
    }
  }
  walk(data, 0);
  progressStats.value = {
    total,
    done,
    percent: total > 0 ? Math.round((done / total) * 100) : 0,
  };
}

async function openEditor(id: number, readonly: boolean) {
  editorRecordId.value = id;
  editorReadonly.value = readonly;
  const detail: any = await getDetail(id);
  editorRecord.value = detail;
  editorTitle.value = detail.title;
  editorIsMain.value = !!detail.isMain;
  editorVisible.value = true;

  await nextTick();
  setTimeout(() => initMindMap(detail), 300);
}

async function initMindMap(detail: any) {
  if (!mindMapContainer.value) return;
  destroyMindMap();

  let mapData: any;
  try {
    const parsed =
      typeof detail.mapData === 'string'
        ? JSON.parse(detail.mapData)
        : detail.mapData;
    mapData = convertLegacyData(parsed);
  } catch {
    mapData = { data: { text: detail.title || '中心主题' }, children: [] };
  }

  // 根据暗黑模式适配节点颜色
  adaptNodeColors(mapData, isDark.value);
  calcProgress(mapData);

  const dark = isDark.value;
  const themeConfig = dark
    ? {
        backgroundColor: '#141414',
        lineColor: '#5bb59a',
        lineWidth: 2,
        generalizationLineColor: '#5bb59a',
        associativeLineColor: '#8c8c8c',
        associativeLineTextColor: '#d9d9d9',
        root: {
          fillColor: '#177a56',
          color: '#ffffff',
          borderColor: '#1fa06e',
          borderWidth: 0,
          fontSize: 18,
          fontWeight: 'bold',
        },
        second: {
          fillColor: '#262626',
          color: '#f0f0f0',
          borderColor: '#5bb59a',
          borderWidth: 1,
          fontSize: 15,
          marginX: 80,
          marginY: 50,
        },
        node: {
          fillColor: '#1f1f1f',
          color: '#d9d9d9',
          borderColor: '#434343',
          borderWidth: 1,
          fontSize: 14,
          marginX: 40,
          marginY: 22,
        },
        generalization: {
          fillColor: '#262626',
          color: '#f0f0f0',
          borderColor: '#5bb59a',
          borderWidth: 1,
        },
      }
    : {
        backgroundColor: '#ffffff',
        lineColor: '#409e85',
        lineWidth: 2,
        generalizationLineColor: '#409e85',
        associativeLineColor: '#595959',
        associativeLineTextColor: '#262626',
        root: {
          fillColor: '#409e85',
          color: '#ffffff',
          borderColor: 'transparent',
          borderWidth: 0,
          fontSize: 18,
          fontWeight: 'bold',
        },
        second: {
          fillColor: '#f0f7f4',
          color: '#1a1a1a',
          borderColor: '#409e85',
          borderWidth: 1,
          fontSize: 15,
          marginX: 80,
          marginY: 50,
        },
        node: {
          fillColor: '#f5f5f5',
          color: '#262626',
          borderColor: '#d9d9d9',
          borderWidth: 1,
          fontSize: 14,
          marginX: 40,
          marginY: 22,
        },
        generalization: {
          fillColor: '#f0f7f4',
          color: '#1a1a1a',
          borderColor: '#409e85',
          borderWidth: 1,
        },
      };

  const MindMap = (await import('simple-mind-map')).default;
  mindMapInstance = new MindMap({
    el: mindMapContainer.value,
    data: mapData,
    readonly: editorReadonly.value,
    layout: 'logicalStructure',
    theme: 'default',
    themeConfig,
    scaleRatio: 0.1,
    mousewheelAction: 'zoom',
    enableFreeDrag: false,
  });

  // 监听数据变化以更新进度
  mindMapInstance.on('data_change', (newData: any) => {
    calcProgress(newData);
  });
}

function destroyMindMap() {
  if (mindMapInstance) {
    mindMapInstance.destroy();
    mindMapInstance = null;
  }
}

// 状态节点颜色：确保在两种模式下文字都清晰可见
const COLORS = {
  light: {
    done: '#e6f4ea',
    doneText: '#0d652d',
    doneBorder: '#34a853',
    inProgress: '#fef7e0',
    progressText: '#7c5c00',
    progressBorder: '#f9ab00',
  },
  dark: {
    done: '#1e3a2a',
    doneText: '#81c995',
    doneBorder: '#34a853',
    inProgress: '#3e2e0e',
    progressText: '#fdd663',
    progressBorder: '#f9ab00',
  },
};

function adaptNodeColors(node: any, dark: boolean) {
  if (!node?.data) return;
  const c = dark ? COLORS.dark : COLORS.light;
  const fill = node.data.fillColor;

  // 清理空字符串值 —— 空字符串会被 simple-mind-map 当作黑色，必须删除让主题默认值生效
  if (fill === '' || fill === undefined || fill === null) {
    delete node.data.fillColor;
  }
  if (node.data.color === '' || node.data.color === undefined) {
    delete node.data.color;
  }
  if (node.data.borderColor === '') {
    delete node.data.borderColor;
  }
  if (node.data.fontColor === '') {
    delete node.data.fontColor;
  }

  // 已完成状态节点
  const actualFill = node.data.fillColor;
  if (actualFill === '#d4edda' || actualFill === COLORS.light.done || actualFill === COLORS.dark.done) {
    node.data.fillColor = c.done;
    node.data.color = c.doneText;
    node.data.borderColor = c.doneBorder;
    node.data.borderWidth = 2;
  }
  // 进行中状态节点
  else if (actualFill === '#fff3cd' || actualFill === COLORS.light.inProgress || actualFill === COLORS.dark.inProgress) {
    node.data.fillColor = c.inProgress;
    node.data.color = c.progressText;
    node.data.borderColor = c.progressBorder;
    node.data.borderWidth = 2;
  }

  if (Array.isArray(node.children)) {
    node.children.forEach((child: any) => adaptNodeColors(child, dark));
  }
}

// 监听暗黑模式切换，自动重新渲染
watch(isDark, () => {
  if (mindMapInstance && editorRecord.value) {
    initMindMap(editorRecord.value);
  }
});

const editorSaving = ref(false);

async function handleEditorSave() {
  if (!mindMapInstance || editorReadonly.value) return;
  editorSaving.value = true;
  try {
    const mapData = mindMapInstance.getData(false);
    await postEdit({
      id: editorRecordId.value,
      title: editorTitle.value,
      mapData: JSON.stringify(mapData),
    });
    message.success('保存成功');
  } catch {
    // api wrapper 已处理
  } finally {
    editorSaving.value = false;
  }
}

function handleEditorClose() {
  destroyMindMap();
  editorVisible.value = false;
  gridApi.query();
}

// 节点状态切换：标记已完成/未完成
function handleToggleNodeDone() {
  if (!mindMapInstance || editorReadonly.value) return;
  const activeNodes = mindMapInstance.renderer?.activeNodeList;
  if (!activeNodes || activeNodes.length === 0) {
    message.warning('请先点击选中一个节点');
    return;
  }
  activeNodes.forEach((node: any) => {
    const tags: string[] = node.getData('tag') || [];
    const isNodeDone = tags.includes('已完成');
    let newTags: string[];
    const c = isDark.value ? COLORS.dark : COLORS.light;
    if (isNodeDone) {
      newTags = tags.filter((t: string) => t !== '已完成');
      node.setData({ tag: newTags, fillColor: null, color: null, borderColor: null, borderWidth: null });
    } else {
      const statusLabels = [
        'Draft',
        'Todo',
        'InProgress',
        'Testing',
        'Done',
        'Closed',
        '未完成',
      ];
      newTags = tags.filter((t: string) => !statusLabels.includes(t));
      newTags.unshift('已完成');
      node.setData({ tag: newTags, fillColor: c.done, color: c.doneText, borderColor: c.doneBorder, borderWidth: 2 });
    }
  });
  mindMapInstance.render();
}

// 同步主导图
const syncing = ref(false);
async function handleSyncMain() {
  if (!editorRecord.value) return;
  syncing.value = true;
  try {
    const result: any = await postSyncMain(editorRecord.value.projectId);
    if (result?.mapData) {
      editorRecord.value.mapData = result.mapData;
      await initMindMap(result);
      message.success('已从需求同步');
    }
  } catch {
    // handled
  } finally {
    syncing.value = false;
  }
}

// 工具栏操作
function handleZoomIn() {
  if (!mindMapInstance) return;
  const scale = mindMapInstance.view.scale + 0.1;
  mindMapInstance.view.setScale(scale);
}
function handleZoomOut() {
  if (!mindMapInstance) return;
  const scale = Math.max(0.1, mindMapInstance.view.scale - 0.1);
  mindMapInstance.view.setScale(scale);
}
function handleFitCanvas() {
  mindMapInstance?.view?.reset();
}
function handleExpandAll() {
  mindMapInstance?.renderer?.expandAllNode();
}
function handleCollapseAll() {
  mindMapInstance?.renderer?.unexpandAllNode();
}

// ========== 列表操作 ==========

function handleView(id: number) {
  openEditor(id, true);
}

function handleEdit(id: number) {
  openEditor(id, false);
}

function handleDel(row: any) {
  if (row.isMain) {
    message.warning('主导图不能删除');
    return;
  }
  postDel(row.id).then(() => {
    message.success('删除成功');
    gridApi.query();
  });
}

function formatDateTime(dateStr: string) {
  if (!dateStr) return '-';
  return new Date(dateStr).toLocaleString('zh-CN');
}

onBeforeUnmount(() => {
  destroyMindMap();
});
</script>

<template>
  <Page auto-content-height>
    <Grid>
      <template #toolbar-tools>
        <Button type="primary" @click="handleAdd">新增</Button>
      </template>

      <template #title="{ row }">
        <span>{{ row.title }}</span>
        <Tag v-if="row.isMain" color="red" style="margin-left: 6px">
          主导图
        </Tag>
      </template>
      <template #type="{ row }">
        <Tag :color="typeColorMap[row.type] || 'default'">{{
          typeLabelMap[row.type] || row.type
        }}</Tag>
      </template>
      <template #generatedBy="{ row }">
        <Tag :color="row.generatedBy === 'Auto' ? 'green' : 'blue'">{{
          generatedByLabels[row.generatedBy] || row.generatedBy
        }}</Tag>
      </template>
      <template #createdTime="{ row }">{{
        formatDateTime(row.createdTime)
      }}</template>

      <template #action="{ row }">
        <Space wrap>
          <Button @click="handleView(row.id)">查看</Button>
          <Button @click="handleEdit(row.id)">编辑</Button>
          <template v-if="!row.isMain">
            <Popconfirm title="确定删除？" @confirm="handleDel(row)">
              <Button danger>删除</Button>
            </Popconfirm>
          </template>
          <Tag v-else color="default" style="cursor: default">不可删除</Tag>
        </Space>
      </template>
    </Grid>

    <!-- 新增弹窗 -->
    <Modal
      v-model:open="addModalVisible"
      title="新增思维导图"
      :footer="null"
      :destroy-on-close="false"
      width="500px"
    >
      <AddForm />
    </Modal>

    <!-- 思维导图编辑器 (全屏弹窗) -->
    <Modal
      :open="editorVisible"
      :title="null"
      :footer="null"
      :closable="false"
      :mask-closable="false"
      width="100vw"
      wrap-class-name="mindmap-editor-modal"
      :style="{ top: 0, padding: 0, maxWidth: '100vw' }"
      :body-style="{
        padding: 0,
        height: '100vh',
        display: 'flex',
        flexDirection: 'column',
      }"
      @cancel="handleEditorClose"
    >
      <!-- 顶部工具栏 -->
      <div class="mindmap-toolbar">
        <div class="mindmap-toolbar-left">
          <Input
            v-model:value="editorTitle"
            :bordered="false"
            :disabled="editorReadonly"
            style=" width: 300px;font-size: 16px; font-weight: 600"
          />
          <Tag v-if="editorIsMain" color="red">主导图</Tag>
          <Tag v-if="editorReadonly" color="default">只读</Tag>
          <!-- 进度统计 -->
          <span
            v-if="progressStats.total > 0"
            style="
              margin-left: 8px;
              font-size: 13px;
              color: #666;
              white-space: nowrap;
            "
          >
            进度:
            <span style="font-weight: 600; color: #52c41a">{{
              progressStats.done
            }}</span>
            / {{ progressStats.total }} ({{ progressStats.percent }}%)
          </span>
        </div>
        <div class="mindmap-toolbar-center">
          <Space>
            <Tooltip title="标记选中节点 已完成/未完成">
              <Button
                v-if="!editorReadonly"
                size="small"
                type="primary"
                ghost
                @click="handleToggleNodeDone"
              >
                切换完成
              </Button>
            </Tooltip>
            <Tooltip v-if="editorIsMain && !editorReadonly" title="从需求同步">
              <Button
                size="small"
                :loading="syncing"
                @click="handleSyncMain"
              >
                同步需求
              </Button>
            </Tooltip>
            <Tooltip title="放大">
              <Button size="small" @click="handleZoomIn">+</Button>
            </Tooltip>
            <Tooltip title="缩小">
              <Button size="small" @click="handleZoomOut">-</Button>
            </Tooltip>
            <Tooltip title="适应画布">
              <Button size="small" @click="handleFitCanvas">适应</Button>
            </Tooltip>
            <Tooltip title="展开全部">
              <Button size="small" @click="handleExpandAll">展开</Button>
            </Tooltip>
            <Tooltip title="收起全部">
              <Button size="small" @click="handleCollapseAll">收起</Button>
            </Tooltip>
          </Space>
        </div>
        <div class="mindmap-toolbar-right">
          <Space>
            <Button
              v-if="!editorReadonly"
              type="primary"
              :loading="editorSaving"
              @click="handleEditorSave"
            >
              保存
            </Button>
            <Button @click="handleEditorClose">关闭</Button>
          </Space>
        </div>
      </div>

      <!-- 思维导图容器 -->
      <div ref="mindMapContainer" class="mindmap-container"></div>
    </Modal>
  </Page>
</template>

<style scoped>
.mindmap-toolbar {
  display: flex;
  flex-shrink: 0;
  align-items: center;
  justify-content: space-between;
  padding: 8px 16px;
  color: hsl(var(--foreground));
  background: hsl(var(--background));
  border-bottom: 1px solid hsl(var(--border));
}

.mindmap-toolbar-left {
  display: flex;
  gap: 8px;
  align-items: center;
}

.mindmap-toolbar-center {
  display: flex;
  align-items: center;
}

.mindmap-toolbar-right {
  display: flex;
  align-items: center;
}

.mindmap-container {
  flex: 1;
  width: 100%;
  overflow: hidden;
}
</style>

<style>
.mindmap-editor-modal .ant-modal {
  max-width: 100vw !important;
  padding: 0 !important;
  margin: 0 !important;
}

.mindmap-editor-modal .ant-modal-content {
  height: 100vh;
  background: hsl(var(--background)) !important;
  border-radius: 0 !important;
}

.mindmap-editor-modal .ant-modal-body {
  display: flex;
  flex-direction: column;
  height: 100vh;
  padding: 0 !important;
  background: hsl(var(--background)) !important;
}
</style>

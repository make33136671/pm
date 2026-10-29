<script lang="ts" setup>
import type { VbenFormProps } from '#/adapter/form';
import type { VxeGridProps } from '#/adapter/vxe-table';

import { onBeforeUnmount, onMounted, ref } from 'vue';

import { Page } from '@vben/common-ui';

import {
  Tag,
  Button,
  Popconfirm,
  Modal,
  Drawer,
  Form,
  FormItem,
  Input,
  InputNumber,
  Select,
  Switch,
  Textarea,
  Timeline,
  TimelineItem,
  message,
  Space,
} from 'ant-design-vue';

import { useVbenVxeGrid } from '#/adapter/vxe-table';

import {
  getList,
  postAdd,
  postEdit,
  postDel,
  postTriggerBuild,
  postOneClickDeploy,
  getStatus,
  getVersions,
  postTriggerRollback,
  postApproveBuild,
  postRejectBuild,
  postApproveDeploy,
  postRejectDeploy,
} from '../api';

import {
  versionStrategyLabels,
  triggerModeLabels,
  deployModeLabels,
  buildSourceLabels,
  deployStrategyLabels,
  toOptions,
  t,
} from '#/utils/enum-labels';

import { api } from '#/api/request';
import { useRoute } from 'vue-router';

// ========== 项目/子项目/仓库/环境 下拉数据 ==========
const projectOptions = ref<any[]>([]);
const subProjectOptions = ref<any[]>([]);
const repoOptions = ref<any[]>([]);
const envOptions = ref<any[]>([]);
const serverOptions = ref<any[]>([]);

async function loadProjectOptions() {
  try {
    const data: any = await api.get('/Project/List', { params: { pageSize: 200 } });
    projectOptions.value = (data.items || []).map((p: any) => ({ label: p.name, value: p.id }));
  } catch { /* empty */ }
}

async function loadRepoOptions(projectId: number) {
  try {
    const data: any = await api.get('/GitRepository/List', { params: { projectId, pageSize: 200 } });
    repoOptions.value = (data.items || []).map((r: any) => ({
      label: r.repoFullName || r.repoUrl,
      value: r.id,
    }));
  } catch { /* empty */ }
}

async function loadEnvOptions(projectId: number) {
  try {
    const data: any = await api.get('/ProjectEnvironment/List', { params: { projectId, pageSize: 200 } });
    envOptions.value = (data.items || []).map((e: any) => ({ label: `${e.name} (${e.code})`, value: e.id }));
  } catch { /* empty */ }
}

async function loadServerOptions(projectId: number) {
  try {
    const data: any = await api.get('/ProjectServer/List', { params: { projectId, pageSize: 200 } });
    serverOptions.value = (data.items || []).map((s: any) => ({ label: s.name || s.host, value: s.id }));
  } catch { /* empty */ }
}

async function loadSubProjectOptions(projectId: number) {
  try {
    const data: any = await api.get('/SubProject/List', { params: { projectId, pageSize: 200 } });
    subProjectOptions.value = (data.items || []).map((s: any) => ({
      label: s.code ? `${s.name} (${s.code})` : s.name,
      value: s.id,
      localPath: s.localPath,
      defaultBranch: s.defaultBranch,
    }));
  } catch { /* empty */ }
}

loadProjectOptions();

// ========== 搜索表单 ==========

const formOptions: VbenFormProps = {
  schema: [
    {
      label: '项目',
      fieldName: 'projectId',
      component: 'Select',
      componentProps: {
        placeholder: '全部项目',
        options: projectOptions,
        allowClear: true,
        showSearch: true,
        filterOption: (input: string, option: any) =>
          option.label.toLowerCase().includes(input.toLowerCase()),
      },
    },
  ],
  wrapperClass: 'grid-cols-1',
  showCollapseButton: false,
  submitOnChange: true,
};

// ========== 表格 ==========

const gridOptions: VxeGridProps = {
  columns: [
    { field: 'id', title: 'Id', width: 60 },
    { field: 'name', title: '配置名称', minWidth: 150 },
    { field: 'subProject.name', title: '子项目', width: 120, slots: { default: 'subProject' } },
    { field: 'gitRepository.repoFullName', title: '仓库', minWidth: 150, slots: { default: 'repo' } },
    { field: 'defaultBranch', title: '默认分支', width: 100 },
    { field: 'environment.name', title: '环境', width: 80, slots: { default: 'env' } },
    { field: 'buildSource', title: '产物来源', width: 110, slots: { default: 'buildSource' } },
    { field: 'versionStrategy', title: '版本策略', width: 100, slots: { default: 'versionStrategy' } },
    { field: 'triggerMode', title: '触发方式', width: 110, slots: { default: 'triggerMode' } },
    { field: 'deployMode', title: '部署模式', width: 130, slots: { default: 'deployMode' } },
    { field: 'isEnabled', title: '状态', width: 70, slots: { default: 'enabled' } },
    { field: 'action', title: '操作', width: 180, fixed: 'right', align: 'center', slots: { default: 'action' } },
  ],
  height: 'auto',
  pagerConfig: {},
  proxyConfig: {
    ajax: {
      query: async ({ page }, formValues) => {
        return await getList({ page: page.currentPage, pageSize: page.pageSize, ...formValues });
      },
    },
  },
  rowConfig: { isHover: true },
  toolbarConfig: { refresh: true, custom: true },
};

const [Grid, gridApi] = useVbenVxeGrid({ formOptions, gridOptions });

// ========== 新增/编辑 Modal ==========

const modalVisible = ref(false);
const modalTitle = ref('');
const formData = ref<any>({});
const formLoading = ref(false);

// Pipeline 变量编辑
const variableKeys = ref<string[]>([]);
const variableValues = ref<string[]>([]);

function syncVariablesToForm() {
  const vars: Record<string, string> = {};
  variableKeys.value.forEach((k, i) => {
    if (k) vars[k] = variableValues.value[i] || '';
  });
  formData.value.pipelineVariables = vars;
}

function syncFormToVariables() {
  const vars = formData.value.pipelineVariables || {};
  variableKeys.value = Object.keys(vars);
  variableValues.value = Object.values(vars);
}

function addVariable() {
  variableKeys.value.push('');
  variableValues.value.push('');
}

function removeVariable(index: number) {
  variableKeys.value.splice(index, 1);
  variableValues.value.splice(index, 1);
}

function handleAdd() {
  formData.value = {
    projectId: undefined,
    subProjectId: undefined,
    name: '',
    gitRepositoryId: undefined,
    defaultBranch: 'main',
    environmentId: undefined,
    versionStrategy: 'AutoIncrement',
    triggerMode: 'Manual',
    deployMode: 'None',
    buildSource: 'GitLabCi',
    deployStrategy: 'Replace',
    requireApproval: false,
    buildCommand: '',
    artifactDir: '',
    deployServerIds: [],
    autoDeployOnSuccess: false,
    artifactJobName: '',
    buildTimeout: 30,
    notifyOnSuccess: true,
    notifyOnFailure: true,
    isEnabled: true,
    pipelineVariables: {},
  };
  variableKeys.value = [];
  variableValues.value = [];
  modalTitle.value = '新增流水线配置';
  modalVisible.value = true;
}

async function handleEdit(row: any) {
  formData.value = { ...row };
  syncFormToVariables();
  modalTitle.value = '编辑流水线配置';
  // 加载关联下拉
  if (row.projectId) {
    await Promise.all([
      loadRepoOptions(row.projectId),
      loadEnvOptions(row.projectId),
      loadServerOptions(row.projectId),
      loadSubProjectOptions(row.projectId),
    ]);
  }
  modalVisible.value = true;
}

async function handleProjectChange(projectId: any) {
  formData.value.gitRepositoryId = undefined;
  formData.value.subProjectId = undefined;
  formData.value.environmentId = undefined;
  formData.value.deployServerIds = [];
  if (projectId) {
    await Promise.all([
      loadRepoOptions(projectId),
      loadEnvOptions(projectId),
      loadServerOptions(projectId),
      loadSubProjectOptions(projectId),
    ]);
  }
}

// 选中子项目时,用其默认分支预填(仅当用户未自定义分支)
function handleSubProjectChange(subProjectId: any) {
  const sp = subProjectOptions.value.find((s) => s.value === subProjectId);
  if (sp?.defaultBranch && (!formData.value.defaultBranch || formData.value.defaultBranch === 'main')) {
    formData.value.defaultBranch = sp.defaultBranch;
  }
}

// 从子项目卡片「管理部署方案」跳转而来：预填项目+子项目并打开新增表单
const route = useRoute();
onMounted(async () => {
  const projectId = Number(route.query.projectId);
  const subProjectId = Number(route.query.subProjectId);
  if (route.query.action === 'add' && projectId) {
    handleAdd();
    formData.value.projectId = projectId;
    await Promise.all([
      loadRepoOptions(projectId),
      loadEnvOptions(projectId),
      loadServerOptions(projectId),
      loadSubProjectOptions(projectId),
    ]);
    if (subProjectId) {
      formData.value.subProjectId = subProjectId;
      handleSubProjectChange(subProjectId);
    }
  }
});

async function handleSave() {
  syncVariablesToForm();
  formLoading.value = true;
  try {
    const fn = formData.value.id ? postEdit : postAdd;
    await fn(formData.value);
    message.success(formData.value.id ? '更新成功' : '创建成功');
    modalVisible.value = false;
    gridApi.query();
  } finally {
    formLoading.value = false;
  }
}

async function handleDel(id: number) {
  await postDel(id);
  gridApi.query();
}

// ========== 触发构建 ==========

const buildModalVisible = ref(false);
const buildForm = ref<any>({ profileId: 0, branch: '', version: '' });

function handleTriggerBuild(row: any) {
  buildForm.value = {
    profileId: row.id,
    branch: row.defaultBranch,
    version: '',
  };
  buildModalVisible.value = true;
}

async function submitTriggerBuild() {
  const data: any = { profileId: buildForm.value.profileId };
  if (buildForm.value.branch) data.branch = buildForm.value.branch;
  if (buildForm.value.version) data.version = buildForm.value.version;
  await postTriggerBuild(data);
  buildModalVisible.value = false;
}

// ========== 一键运行(OneClickDeploy) + 进度抽屉 ==========

const runDrawerOpen = ref(false);
const runProfile = ref<any>(null);
const runBuildId = ref<number | undefined>();
const runStatus = ref<any>(null);
let runPollTimer: ReturnType<typeof setInterval> | null = null;

const BUILD_TERMINAL = new Set(['Failed', 'Rejected']);
const DEPLOY_TERMINAL = new Set(['Success', 'Failed', 'Rollback']);

const buildStatusMeta: Record<string, { color: string; text: string }> = {
  PendingApproval: { color: 'warning', text: '待审批' },
  Approved: { color: 'processing', text: '已批准' },
  Building: { color: 'processing', text: '构建中' },
  Success: { color: 'success', text: '构建成功' },
  Failed: { color: 'error', text: '构建失败' },
  Rejected: { color: 'default', text: '已驳回' },
};
const deployStatusMeta: Record<string, { color: string; text: string }> = {
  Pending: { color: 'warning', text: '待审批' },
  Approved: { color: 'processing', text: '已批准' },
  Deploying: { color: 'processing', text: '部署中' },
  Success: { color: 'success', text: '部署成功' },
  Failed: { color: 'error', text: '部署失败' },
  Rollback: { color: 'default', text: '已回滚' },
};

function stopRunPoll() {
  if (runPollTimer) {
    clearInterval(runPollTimer);
    runPollTimer = null;
  }
}

async function runPoll() {
  if (!runBuildId.value) return;
  try {
    const res: any = await getStatus({ buildId: runBuildId.value });
    runStatus.value = res;
    const buildDone = BUILD_TERMINAL.has(res.status) || res.status === 'Success';
    const deployDone = res.deploy_status && DEPLOY_TERMINAL.has(res.deploy_status);
    // 构建失败/驳回即结束;纯构建(无部署)构建成功即结束;有部署则等部署到终态
    const noDeploy = res.status === 'Success' && !res.deploy_id && !res.deploy_status;
    if (BUILD_TERMINAL.has(res.status) || noDeploy || (buildDone && deployDone)) {
      stopRunPoll();
    }
  } catch {
    /* 轮询失败忽略,下一拍再试 */
  }
}

function handleRun(row: any) {
  const isDeploy = row.deployMode && row.deployMode !== 'None';
  Modal.confirm({
    title: `运行流水线:${row.name}`,
    content: isDeploy
      ? `将从【${t(buildSourceLabels, row.buildSource)}】出产物并部署(${row.requireApproval ? '需人工审批' : '一键直发'})。是否继续?`
      : `将执行【${t(buildSourceLabels, row.buildSource)}】构建(仅构建,不部署)。是否继续?`,
    okText: '开始运行',
    cancelText: '取消',
    onOk: async () => {
      const res: any = await postOneClickDeploy({ profileId: row.id, branch: row.defaultBranch });
      runProfile.value = row;
      runBuildId.value = res.build_id;
      runStatus.value = { status: 'Building', version: res.version, branch: res.branch };
      runDrawerOpen.value = true;
      stopRunPoll();
      runPollTimer = setInterval(runPoll, 2000);
      runPoll();
    },
  });
}

function onRunDrawerClose() {
  runDrawerOpen.value = false;
  stopRunPoll();
}

// ========== 审批(构建 / 部署) ==========

async function approveBuild() {
  if (!runStatus.value?.build_id) return;
  await postApproveBuild({ targetId: runStatus.value.build_id });
  runPoll();
}
async function rejectBuild() {
  if (!runStatus.value?.build_id) return;
  await postRejectBuild({ targetId: runStatus.value.build_id });
  runPoll();
}
async function approveDeploy() {
  if (!runStatus.value?.deploy_id) return;
  await postApproveDeploy({ targetId: runStatus.value.deploy_id });
  runPoll();
}
async function rejectDeploy() {
  if (!runStatus.value?.deploy_id) return;
  await postRejectDeploy({ targetId: runStatus.value.deploy_id });
  runPoll();
}

onBeforeUnmount(stopRunPoll);

// ========== 颜色映射 ==========

const deployModeColorMap: Record<string, string> = {
  None: 'default',
  GitLabDeploy: 'orange',
  SbpmDeploy: 'purple',
};

// ========== 版本历史 Drawer ==========

const versionDrawerVisible = ref(false);
const versionDrawerTitle = ref('');
const versionList = ref<any[]>([]);
const versionLoading = ref(false);
const currentProfileId = ref(0);

async function handleShowVersions(row: any) {
  currentProfileId.value = row.id;
  versionDrawerTitle.value = `${row.name} — 版本历史`;
  versionDrawerVisible.value = true;
  versionLoading.value = true;
  try {
    versionList.value = await getVersions(row.id, 20) as any;
  } catch {
    versionList.value = [];
  } finally {
    versionLoading.value = false;
  }
}

async function handleRollback(version: string) {
  await postTriggerRollback({ profileId: currentProfileId.value, targetVersion: version });
  message.success('回滚申请已提交');
}

async function reloadVersions() {
  if (!currentProfileId.value) return;
  versionList.value = (await getVersions(currentProfileId.value, 20)) as any;
}

async function approveBuildAt(buildId: number) {
  await postApproveBuild({ targetId: buildId });
  await reloadVersions();
}
async function rejectBuildAt(buildId: number) {
  await postRejectBuild({ targetId: buildId });
  await reloadVersions();
}
async function approveDeployAt(deployId: number) {
  await postApproveDeploy({ targetId: deployId });
  await reloadVersions();
}
async function rejectDeployAt(deployId: number) {
  await postRejectDeploy({ targetId: deployId });
  await reloadVersions();
}

const buildStatusColorMap: Record<string, string> = {
  PendingApproval: 'orange',
  Building: 'blue',
  Success: 'green',
  Failed: 'red',
  Rejected: 'default',
};

const deployStatusColorMap: Record<string, string> = {
  Pending: 'orange',
  Deploying: 'blue',
  Success: 'green',
  Failed: 'red',
  Rollback: 'purple',
};

function formatTime(v: string) {
  if (!v) return '-';
  return new Date(v).toLocaleString('zh-CN');
}
</script>

<template>
  <Page auto-content-height>
    <Grid>
      <template #toolbar-tools>
        <Button type="primary" @click="handleAdd">新增配置</Button>
      </template>

      <template #subProject="{ row }">
        <Tag v-if="row.subProject" color="cyan">{{ row.subProject.name }}</Tag>
        <span v-else style="color: var(--ant-color-text-tertiary)">项目级</span>
      </template>

      <template #repo="{ row }">
        <span>{{ row.gitRepository?.repoFullName || '-' }}</span>
      </template>

      <template #env="{ row }">
        <Tag v-if="row.environment" color="blue">{{ row.environment.name }}</Tag>
        <span v-else>-</span>
      </template>

      <template #buildSource="{ row }">
        {{ t(buildSourceLabels, row.buildSource) }}
      </template>

      <template #versionStrategy="{ row }">
        {{ t(versionStrategyLabels, row.versionStrategy) }}
      </template>

      <template #triggerMode="{ row }">
        {{ t(triggerModeLabels, row.triggerMode) }}
      </template>

      <template #deployMode="{ row }">
        <Tag :color="deployModeColorMap[row.deployMode] || 'default'">
          {{ t(deployModeLabels, row.deployMode) }}
        </Tag>
      </template>

      <template #enabled="{ row }">
        <Tag :color="row.isEnabled ? 'success' : 'default'">
          {{ row.isEnabled ? '启用' : '禁用' }}
        </Tag>
      </template>

      <template #action="{ row }">
        <Space wrap>
          <Button type="primary" @click="handleRun(row)" :disabled="!row.isEnabled">
            运行
          </Button>
          <Button @click="handleTriggerBuild(row)" :disabled="!row.isEnabled">
            构建审批
          </Button>
          <Button @click="handleShowVersions(row)">版本</Button>
          <Button @click="handleEdit(row)">编辑</Button>
          <Popconfirm title="确认删除？" @confirm="handleDel(row.id)">
            <Button danger>删除</Button>
          </Popconfirm>
        </Space>
      </template>
    </Grid>

    <!-- 新增/编辑 Modal -->
    <Modal
      v-model:open="modalVisible"
      :title="modalTitle"
      width="720px"
      :confirm-loading="formLoading"
      @ok="handleSave"
    >
      <Form :model="formData" layout="vertical" style="max-height: 60vh; overflow-y: auto;">
        <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 0 16px;">
          <FormItem label="配置名称" required>
            <Input v-model:value="formData.name" placeholder="如：前端-测试环境" />
          </FormItem>
          <FormItem label="所属项目" required>
            <Select
              v-model:value="formData.projectId"
              :options="projectOptions"
              placeholder="请选择项目"
              show-search
              :filter-option="(input: string, option: any) => option.label.toLowerCase().includes(input.toLowerCase())"
              @change="handleProjectChange"
            />
          </FormItem>
          <FormItem label="归属子项目">
            <Select
              v-model:value="formData.subProjectId"
              :options="subProjectOptions"
              placeholder="发布单元(留空=项目级方案)"
              allow-clear
              show-search
              :filter-option="(input: string, option: any) => option.label.toLowerCase().includes(input.toLowerCase())"
              @change="handleSubProjectChange"
            />
          </FormItem>
          <FormItem label="产物来源" required>
            <Select v-model:value="formData.buildSource" :options="toOptions(buildSourceLabels)" />
          </FormItem>
          <FormItem
            :label="formData.buildSource === 'LocalBuild' ? '关联仓库(本机构建可选)' : '关联仓库'"
            :required="formData.buildSource !== 'LocalBuild'"
          >
            <Select v-model:value="formData.gitRepositoryId" :options="repoOptions" placeholder="请选择仓库" allow-clear />
          </FormItem>
          <FormItem label="默认分支">
            <Input v-model:value="formData.defaultBranch" placeholder="main" />
          </FormItem>
          <FormItem label="关联环境">
            <Select v-model:value="formData.environmentId" :options="envOptions" placeholder="请选择环境" allow-clear />
          </FormItem>
          <FormItem label="版本策略">
            <Select v-model:value="formData.versionStrategy" :options="toOptions(versionStrategyLabels)" />
          </FormItem>
          <FormItem label="触发方式">
            <Select v-model:value="formData.triggerMode" :options="toOptions(triggerModeLabels)" />
          </FormItem>
          <FormItem label="部署模式">
            <Select v-model:value="formData.deployMode" :options="toOptions(deployModeLabels)" />
          </FormItem>
        </div>

        <!-- 本机构建参数 -->
        <template v-if="formData.buildSource === 'LocalBuild'">
          <FormItem label="构建命令">
            <Textarea
              v-model:value="formData.buildCommand"
              :rows="2"
              placeholder="在子项目工作目录执行,如：pnpm install && pnpm build  或  dotnet publish -c Release -o publish"
            />
            <div style="margin-top: 4px; font-size: 12px; color: var(--ant-color-text-tertiary);">
              工作目录优先取子项目 LocalPath，其次项目 LocalPath。
            </div>
          </FormItem>
          <FormItem label="产物目录">
            <Input v-model:value="formData.artifactDir" placeholder="相对工作目录,整目录打包为 zip 部署,如：publish 或 dist" />
          </FormItem>
        </template>

        <!-- 部署策略 / 审批(仅在启用部署时) -->
        <div v-if="formData.deployMode !== 'None'" style="display: grid; grid-template-columns: 1fr 1fr; gap: 0 16px;">
          <FormItem label="部署策略">
            <Select v-model:value="formData.deployStrategy" :options="toOptions(deployStrategyLabels)" />
          </FormItem>
          <FormItem label="部署需人工审批">
            <Switch v-model:checked="formData.requireApproval" />
            <span style="margin-left: 8px; font-size: 12px; color: var(--ant-color-text-tertiary);">
              {{ formData.requireApproval ? '构建后挂起待审批' : '一键直发,无人工卡点' }}
            </span>
          </FormItem>
        </div>

        <div v-if="formData.deployMode === 'SbpmDeploy'" style="display: grid; grid-template-columns: 1fr 1fr; gap: 0 16px;">
          <FormItem label="目标服务器">
            <Select
              v-model:value="formData.deployServerIds"
              :options="serverOptions"
              mode="multiple"
              placeholder="选择部署目标服务器"
            />
          </FormItem>
          <FormItem label="Artifact Job 名称">
            <Input v-model:value="formData.artifactJobName" placeholder="GitLab CI 中的 Job 名" />
          </FormItem>
        </div>

        <div v-if="formData.deployMode === 'GitLabDeploy'" style="display: grid; grid-template-columns: 1fr 1fr; gap: 0 16px;">
          <FormItem label="Deploy Pipeline Ref">
            <Input v-model:value="formData.deployPipelineRef" placeholder="deploy Pipeline 触发分支" />
          </FormItem>
        </div>

        <div style="display: grid; grid-template-columns: 1fr 1fr 1fr; gap: 0 16px;">
          <FormItem label="构建超时(分钟)">
            <InputNumber v-model:value="formData.buildTimeout" :min="5" :max="120" style="width: 100%;" />
          </FormItem>
          <FormItem label="构建成功自动部署">
            <Switch v-model:checked="formData.autoDeployOnSuccess" :disabled="formData.deployMode === 'None'" />
          </FormItem>
          <FormItem label="启用">
            <Switch v-model:checked="formData.isEnabled" />
          </FormItem>
        </div>

        <!-- Pipeline 变量 -->
        <FormItem label="Pipeline 变量">
          <div
            v-for="(_, index) in variableKeys"
            :key="index"
            style="display: flex; gap: 8px; margin-bottom: 8px;"
          >
            <Input v-model:value="variableKeys[index]" placeholder="变量名" style="width: 40%;" />
            <Input v-model:value="variableValues[index]" placeholder="值" style="flex: 1;" />
            <Button danger size="small" @click="removeVariable(index)">删除</Button>
          </div>
          <Button size="small" @click="addVariable">添加变量</Button>
        </FormItem>
      </Form>
    </Modal>

    <!-- 触发构建 Modal -->
    <Modal
      v-model:open="buildModalVisible"
      title="触发构建"
      @ok="submitTriggerBuild"
    >
      <Form :model="buildForm" layout="vertical">
        <FormItem label="分支">
          <Input v-model:value="buildForm.branch" placeholder="默认使用预设分支" />
        </FormItem>
        <FormItem label="版本号">
          <Input v-model:value="buildForm.version" placeholder="留空则按策略自动生成" />
        </FormItem>
      </Form>
    </Modal>

    <!-- 一键运行进度 Drawer -->
    <Drawer
      :open="runDrawerOpen"
      :title="`运行进度 · ${runProfile?.name || ''}`"
      width="640"
      @close="onRunDrawerClose"
    >
      <div v-if="runStatus">
        <Space style="margin-bottom: 16px">
          <Tag color="blue">{{ runStatus.branch }}</Tag>
          <span>版本 <code>{{ runStatus.version }}</code></span>
        </Space>

        <!-- ① 构建 -->
        <div style="margin-bottom: 12px; padding: 12px; border: 1px solid var(--ant-color-border); border-radius: 6px">
          <div style="display: flex; gap: 8px; align-items: center; margin-bottom: 8px">
            <span style="font-weight: 600">① 构建</span>
            <Tag :color="buildStatusMeta[runStatus.status]?.color || 'default'">
              {{ buildStatusMeta[runStatus.status]?.text || runStatus.status }}
            </Tag>
            <span v-if="runStatus.duration" style="font-size: 12px; color: var(--ant-color-text-tertiary)">{{ runStatus.duration }}s</span>
            <template v-if="runStatus.status === 'PendingApproval'">
              <Button type="primary" size="small" @click="approveBuild">通过</Button>
              <Button danger size="small" @click="rejectBuild">驳回</Button>
            </template>
          </div>
          <pre v-if="runStatus.build_log" style="max-height: 200px; overflow: auto; margin: 0; padding: 8px; font-size: 12px; background: var(--ant-color-fill-quaternary); border-radius: 4px; white-space: pre-wrap">{{ runStatus.build_log }}</pre>
        </div>

        <!-- ② 部署 -->
        <div style="padding: 12px; border: 1px solid var(--ant-color-border); border-radius: 6px">
          <div style="display: flex; gap: 8px; align-items: center; margin-bottom: 8px">
            <span style="font-weight: 600">② 部署</span>
            <Tag v-if="runStatus.deploy_status" :color="deployStatusMeta[runStatus.deploy_status]?.color || 'default'">
              {{ deployStatusMeta[runStatus.deploy_status]?.text || runStatus.deploy_status }}
            </Tag>
            <span v-else style="font-size: 12px; color: var(--ant-color-text-tertiary)">
              {{ runProfile?.deployMode === 'None' ? '纯构建方案,无部署' : (runStatus.status === 'Success' ? '准备中…' : '等待构建完成') }}
            </span>
            <template v-if="runStatus.deploy_status === 'Pending'">
              <Button type="primary" size="small" @click="approveDeploy">通过</Button>
              <Button danger size="small" @click="rejectDeploy">驳回</Button>
            </template>
          </div>
          <pre v-if="runStatus.deploy_log" style="max-height: 240px; overflow: auto; margin: 0; padding: 8px; font-size: 12px; background: var(--ant-color-fill-quaternary); border-radius: 4px; white-space: pre-wrap">{{ runStatus.deploy_log }}</pre>
        </div>
      </div>
    </Drawer>

    <!-- 版本历史 Drawer -->
    <Drawer
      v-model:open="versionDrawerVisible"
      :title="versionDrawerTitle"
      width="520"
      :loading="versionLoading"
    >
      <div v-if="versionList.length === 0" style=" padding: 40px 0; color: var(--ant-color-text-tertiary);text-align: center;">
        暂无版本记录
      </div>
      <Timeline v-else>
        <TimelineItem
          v-for="v in versionList"
          :key="v.build_id"
          :color="v.build_status === 'Success' ? 'green' : v.build_status === 'Failed' ? 'red' : 'blue'"
        >
          <div style="display: flex; align-items: center; justify-content: space-between;">
            <div>
              <strong style="font-size: 15px;">v{{ v.version }}</strong>
              <span style="margin-left: 8px; font-family: monospace; font-size: 12px; color: var(--ant-color-text-tertiary);">
                {{ v.commit_sha ? v.commit_sha.substring(0, 8) : '' }}
              </span>
            </div>
            <Button
              v-if="v.can_rollback"
              size="small"
              @click="handleRollback(v.version)"
            >
              回滚到此版本
            </Button>
          </div>
          <div style="margin-top: 4px; font-size: 12px; color: var(--ant-color-text-secondary);">
            <Space>
              <span>分支: {{ v.branch }}</span>
              <Tag :color="buildStatusColorMap[v.build_status] || 'default'" style="margin: 0;">
                构建{{ v.build_status === 'Success' ? '成功' : v.build_status === 'Failed' ? '失败' : v.build_status }}
              </Tag>
              <Tag v-if="v.deploy_status" :color="deployStatusColorMap[v.deploy_status] || 'default'" style="margin: 0;">
                部署{{ v.deploy_status === 'Success' ? '成功' : v.deploy_status === 'Failed' ? '失败' : v.deploy_status }}
              </Tag>
              <template v-if="v.build_status === 'PendingApproval'">
                <Button type="primary" size="small" @click="approveBuildAt(v.build_id)">批构建</Button>
                <Button danger size="small" @click="rejectBuildAt(v.build_id)">驳回</Button>
              </template>
              <template v-if="v.deploy_status === 'Pending' && v.deploy_id">
                <Button type="primary" size="small" @click="approveDeployAt(v.deploy_id)">批部署</Button>
                <Button danger size="small" @click="rejectDeployAt(v.deploy_id)">驳回</Button>
              </template>
            </Space>
          </div>
          <div style="margin-top: 2px; font-size: 11px; color: var(--ant-color-text-quaternary);">
            构建: {{ formatTime(v.built_at) }}
            <span v-if="v.deployed_at"> | 部署: {{ formatTime(v.deployed_at) }}</span>
          </div>
        </TimelineItem>
      </Timeline>
    </Drawer>
  </Page>
</template>

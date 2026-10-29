<script lang="ts" setup>
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { usePreferences } from '@vben/preferences';
import { useAppConfig } from '@vben/hooks';
import { MdPreview } from 'md-editor-v3';
import 'md-editor-v3/lib/preview.css';

import { Page } from '@vben/common-ui';

import {
  Button,
  Card,
  Checkbox,
  DatePicker,
  Descriptions,
  DescriptionsItem,
  Empty,
  Input,
  InputNumber,
  List,
  ListItem,
  message,
  Modal,
  Popconfirm,
  Progress,
  Select,
  Space,
  Spin,
  Table,
  Tabs,
  TabPane,
  Tag,
  Textarea,
  Tooltip,
  Tree,
  TreeSelect,
  Drawer,
} from 'ant-design-vue';

import {
  projectStatusLabels,
  requirementStatusLabels,
  requirementTypeLabels,
  toOptions,
} from '#/utils/enum-labels';

import {
  getDetail,
  getOverview,
  getActivityList,
  getEnvironmentList,
  getConfigList,
  postConfigDecryptValue,
  postConfigSave,
  getSubProjectList,
  getSubProjectDetail,
  getSubProjectCommits,
  postSubProjectSyncCode,
  postSubProjectGitCommit,
  postSubProjectGitPush,
  getGitLabGroupProjects,
  postSubProjectSaveList,
} from '../api';

import {
  getList as getMindMapList,
  getDetail as getMindMapDetail,
} from '../../mindmap/api';

import SubProjectDeployButton from './SubProjectDeployButton.vue';

import { postAiExecute } from '../../ai/api';

import {
  getGroupList as getChatGroupList,
  postGroupEdit as postChatGroupEdit,
  postGroupDel as postChatGroupDel,
  getUserList as getChatUserList,
  postUserDel as postChatUserDel,
  getRequirementList as getChatRequirementList,
  postRequirementAnalyze as postChatAnalyze,
  postRequirementSendToGroup as postChatSendToGroup,
  postRequirementConfirm as postChatReqConfirm,
  postRequirementReject as postChatReqReject,
} from '../../chat/api';

import {
  getList as getDocList,
  postAdd as postDocAdd,
  postEdit as postDocEdit,
  postDel as postDocDel,
} from '../../document/api';

import { uploadFileApi } from '#/api/core/admin';
import { Image as AImage, Upload as AUpload, Upload } from 'ant-design-vue';
import GroupBindModal from '../../chat/components/GroupBindModal.vue';
import UserBindModal from '../../chat/components/UserBindModal.vue';
import ChatWindow from '../../chat/components/ChatWindow.vue';
import RequirementInfoCell from '#/components/business/RequirementInfoCell.vue';
import TaskItemInfoCell from '#/components/business/TaskItemInfoCell.vue';

import {
  getTree as getFeatureTree,
  getDetail as getFeatureDetail,
  postAdd as postFeatureAdd,
  postEdit as postFeatureEdit,
  postDel as postFeatureDel,
  getRelations as getFeatureRelations,
  saveRelations as saveFeatureRelations,
  postSyncMap as syncFeatureMap,
} from '../../feature-module/api';

import {
  getList as getRequirementList,
  postAdd as postRequirementAdd,
  postEdit as postRequirementEdit,
  postDel as postRequirementDel,
  postAnalyzeAndSplit,
  getStagingList,
  postEditStaging,
  postConfirmSingle as postReqConfirmSingle,
  postConfirmAll as postReqConfirmAll,
  postRejectSingle,
  postRestoreSingle,
} from '../../requirement/api';

import {
  getList as getTaskList,
} from '../../task-item/api';

import {
  taskItemTypeLabels, taskItemClaimStatusLabels, taskItemExecStatusLabels,
} from '#/utils/enum-labels';

import DocPreview from '#/components/DocPreview/index.vue';

import {
  getRepoList,
  getRepoCommits,
  postRepoSync,
} from '../../git/api';

import {
  getProjectServerList,
  postProjectServerAdd,
  postProjectServerEdit,
  postProjectServerDel,
  getServerSimpleList,
  getEnvironmentList as getEnvListForDeploy,
} from '../api';

import {
  deployMethodLabels,
  serverStatusLabels,
} from '#/utils/enum-labels';

const route = useRoute();
const router = useRouter();
const { isDark } = usePreferences();
const descExpanded = ref(false);

const id = ref<number>(0);
const loading = ref(false);
const activeTab = ref('overview');

// Project detail
const project = ref<any>({});

// Overview
const overview = ref<any>({});
const activities = ref<any[]>([]);

// Git
const gitRepos = ref<any[]>([]);
const gitReposLoaded = ref(false);
const gitCommits = ref<Record<number, any[]>>({});
const gitTabLoaded = ref(false);

// Config
const configs = ref<any[]>([]);
const configLoaded = ref(false);
const configSaving = ref(false);
const decryptedValues = ref<Record<number, string>>({});

// Requirement
const requirements = ref<any[]>([]);
const reqLoaded = ref(false);
const reqModalVisible = ref(false);
const reqModalTitle = ref('新增需求');

// AI 分析拆分
const aiAnalyzeVisible = ref(false);
const aiAnalyzeContent = ref('');
const aiAnalyzing = ref(false);
const aiAnalyzeBatchId = ref('');
const aiStagingList = ref<any[]>([]);
const aiEditingRow = ref<any>(null);
const reqSaving = ref(false);
const reqForm = ref<any>({});

// SubProject
const subProjects = ref<any[]>([]);
const subProjectLoaded = ref(false);
const subProjectDetails = ref<Record<number, any>>({});
const subProjectDetailLoading = ref<Record<number, boolean>>({});
const subProjectTab = ref<Record<number, string>>({});
const subProjectCommits = ref<Record<number, any[]>>({});

// Git 操作状态
const syncingCode = ref<Record<number, boolean>>({});
const commitModalVisible = ref(false);
const commitModalSpId = ref(0);
const commitMessage = ref('');
const committing = ref(false);
const pushing = ref<Record<number, boolean>>({});

async function loadSubProjectDetail(spId: number) {
  subProjectDetailLoading.value[spId] = true;
  try {
    const detail: any = await getSubProjectDetail(spId);
    subProjectDetails.value[spId] = detail;
  } catch { }
  subProjectDetailLoading.value[spId] = false;
}

async function handleSyncCode(sp: any) {
  syncingCode.value[sp.id] = true;
  try {
    const res: any = await postSubProjectSyncCode(sp.id);
    message.success(`同步完成: ${res.branch} - ${res.commit}`);
    sp.localPath = res.localPath;
    await loadSubProjectDetail(sp.id);
  } catch { }
  syncingCode.value[sp.id] = false;
}

function openCommitModal(spId: number) {
  commitModalSpId.value = spId;
  commitMessage.value = '';
  commitModalVisible.value = true;
}

async function handleCommit() {
  if (!commitMessage.value.trim()) { message.warning('请输入提交信息'); return; }
  committing.value = true;
  try {
    await postSubProjectGitCommit(commitModalSpId.value, commitMessage.value);
    commitModalVisible.value = false;
    await loadSubProjectDetail(commitModalSpId.value);
  } catch { }
  committing.value = false;
}

async function handlePush(spId: number) {
  pushing.value[spId] = true;
  try {
    await postSubProjectGitPush(spId);
    await loadSubProjectDetail(spId);
  } catch { }
  pushing.value[spId] = false;
}

// Environment
const environments = ref<any[]>([]);
const envLoaded = ref(false);

// Deploy Config (ProjectServer)
const deployConfigs = ref<any[]>([]);
const deployLoaded = ref(false);
const deployModalVisible = ref(false);
const deployModalTitle = ref('新增部署配置');
const deploySaving = ref(false);
const deployForm = ref<any>({});
const serverOptions = ref<any[]>([]);
const serverMap = ref<Record<number, any>>({});
const envOptions = ref<any[]>([]);

// 部署方式可拖拽排序列表
const allDeployMethods = Object.entries(deployMethodLabels).map(([value, label]) => ({ value, label }));
const deployMethodOrder = ref<{ value: string; label: string }[]>([]);
const deployMethodListEl = ref<HTMLElement | null>(null);

function initDeployMethodOrder() {
  // 从 availableDeployMethods 解析已有顺序
  const existing = deployForm.value.availableDeployMethods;
  if (existing) {
    const keys = existing.split(',').map((s: string) => s.trim()).filter(Boolean);
    const ordered = keys
      .map((k: string) => allDeployMethods.find(m => m.value === k))
      .filter(Boolean) as { value: string; label: string }[];
    // 追加未选中的
    const usedKeys = new Set(keys);
    const rest = allDeployMethods.filter(m => !usedKeys.has(m.value));
    deployMethodOrder.value = [...ordered, ...rest];
  } else {
    deployMethodOrder.value = [...allDeployMethods];
  }
}

function moveDeployMethod(index: number, direction: -1 | 1) {
  const target = index + direction;
  if (target < 0 || target >= deployMethodOrder.value.length) return;
  const arr = [...deployMethodOrder.value];
  [arr[index]!, arr[target]!] = [arr[target]!, arr[index]!];
  deployMethodOrder.value = arr;
  syncDeployMethodsToForm();
}

function syncDeployMethodsToForm() {
  deployForm.value.availableDeployMethods = deployMethodOrder.value.map(m => m.value).join(',');
  deployForm.value.preferredDeployMethod = deployMethodOrder.value[0]?.value || null;
}

async function loadDeployConfigs() {
  if (deployLoaded.value) return;
  const [res, srvRes, envRes]: any[] = await Promise.all([
    getProjectServerList({ projectId: id.value, pageSize: 100 }),
    getServerSimpleList(),
    getEnvListForDeploy({ projectId: id.value }),
  ]);
  deployConfigs.value = res?.items || [];
  const servers = Array.isArray(srvRes) ? srvRes : [];
  serverOptions.value = servers.map((s: any) => ({ label: `${s.name} (${s.host})`, value: s.id }));
  serverMap.value = Object.fromEntries(servers.map((s: any) => [s.id, s]));
  envOptions.value = (envRes?.items || []).map((e: any) => ({ label: e.name, value: e.id }));
  deployLoaded.value = true;
}

function onServerChange(serverId: number) {
  const srv = serverMap.value[serverId];
  if (!srv) return;
  // 仅新增时或字段为空时自动填充，不覆盖已有值
  const f = deployForm.value;
  if (!f.databaseName && srv.dbType) f.databaseName = '';
  if (!f.btSiteName && srv.btPanelUrl) f.btSiteName = '';
  if (srv.sshPort && !f.apiPort) f.apiPort = srv.sshPort === 22 ? null : null;
  // 根据服务器能力自动设置可用部署方式
  const methods: string[] = [];
  if (srv.btPanelUrl) methods.push('BuiltInApi', 'BtPanel');
  if (srv.ftpHost) methods.push('FTP');
  if (srv.host) methods.push('SSH');
  methods.push('Manual');
  if (!f.availableDeployMethods) {
    f.availableDeployMethods = methods.join(',');
    f.preferredDeployMethod = methods[0] || null;
    initDeployMethodOrder();
  }
}

function handleDeployAdd() {
  deployForm.value = { projectId: id.value };
  deployModalTitle.value = '新增部署配置';
  initDeployMethodOrder();
  deployModalVisible.value = true;
}

function handleDeployEdit(row: any) {
  deployForm.value = { ...row };
  deployModalTitle.value = '编辑部署配置';
  initDeployMethodOrder();
  deployModalVisible.value = true;
}

async function handleDeploySave() {
  deploySaving.value = true;
  try {
    syncDeployMethodsToForm();
    const fn = deployForm.value.id ? postProjectServerEdit : postProjectServerAdd;
    const res: any = await fn({ ...deployForm.value, projectId: id.value });
    if (res.code === 0) {
      message.success('保存成功');
      deployModalVisible.value = false;
      deployLoaded.value = false;
      await loadDeployConfigs();
    } else {
      message.error(res.message);
    }
  } finally {
    deploySaving.value = false;
  }
}

async function handleDeployDel(delId: number) {
  await postProjectServerDel(delId);
  message.success('删除成功');
  deployLoaded.value = false;
  await loadDeployConfigs();
}

let deploySortableInstance: any = null;
watch(deployModalVisible, (visible) => {
  if (visible) {
    nextTick(async () => {
      if (deployMethodListEl.value && !deploySortableInstance) {
        const Sortable = (await import('sortablejs')).default;
        deploySortableInstance = Sortable.create(deployMethodListEl.value!, {
          animation: 150,
          handle: '.deploy-drag-handle',
          onEnd(evt: any) {
            const arr = [...deployMethodOrder.value];
            const [moved] = arr.splice(evt.oldIndex!, 1);
            arr.splice(evt.newIndex!, 0, moved!);
            deployMethodOrder.value = arr;
            syncDeployMethodsToForm();
          },
        });
      }
    });
  } else {
    deploySortableInstance?.destroy();
    deploySortableInstance = null;
  }
});

// ---- Color Maps ----

const statusColorMap: Record<string, string> = {
  Planning: 'blue', Active: 'green', OnHold: 'orange', Completed: 'cyan', Archived: 'default',
};
const reqStatusColorMap: Record<string, string> = {
  Draft: 'default', Todo: 'blue', InProgress: 'processing', Testing: 'orange', Done: 'success', Closed: 'default',
};
const reqProgressMap: Record<string, number> = {
  Draft: 0, Todo: 0, InProgress: 40, Testing: 75, Done: 100, Closed: 100,
};

// ---- Init ----

onMounted(async () => {
  id.value = Number(route.query.id) || 0;
  if (id.value > 0) {
    await loadProject();
  }
});

async function loadProject() {
  loading.value = true;
  try {
    project.value = await getDetail(id.value);
    await Promise.all([loadOverview(), loadGitRepos(), loadSubProjects()]);
  } finally {
    loading.value = false;
  }
}

async function loadOverview() {
  overview.value = await getOverview(id.value);
  const activityResult: any = await getActivityList({
    projectId: id.value, page: 1, pageSize: 10,
  });
  activities.value = activityResult?.items || [];
}

// ---- Git ----

const gitStatus = computed(() => {
  if (!gitReposLoaded.value) return null;
  const hasSubRepos = subProjects.value.some((sp: any) => sp.gitlabProjectUrl);
  if (gitRepos.value.length === 0 && !hasSubRepos) return { text: '仓库未初始化', color: 'default' };
  if (gitRepos.value.some((r: any) => r.syncStatus === 'Error')) return { text: '同步异常', color: 'red' };
  if (gitRepos.value.some((r: any) => r.syncStatus === 'Syncing')) return { text: '同步中', color: 'blue' };
  const repoCount = gitRepos.value.length + subProjects.value.filter((sp: any) => sp.gitlabProjectUrl).length;
  return { text: `${repoCount} 个仓库`, color: 'green' };
});

async function loadGitRepos() {
  try {
    const result: any = await getRepoList({ projectId: id.value, page: 1, pageSize: 50 });
    gitRepos.value = result?.items || [];
  } catch {
    gitRepos.value = [];
  }
  gitReposLoaded.value = true;
}

async function loadGitTab() {
  if (gitTabLoaded.value) return;
  if (!gitReposLoaded.value) await loadGitRepos();
  // 并行加载所有仓库的最近提交
  await Promise.all(
    gitRepos.value.map(async (repo: any) => {
      try {
        const commits: any = await getRepoCommits(repo.id);
        gitCommits.value[repo.id] = Array.isArray(commits) ? commits : [];
      } catch {
        gitCommits.value[repo.id] = [];
      }
    }),
  );
  gitTabLoaded.value = true;
}

async function handleRepoSync(repoId: number) {
  try {
    await postRepoSync(repoId);
    await loadGitRepos();
  } catch {
    // API wrapper 已处理
  }
}

// ---- Config ----

const configColumns = [
  { title: '分类', dataIndex: ['category', 'name'], key: 'category', width: 120 },
  { title: '配置键', dataIndex: 'configKey', key: 'configKey', width: 160 },
  { title: '配置名称', dataIndex: 'configName', key: 'configName', width: 140 },
  { title: '配置值', dataIndex: 'configValue', key: 'configValue' },
  { title: '敏感', dataIndex: 'isSensitive', key: 'isSensitive', width: 80, align: 'center' as const },
];

async function loadConfigs() {
  const result: any = await getConfigList({ projectId: id.value });
  configs.value = Array.isArray(result) ? result : (result?.items || []);
  decryptedValues.value = {};
  configLoaded.value = true;
}

async function handleDecrypt(configId: number) {
  const result: any = await postConfigDecryptValue(configId);
  const value = result?.value || result || '';
  decryptedValues.value[configId] = value;
  const config = configs.value.find((c: any) => c.id === configId);
  if (config) config.configValue = value;
}

async function handleConfigSave() {
  const toSave = configs.value.map((item: any) => ({
    id: item.id, projectId: id.value, environmentId: item.environmentId,
    categoryId: item.categoryId, configKey: item.configKey, configName: item.configName,
    configValue: item.configValue, isSensitive: item.isSensitive,
  }));
  configSaving.value = true;
  try { await postConfigSave(toSave); } catch {} finally { configSaving.value = false; }
}

// ---- Requirement ----

const reqStatusOptions = toOptions(requirementStatusLabels);
const reqTypeOptions = toOptions(requirementTypeLabels);
const reqColumns = [
  { title: '信息', key: 'info' },
  { title: '状态', key: 'status', width: 260 },
  { title: '操作', key: 'action', width: 180 },
];

async function loadRequirements() {
  const result: any = await getRequirementList({ projectId: id.value, page: 1, pageSize: 200 });
  requirements.value = result?.items || [];
  reqLoaded.value = true;
}

function resetReqForm() {
  reqForm.value = {
    projectId: id.value, title: '', content: '', type: 'Feature',
    status: 'Draft', assignee: '',
    estimatedHours: undefined, actualHours: undefined, dueDate: undefined,
  };
}
function handleReqAdd() {
  resetReqForm();
  reqModalTitle.value = '新增需求';
  reqFeatureModuleIds.value = [];
  loadFeatureTree();
  reqModalVisible.value = true;
}
function handleReqEdit(record: any) {
  reqForm.value = { ...record };
  reqModalTitle.value = '编辑需求';
  loadFeatureTree();
  if (record.id) loadReqFeatureRelations(record.id);
  else reqFeatureModuleIds.value = [];
  reqModalVisible.value = true;
}

async function handleReqSubmit() {
  if (!reqForm.value.title) { message.warning('请输入需求标题'); return; }
  reqSaving.value = true;
  try {
    let reqId = reqForm.value.id;
    if (reqId) {
      await postRequirementEdit(reqForm.value);
    } else {
      const result: any = await postRequirementAdd(reqForm.value);
      reqId = result?.id;
    }
    // 保存功能模块关联
    if (reqId && reqFeatureModuleIds.value.length > 0) {
      await saveReqFeatureRelations(reqId);
    }
    reqModalVisible.value = false;
    await loadRequirements();
  } catch {} finally { reqSaving.value = false; }
}

async function handleReqDel(reqId: number) {
  try { await postRequirementDel(reqId); await loadRequirements(); } catch {}
}

function handleReqToAi(record: any) {
  activeTab.value = 'ai';
  aiTaskType.value = 'generate';
  aiInput.value = `请根据以下需求编写代码：\n\n标题: ${record.title}\n类型: ${record.type}\n\n${record.content || ''}`;
}

// AI 分析拆分需求
function openAiAnalyze() {
  aiAnalyzeVisible.value = true;
  aiAnalyzeContent.value = '';
  aiAnalyzeBatchId.value = '';
  aiStagingList.value = [];
  aiAnalyzing.value = false;
}

async function handleAiAnalyze() {
  if (!aiAnalyzeContent.value.trim()) { message.warning('请粘贴要分析的内容'); return; }
  aiAnalyzing.value = true;
  aiStagingList.value = [];
  try {
    const res: any = await postAnalyzeAndSplit(id.value, aiAnalyzeContent.value);
    aiAnalyzeBatchId.value = res.batchId;
    // 加载分析结果
    await refreshStagingList();
  } catch { }
  aiAnalyzing.value = false;
}

async function refreshStagingList() {
  if (!aiAnalyzeBatchId.value) return;
  const list: any = await getStagingList(aiAnalyzeBatchId.value);
  aiStagingList.value = Array.isArray(list) ? list : [];
}

async function handleStagingReject(row: any) {
  await postRejectSingle(row.id);
  row.isDeleted = true;
}

async function handleStagingRestore(row: any) {
  await postRestoreSingle(row.id);
  row.isDeleted = false;
}

async function handleStagingConfirmSingle(row: any) {
  await postReqConfirmSingle(row.id);
  row.isConfirmed = true;
  message.success('已确认');
}

async function handleStagingConfirmAll() {
  await postReqConfirmAll(aiAnalyzeBatchId.value);
  await refreshStagingList();
  message.success('已全部确认');
  reqLoaded.value = false;
  await loadRequirements();
}

function startEditStaging(row: any) {
  aiEditingRow.value = { ...row };
}

async function saveEditStaging() {
  if (!aiEditingRow.value) return;
  const r = aiEditingRow.value;
  await postEditStaging({ id: r.id, title: r.title, content: r.content, type: r.type });
  const target = aiStagingList.value.find((s: any) => s.id === r.id);
  if (target) { target.title = r.title; target.content = r.content; target.type = r.type; }
  aiEditingRow.value = null;
}

// 手动同步主导图
const syncingMindMap = ref(false);
async function handleSyncMindMap() {
  syncingMindMap.value = true;
  try {
    await import('../../mindmap/api').then(m => m.postSyncMain(id.value));
    message.success('导图同步成功');
    if (mindMapLoaded.value) { mindMapLoaded.value = false; loadMindMaps(); }
  } catch { /* handled */ } finally {
    syncingMindMap.value = false;
  }
}

// ---- SubProject ----

async function loadSubProjects() {
  subProjectLoaded.value = false;
  const result: any = await getSubProjectList({ projectId: id.value });
  subProjects.value = result?.items || [];
  subProjectLoaded.value = true;
  // 列表渲染后，逐个加载详情（含 git 状态 + 部署配置 + 最近提交）
  for (const sp of subProjects.value) {
    subProjectTab.value[sp.id] = 'info';
    loadSubProjectDetail(sp.id);
    getSubProjectCommits(sp.id).then((commits: any) => {
      subProjectCommits.value[sp.id] = commits || [];
    }).catch(() => {});
  }
}

// ---- 从 GitLab 群组探测子项目 ----

const detectModalVisible = ref(false);
const detectLoading = ref(false);
const detectSaving = ref(false);
// 每项: { id, name, path, httpUrlToRepo, webUrl, defaultBranch, exists, checked }
const detectList = ref<any[]>([]);

// 仅可添加（未存在）的项参与全选/计数
const selectableDetect = computed(() => detectList.value.filter((p: any) => !p.exists));
const allDetectChecked = computed(
  () => selectableDetect.value.length > 0 && selectableDetect.value.every((p: any) => p.checked),
);
const detectIndeterminate = computed(() => {
  const checked = selectableDetect.value.filter((p: any) => p.checked).length;
  return checked > 0 && checked < selectableDetect.value.length;
});
function toggleDetectAll(e: any) {
  const v = e.target.checked;
  for (const p of detectList.value) if (!p.exists) p.checked = v;
}

function calcSubLocalPath(code: string): string {
  const base = project.value?.localPath?.trim();
  return base && code ? `${base}/${code}` : '';
}

async function openDetectModal() {
  if (!project.value?.gitlabGroupId) {
    message.warning('当前项目未绑定 GitLab 群组，请先到编辑页选择群组');
    return;
  }
  detectModalVisible.value = true;
  detectLoading.value = true;
  detectList.value = [];
  try {
    const result: any = await getGitLabGroupProjects(project.value.gitlabGroupId, { perPage: 100 });
    // 已添加的子项目按 gitlabProjectId / 路径去重，置灰不可勾选
    const existingIds = new Set(subProjects.value.map((s: any) => s.gitlabProjectId).filter(Boolean));
    const existingPaths = new Set(subProjects.value.map((s: any) => s.gitlabProjectPath).filter(Boolean));
    detectList.value = (Array.isArray(result) ? result : []).map((p: any) => {
      const exists =
        (p.id && existingIds.has(p.id)) ||
        (p.pathWithNamespace && existingPaths.has(p.pathWithNamespace));
      return {
        id: p.id,
        name: p.name,
        path: p.pathWithNamespace,
        httpUrlToRepo: p.httpUrlToRepo,
        webUrl: p.webUrl,
        defaultBranch: p.defaultBranch || 'main',
        exists,
        checked: !exists, // 默认勾选未添加的
      };
    });
  } catch {
    detectList.value = [];
  } finally {
    detectLoading.value = false;
  }
}

async function handleDetectSave() {
  const picked = detectList.value.filter((p: any) => p.checked && !p.exists);
  if (picked.length === 0) {
    message.warning('请至少勾选一个要添加的子项目');
    return;
  }
  // SaveList 按 Id 增量 diff，会删掉未提交的项 —— 必须把已有子项目一并回传
  const existingPayload = subProjects.value.map((s: any) => ({
    id: s.id,
    name: s.name,
    code: s.code,
    description: s.description,
    baseRepoUrl: s.baseRepoUrl,
    gitlabProjectId: s.gitlabProjectId,
    gitlabProjectPath: s.gitlabProjectPath,
    gitlabProjectUrl: s.gitlabProjectUrl,
    defaultBranch: s.defaultBranch || 'main',
    localPath: s.localPath,
    sort: s.sort,
  }));
  const newPayload = picked.map((p: any, i: number) => {
    const code = p.path?.split('/').pop() || '';
    return {
      id: 0,
      name: p.name,
      code,
      description: '',
      baseRepoUrl: undefined,
      gitlabProjectId: p.id,
      gitlabProjectPath: p.path,
      gitlabProjectUrl: p.httpUrlToRepo || p.webUrl,
      defaultBranch: p.defaultBranch || 'main',
      localPath: calcSubLocalPath(code),
      sort: existingPayload.length + i,
    };
  });
  detectSaving.value = true;
  try {
    await postSubProjectSaveList(id.value, [...existingPayload, ...newPayload]);
    message.success(`已添加 ${newPayload.length} 个子项目`);
    detectModalVisible.value = false;
    subProjectLoaded.value = false;
    await loadSubProjects();
  } catch {
    /* error handled by api wrapper */
  } finally {
    detectSaving.value = false;
  }
}

function goToEditSubProjects() {
  router.push({ path: '/project/form', query: { id: String(id.value) } });
}

// ---- Environment ----

async function loadEnvironments() {
  const result: any = await getEnvironmentList({ projectId: id.value, page: 1, pageSize: 100 });
  environments.value = result?.items || [];
  envLoaded.value = true;
}

// ---- Task ----

const tasks = ref<any[]>([]);
const taskLoaded = ref(false);
const reqDocRef = ref<InstanceType<typeof DocPreview>>();
const taskDocRef = ref<InstanceType<typeof DocPreview>>();

const taskColumns = [
  { title: '信息', key: 'info' },
  { title: '状态', key: 'status', width: 280 },
  { title: '操作', key: 'action', width: 130 },
];

const taskClaimColorMap: Record<string, string> = { Pending: 'default', Claimed: 'blue' };
const taskExecColorMap: Record<string, string> = { Idle: 'default', Running: 'processing', Success: 'green', Failed: 'red', Timeout: 'orange', Hibernated: 'purple' };

async function loadTasks() {
  const result: any = await getTaskList({ projectId: id.value, page: 1, pageSize: 200 });
  tasks.value = result?.items || [];
  taskLoaded.value = true;
}

// ---- Feature Module ----

const featureTreeData = ref<any[]>([]);
const featureTreeLoaded = ref(false);
const reqFeatureModuleIds = ref<number[]>([]);

async function loadFeatureTree() {
  if (featureTreeLoaded.value || !id.value) return;
  try {
    const result: any = await getFeatureTree(id.value);
    featureTreeData.value = convertFeatureTree(Array.isArray(result) ? result : []);
    featureTreeLoaded.value = true;
  } catch { featureTreeData.value = []; }
}

function convertFeatureTree(nodes: any[]): any[] {
  return nodes.map((n: any) => ({
    value: n.id,
    title: n.name,
    children: n.children?.length > 0 ? convertFeatureTree(n.children) : undefined,
  }));
}

async function loadReqFeatureRelations(requirementId: number) {
  try {
    const ids: any = await getFeatureRelations(requirementId);
    reqFeatureModuleIds.value = Array.isArray(ids) ? ids : [];
  } catch { reqFeatureModuleIds.value = []; }
}

async function saveReqFeatureRelations(requirementId: number) {
  try {
    await saveFeatureRelations(requirementId, reqFeatureModuleIds.value);
  } catch { /* handled */ }
}

// 功能模块操作
const fmSelectedKey = ref<number | undefined>();
const fmNodeDetail = ref<any>(null);
const fmDetailLoading = ref(false);
const fmFormVisible = ref(false);
const fmFormTitle = ref('');
const fmFormData = ref<any>({});
const fmFormSaving = ref(false);
const fmExpandedKeys = ref<number[]>([]);

const fmStatusColorMap: Record<string, string> = {
  '规划中': 'default', '开发中': 'blue', '已上线': 'green', '部分上线': 'orange',
};

function handleFmSelect(keys: number[]) {
  if (keys.length > 0) {
    fmSelectedKey.value = keys[0];
    loadFmDetail(keys[0]!);
  }
}

async function loadFmDetail(nodeId: number) {
  fmDetailLoading.value = true;
  try { fmNodeDetail.value = await getFeatureDetail(nodeId); } finally { fmDetailLoading.value = false; }
}

function handleFmAddRoot() {
  fmFormTitle.value = '新增根模块';
  fmFormData.value = { projectId: id.value, parentId: null, name: '', description: '' };
  fmFormVisible.value = true;
}

function handleFmAddChild(parentId: number) {
  fmFormTitle.value = '新增子模块';
  fmFormData.value = { projectId: id.value, parentId, name: '', description: '' };
  fmFormVisible.value = true;
}

function handleFmEdit() {
  if (!fmNodeDetail.value) return;
  fmFormTitle.value = '编辑模块';
  fmFormData.value = { id: fmNodeDetail.value.id, projectId: fmNodeDetail.value.projectId, parentId: fmNodeDetail.value.parentId, name: fmNodeDetail.value.name, description: fmNodeDetail.value.description };
  fmFormVisible.value = true;
}

async function handleFmFormSubmit() {
  if (!fmFormData.value.name?.trim()) return message.warning('请输入模块名称');
  fmFormSaving.value = true;
  try {
    if (fmFormData.value.id) await postFeatureEdit(fmFormData.value);
    else await postFeatureAdd(fmFormData.value);
    fmFormVisible.value = false;
    featureTreeLoaded.value = false;
    await loadFeatureTree();
    if (fmSelectedKey.value) loadFmDetail(fmSelectedKey.value);
  } finally { fmFormSaving.value = false; }
}

async function handleFmDelete(nodeId: number) {
  await postFeatureDel(nodeId);
  fmSelectedKey.value = undefined;
  fmNodeDetail.value = null;
  featureTreeLoaded.value = false;
  await loadFeatureTree();
}

const syncing = ref(false);
async function handleSyncFeatureMap() {
  syncing.value = true;
  try {
    await syncFeatureMap(id.value);
    message.success('功能地图同步成功');
  } finally { syncing.value = false; }
}

// ---- AI Chat (in project detail) ----

const aiMessages = ref<any[]>([]);
const aiInput = ref('');
const aiSending = ref(false);
const aiTaskType = ref('chat');
const aiSelectedSubProject = ref<number | undefined>();
const aiChatAreaRef = ref<HTMLElement>();

const aiTaskTypes = [
  { key: 'chat', label: '对话', icon: '💬' },
  { key: 'analyze', label: '代码分析', icon: '🔍' },
  { key: 'generate', label: '编码', icon: '⚡' },
  { key: 'design', label: '设计', icon: '📐' },
  { key: 'review', label: 'Review', icon: '👁' },
  { key: 'extractfeaturerequirements', label: '提取需求', icon: '📋' },
  { key: 'analyzetaskprogress', label: '进度分析', icon: '📊' },
  { key: 'analyzerequirementcoverage', label: '覆盖度', icon: '🗺️' },
  { key: 'analyzedocuments', label: '文档分析', icon: '📄' },
];

async function handleAiSend() {
  const instruction = aiInput.value?.trim();
  if (!instruction || aiSending.value) return;

  aiMessages.value.push({ role: 'user', content: instruction });
  aiInput.value = '';
  aiMessages.value.push({ role: 'assistant', content: '', loading: true });
  aiSending.value = true;
  scrollAiToBottom();

  try {
    const result: any = await postAiExecute({
      projectId: id.value,
      subProjectId: aiSelectedSubProject.value || null,
      instruction,
      taskType: aiTaskType.value,
    });
    const lastIdx = aiMessages.value.length - 1;
    aiMessages.value[lastIdx] = {
      role: 'assistant',
      content: result.content || '（无返回内容）',
      model: result.modelUsed,
    };
  } catch (err: any) {
    const lastIdx = aiMessages.value.length - 1;
    aiMessages.value[lastIdx] = { role: 'assistant', content: err?.message || '请求失败', error: true };
  } finally {
    aiSending.value = false;
    scrollAiToBottom();
  }
}

function scrollAiToBottom() {
  nextTick(() => { if (aiChatAreaRef.value) aiChatAreaRef.value.scrollTop = aiChatAreaRef.value.scrollHeight; });
}

function simpleMarkdown(text: string): string {
  if (!text) return '';
  return text
    .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
    .replace(/```(\w*)\n([\s\S]*?)```/g, '<pre><code>$2</code></pre>')
    .replace(/`([^`]+)`/g, '<code>$1</code>')
    .replace(/\*\*(.+?)\*\*/g, '<strong>$1</strong>')
    .replace(/^### (.+)$/gm, '<h4>$1</h4>')
    .replace(/^## (.+)$/gm, '<h3>$1</h3>')
    .replace(/\n/g, '<br/>');
}

// ---- MindMap ----

const mindMapList = ref<any[]>([]);
const mindMapLoading = ref(false);
const mindMapActiveId = ref<number>(0);
const mindMapContainer = ref<HTMLElement>();
const mindMapLoaded = ref(false);
let mindMapInstance: any = null;

async function loadMindMaps() {
  if (mindMapLoaded.value) return;
  mindMapLoading.value = true;
  try {
    const result: any = await getMindMapList({ projectId: id.value, page: 1, pageSize: 100 });
    mindMapList.value = result?.items || [];
    if (mindMapList.value.length > 0) {
      await nextTick();
      await nextTick();
      await loadMindMapById(mindMapList.value[0]!.id);
    }
  } finally {
    mindMapLoading.value = false;
    mindMapLoaded.value = true;
  }
}

async function selectMindMap(mapId: number) {
  if (mindMapActiveId.value === mapId) return;
  mindMapLoading.value = true;
  try { await loadMindMapById(mapId); } finally { mindMapLoading.value = false; }
}

async function loadMindMapById(mapId: number) {
  mindMapActiveId.value = mapId;
  const detail: any = await getMindMapDetail(mapId);
  await nextTick();
  await new Promise((resolve) => setTimeout(resolve, 350));
  initMindMap(detail);
}

function convertLegacyData(data: any): any {
  if (data?.data?.text !== undefined) { stripNodeColors(data); return data; }
  if (data?.root) return convertNode(data.root);
  if (data?.text !== undefined) return convertNode(data);
  return { data: { text: '中心主题' }, children: [] };
}

function stripNodeColors(node: any) {
  if (node?.data) { delete node.data.fillColor; delete node.data.color; delete node.data.borderColor; delete node.data.fontWeight; delete node.data.fontSize; }
  if (Array.isArray(node?.children)) node.children.forEach(stripNodeColors);
}

function convertNode(node: any): any {
  const result: any = { data: { text: node.text || '' }, children: [] };
  if (node.status) result.data.tag = [node.status];
  if (Array.isArray(node.children)) result.children = node.children.map(convertNode);
  return result;
}

async function initMindMap(detail: any) {
  if (!mindMapContainer.value) return;
  destroyMindMap();
  let mapData: any;
  try {
    const parsed = typeof detail.mapData === 'string' ? JSON.parse(detail.mapData) : detail.mapData;
    mapData = convertLegacyData(parsed);
  } catch { mapData = { data: { text: detail.title || '中心主题' }, children: [] }; }

  const dark = isDark.value;
  const themeConfig = dark
    ? { backgroundColor: '#141414', lineColor: '#5bb59a', lineWidth: 2,
        root: { fillColor: '#177a56', color: '#ffffff', borderColor: '#1fa06e', borderWidth: 0, fontSize: 18, fontWeight: 'bold' },
        second: { fillColor: '#262626', color: '#f0f0f0', borderColor: '#5bb59a', borderWidth: 1, fontSize: 15, marginX: 80, marginY: 50 },
        node: { fillColor: '#1f1f1f', color: '#d9d9d9', borderColor: '#434343', borderWidth: 1, fontSize: 14, marginX: 40, marginY: 22 } }
    : { backgroundColor: '#ffffff', lineColor: '#409e85', lineWidth: 2,
        root: { fillColor: '#409e85', color: '#ffffff', borderColor: 'transparent', borderWidth: 0, fontSize: 18, fontWeight: 'bold' },
        second: { fillColor: '#f0f7f4', color: '#1a1a1a', borderColor: '#409e85', borderWidth: 1, fontSize: 15, marginX: 80, marginY: 50 },
        node: { fillColor: '#f5f5f5', color: '#262626', borderColor: '#d9d9d9', borderWidth: 1, fontSize: 14, marginX: 40, marginY: 22 } };

  const MindMap = (await import('simple-mind-map')).default;
  mindMapInstance = new MindMap({
    el: mindMapContainer.value, data: mapData, readonly: true,
    layout: 'logicalStructure', theme: 'default', themeConfig,
    scaleRatio: 0.1, mousewheelAction: 'zoom', enableFreeDrag: false,
  });
}

function destroyMindMap() {
  if (mindMapInstance) { mindMapInstance.destroy(); mindMapInstance = null; }
}

watch(isDark, () => {
  if (mindMapInstance && mindMapActiveId.value) {
    getMindMapDetail(mindMapActiveId.value).then(initMindMap);
  }
});

onBeforeUnmount(() => destroyMindMap());

// ---- Chat (聊天平台) ----

const chatLoaded = ref(false);
const chatGroups = ref<any[]>([]);
const chatUsers = ref<any[]>([]);
const chatRequirements = ref<any[]>([]);
const chatGroupModalOpen = ref(false);
const chatBindModalOpen = ref(false);
const chatDetailDrawerOpen = ref(false);
const chatAnalyzing = ref(false);
const chatWindowRef = ref<InstanceType<typeof ChatWindow>>();

async function loadChatData() {
  const pid = id.value;
  const [groupsRes, usersRes, reqsRes] = await Promise.all([
    getChatGroupList({ projectId: pid, pageSize: 100 }),
    getChatUserList({ projectId: pid, pageSize: 100 }),
    getChatRequirementList({ projectId: pid, pageSize: 50 }),
  ]);
  chatGroups.value = groupsRes?.items || [];
  chatUsers.value = usersRes?.items || [];
  chatRequirements.value = reqsRes?.items || [];
  chatWindowRef.value?.refresh();
  chatLoaded.value = true;
}


function handleChatGroupDel(gid: number) {
  postChatGroupDel(gid).then(() => { message.success('已删除'); loadChatData(); });
}

function handleChatGroupToggleAi(group: any) {
  postChatGroupEdit({ id: group.id, accountId: group.accountId, projectId: group.projectId, aiEnabled: !group.aiEnabled, aiRealtimeEnabled: group.aiRealtimeEnabled, status: group.status })
    .then(() => loadChatData());
}

function handleChatGroupToggleRealtime(group: any) {
  postChatGroupEdit({ id: group.id, accountId: group.accountId, projectId: group.projectId, aiEnabled: group.aiEnabled, aiRealtimeEnabled: !group.aiRealtimeEnabled, status: group.status })
    .then(() => loadChatData());
}


function handleChatUserDel(uid: number) {
  postChatUserDel(uid).then(() => { message.success('已解绑'); loadChatData(); });
}

async function handleChatAnalyze(groupBindingId: number) {
  chatAnalyzing.value = true;
  try {
    const res: any = await postChatAnalyze(groupBindingId);
    message.success(res?.message || '分析完成');
    loadChatData();
  } finally { chatAnalyzing.value = false; }
}

async function handleChatSendToGroup(groupBindingId: number) {
  await postChatSendToGroup(groupBindingId);
  message.success('已发送到群组');
  loadChatData();
}

function handleChatReqConfirm(reqId: number) {
  postChatReqConfirm(reqId, 1).then(() => { message.success('已确认'); loadChatData(); });
}

function handleChatReqReject(reqId: number) {
  postChatReqReject(reqId).then(() => { message.success('已拒绝'); loadChatData(); });
}

const chatGroupStatusLabels: Record<string, string> = { Pending: '待绑定', Active: '已绑定', Disabled: '已禁用' };
const chatGroupStatusColors: Record<string, string> = { Pending: 'orange', Active: 'green', Disabled: 'default' };
const chatUserRoleLabels: Record<string, string> = { Admin: '管理员', TeamMember: '团队成员', Client: '客户' };
const chatReqStatusLabels: Record<string, string> = { Draft: '草稿', PendingConfirm: '待确认', Confirmed: '已确认', Rejected: '已拒绝' };
const chatReqStatusColors: Record<string, string> = { Draft: 'default', PendingConfirm: 'orange', Confirmed: 'green', Rejected: 'red' };

// ---- 资源管理 ----
const docLoaded = ref(false);
const docList = ref<any[]>([]);
const docCategory = ref<string>('');
const docUploading = ref(false);
const docDrawerVisible = ref(false);
const docDrawerItem = ref<any>(null);
const docMetaEditing = ref(false);
const docMetaForm = ref<Record<string, any>>({});

// 批量上传
const uploadModalVisible = ref(false);
const uploadCategory = ref<string>('Inbox');
interface UploadFileItem {
  file: File;
  status: 'pending' | 'uploading' | 'done' | 'error';
  progress: number;
  error?: string;
}
const uploadFiles = ref<UploadFileItem[]>([]);
const isUploading = ref(false);
const { apiURL } = useAppConfig(import.meta.env, import.meta.env.PROD);

const docCategoryOptions = [
  { label: '待分类', value: 'Inbox' },
  { label: '设计图', value: 'Design' },
  { label: 'Logo', value: 'Logo' },
  { label: '修改文稿', value: 'Revision' },
  { label: '算法文档', value: 'Algorithm' },
  { label: '公告文档', value: 'Announcement' },
  { label: '合同', value: 'Contract' },
  { label: '其他', value: 'Other' },
];
const docCatColorMap: Record<string, string> = { Inbox: 'orange', Design: 'purple', Logo: 'magenta', Revision: 'blue', Algorithm: 'cyan', Announcement: 'green', Contract: 'gold', Other: 'default' };
const docCatLabelMap: Record<string, string> = Object.fromEntries(docCategoryOptions.map(o => [o.value, o.label]));
const docImageExts = new Set(['jpg', 'jpeg', 'png', 'gif', 'webp', 'svg', 'bmp']);

// 各分类的 metadata 字段定义
const docMetaFields: Record<string, { key: string; label: string; type: 'input' | 'select'; options?: string[] }[]> = {
  Design: [
    { key: 'status', label: '状态', type: 'select', options: ['待确认', '进行中', '已完成', '已拒绝'] },
    { key: 'page', label: '对应页面', type: 'input' },
    { key: 'version', label: '版本', type: 'input' },
  ],
  Logo: [
    { key: 'variants', label: '规格', type: 'input' },
    { key: 'format', label: '格式', type: 'input' },
  ],
  Revision: [
    { key: 'status', label: '状态', type: 'select', options: ['待处理', '已采纳', '已拒绝'] },
    { key: 'round', label: '轮次', type: 'input' },
  ],
  Algorithm: [
    { key: 'version', label: '版本', type: 'input' },
    { key: 'relatedModules', label: '关联模块', type: 'input' },
  ],
  Announcement: [
    { key: 'status', label: '状态', type: 'select', options: ['草稿', '已发布'] },
    { key: 'effectiveDate', label: '生效日期', type: 'input' },
  ],
  Contract: [
    { key: 'status', label: '状态', type: 'select', options: ['草稿', '已签署', '已过期'] },
    { key: 'party', label: '签约方', type: 'input' },
  ],
};

const docStatusColorMap: Record<string, string> = {
  '待确认': 'orange', '进行中': 'processing', '已完成': 'green', '已采纳': 'green',
  '待处理': 'orange', '已拒绝': 'red', '已发布': 'green', '已签署': 'green',
  '草稿': 'default', '已过期': 'default',
};

function parseMetadata(doc: any): Record<string, any> {
  if (!doc?.metadata) return {};
  try { return JSON.parse(doc.metadata); } catch { return {}; }
}

async function loadDocs() {
  const res = await getDocList({ projectId: id.value, category: docCategory.value || undefined, pageSize: 200 });
  docList.value = res.items || [];
  docLoaded.value = true;
}

function getDocFileUrl(doc: any) {
  if (!doc.filePath) return '';
  if (doc.filePath.startsWith('http')) return doc.filePath;
  const serverBase = apiURL.replace(/\/admin$/, '');
  return `${serverBase}${doc.filePath}`;
}

function openUploadModal() {
  uploadFiles.value = [];
  uploadCategory.value = 'Inbox';
  isUploading.value = false;
  uploadModalVisible.value = true;
}

function handleBeforeUpload(file: File) {
  uploadFiles.value.push({ file, status: 'pending', progress: 0 });
  return false;
}

function removeUploadFile(index: number) {
  uploadFiles.value.splice(index, 1);
}

function getFileIcon(name: string) {
  const ext = name.split('.').pop()?.toLowerCase() || '';
  if (docImageExts.has(ext)) return '🖼️';
  if (['pdf'].includes(ext)) return '📄';
  if (['doc', 'docx'].includes(ext)) return '📝';
  if (['xls', 'xlsx'].includes(ext)) return '📊';
  if (['zip', 'rar', '7z'].includes(ext)) return '📦';
  if (['mp4', 'mov', 'avi'].includes(ext)) return '🎬';
  return '📎';
}

async function startBatchUpload() {
  if (uploadFiles.value.length === 0) {
    message.warning('请先添加文件');
    return;
  }
  isUploading.value = true;
  let successCount = 0;
  for (const item of uploadFiles.value) {
    if (item.status === 'done') { successCount++; continue; }
    item.status = 'uploading';
    item.progress = 30;
    try {
      const result: any = await uploadFileApi(item.file);
      item.progress = 70;
      const ext = item.file.name.split('.').pop()?.toLowerCase() || '';
      await postDocAdd({
        projectId: id.value,
        title: item.file.name,
        category: uploadCategory.value,
        filePath: result.fileName,
        fileSize: item.file.size,
        fileType: ext,
      });
      item.status = 'done';
      item.progress = 100;
      successCount++;
    } catch (e: any) {
      item.status = 'error';
      item.error = e?.message || '上传失败';
    }
  }
  isUploading.value = false;
  if (successCount > 0) {
    message.success(`成功上传 ${successCount} 个文件`);
    loadDocs();
  }
}

async function handleDocClassify(doc: any, category: string) {
  await postDocEdit({ ...doc, category });
  message.success('分类成功');
  loadDocs();
  if (docDrawerItem.value?.id === doc.id) {
    docDrawerItem.value.category = category;
  }
}

async function handleDocDelete(docId: number) {
  await postDocDel(docId);
  message.success('删除成功');
  docDrawerVisible.value = false;
  loadDocs();
}

function handleDocPreview(doc: any) {
  const url = getDocFileUrl(doc);
  if (url) window.open(url, '_blank');
}

function openDocDrawer(doc: any) {
  docDrawerItem.value = doc;
  docMetaForm.value = parseMetadata(doc);
  docMetaEditing.value = false;
  docDrawerVisible.value = true;
}

async function saveDocMeta() {
  if (!docDrawerItem.value) return;
  const metadataStr = JSON.stringify(docMetaForm.value);
  await postDocEdit({ id: docDrawerItem.value.id, title: docDrawerItem.value.title, metadata: metadataStr });
  docDrawerItem.value.metadata = metadataStr;
  docMetaEditing.value = false;
  message.success('保存成功');
  loadDocs();
}

function onDocCategoryChange(val: string) {
  docCategory.value = val;
  loadDocs();
}

function formatSize(bytes: number) {
  if (!bytes && bytes !== 0) return '-';
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

// ---- Tab ----

function onTabChange(key: string) {
  activeTab.value = key;
  if (key === 'subproject' && !subProjectLoaded.value) loadSubProjects();
  else if (key === 'requirement' && !reqLoaded.value) loadRequirements();
  else if (key === 'git' && !gitTabLoaded.value) loadGitTab();
  else if (key === 'config' && !configLoaded.value) loadConfigs();
  else if (key === 'environment' && !envLoaded.value) loadEnvironments();
  else if (key === 'deploy' && !deployLoaded.value) loadDeployConfigs();
  else if (key === 'task' && !taskLoaded.value) loadTasks();
  else if (key === 'feature') { featureTreeLoaded.value = false; loadFeatureTree(); }
  else if (key === 'mindmap' && !mindMapLoaded.value) loadMindMaps();
  else if (key === 'ai') { /* 无需预加载 */ }
  else if (key === 'chat' && !chatLoaded.value) loadChatData();
  else if (key === 'resource' && !docLoaded.value) loadDocs();
}

function formatDate(dateStr: string) {
  if (!dateStr) return '-';
  return new Date(dateStr).toLocaleDateString('zh-CN');
}
function formatDateTime(dateStr: string) {
  if (!dateStr) return '-';
  return new Date(dateStr).toLocaleString('zh-CN');
}
</script>

<template>
  <Page auto-content-height>
    <Spin :spinning="loading">
      <Tabs :active-key="activeTab" @change="onTabChange" style="padding: 0 16px">
        <!-- Overview Tab -->
        <TabPane key="overview" tab="概览">
          <Card title="项目信息" style="margin-bottom: 16px">
            <template #extra>
              <Tag v-if="gitStatus" :color="gitStatus.color">Git: {{ gitStatus.text }}</Tag>
            </template>
            <div v-if="project.logoUrl" class="project-logo-banner">
              <img :src="project.logoUrl" alt="项目 Logo" class="project-logo-img" />
              <div class="project-logo-name">{{ project.name }}</div>
            </div>
            <Descriptions :column="3" bordered>
              <DescriptionsItem label="项目名称">{{ project.name }}</DescriptionsItem>
              <DescriptionsItem label="项目编码">{{ project.code }}</DescriptionsItem>
              <DescriptionsItem label="状态">
                <Tag :color="statusColorMap[project.status] || 'default'">{{ projectStatusLabels[project.status] || project.status }}</Tag>
              </DescriptionsItem>
              <DescriptionsItem label="开始日期">{{ formatDate(project.startDate) }}</DescriptionsItem>
              <DescriptionsItem label="截止日期">{{ formatDate(project.dueDate) }}</DescriptionsItem>
              <DescriptionsItem label="端口范围">
                {{ project.portRangeStart && project.portRangeEnd ? `${project.portRangeStart} - ${project.portRangeEnd}` : project.portRangeStart || '-' }}
              </DescriptionsItem>
              <DescriptionsItem label="GitLab 群组">
                <a v-if="project.gitlabGroupUrl" :href="project.gitlabGroupUrl" target="_blank" rel="noopener">{{ project.gitlabGroupPath || project.gitlabGroupUrl }}</a>
                <span v-else>-</span>
              </DescriptionsItem>
              <DescriptionsItem label="子项目数">
                <Tag color="blue">{{ subProjects.length }}</Tag>
              </DescriptionsItem>
            </Descriptions>
          </Card>

          <!-- 项目描述 -->
          <Card v-if="project.description" style="margin-bottom: 16px">
            <template #title>
              <div style="display: flex; align-items: center; justify-content: space-between;">
                <span>项目描述</span>
                <Button size="small" type="link" @click="descExpanded = !descExpanded">
                  {{ descExpanded ? '收起' : '展开' }}
                </Button>
              </div>
            </template>
            <div class="project-desc-body" :class="{ dark: isDark, collapsed: !descExpanded }">
              <MdPreview :model-value="project.description" :theme="isDark ? 'dark' : 'light'" />
            </div>
          </Card>

          <!-- 子项目概览 -->
          <Card v-if="subProjects.length > 0" style="margin-bottom: 16px">
            <template #title>
              <div style="display: flex; gap: 8px; align-items: center">
                <span>子项目</span>
                <Tag>{{ subProjects.length }}</Tag>
              </div>
            </template>
            <Table :data-source="subProjects" :pagination="false" row-key="id" size="middle">
              <template #bodyCell="{ column, record }">
                <template v-if="column.key === 'name'">
                  <div style="display: flex; gap: 6px; align-items: center">
                    <span style="font-weight: 500">{{ record.name }}</span>
                    <Tag v-if="record.code" color="blue" style="font-size: 11px">{{ record.code }}</Tag>
                  </div>
                </template>
                <template v-else-if="column.key === 'status'">
                  <Tag :color="record.status === 'Active' ? 'green' : record.status === 'Deployed' ? 'cyan' : record.status === 'Testing' ? 'orange' : record.status === 'Archived' ? 'default' : 'blue'">
                    {{ { Planning: '规划中', Active: '开发中', Testing: '测试中', Deployed: '已部署', Archived: '已归档' }[record.status] || record.status }}
                  </Tag>
                </template>
                <template v-else-if="column.key === 'progress'">
                  <Progress :percent="record.progress || 0" size="small" :stroke-color="record.progress >= 100 ? '#52c41a' : '#1890ff'" style="width: 120px; margin: 0" />
                </template>
                <template v-else-if="column.key === 'deploy'">
                  <div style="display: flex; gap: 6px; align-items: center">
                    <Tag v-if="record.deployStatus === 'Success'" color="green">成功</Tag>
                    <Tag v-else-if="record.deployStatus === 'Failed'" color="red">失败</Tag>
                    <Tag v-else-if="record.deployStatus === 'Deploying'" color="blue">部署中</Tag>
                    <Tag v-else color="default">未部署</Tag>
                    <span v-if="record.deployEnv" style="font-size: 12px; color: var(--ant-color-text-tertiary)">{{ record.deployEnv }}</span>
                  </div>
                </template>
                <template v-else-if="column.key === 'lastDeploy'">
                  <span style="font-size: 12px">{{ record.lastDeployTime ? formatDateTime(record.lastDeployTime) : '-' }}</span>
                </template>
                <template v-else-if="column.key === 'lastCommit'">
                  <div v-if="subProjectCommits[record.id]?.length" style="display: flex; gap: 6px; align-items: baseline; font-size: 12px">
                    <code style=" flex-shrink: 0;color: var(--ant-color-primary)">{{ (subProjectCommits[record.id][0].shortId || subProjectCommits[record.id][0].id || '').slice(0, 7) }}</code>
                    <span style=" max-width: 200px;overflow: hidden; text-overflow: ellipsis; white-space: nowrap">{{ subProjectCommits[record.id][0].title || '' }}</span>
                  </div>
                  <span v-else style="font-size: 12px; color: var(--ant-color-text-tertiary)">-</span>
                </template>
              </template>
              <Table.Column key="name" title="名称" data-index="name" />
              <Table.Column key="status" title="状态" width="100" />
              <Table.Column key="progress" title="进度" width="150" />
              <Table.Column key="deploy" title="部署" width="150" />
              <Table.Column key="lastDeploy" title="最近部署" width="160" />
              <Table.Column key="lastCommit" title="最近提交" width="280" />
            </Table>
          </Card>

          <Card v-if="overview" title="需求统计" style="margin-bottom: 16px">
            <Descriptions :column="5" bordered>
              <DescriptionsItem label="总需求">{{ overview.stats?.reqTotal ?? '-' }}</DescriptionsItem>
              <DescriptionsItem label="待处理">{{ overview.stats?.reqTodo ?? '-' }}</DescriptionsItem>
              <DescriptionsItem label="进行中">{{ overview.stats?.reqInProgress ?? '-' }}</DescriptionsItem>
              <DescriptionsItem label="测试中">{{ overview.stats?.reqTesting ?? '-' }}</DescriptionsItem>
              <DescriptionsItem label="已完成">{{ overview.stats?.reqDone ?? '-' }}</DescriptionsItem>
            </Descriptions>
          </Card>

          <Card title="最近动态">
            <List :data-source="activities" :locale="{ emptyText: '暂无动态' }">
              <template #renderItem="{ item }">
                <ListItem>
                  <div style="display: flex; justify-content: space-between; width: 100%">
                    <div>
                      <span style="font-weight: 500">{{ item.userName || item.operator }}</span>
                      <span style="margin-left: 8px; color: #666">{{ item.action }}</span>
                      <span style="margin-left: 8px">{{ item.content || item.description }}</span>
                    </div>
                    <div style=" font-size: 12px;color: #999">{{ formatDateTime(item.createdTime) }}</div>
                  </div>
                </ListItem>
              </template>
            </List>
          </Card>
        </TabPane>

        <!-- SubProject Tab -->
        <TabPane key="subproject" tab="子项目">
          <Spin :spinning="!subProjectLoaded" tip="加载子项目...">
          <div style="display: flex; align-items: center; justify-content: space-between; margin-bottom: 16px;">
            <span style="color: var(--ant-color-text-secondary)">子项目对应该项目 GitLab 群组下的代码仓库</span>
            <Space>
              <Button v-if="project.gitlabGroupId" @click="openDetectModal">从 GitLab 群组探测</Button>
              <Button type="primary" @click="goToEditSubProjects">编辑子项目</Button>
            </Space>
          </div>
          <Empty v-if="subProjectLoaded && subProjects.length === 0" description="暂无子项目">
            <Space>
              <Button v-if="project.gitlabGroupId" type="primary" @click="openDetectModal">从 GitLab 群组探测</Button>
              <Button @click="goToEditSubProjects">手动添加子项目</Button>
            </Space>
          </Empty>
          <div style="display: flex; flex-direction: column; gap: 16px; min-height: 100px;">
            <Card v-for="sp in subProjects" :key="sp.id">
              <template #title>
                <div style="display: flex; gap: 8px; align-items: center">
                  <span style="font-size: 16px; font-weight: 600">{{ sp.name }}</span>
                  <Tag v-if="sp.code" color="blue">{{ sp.code }}</Tag>
                  <Tag :color="sp.status === 'Active' ? 'green' : sp.status === 'Deployed' ? 'cyan' : sp.status === 'Testing' ? 'orange' : 'default'">
                    {{ { Planning: '规划中', Active: '开发中', Testing: '测试中', Deployed: '已部署', Archived: '已归档' }[sp.status] || sp.status }}
                  </Tag>
                  <Progress :percent="sp.progress || 0" :steps="10" size="small" style="width: 100px; margin-left: 8px;" />
                </div>
              </template>
              <template #extra>
                <SubProjectDeployButton :sub-project="sp" />
              </template>

              <Spin :spinning="!!subProjectDetailLoading[sp.id]" tip="加载详情...">
              <Tabs :active-key="subProjectTab[sp.id] || 'info'" @change="(k: string) => subProjectTab[sp.id] = k" size="small">
                <!-- Tab 1: 基本信息 -->
                <TabPane key="info" tab="基本信息">
                  <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 16px">
                    <Descriptions :column="1" size="small" bordered>
                      <DescriptionsItem label="项目名称">{{ sp.name }}</DescriptionsItem>
                      <DescriptionsItem label="编码">{{ sp.code || '-' }}</DescriptionsItem>
                      <DescriptionsItem label="默认分支"><Tag color="blue">{{ sp.defaultBranch || 'main' }}</Tag></DescriptionsItem>
                      <DescriptionsItem label="部署状态">
                        <Tag v-if="sp.deployStatus === 'Success'" color="green">成功</Tag>
                        <Tag v-else-if="sp.deployStatus === 'Failed'" color="red">失败</Tag>
                        <Tag v-else-if="sp.deployStatus === 'Deploying'" color="blue">部署中</Tag>
                        <span v-else style="color: var(--ant-color-text-tertiary)">未部署</span>
                      </DescriptionsItem>
                      <DescriptionsItem label="最近部署">{{ sp.lastDeployTime ? formatDateTime(sp.lastDeployTime) : '-' }}</DescriptionsItem>
                      <DescriptionsItem label="本地路径">
                        <code v-if="sp.localPath" style="font-size: 12px; word-break: break-all">{{ sp.localPath }}</code>
                        <span v-else style="color: var(--ant-color-text-tertiary)">未同步</span>
                      </DescriptionsItem>
                    </Descriptions>
                    <Descriptions :column="1" size="small" bordered title="部署配置" v-if="subProjectDetails[sp.id]?.deploy">
                      <DescriptionsItem label="服务器">{{ subProjectDetails[sp.id].deploy.serverName }} ({{ subProjectDetails[sp.id].deploy.serverHost }})</DescriptionsItem>
                      <DescriptionsItem label="环境">{{ subProjectDetails[sp.id].deploy.envName || '-' }}</DescriptionsItem>
                      <DescriptionsItem label="API 端口">{{ subProjectDetails[sp.id].deploy.apiPort || '-' }}</DescriptionsItem>
                      <DescriptionsItem label="前端端口">{{ subProjectDetails[sp.id].deploy.frontendPort || '-' }}</DescriptionsItem>
                      <DescriptionsItem label="数据库">{{ subProjectDetails[sp.id].deploy.databaseName || '-' }}</DescriptionsItem>
                      <DescriptionsItem label="服务名">{{ subProjectDetails[sp.id].deploy.systemdServiceName || '-' }}</DescriptionsItem>
                      <DescriptionsItem label="宝塔站点">{{ subProjectDetails[sp.id].deploy.btSiteName || '-' }}</DescriptionsItem>
                      <DescriptionsItem label="部署方式">{{ subProjectDetails[sp.id].deploy.preferredDeployMethod ? (deployMethodLabels[subProjectDetails[sp.id].deploy.preferredDeployMethod] || subProjectDetails[sp.id].deploy.preferredDeployMethod) : '-' }}</DescriptionsItem>
                    </Descriptions>
                    <div v-else-if="subProjectDetails[sp.id] && !subProjectDetails[sp.id].deploy" style="display: flex; align-items: center; justify-content: center; color: var(--ant-color-text-tertiary);">
                      未配置部署信息
                    </div>
                  </div>
                  <div v-if="sp.description" style=" padding-top: 12px;margin-top: 12px; font-size: 13px; color: var(--ant-color-text-secondary); border-top: 1px solid var(--ant-color-border-secondary)">
                    {{ sp.description }}
                  </div>
                </TabPane>

                <!-- Tab 2: 代码管理 -->
                <TabPane key="code" tab="代码管理">
                  <!-- 操作按钮 -->
                  <Space style="margin-bottom: 12px;">
                    <Button :loading="syncingCode[sp.id]" :disabled="!sp.gitlabProjectUrl" @click="handleSyncCode(sp)">同步代码</Button>
                    <Button :disabled="!subProjectDetails[sp.id]?.gitStatus?.hasChanges" @click="openCommitModal(sp.id)">提交</Button>
                    <Button :loading="pushing[sp.id]" :disabled="!subProjectDetails[sp.id]?.gitStatus || subProjectDetails[sp.id]?.gitStatus?.ahead === 0" @click="handlePush(sp.id)">推送</Button>
                  </Space>

                  <!-- Git 状态 -->
                  <div v-if="subProjectDetails[sp.id]?.gitStatus" style="padding: 10px 14px; margin-bottom: 12px; border: 1px solid var(--ant-color-border); border-radius: 6px;">
                    <div style="display: flex; gap: 8px; align-items: center; margin-bottom: 6px;">
                      <Tag color="blue">{{ subProjectDetails[sp.id].gitStatus.branch }}</Tag>
                      <code style="font-size: 12px; color: var(--ant-color-primary)">{{ subProjectDetails[sp.id].gitStatus.commitHash }}</code>
                      <span style="flex: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap;">{{ subProjectDetails[sp.id].gitStatus.commitMessage }}</span>
                    </div>
                    <div style="display: flex; flex-wrap: wrap; gap: 12px; align-items: center; font-size: 12px; color: var(--ant-color-text-secondary);">
                      <span>{{ subProjectDetails[sp.id].gitStatus.commitAuthor }}</span>
                      <span>{{ subProjectDetails[sp.id].gitStatus.commitTime?.split(' ').slice(0, 2).join(' ') }}</span>
                      <Tag v-if="subProjectDetails[sp.id].gitStatus.ahead > 0" color="orange" style="margin: 0;">{{ subProjectDetails[sp.id].gitStatus.ahead }} 待推送</Tag>
                      <Tag v-if="subProjectDetails[sp.id].gitStatus.behind > 0" color="cyan" style="margin: 0;">{{ subProjectDetails[sp.id].gitStatus.behind }} 待拉取</Tag>
                      <Tag v-if="subProjectDetails[sp.id].gitStatus.ahead === 0 && subProjectDetails[sp.id].gitStatus.behind === 0" color="green" style="margin: 0;">已同步</Tag>
                    </div>
                    <!-- 未提交变更 -->
                    <div v-if="subProjectDetails[sp.id].gitStatus.hasChanges" style=" display: flex; gap: 12px; padding-top: 8px;margin-top: 8px; font-size: 12px; border-top: 1px dashed var(--ant-color-border);">
                      <span style="color: var(--ant-color-warning);">未提交变更:</span>
                      <span v-if="subProjectDetails[sp.id].gitStatus.modified > 0">{{ subProjectDetails[sp.id].gitStatus.modified }} 修改</span>
                      <span v-if="subProjectDetails[sp.id].gitStatus.added > 0" style="color: var(--ant-color-success)">{{ subProjectDetails[sp.id].gitStatus.added }} 新增</span>
                      <span v-if="subProjectDetails[sp.id].gitStatus.deleted > 0" style="color: var(--ant-color-error)">{{ subProjectDetails[sp.id].gitStatus.deleted }} 删除</span>
                      <span v-if="subProjectDetails[sp.id].gitStatus.untracked > 0" style="color: var(--ant-color-text-tertiary)">{{ subProjectDetails[sp.id].gitStatus.untracked }} 未跟踪</span>
                    </div>
                  </div>
                  <div v-else-if="!sp.localPath" style=" margin-bottom: 12px; font-size: 13px;color: var(--ant-color-text-tertiary);">未同步到服务器，请先点击「同步代码」</div>

                  <!-- 仓库最近提交 -->
                  <div style=" margin-bottom: 8px; font-size: 14px;font-weight: 500">仓库最近提交</div>
                  <template v-if="subProjectDetails[sp.id]?.recentCommits">
                    <div v-if="subProjectDetails[sp.id].recentCommits.length === 0" style=" font-size: 13px;color: var(--ant-color-text-tertiary);">暂无提交记录</div>
                    <div v-for="(commit, ci) in subProjectDetails[sp.id].recentCommits.slice(0, 8)" :key="ci" style="display: flex; gap: 8px; align-items: baseline; padding: 5px 0; font-size: 13px; border-bottom: 1px solid var(--ant-color-border-secondary)">
                      <code style=" flex-shrink: 0; font-size: 12px;color: var(--ant-color-primary)">{{ (commit.shortId || commit.id || '').slice(0, 7) }}</code>
                      <span style="flex: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap">{{ commit.title || commit.message || '' }}</span>
                      <span style=" flex-shrink: 0; font-size: 11px;color: var(--ant-color-text-tertiary)">{{ commit.authorName || '' }}</span>
                    </div>
                  </template>
                  <div v-else style=" font-size: 13px;color: var(--ant-color-text-tertiary);">
                    {{ sp.gitlabProjectId ? '加载中...' : '未关联 GitLab 仓库' }}
                  </div>
                </TabPane>
              </Tabs>
              </Spin>
            </Card>
          </div>
          </Spin>

          <!-- 提交弹窗 -->
          <Modal v-model:open="commitModalVisible" title="提交代码" @ok="handleCommit" :confirmLoading="committing" okText="提交">
            <div style="padding: 8px 0;">
              <label style="display: block; margin-bottom: 6px; font-weight: 500;">提交信息 *</label>
              <Textarea v-model:value="commitMessage" :rows="3" placeholder="请输入提交信息" />
            </div>
          </Modal>

          <!-- 从 GitLab 群组探测子项目 -->
          <Modal
            v-model:open="detectModalVisible"
            title="从 GitLab 群组探测子项目"
            width="640px"
            :confirmLoading="detectSaving"
            okText="添加所选"
            @ok="handleDetectSave"
          >
            <Spin :spinning="detectLoading" tip="正在探测群组下的仓库...">
              <div v-if="!detectLoading && detectList.length === 0" style="padding: 24px 0; text-align: center; color: var(--ant-color-text-tertiary)">
                该群组下没有探测到任何仓库
              </div>
              <template v-else>
                <div style="margin-bottom: 8px;">
                  <Checkbox
                    :checked="allDetectChecked"
                    :indeterminate="detectIndeterminate"
                    :disabled="selectableDetect.length === 0"
                    @change="toggleDetectAll"
                  >
                    全选可添加项（{{ selectableDetect.length }}）
                  </Checkbox>
                </div>
                <div style="max-height: 420px; overflow-y: auto;">
                  <div
                    v-for="p in detectList"
                    :key="p.id"
                    style="display: flex; align-items: center; gap: 10px; padding: 8px 10px; margin-bottom: 8px; border: 1px solid var(--ant-color-border-secondary); border-radius: 6px;"
                  >
                    <Checkbox v-model:checked="p.checked" :disabled="p.exists" />
                    <div style="flex: 1; overflow: hidden;">
                      <div style="display: flex; align-items: center; gap: 8px;">
                        <span style="font-weight: 500;">{{ p.name }}</span>
                        <Tag v-if="p.exists" color="default">已添加</Tag>
                        <Tag v-else color="green">可添加</Tag>
                      </div>
                      <div style="font-size: 12px; color: var(--ant-color-text-tertiary); overflow: hidden; text-overflow: ellipsis; white-space: nowrap;">
                        {{ p.path }}
                      </div>
                    </div>
                  </div>
                </div>
              </template>
            </Spin>
          </Modal>
        </TabPane>

        <!-- Requirement Tab -->
        <TabPane key="requirement" tab="需求">
          <Card>
            <template #title>
              <div style="display: flex; align-items: center; justify-content: space-between">
                <span>需求列表</span>
                <Space>
                  <Button size="small" :loading="syncingMindMap" @click="handleSyncMindMap">同步导图</Button>
                  <Button size="small" @click="openAiAnalyze">AI 分析</Button>
                  <Button type="primary" size="small" @click="handleReqAdd">新增需求</Button>
                </Space>
              </div>
            </template>
            <Table :columns="reqColumns" :data-source="requirements" :pagination="false" row-key="id" size="middle">
              <template #bodyCell="{ column, record }">
                <template v-if="column.key === 'info'">
                  <RequirementInfoCell :record="record" group="info" />
                </template>
                <template v-else-if="column.key === 'status'">
                  <RequirementInfoCell :record="record" group="status" />
                </template>
                <template v-else-if="column.key === 'action'">
                  <Space wrap>
                    <Button @click="handleReqEdit(record)">编辑</Button>
                    <Button @click="reqDocRef?.openPreview(record.id, record.title)">预览</Button>
                    <Tooltip title="交给 AI 处理此需求">
                      <Button type="primary" ghost @click="handleReqToAi(record)">AI</Button>
                    </Tooltip>
                    <Popconfirm title="确认删除？" @confirm="handleReqDel(record.id)">
                      <Button danger>删除</Button>
                    </Popconfirm>
                  </Space>
                </template>
              </template>
            </Table>
          </Card>
          <DocPreview ref="reqDocRef" source-type="Requirement" />
        </TabPane>

        <!-- Task Tab -->
        <TabPane key="task" tab="任务">
          <Card>
            <template #title>
              <div style="display: flex; align-items: center; justify-content: space-between">
                <span>任务列表</span>
              </div>
            </template>
            <Table :columns="taskColumns" :data-source="tasks" :pagination="false" row-key="id" size="middle">
              <template #bodyCell="{ column, record }">
                <template v-if="column.key === 'info'">
                  <TaskItemInfoCell :record="record" group="info" />
                </template>
                <template v-else-if="column.key === 'status'">
                  <TaskItemInfoCell :record="record" group="status" />
                </template>
                <template v-else-if="column.key === 'action'">
                  <Space wrap>
                    <Button @click="taskDocRef?.openPreview(record.id, record.title)">预览</Button>
                    <Button @click="taskDocRef?.download(record.id, record.title)">下载</Button>
                    <Button @click="taskDocRef?.openHistory(record.id, record.title)">版本</Button>
                  </Space>
                </template>
              </template>
            </Table>
            <Empty v-if="tasks.length === 0" description="该项目暂无任务" />
          </Card>
          <DocPreview ref="taskDocRef" source-type="TaskItem" />
        </TabPane>

        <!-- Feature Module Tab -->
        <TabPane key="feature" tab="功能模块">
          <div class="fm-tab-container">
            <!-- 左侧树 -->
            <div class="fm-tab-tree">
              <div style="display: flex; gap: 6px; margin-bottom: 10px">
                <Button size="small" type="primary" @click="handleFmAddRoot">新增根模块</Button>
                <Button size="small" :loading="syncing" @click="handleSyncFeatureMap">同步导图</Button>
              </div>
              <Tree
                v-if="featureTreeData.length > 0"
                :tree-data="featureTreeData"
                :expanded-keys="fmExpandedKeys"
                :selected-keys="fmSelectedKey ? [fmSelectedKey] : []"
                block-node
                :field-names="{ children: 'children', title: 'title', key: 'value' }"
                @expand="(keys: number[]) => fmExpandedKeys = keys"
                @select="handleFmSelect"
              />
              <Empty v-else description="暂无功能模块，点击上方新增" style="padding: 30px 0" />
            </div>

            <!-- 右侧详情 -->
            <div class="fm-tab-detail">
              <Spin :spinning="fmDetailLoading">
                <template v-if="fmNodeDetail">
                  <div style="display: flex; align-items: center; justify-content: space-between; margin-bottom: 12px">
                    <div style="display: flex; gap: 8px; align-items: center">
                      <span style="font-size: 16px; font-weight: 600">{{ fmNodeDetail.name }}</span>
                      <Tag :color="fmStatusColorMap[fmNodeDetail.status] || 'default'">{{ fmNodeDetail.status }}</Tag>
                      <Tag>{{ fmNodeDetail.requirementCount }} 个需求</Tag>
                    </div>
                    <Space>
                      <Button size="small" @click="handleFmAddChild(fmNodeDetail.id)">新增子模块</Button>
                      <Button size="small" @click="handleFmEdit">编辑</Button>
                      <Popconfirm title="确定删除？子节点也会一并删除" @confirm="handleFmDelete(fmNodeDetail.id)">
                        <Button size="small" danger>删除</Button>
                      </Popconfirm>
                    </Space>
                  </div>

                  <div v-if="fmNodeDetail.description" style=" padding: 8px 12px; margin-bottom: 16px;color: var(--ant-color-text-secondary); background: var(--ant-color-fill-quaternary); border-radius: 6px">
                    {{ fmNodeDetail.description }}
                  </div>

                  <!-- 关联需求 -->
                  <div style=" margin-bottom: 8px;font-weight: 500">关联需求</div>
                  <Table
                    v-if="fmNodeDetail.requirements?.length > 0"
                    :data-source="fmNodeDetail.requirements"
                    :pagination="false"
                    row-key="id"
                    size="small"
                  >
                    <Table.Column title="标题" data-index="title" ellipsis />
                    <Table.Column title="类型" key="type" width="80">
                      <template #default="{ record }"><Tag>{{ requirementTypeLabels[record.type] || record.type }}</Tag></template>
                    </Table.Column>
                    <Table.Column title="状态" key="status" width="90">
                      <template #default="{ record }">
                        <Tag :color="reqStatusColorMap[record.status] || 'default'">{{ requirementStatusLabels[record.status] || record.status }}</Tag>
                      </template>
                    </Table.Column>
                  </Table>
                  <Empty v-else description="暂无关联需求" />
                </template>
                <Empty v-else description="选择左侧节点查看详情" style="padding: 60px 0" />
              </Spin>
            </div>
          </div>

          <!-- 功能模块新增/编辑弹窗 -->
          <Modal v-model:open="fmFormVisible" :title="fmFormTitle" :confirm-loading="fmFormSaving" @ok="handleFmFormSubmit" width="500px">
            <div style=" display: flex; flex-direction: column; gap: 16px;padding: 16px 0">
              <div>
                <div style="margin-bottom: 4px; font-weight: 500">名称 <span style="color: red">*</span></div>
                <Input v-model:value="fmFormData.name" placeholder="功能模块名称" />
              </div>
              <div>
                <div style="margin-bottom: 4px; font-weight: 500">描述</div>
                <Textarea v-model:value="fmFormData.description" :rows="3" placeholder="简要描述该模块的功能" />
              </div>
            </div>
          </Modal>
        </TabPane>

        <!-- Git Tab -->
        <TabPane key="git" tab="Git">
          <!-- 子项目仓库 -->
          <div v-if="subProjects.some((sp: any) => sp.gitlabProjectUrl)" style="margin-bottom: 16px">
            <div style=" margin-bottom: 12px; font-size: 15px;font-weight: 500">子项目仓库</div>
            <div style="display: grid; grid-template-columns: repeat(auto-fill, minmax(480px, 1fr)); gap: 12px">
              <Card v-for="sp in subProjects.filter((s: any) => s.gitlabProjectUrl)" :key="'sp-' + sp.id" size="small">
                <template #title>
                  <div style="display: flex; gap: 8px; align-items: center">
                    <Tag color="#fc6d26">GitLab</Tag>
                    <span>{{ sp.name }}</span>
                    <Tag v-if="sp.code" style="margin-left: auto">{{ sp.code }}</Tag>
                  </div>
                </template>
                <Descriptions :column="2" size="small">
                  <DescriptionsItem label="仓库地址" :span="2">
                    <a :href="sp.gitlabProjectUrl.replace('.git','')" target="_blank" rel="noopener" style="word-break: break-all">{{ sp.gitlabProjectUrl }}</a>
                  </DescriptionsItem>
                  <DescriptionsItem label="默认分支"><Tag color="blue">{{ sp.defaultBranch || 'main' }}</Tag></DescriptionsItem>
                  <DescriptionsItem v-if="sp.baseRepoUrl" label="基座仓库">
                    <a :href="sp.baseRepoUrl.replace('.git','')" target="_blank" rel="noopener" style=" font-size: 12px;word-break: break-all">{{ sp.baseRepoUrl }}</a>
                  </DescriptionsItem>
                </Descriptions>
              </Card>
            </div>
          </div>

          <!-- 旧版 GitRepository -->
          <div v-if="gitRepos.length > 0">
            <div style=" margin-bottom: 12px; font-size: 15px;font-weight: 500">关联仓库</div>
          </div>
          <Empty v-if="gitRepos.length === 0 && !subProjects.some((sp: any) => sp.gitlabProjectUrl) && gitTabLoaded" description="暂无关联仓库" />
          <div style="display: grid; grid-template-columns: repeat(auto-fill, minmax(480px, 1fr)); gap: 16px">
            <Card v-for="repo in gitRepos" :key="repo.id">
              <template #title>
                <div style="display: flex; gap: 8px; align-items: center">
                  <Tag :color="repo.platform === 'GitHub' ? '#24292e' : '#fc6d26'">{{ repo.platform }}</Tag>
                  <span>{{ repo.repoFullName || repo.repoUrl }}</span>
                </div>
              </template>
              <template #extra>
                <Button size="small" @click="handleRepoSync(repo.id)" :loading="repo.syncStatus === 'Syncing'">
                  同步
                </Button>
              </template>

              <Descriptions :column="2" size="small">
                <DescriptionsItem label="仓库地址" :span="2">
                  <a :href="repo.repoUrl" target="_blank" rel="noopener">{{ repo.repoUrl }}</a>
                </DescriptionsItem>
                <DescriptionsItem label="默认分支">
                  <Tag color="blue">{{ repo.defaultBranch }}</Tag>
                </DescriptionsItem>
                <DescriptionsItem label="同步状态">
                  <Tag :color="repo.syncStatus === 'Idle' ? 'green' : repo.syncStatus === 'Syncing' ? 'blue' : 'red'">
                    {{ repo.syncStatus === 'Idle' ? '正常' : repo.syncStatus === 'Syncing' ? '同步中' : '异常' }}
                  </Tag>
                </DescriptionsItem>
                <DescriptionsItem label="最后同步">{{ formatDateTime(repo.lastSyncTime) }}</DescriptionsItem>
                <DescriptionsItem label="添加时间">{{ formatDateTime(repo.createdTime) }}</DescriptionsItem>
              </Descriptions>

              <!-- 最近提交 -->
              <div style=" padding-top: 12px;margin-top: 16px; border-top: 1px solid var(--ant-color-border, #f0f0f0)">
                <div style=" margin-bottom: 8px;font-weight: 500">最近提交</div>
                <div v-if="!gitCommits[repo.id] || gitCommits[repo.id].length === 0" class="text-muted-foreground" style="font-size: 13px">
                  暂无提交记录
                </div>
                <div v-for="(commit, ci) in (gitCommits[repo.id] || []).slice(0, 8)" :key="ci" style="display: flex; gap: 8px; align-items: baseline; padding: 4px 0; font-size: 13px">
                  <code style=" flex-shrink: 0; font-size: 12px;color: var(--ant-color-primary, #1677ff)">
                    {{ (commit.short_id || commit.sha || commit.id || '').slice(0, 7) }}
                  </code>
                  <span style="flex: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap">
                    {{ commit.title || commit.message || commit.commit?.message || '' }}
                  </span>
                  <span class="text-muted-foreground" style=" flex-shrink: 0;font-size: 12px">
                    {{ commit.author_name || commit.commit?.author?.name || '' }}
                  </span>
                </div>
              </div>
            </Card>
          </div>
        </TabPane>

        <!-- Config Tab -->
        <!-- MindMap Tab -->
        <TabPane key="mindmap" tab="思维导图">
          <div class="mindmap-wrapper">
          <Spin :spinning="mindMapLoading" class="mindmap-spin">
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
            <div v-if="mindMapList.length > 0" ref="mindMapContainer" class="mindmap-render-area"></div>
            <Empty v-if="!mindMapLoading && mindMapList.length === 0" description="该项目暂无思维导图" style="margin: 60px auto" />
          </Spin>
          </div>
        </TabPane>

        <TabPane key="config" tab="配置">
          <Card>
            <template #title>
              <div style="display: flex; align-items: center; justify-content: space-between">
                <span>配置项</span>
                <Button v-if="configs.length > 0" type="primary" size="small" :loading="configSaving" @click="handleConfigSave">保存修改</Button>
              </div>
            </template>
            <Table :columns="configColumns" :data-source="configs" :pagination="false" row-key="id" size="middle">
              <template #bodyCell="{ column, record }">
                <template v-if="column.key === 'configValue'">
                  <template v-if="record.isSensitive && !decryptedValues[record.id]">
                    <div style="display: flex; gap: 4px; align-items: center">
                      <span class="text-muted-foreground">******</span>
                      <Button size="small" @click="handleDecrypt(record.id)">解密</Button>
                    </div>
                  </template>
                  <Input v-else v-model:value="record.configValue" style="font-family: monospace" />
                </template>
                <template v-else-if="column.key === 'isSensitive'">
                  <Tag :color="record.isSensitive ? 'red' : 'default'">{{ record.isSensitive ? '是' : '否' }}</Tag>
                </template>
              </template>
            </Table>
          </Card>
        </TabPane>

        <!-- Environment Tab -->
        <TabPane key="environment" tab="环境">
          <Card title="项目环境">
            <List :data-source="environments" :locale="{ emptyText: '暂无环境' }">
              <template #renderItem="{ item }">
                <ListItem>
                  <div style="display: flex; align-items: center; justify-content: space-between; width: 100%">
                    <div>
                      <span style=" margin-right: 8px;font-weight: 500">{{ item.name }}</span>
                      <span class="text-muted-foreground text-xs">{{ item.code }}</span>
                    </div>
                    <span class="text-muted-foreground text-xs">{{ formatDateTime(item.createdTime) }}</span>
                  </div>
                </ListItem>
              </template>
            </List>
          </Card>
        </TabPane>

        <!-- 部署配置 Tab -->
        <TabPane key="deploy" tab="部署配置">
          <Card title="项目部署配置">
            <template #extra>
              <Button type="primary" size="small" @click="handleDeployAdd">新增配置</Button>
            </template>
            <Table :data-source="deployConfigs" :columns="[
              { title: '服务器', dataIndex: ['server', 'name'], key: 'server', width: 120 },
              { title: '地址', dataIndex: ['server', 'host'], key: 'host', width: 130 },
              { title: '子项目', dataIndex: ['subProject', 'name'], key: 'subProject', width: 100 },
              { title: '标签', dataIndex: 'label', key: 'label', width: 80 },
              { title: '部署方式', dataIndex: 'availableDeployMethods', key: 'method', width: 180 },
              { title: 'API端口', dataIndex: 'apiPort', key: 'apiPort', width: 80 },
              { title: '前端端口', dataIndex: 'frontendPort', key: 'fePort', width: 80 },
              { title: '数据库', dataIndex: 'databaseName', key: 'db', width: 100 },
              { title: '服务名', dataIndex: 'systemdServiceName', key: 'svc', width: 110 },
              { title: '默认', dataIndex: 'isDefault', key: 'default', width: 60 },
              { title: '操作', key: 'action', width: 130, fixed: 'right' },
            ]" :scroll="{ x: 1100 }" size="small" row-key="id" :pagination="false">
              <template #bodyCell="{ column, record }">
                <template v-if="column.key === 'method'">
                  <template v-if="record.availableDeployMethods">
                    <Tag v-for="(m, i) in record.availableDeployMethods.split(',')" :key="m" :color="i === 0 ? 'blue' : 'default'" style="margin: 1px;">{{ deployMethodLabels[m] || m }}</Tag>
                  </template>
                  <span v-else>-</span>
                </template>
                <template v-else-if="column.key === 'subProject'">
                  {{ record.subProject?.name || '项目级' }}
                </template>
                <template v-else-if="column.key === 'default'">
                  <Tag v-if="record.isDefault" color="green">是</Tag>
                </template>
                <template v-else-if="column.key === 'action'">
                  <Space wrap>
                    <Button @click="handleDeployEdit(record)">编辑</Button>
                    <Popconfirm title="确认删除?" @confirm="handleDeployDel(record.id)">
                      <Button danger>删除</Button>
                    </Popconfirm>
                  </Space>
                </template>
              </template>
            </Table>
          </Card>

          <!-- 部署配置弹窗 -->
          <Modal v-model:open="deployModalVisible" :title="deployModalTitle" width="650px" @ok="handleDeploySave" :confirmLoading="deploySaving">
            <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 0 16px; max-height: 60vh; padding: 8px 0; overflow-y: auto;">
              <div style="grid-column: 1 / -1; margin: 8px 0 4px; font-weight: bold; color: #666;">关联</div>
              <div>
                <label style="display: block; margin-bottom: 4px;">服务器 *</label>
                <Select v-model:value="deployForm.serverId" :options="serverOptions" placeholder="选择服务器" style="width: 100%" show-search :filter-option="(input: string, opt: any) => opt.label.toLowerCase().includes(input.toLowerCase())" @change="onServerChange" />
              </div>
              <div>
                <label style="display: block; margin-bottom: 4px;">子项目</label>
                <Select v-model:value="deployForm.subProjectId" :options="subProjects.map((s: any) => ({ label: s.name, value: s.id }))" placeholder="项目级（留空）" style="width: 100%" allowClear />
              </div>
              <div>
                <label style="display: block; margin-bottom: 4px;">环境</label>
                <Select v-model:value="deployForm.environmentId" :options="envOptions" placeholder="选择环境" style="width: 100%" allowClear />
              </div>
              <div>
                <label style="display: block; margin-bottom: 4px;">标签</label>
                <Input v-model:value="deployForm.label" placeholder="如：测试服" />
              </div>

              <div style="grid-column: 1 / -1; margin: 12px 0 4px; font-weight: bold; color: #666;">部署方式优先级（拖拽或点击箭头排序，第一项为优先方式）</div>
              <div style="grid-column: 1 / -1;">
                <div ref="deployMethodListEl" style="display: flex; flex-direction: column; gap: 4px;">
                  <div v-for="(method, idx) in deployMethodOrder" :key="method.value"
                    :style="{
                      display: 'flex', alignItems: 'center', gap: '8px', padding: '6px 12px',
                      borderRadius: '6px', cursor: 'move', userSelect: 'none',
                      border: idx === 0 ? '1px solid #1677ff' : `1px solid ${isDark ? '#424242' : '#d9d9d9'}`,
                      background: idx === 0 ? (isDark ? '#111a2c' : '#e6f4ff') : (isDark ? '#1f1f1f' : '#fafafa'),
                    }"
                  >
                    <span class="deploy-drag-handle" style=" color: #999;cursor: move;">&#x2630;</span>
                    <Tag v-if="idx === 0" color="blue" style="margin: 0;">优先</Tag>
                    <span style="flex: 1;">{{ method.label }}</span>
                    <Button size="small" :disabled="idx === 0" @click="moveDeployMethod(idx, -1)">&uarr;</Button>
                    <Button size="small" :disabled="idx === deployMethodOrder.length - 1" @click="moveDeployMethod(idx, 1)">&darr;</Button>
                  </div>
                </div>
              </div>

              <div style="grid-column: 1 / -1; margin: 12px 0 4px; font-weight: bold; color: #666;">内置 API</div>
              <div>
                <label style="display: block; margin-bottom: 4px;">内置API地址</label>
                <Input v-model:value="deployForm.builtInApiUrl" placeholder="http://ip:port/admin/App/UploadApi" />
              </div>
              <div>
                <label style="display: block; margin-bottom: 4px;">内置API密钥</label>
                <Input.Password v-model:value="deployForm.builtInApiKey" placeholder="部署访问令牌 sk_…（需 deploy scope）" />
              </div>

              <div style="grid-column: 1 / -1; margin: 12px 0 4px; font-weight: bold; color: #666;">目录 / 端口 / 服务</div>
              <div>
                <label style="display: block; margin-bottom: 4px;">API 部署目录</label>
                <Input v-model:value="deployForm.apiDeployPath" placeholder="/www/wwwroot/..." />
              </div>
              <div>
                <label style="display: block; margin-bottom: 4px;">前端部署目录</label>
                <Input v-model:value="deployForm.frontendDeployPath" placeholder="/www/wwwroot/..." />
              </div>
              <div>
                <label style="display: block; margin-bottom: 4px;">API 端口</label>
                <InputNumber v-model:value="deployForm.apiPort" :min="1" :max="65535" style="width: 100%" />
              </div>
              <div>
                <label style="display: block; margin-bottom: 4px;">前端端口</label>
                <InputNumber v-model:value="deployForm.frontendPort" :min="1" :max="65535" style="width: 100%" />
              </div>
              <div>
                <label style="display: block; margin-bottom: 4px;">数据库名</label>
                <Input v-model:value="deployForm.databaseName" placeholder="项目数据库名" />
              </div>
              <div>
                <label style="display: block; margin-bottom: 4px;">Systemd 服务名</label>
                <Input v-model:value="deployForm.systemdServiceName" placeholder="如 pm-api" />
              </div>
              <div>
                <label style="display: block; margin-bottom: 4px;">宝塔站点名</label>
                <Input v-model:value="deployForm.btSiteName" placeholder="如 pm-test-admin.local" />
              </div>
              <div>
                <label style="display: block; margin-bottom: 4px;">默认部署目标</label>
                <Select v-model:value="deployForm.isDefault" :options="[{ label: '是', value: true }, { label: '否', value: false }]" style="width: 100%" />
              </div>
              <div style="grid-column: 1 / -1;">
                <label style="display: block; margin-bottom: 4px;">备注</label>
                <Textarea v-model:value="deployForm.remark" :rows="2" />
              </div>
            </div>
          </Modal>
        </TabPane>

        <!-- AI Tab -->
        <TabPane key="ai" tab="AI 助手">
          <div class="ai-tab-container">
            <!-- 顶部操作栏 -->
            <div style="display: flex; flex-wrap: wrap; gap: 8px; align-items: center; margin-bottom: 12px">
              <Select
                v-if="subProjects.length > 0"
                v-model:value="aiSelectedSubProject"
                placeholder="选择子项目"
                allow-clear
                style="width: 200px"
              >
                <Select.Option v-for="sp in subProjects" :key="sp.id" :value="sp.id">{{ sp.name }}</Select.Option>
              </Select>
              <div style="display: flex; gap: 4px">
                <Tag
                  v-for="t in aiTaskTypes"
                  :key="t.key"
                  :color="aiTaskType === t.key ? 'blue' : 'default'"
                  style="cursor: pointer; user-select: none"
                  @click="aiTaskType = t.key"
                >
                  {{ t.icon }} {{ t.label }}
                </Tag>
              </div>
              <Button v-if="aiMessages.length > 0" size="small" @click="aiMessages = []" style="margin-left: auto">清空</Button>
            </div>

            <!-- 对话区域 -->
            <div ref="aiChatAreaRef" class="ai-messages-area">
              <div v-if="aiMessages.length === 0" style=" padding: 60px 0; color: var(--ant-color-text-tertiary);text-align: center">
                选择任务类型，输入指令开始与 AI 对话
              </div>
              <div v-for="(msg, idx) in aiMessages" :key="idx" class="ai-msg" :class="msg.role">
                <div class="ai-msg-bubble" :class="{ error: msg.error }">
                  <Spin v-if="msg.loading" size="small" />
                  <div v-else-if="msg.role === 'user'" style="white-space: pre-wrap">{{ msg.content }}</div>
                  <div v-else v-html="simpleMarkdown(msg.content)"></div>
                </div>
              </div>
            </div>

            <!-- 输入 -->
            <div style="display: flex; gap: 8px; padding-top: 12px; border-top: 1px solid var(--ant-color-border-secondary)">
              <Input.TextArea
                v-model:value="aiInput"
                :auto-size="{ minRows: 1, maxRows: 4 }"
                placeholder="输入指令..."
                @press-enter="(e: KeyboardEvent) => { if (!e.shiftKey) { e.preventDefault(); handleAiSend(); } }"
              />
              <Button type="primary" :loading="aiSending" :disabled="!aiInput?.trim()" @click="handleAiSend">发送</Button>
            </div>
          </div>
        </TabPane>

        <!-- 聊天平台 Tab -->
        <TabPane key="chat" tab="聊天平台">
          <!-- 顶部状态栏：绑定信息摘要 + 操作按钮 -->
          <div style="display: flex; flex-wrap: wrap; gap: 8px; align-items: center; justify-content: space-between; margin-bottom: 12px;">
            <!-- 左侧：绑定状态摘要 -->
            <Space size="middle">
              <span style=" font-size: 13px;color: var(--ant-color-text-secondary);">
                群组: <Tag :color="chatGroups.length > 0 ? 'green' : 'default'">{{ chatGroups.length }} 个</Tag>
                用户: <Tag :color="chatUsers.length > 0 ? 'green' : 'default'">{{ chatUsers.length }} 个</Tag>
                <template v-for="g in chatGroups" :key="g.id">
                  <Tag v-if="g.status === 'Active'" style="margin-left: 4px;">{{ g.groupName || `#${g.id}` }}</Tag>
                </template>
              </span>
            </Space>
            <!-- 右侧：操作按钮 -->
            <Button size="small" @click="chatDetailDrawerOpen = true">管理详情</Button>
          </div>

          <!-- 聊天窗口（主体） -->
          <ChatWindow ref="chatWindowRef" height="calc(100vh - 320px)"
                      :group-binding-id="chatGroups.find((g: any) => g.status === 'Active')?.id" />

          <!-- 管理详情抽屉（低频操作收纳到这里） -->
          <Drawer v-model:open="chatDetailDrawerOpen" title="聊天平台管理" width="680" placement="right">
            <!-- 群组绑定 -->
            <Card title="群组绑定" size="small" style="margin-bottom: 16px;">
              <template #extra>
                <Button size="small" type="primary" @click="chatGroupModalOpen = true">绑定群组</Button>
              </template>
              <Table :data-source="chatGroups" :pagination="false" size="small" row-key="id">
                <Table.Column title="群组" data-index="groupName" />
                <Table.Column title="绑定码">
                  <template #default="{ record }">
                    <Tag v-if="record.bindCode" color="blue">{{ record.bindCode }}</Tag>
                    <span v-else>-</span>
                  </template>
                </Table.Column>
                <Table.Column title="状态" :width="70">
                  <template #default="{ record }">
                    <Tag :color="chatGroupStatusColors[record.status] || 'default'">{{ chatGroupStatusLabels[record.status] || record.status }}</Tag>
                  </template>
                </Table.Column>
                <Table.Column title="AI" :width="50">
                  <template #default="{ record }">
                    <Tag :color="record.aiEnabled ? 'green' : 'default'" style="cursor:pointer" @click="handleChatGroupToggleAi(record)">{{ record.aiEnabled ? '开' : '关' }}</Tag>
                  </template>
                </Table.Column>
                <Table.Column title="操作" :width="130">
                  <template #default="{ record }">
                    <Space wrap>
                      <Button :loading="chatAnalyzing" @click="handleChatAnalyze(record.id)">整理需求</Button>
                      <Button @click="handleChatSendToGroup(record.id)">发到群</Button>
                      <Popconfirm title="确定删除？" @confirm="handleChatGroupDel(record.id)">
                        <Button danger>删</Button>
                      </Popconfirm>
                    </Space>
                  </template>
                </Table.Column>
              </Table>
            </Card>

            <!-- 用户绑定 -->
            <Card title="用户绑定" size="small" style="margin-bottom: 16px;">
              <template #extra>
                <Button size="small" type="primary" @click="chatBindModalOpen = true">绑定用户</Button>
              </template>
              <Table :data-source="chatUsers" :pagination="false" size="small" row-key="id">
                <Table.Column title="用户名" data-index="platformUserName" />
                <Table.Column title="角色" :width="80">
                  <template #default="{ record }">
                    <Tag>{{ chatUserRoleLabels[record.role] || record.role }}</Tag>
                  </template>
                </Table.Column>
                <Table.Column title="状态" :width="70">
                  <template #default="{ record }">
                    <Tag :color="record.status === 'Active' ? 'green' : 'orange'">{{ record.status === 'Active' ? '已绑定' : '待验证' }}</Tag>
                  </template>
                </Table.Column>
                <Table.Column title="操作" :width="130">
                  <template #default="{ record }">
                    <Space wrap>
                      <Popconfirm title="确定解绑？" @confirm="handleChatUserDel(record.id)">
                        <Button danger>解绑</Button>
                      </Popconfirm>
                    </Space>
                  </template>
                </Table.Column>
              </Table>
            </Card>

            <!-- AI 需求 -->
            <Card title="AI 提取需求" size="small">
              <Table :data-source="chatRequirements" :pagination="false" size="small" row-key="id">
                <Table.Column title="标题" data-index="title" />
                <Table.Column title="状态" :width="80">
                  <template #default="{ record }">
                    <Tag :color="chatReqStatusColors[record.status] || 'default'">{{ chatReqStatusLabels[record.status] || record.status }}</Tag>
                  </template>
                </Table.Column>
                <Table.Column title="操作" :width="130">
                  <template #default="{ record }">
                    <Space wrap v-if="record.status === 'Draft' || record.status === 'PendingConfirm'">
                      <Button type="primary" @click="handleChatReqConfirm(record.id)">确认</Button>
                      <Button @click="handleChatReqReject(record.id)">拒绝</Button>
                    </Space>
                    <Tag v-else :color="chatReqStatusColors[record.status]">{{ chatReqStatusLabels[record.status] }}</Tag>
                  </template>
                </Table.Column>
              </Table>
            </Card>
          </Drawer>

          <GroupBindModal v-model:open="chatGroupModalOpen" :project-id="id" @success="loadChatData" />
          <UserBindModal v-model:open="chatBindModalOpen" :project-id="id" @success="loadChatData" />
        </TabPane>

        <!-- 资源管理 Tab -->
        <TabPane key="resource" tab="资源管理">
          <Card>
            <template #title>
              <Space>
                <span>项目资源</span>
                <Select
                  :value="docCategory"
                  placeholder="全部分类"
                  :options="[{ label: '全部', value: '' }, ...docCategoryOptions]"
                  style="width: 130px"
                  size="small"
                  @change="onDocCategoryChange"
                />
              </Space>
            </template>
            <template #extra>
              <Button type="primary" size="small" @click="openUploadModal">上传资源</Button>
            </template>

            <div v-if="docList.length === 0" style=" padding: 40px 0; color: #999;text-align: center">
              暂无资源文件
            </div>
            <div v-else style="display: grid; grid-template-columns: repeat(auto-fill, minmax(180px, 1fr)); gap: 12px">
              <div
                v-for="doc in docList"
                :key="doc.id"
                style=" position: relative; overflow: hidden; cursor: pointer;border: 1px solid #f0f0f0; border-radius: 8px; transition: box-shadow 0.2s"
                @click="openDocDrawer(doc)"
                @mouseenter="($event.currentTarget as HTMLElement).style.boxShadow = '0 2px 8px rgba(0,0,0,0.1)'"
                @mouseleave="($event.currentTarget as HTMLElement).style.boxShadow = 'none'"
              >
                <!-- 状态徽标 -->
                <Tag
                  v-if="parseMetadata(doc).status"
                  :color="docStatusColorMap[parseMetadata(doc).status] || 'default'"
                  style="position: absolute; top: 6px; right: 6px; z-index: 1; margin: 0; font-size: 11px"
                >
                  {{ parseMetadata(doc).status }}
                </Tag>
                <!-- 预览区域 -->
                <div style=" display: flex; align-items: center; justify-content: center;height: 120px; overflow: hidden; background: #fafafa">
                  <AImage
                    v-if="docImageExts.has((doc.fileType || '').toLowerCase()) && doc.filePath"
                    :src="getDocFileUrl(doc)"
                    :width="'100%'"
                    :height="120"
                    style="object-fit: cover"
                    :preview="{ src: getDocFileUrl(doc) }"
                    @click.stop
                  />
                  <span v-else style="font-size: 32px; color: #ccc">
                    {{ (doc.fileType || 'FILE').toUpperCase() }}
                  </span>
                </div>
                <!-- 信息区域 -->
                <div style="padding: 8px 10px">
                  <div style=" margin-bottom: 4px; overflow: hidden; text-overflow: ellipsis;font-size: 13px; font-weight: 500; white-space: nowrap" :title="doc.title">
                    {{ doc.title }}
                  </div>
                  <div style="display: flex; align-items: center; justify-content: space-between">
                    <Tag :color="docCatColorMap[doc.category] || 'default'" style="margin: 0">
                      {{ docCatLabelMap[doc.category] || doc.category || '-' }}
                    </Tag>
                    <span style="font-size: 11px; color: #999">{{ formatSize(doc.fileSize) }}</span>
                  </div>
                </div>
              </div>
            </div>
          </Card>

          <!-- 详情抽屉 -->
          <Drawer
            v-model:open="docDrawerVisible"
            :title="docDrawerItem?.title || '文档详情'"
            width="520"
            :destroy-on-close="false"
          >
            <template v-if="docDrawerItem">
              <!-- 文件预览 -->
              <div style=" display: flex; align-items: center; justify-content: center; min-height: 200px; padding: 12px;margin-bottom: 16px; text-align: center; background: #fafafa; border-radius: 8px">
                <AImage
                  v-if="docImageExts.has((docDrawerItem.fileType || '').toLowerCase()) && docDrawerItem.filePath"
                  :src="getDocFileUrl(docDrawerItem)"
                  style="max-width: 100%; max-height: 400px; border-radius: 4px"
                />
                <div v-else-if="docDrawerItem.filePath" style="text-align: center">
                  <div style=" margin-bottom: 8px;font-size: 48px; color: #ccc">{{ (docDrawerItem.fileType || 'FILE').toUpperCase() }}</div>
                  <Button type="primary" size="small" @click="handleDocPreview(docDrawerItem)">在新窗口打开</Button>
                </div>
                <span v-else style="color: #ccc">无文件</span>
              </div>

              <!-- 基本信息 -->
              <Descriptions :column="2" bordered size="small" style="margin-bottom: 16px">
                <DescriptionsItem label="分类">
                  <Tag :color="docCatColorMap[docDrawerItem.category] || 'default'">{{ docCatLabelMap[docDrawerItem.category] || docDrawerItem.category }}</Tag>
                </DescriptionsItem>
                <DescriptionsItem label="文件类型">{{ docDrawerItem.fileType || '-' }}</DescriptionsItem>
                <DescriptionsItem label="文件大小">{{ formatSize(docDrawerItem.fileSize) }}</DescriptionsItem>
                <DescriptionsItem label="来源">{{ docDrawerItem.operatorName || '-' }}</DescriptionsItem>
                <DescriptionsItem label="创建时间" :span="2">{{ formatDateTime(docDrawerItem.createdTime) }}</DescriptionsItem>
              </Descriptions>

              <!-- 元数据 -->
              <Card
                v-if="docMetaFields[docDrawerItem.category]"
                size="small"
                title="附加信息"
                style="margin-bottom: 16px"
              >
                <template #extra>
                  <Button v-if="!docMetaEditing" type="link" size="small" @click="docMetaEditing = true">编辑</Button>
                  <Space v-else>
                    <Button type="link" size="small" @click="saveDocMeta">保存</Button>
                    <Button type="link" size="small" @click="docMetaEditing = false; docMetaForm = parseMetadata(docDrawerItem)">取消</Button>
                  </Space>
                </template>
                <Descriptions :column="1" bordered size="small" v-if="!docMetaEditing">
                  <DescriptionsItem
                    v-for="field in docMetaFields[docDrawerItem.category]"
                    :key="field.key"
                    :label="field.label"
                  >
                    <Tag v-if="field.key === 'status' && docMetaForm[field.key]" :color="docStatusColorMap[docMetaForm[field.key]] || 'default'">
                      {{ docMetaForm[field.key] || '-' }}
                    </Tag>
                    <span v-else>{{ docMetaForm[field.key] || '-' }}</span>
                  </DescriptionsItem>
                </Descriptions>
                <div v-else style="display: grid; gap: 12px">
                  <div v-for="field in docMetaFields[docDrawerItem.category]" :key="field.key" style="display: flex; gap: 8px; align-items: center">
                    <span style=" flex-shrink: 0;width: 80px; font-size: 13px; text-align: right">{{ field.label }}:</span>
                    <Select
                      v-if="field.type === 'select'"
                      v-model:value="docMetaForm[field.key]"
                      :options="(field.options || []).map(o => ({ label: o, value: o }))"
                      style="flex: 1"
                      size="small"
                      allow-clear
                    />
                    <Input v-else v-model:value="docMetaForm[field.key]" size="small" style="flex: 1" />
                  </div>
                </div>
              </Card>

              <!-- 文档内容 -->
              <Card v-if="docDrawerItem.content" size="small" title="文档内容" style="margin-bottom: 16px">
                <div :class="{ 'doc-content-dark': isDark }" style=" overflow: hidden;border-radius: 6px">
                  <MdPreview :model-value="docDrawerItem.content" :theme="isDark ? 'dark' : 'light'" />
                </div>
              </Card>

              <!-- 操作按钮 -->
              <Space>
                <Button v-if="docDrawerItem.filePath" @click="handleDocPreview(docDrawerItem)">下载/预览</Button>
                <Popconfirm
                  v-if="docDrawerItem.category === 'Inbox'"
                  title="选择分类"
                  :show-cancel="false"
                  ok-text="关闭"
                >
                  <template #description>
                    <Space wrap style="max-width: 240px; padding: 6px 0">
                      <Button
                        v-for="opt in docCategoryOptions.filter(o => o.value !== 'Inbox')"
                        :key="opt.value"
                        size="small"
                        @click="handleDocClassify(docDrawerItem, opt.value)"
                      >
                        {{ opt.label }}
                      </Button>
                    </Space>
                  </template>
                  <Button type="primary">分类</Button>
                </Popconfirm>
                <Popconfirm title="确定删除？" @confirm="handleDocDelete(docDrawerItem.id)">
                  <Button danger>删除</Button>
                </Popconfirm>
              </Space>
            </template>
          </Drawer>

          <!-- 批量上传弹窗 -->
          <Modal
            v-model:open="uploadModalVisible"
            title="上传资源"
            :footer="null"
            width="600px"
            :mask-closable="!isUploading"
            :closable="!isUploading"
          >
            <div style="display: flex; gap: 12px; margin-bottom: 16px">
              <Select
                v-model:value="uploadCategory"
                placeholder="分类"
                :options="docCategoryOptions"
                style="width: 160px"
                :disabled="isUploading"
              />
            </div>

            <Upload.Dragger
              :before-upload="handleBeforeUpload"
              :show-upload-list="false"
              :multiple="true"
              :disabled="isUploading"
            >
              <div style="padding: 16px 0">
                <p style="font-size: 36px; color: #1890ff">+</p>
                <p style="font-size: 14px; color: rgb(0 0 0 / 85%)">
                  点击或拖拽文件到此区域（支持批量）
                </p>
              </div>
            </Upload.Dragger>

            <div
              v-if="uploadFiles.length > 0"
              style=" max-height: 300px;margin-top: 16px; overflow-y: auto; border: 1px solid #f0f0f0; border-radius: 6px"
            >
              <div
                v-for="(item, idx) in uploadFiles"
                :key="idx"
                style="display: flex; gap: 8px; align-items: center; padding: 8px 12px; border-bottom: 1px solid #f5f5f5"
              >
                <span style="font-size: 18px">{{ getFileIcon(item.file.name) }}</span>
                <div style="flex: 1; min-width: 0">
                  <div style=" overflow: hidden; text-overflow: ellipsis;font-size: 13px; white-space: nowrap">
                    {{ item.file.name }}
                  </div>
                  <div style="font-size: 11px; color: #999">{{ formatSize(item.file.size) }}</div>
                  <Progress v-if="item.status === 'uploading'" :percent="item.progress" size="small" :show-info="false" style="margin: 2px 0 0" />
                </div>
                <Tag v-if="item.status === 'done'" color="success">完成</Tag>
                <Tag v-else-if="item.status === 'error'" color="error">{{ item.error || '失败' }}</Tag>
                <Tag v-else-if="item.status === 'uploading'" color="processing">上传中</Tag>
                <Button v-if="item.status === 'pending'" type="text" size="small" danger @click="removeUploadFile(idx)">移除</Button>
              </div>
            </div>

            <div v-if="uploadFiles.length > 0" style=" display: flex; gap: 8px; justify-content: flex-end;margin-top: 16px">
              <Button v-if="!isUploading" @click="uploadFiles = []">清空列表</Button>
              <Button
                type="primary"
                :loading="isUploading"
                :disabled="uploadFiles.every(f => f.status === 'done')"
                @click="startBatchUpload"
              >
                {{ isUploading ? '上传中...' : `开始上传（${uploadFiles.filter(f => f.status !== 'done').length} 个文件）` }}
              </Button>
            </div>
          </Modal>
        </TabPane>

      </Tabs>
    </Spin>

    <!-- 需求新增/编辑弹窗 -->
    <Modal v-model:open="reqModalVisible" :title="reqModalTitle" :confirm-loading="reqSaving" @ok="handleReqSubmit" width="640px">
      <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 16px; padding: 16px 0">
        <div style="grid-column: 1 / -1">
          <div style="margin-bottom: 4px; font-weight: 500">标题 <span style="color: red">*</span></div>
          <Input v-model:value="reqForm.title" placeholder="请输入需求标题" />
        </div>
        <div>
          <div style="margin-bottom: 4px; font-weight: 500">类型</div>
          <Select v-model:value="reqForm.type" :options="reqTypeOptions" style="width: 100%" />
        </div>
        <div>
          <div style="margin-bottom: 4px; font-weight: 500">状态</div>
          <Select v-model:value="reqForm.status" :options="reqStatusOptions" style="width: 100%" />
        </div>
        <div>
          <div style="margin-bottom: 4px; font-weight: 500">负责人</div>
          <Input v-model:value="reqForm.assignee" placeholder="负责人" />
        </div>
        <div>
          <div style="margin-bottom: 4px; font-weight: 500">预估工时 (h)</div>
          <InputNumber v-model:value="reqForm.estimatedHours" :min="0" placeholder="预估工时" style="width: 100%" />
        </div>
        <div>
          <div style="margin-bottom: 4px; font-weight: 500">实际工时 (h)</div>
          <InputNumber v-model:value="reqForm.actualHours" :min="0" placeholder="实际工时" style="width: 100%" />
        </div>
        <div style="grid-column: 1 / -1">
          <div style="margin-bottom: 4px; font-weight: 500">截止日期</div>
          <DatePicker v-model:value="reqForm.dueDate" style="width: 100%" placeholder="选择截止日期" />
        </div>
        <div style="grid-column: 1 / -1">
          <div style="margin-bottom: 4px; font-weight: 500">关联功能模块</div>
          <TreeSelect
            v-model:value="reqFeatureModuleIds"
            :tree-data="featureTreeData"
            placeholder="选择关联的功能模块"
            tree-checkable
            :show-checked-strategy="TreeSelect.SHOW_ALL"
            allow-clear
            tree-default-expand-all
            style="width: 100%"
          />
        </div>
        <div style="grid-column: 1 / -1">
          <div style="margin-bottom: 4px; font-weight: 500">需求描述</div>
          <Textarea v-model:value="reqForm.content" :rows="4" placeholder="请输入需求描述" />
        </div>
      </div>
    </Modal>

    <!-- AI 分析拆分需求弹窗 -->
    <Modal v-model:open="aiAnalyzeVisible" title="AI 分析拆分需求" width="900px" :footer="null" :maskClosable="false">
      <div style="padding: 8px 0;">
        <!-- 文本输入区 -->
        <Textarea
          v-model:value="aiAnalyzeContent"
          :rows="aiStagingList.length > 0 ? 3 : 8"
          placeholder="粘贴聊天记录、需求文档或其他文本内容（图片以地址形式粘贴）..."
          :disabled="aiAnalyzing"
          style="transition: all 0.3s;"
        />
        <div style=" display: flex; gap: 8px;margin-top: 8px;">
          <Button type="primary" :loading="aiAnalyzing" @click="handleAiAnalyze" :disabled="!aiAnalyzeContent.trim()">
            {{ aiAnalyzing ? '分析中...' : '分析拆分需求' }}
          </Button>
          <Button v-if="aiStagingList.length > 0 && !aiAnalyzing" @click="handleStagingConfirmAll" :disabled="aiStagingList.filter((s: any) => !s.isDeleted && !s.isConfirmed).length === 0">
            全部确认 ({{ aiStagingList.filter((s: any) => !s.isDeleted && !s.isConfirmed).length }})
          </Button>
        </div>

        <!-- 分析结果表格 -->
        <div v-if="aiStagingList.length > 0 || aiAnalyzing" style="margin-top: 16px;">
          <Spin :spinning="aiAnalyzing" tip="AI 正在分析拆分需求...">
          <Table :data-source="aiStagingList" row-key="id" size="small" :pagination="false"
            :row-class-name="(record: any) => record.isDeleted ? 'staging-deleted' : record.isConfirmed ? 'staging-confirmed' : ''"
            :columns="[
              { title: '标题', dataIndex: 'title', key: 'title', ellipsis: true },
              { title: '类型', dataIndex: 'type', key: 'type', width: 80 },
              { title: '描述', dataIndex: 'content', key: 'content', ellipsis: true, width: 200 },
              { title: '状态', key: 'state', width: 80 },
              { title: '操作', key: 'action', width: 130 },
            ]">
            <template #bodyCell="{ column, record }">
              <template v-if="column.key === 'type'">
                <Tag>{{ requirementTypeLabels[record.type] || record.type }}</Tag>
              </template>
              <template v-else-if="column.key === 'state'">
                <Tag v-if="record.isConfirmed" color="green">已确认</Tag>
                <Tag v-else-if="record.isDeleted" color="default">已删除</Tag>
                <Tag v-else color="blue">待确认</Tag>
              </template>
              <template v-else-if="column.key === 'action'">
                <Space wrap v-if="!record.isConfirmed">
                  <Button v-if="!record.isDeleted" @click="startEditStaging(record)">编辑</Button>
                  <Button v-if="!record.isDeleted" type="primary" @click="handleStagingConfirmSingle(record)">确认</Button>
                  <Button v-if="!record.isDeleted" danger @click="handleStagingReject(record)">删除</Button>
                  <Button v-if="record.isDeleted" @click="handleStagingRestore(record)">恢复</Button>
                </Space>
                <span v-else style="color: var(--ant-color-text-tertiary)">-</span>
              </template>
            </template>
          </Table>
          </Spin>
        </div>
      </div>

      <!-- 编辑暂存需求弹窗 -->
      <Modal v-model:open="aiEditingRow" :title="aiEditingRow ? '编辑需求' : ''" @ok="saveEditStaging" width="500px"
        :open="!!aiEditingRow" @cancel="aiEditingRow = null">
        <template v-if="aiEditingRow">
          <div style=" display: flex; flex-direction: column; gap: 12px;padding: 8px 0;">
            <div>
              <label style="display: block; margin-bottom: 4px; font-weight: 500;">标题</label>
              <Input v-model:value="aiEditingRow.title" />
            </div>
            <div>
              <label style="display: block; margin-bottom: 4px; font-weight: 500;">类型</label>
              <Select v-model:value="aiEditingRow.type" :options="toOptions(requirementTypeLabels)" style="width: 100%" />
            </div>
            <div>
              <label style="display: block; margin-bottom: 4px; font-weight: 500;">描述</label>
              <Textarea v-model:value="aiEditingRow.content" :rows="4" />
            </div>
          </div>
        </template>
      </Modal>
    </Modal>
  </Page>
</template>

<style scoped>
.project-logo-banner {
  display: flex;
  gap: 16px;
  align-items: center;
  margin-bottom: 16px;
}

.project-logo-img {
  width: 56px;
  height: 56px;
  object-fit: contain;
  background: var(--ant-color-bg-container);
  border: 1px solid var(--ant-color-border-secondary);
  border-radius: 8px;
}

.project-logo-name {
  font-size: 18px;
  font-weight: 600;
  color: var(--ant-color-text);
}

.sp-overview-card {
  padding: 14px 16px;
  background: var(--ant-color-fill-quaternary);
  border: 1px solid var(--ant-color-border);
  border-radius: 8px;
  transition: border-color 0.2s;
}

.sp-overview-card:hover {
  border-color: var(--ant-color-primary);
}

/* AI Tab */
.ai-tab-container {
  display: flex;
  flex-direction: column;
  height: calc(100vh - 240px);
  min-height: 400px;
}

.ai-messages-area {
  display: flex;
  flex: 1;
  flex-direction: column;
  gap: 12px;
  padding: 12px 0;
  overflow-y: auto;
}

.ai-msg {
  display: flex;
}

.ai-msg.user {
  justify-content: flex-end;
}

.ai-msg-bubble {
  max-width: 80%;
  padding: 10px 14px;
  font-size: 14px;
  line-height: 1.6;
  border-radius: 10px;
}

.ai-msg.user .ai-msg-bubble {
  color: #fff;
  background: var(--ant-color-primary);
  border-bottom-right-radius: 4px;
}

.ai-msg.assistant .ai-msg-bubble {
  background: var(--ant-color-fill-quaternary);
  border: 1px solid var(--ant-color-border);
  border-bottom-left-radius: 4px;
}

.ai-msg-bubble.error {
  color: var(--ant-color-error);
  background: var(--ant-color-error-bg);
  border-color: var(--ant-color-error-border);
}

.ai-msg-bubble :deep(pre) {
  padding: 10px;
  margin: 6px 0;
  overflow-x: auto;
  background: var(--ant-color-bg-elevated);
  border: 1px solid var(--ant-color-border);
  border-radius: 6px;
}

.ai-msg-bubble :deep(code) {
  font-family: Menlo, monospace;
  font-size: 13px;
}

/* Feature Module Tab */
.fm-tab-container {
  display: flex;
  gap: 0;
  height: calc(100vh - 240px);
  min-height: 400px;
}

.fm-tab-tree {
  flex-shrink: 0;
  width: 300px;
  padding: 12px;
  overflow-y: auto;
  border-right: 1px solid var(--ant-color-border);
}

.fm-tab-detail {
  flex: 1;
  padding: 16px;
  overflow-y: auto;
}

.mindmap-tabs {
  display: flex;
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

.mindmap-wrapper {
  display: flex;
  flex-direction: column;
  height: calc(100vh - 200px);
  min-height: 400px;
}

.mindmap-wrapper :deep(.mindmap-spin),
.mindmap-wrapper :deep(.ant-spin-nested-loading),
.mindmap-wrapper :deep(.ant-spin-container) {
  display: flex;
  flex: 1;
  flex-direction: column;
  height: 100%;
}

.mindmap-render-area {
  flex: 1;
  width: 100%;
  min-height: 0;
  overflow: hidden;
}

.project-desc-body {
  overflow: hidden;
  background: #f6f8fa;
  border: 1px solid #e5e7eb;
  border-radius: 8px;
  transition: max-height 0.3s ease;
}

.project-desc-body.collapsed {
  position: relative;
  max-height: 200px;
}

.project-desc-body.collapsed::after {
  position: absolute;
  right: 0;
  bottom: 0;
  left: 0;
  height: 60px;
  pointer-events: none;
  content: '';
  background: linear-gradient(transparent, #f6f8fa);
}

.project-desc-body.dark {
  background: #161b22;
  border-color: #30363d;
}

.project-desc-body.dark.collapsed::after {
  background: linear-gradient(transparent, #161b22);
}

:deep(.staging-deleted) td {
  text-decoration: line-through !important;
  opacity: 0.4;
}

:deep(.staging-confirmed) td {
  opacity: 0.6;
}
</style>

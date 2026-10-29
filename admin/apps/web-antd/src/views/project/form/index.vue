<script lang="ts" setup>
import { computed, onMounted, ref, watch } from 'vue';
import { useRoute, useRouter } from 'vue-router';

import {
  Alert,
  Button,
  Card,
  Checkbox,
  Input,
  message,
  Result,
  Select,
  Space,
  Steps,
  Switch,
  Spin,
  Popconfirm,
  Tag,
  Tooltip,
  Upload,
} from 'ant-design-vue';

import { useVbenForm } from '#/adapter/form';
import { usePreferences } from '@vben/preferences';
import { MdEditor } from 'md-editor-v3';
import 'md-editor-v3/lib/style.css';

import { projectStatusLabels, toOptions } from '#/utils/enum-labels';
import { getSystemSettingByKey } from '#/views/system-setting/api';

import {
  getCheckPathExists,
  getConfigCategoryList,
  getDetail,
  getTemplateList,
  postAdd,
  postConfigSave,
  postEdit,
  getSubProjectList,
  postSubProjectSaveList,
  getGitLabGroups,
  getGitLabGroupProjects,
  getGitLabProjects,
  postGitLabCreateGroup,
  postGitLabCreateProject,
  postUploadProjectLogo,
} from '../api';

const router = useRouter();
const route = useRoute();
const { isDark } = usePreferences();

const id = ref(0);
const currentStep = ref(0);
const saving = ref(false);
const isEdit = computed(() => !!route.query.id);
const editorContent = ref('');
const mdTheme = computed(() => isDark.value ? 'dark' : 'light');

// ========== 项目 Logo ==========

const projectLogoUrl = ref('');
const logoUploading = ref(false);

// 拼成可访问 URL（后端返回相对路径 /storage/...，vite/nginx 已代理）
const logoPreviewUrl = computed(() => projectLogoUrl.value || '');

// Upload before-upload：拦截后手动调后端处理，不走 antd 默认上传
async function handleLogoUpload(file: File) {
  const isImage = file.type.startsWith('image/');
  if (!isImage) {
    message.error('请上传图片文件');
    return Upload.LIST_IGNORE;
  }
  if (file.size > 20 * 1024 * 1024) {
    message.error('图片不能超过 20MB');
    return Upload.LIST_IGNORE;
  }
  logoUploading.value = true;
  try {
    const res = await postUploadProjectLogo(file);
    projectLogoUrl.value = res?.url || '';
    message.success('Logo 上传成功');
  } catch {
    message.error('Logo 上传失败');
  } finally {
    logoUploading.value = false;
  }
  return Upload.LIST_IGNORE; // 阻止 antd 自己上传 + 不进文件列表
}

function removeLogo() {
  projectLogoUrl.value = '';
}

// AI 生成 Logo —— 占位（PM 现有 AI 仅文本模型，文生图能力待接入）
function handleAiGenerateLogo() {
  message.info('AI 生成 Logo 功能即将上线（需接入文生图模型），敬请期待');
}

// ========== 本地路径 ==========

const workspaceRoot = ref('');
const projectCode = ref('');
const localPath = ref('');
const localPathManual = ref(false);
const localPathExists = ref(false);
const localPathForce = ref(false);
let pathCheckTimer: any = null;

async function checkLocalPath(path: string) {
  if (!path?.trim()) { localPathExists.value = false; return; }
  try {
    const result: any = await getCheckPathExists(path.trim());
    localPathExists.value = !!result?.exists;
    if (!result?.exists) localPathForce.value = false;
  } catch { localPathExists.value = false; }
}

watch(localPath, (val) => {
  clearTimeout(pathCheckTimer);
  pathCheckTimer = setTimeout(() => checkLocalPath(val), 400);
});

// ========== GitLab 群组 ==========

const gitlabGroups = ref<any[]>([]);
const groupLoading = ref(false);
const selectedGroupId = ref<number | undefined>();
const gitlabGroupInfo = ref<{ id?: number; path?: string; url?: string }>({});
const newGroupName = ref('');
const creatingGroup = ref(false);

watch(
  [projectCode, () => gitlabGroupInfo.value.path, workspaceRoot],
  () => {
    console.log('[localPath watch] 触发', { projectCode: projectCode.value, groupPath: gitlabGroupInfo.value.path, workspaceRoot: workspaceRoot.value, localPathManual: localPathManual.value });
    if (localPathManual.value) { console.log('[localPath watch] 跳过: localPathManual=true'); return; }
    const root = workspaceRoot.value?.replace(/\/$/, '');
    if (!root) { console.log('[localPath watch] 跳过: workspaceRoot 为空'); return; }
    const code = projectCode.value?.trim();
    const groupPath = gitlabGroupInfo.value.path?.trim() || '';
    if (groupPath && code) {
      localPath.value = `${root}/${groupPath}/${code}`;
    } else if (code) {
      localPath.value = `${root}/${code}`;
    } else {
      localPath.value = '';
    }
    console.log('[localPath watch] 结果', localPath.value);
  },
);

async function loadGitLabGroups(search?: string) {
  groupLoading.value = true;
  try {
    const result: any = await getGitLabGroups({ search, perPage: 50 });
    gitlabGroups.value = (Array.isArray(result) ? result : []).map((g: any) => ({
      label: `${g.fullName || g.name}`,
      value: g.id,
      fullPath: g.fullPath,
      webUrl: g.webUrl,
    }));
  } catch {
    gitlabGroups.value = [];
  } finally {
    groupLoading.value = false;
  }
}

function handleGroupChange(value: any) {
  console.log('[handleGroupChange] 触发, value=', value, 'localPathManual 重置为 false');
  localPathManual.value = false;
  const group = gitlabGroups.value.find((g) => g.value === value);
  if (group) {
    selectedGroupId.value = group.value;
    gitlabGroupInfo.value = { id: group.value, path: group.fullPath, url: group.webUrl };
    loadGroupProjects(group.value);
  } else {
    selectedGroupId.value = undefined;
    gitlabGroupInfo.value = {};
    groupProjects.value = [];
  }
}

async function handleCreateGroup() {
  const name = newGroupName.value?.trim();
  if (!name) return message.warning('请输入群组名称');
  const path = name.toLowerCase().replace(/[^a-z0-9-]/g, '-').replace(/-+/g, '-').replace(/^-|-$/g, '');
  creatingGroup.value = true;
  try {
    const result: any = await postGitLabCreateGroup({ name, path });
    message.success('群组创建成功');
    await loadGitLabGroups();
    selectedGroupId.value = result.id;
    gitlabGroupInfo.value = { id: result.id, path: result.fullPath || path, url: result.webUrl };
    newGroupName.value = '';
    loadGroupProjects(result.id);
  } catch {
    // error handled by api wrapper
  } finally {
    creatingGroup.value = false;
  }
}

// ========== Step 1: 基本信息 ==========

const [BaseForm, baseFormApi] = useVbenForm({
  showDefaultActions: false,
  handleSubmit: () => {},
  commonConfig: {
    colon: true,
    componentProps: { class: 'w-full' },
  },
  layout: 'horizontal',
  schema: [
    {
      fieldName: 'name',
      component: 'Input',
      label: '项目名称',
      rules: 'required',
      componentProps: { placeholder: '请输入项目名称' },
    },
    {
      fieldName: 'code',
      component: 'Input',
      label: '项目编码',
      rules: 'required',
      componentProps: {
        placeholder: '请输入项目编码',
        onBlur: (e: FocusEvent) => {
          const val = (e.target as HTMLInputElement).value;
          console.log('[code onBlur] 触发, value=', val);
          projectCode.value = val;
        },
      },
    },
    {
      fieldName: 'status',
      component: 'Select',
      label: '状态',
      defaultValue: 'Planning',
      componentProps: { placeholder: '请选择状态', options: toOptions(projectStatusLabels) },
    },
    {
      fieldName: 'tags',
      component: 'Input',
      label: '标签',
      componentProps: { placeholder: '逗号分隔' },
    },
    {
      fieldName: 'portRangeStart',
      component: 'InputNumber',
      label: '端口起始',
      componentProps: { placeholder: '如 6020', min: 1, max: 65535, style: { width: '100%' } },
    },
    {
      fieldName: 'portRangeEnd',
      component: 'InputNumber',
      label: '端口结束',
      componentProps: { placeholder: '如 6029', min: 1, max: 65535, style: { width: '100%' } },
    },
    {
      fieldName: 'templateId',
      component: 'ApiSelect',
      label: '项目模板',
      componentProps: {
        placeholder: '请选择项目模板',
        allowClear: true,
        api: getTemplateList,
        afterFetch: (data: any) => data.items.map((item: any) => ({ label: item.name, value: item.id })),
      },
    },
    {
      fieldName: 'startDate',
      component: 'DatePicker',
      label: '开始日期',
      componentProps: { placeholder: '请选择开始日期', style: { width: '100%' }, valueFormat: 'YYYY-MM-DD' },
    },
    {
      fieldName: 'dueDate',
      component: 'DatePicker',
      label: '截止日期',
      componentProps: { placeholder: '请选择截止日期', style: { width: '100%' }, valueFormat: 'YYYY-MM-DD' },
    },
  ],
  wrapperClass: 'grid-cols-1 md:grid-cols-2 lg:grid-cols-3',
});

// ========== Step 2: 子项目管理 ==========

function calcSubLocalPath(subCode: string): string {
  const base = localPath.value?.trim();
  const code = subCode?.trim();
  return base && code ? `${base}/${code}` : '';
}

watch(localPath, () => {
  for (const item of subProjects.value) {
    if (!item._localPathManual && item.code?.trim()) {
      item.localPath = calcSubLocalPath(item.code);
    }
  }
});

const subProjects = ref<any[]>([]);
const groupProjects = ref<any[]>([]);
const groupProjectsLoading = ref(false);
const autoPopulatingSubProjects = ref(false);

// 从 GitLab 群组增量合并子项目：只追加当前列表里还没有的仓库（按 gitlabProjectId / path 去重），
// 因此新建、编辑都能用，重复点击也安全，不会覆盖已手动编辑的子项目。返回新增数量。
async function autoPopulateFromGroup(): Promise<number> {
  if (!gitlabGroupInfo.value.id) return 0;
  autoPopulatingSubProjects.value = true;
  try {
    await loadGroupProjects(gitlabGroupInfo.value.id);
    const existingIds = new Set(subProjects.value.map((s: any) => s.gitlabProjectId).filter(Boolean));
    const existingPaths = new Set(subProjects.value.map((s: any) => s.gitlabProjectPath).filter(Boolean));
    let added = 0;
    for (const p of groupProjects.value) {
      if ((p.id && existingIds.has(p.id)) || (p.path && existingPaths.has(p.path))) continue;
      const code = p.path?.split('/').pop() || '';
      subProjects.value.push({
        id: 0,
        name: p.label?.split('  (')[0]?.trim() || '',
        code,
        description: '',
        baseRepoUrl: undefined,
        gitlabProjectId: p.id,
        gitlabProjectPath: p.path,
        gitlabProjectUrl: p.httpUrlToRepo || p.value,
        defaultBranch: 'main',
        localPath: calcSubLocalPath(code),
        sort: subProjects.value.length,
        _codeManual: true,
      });
      added++;
    }
    return added;
  } finally {
    autoPopulatingSubProjects.value = false;
  }
}

// Step2 手动「从 GitLab 群组同步」按钮
async function handleSyncFromGroup() {
  if (!gitlabGroupInfo.value.id) {
    message.warning('请先在第一步选择 GitLab 群组');
    return;
  }
  const added = await autoPopulateFromGroup();
  if (added > 0) message.success(`已从群组同步 ${added} 个新子项目`);
  else message.info('当前子项目已是最新，没有新增仓库');
}

async function loadGroupProjects(groupId: number) {
  if (!groupId) return;
  groupProjectsLoading.value = true;
  try {
    const result: any = await getGitLabGroupProjects(groupId, { perPage: 100 });
    groupProjects.value = (Array.isArray(result) ? result : []).map((p: any) => ({
      label: `${p.name}  (${p.httpUrlToRepo || p.webUrl})`,
      value: p.httpUrlToRepo || p.webUrl,
      id: p.id,
      path: p.pathWithNamespace,
      webUrl: p.webUrl,
      httpUrlToRepo: p.httpUrlToRepo,
    }));
  } catch {
    groupProjects.value = [];
  } finally {
    groupProjectsLoading.value = false;
  }
}

// 基座仓库：搜索整个 GitLab 的所有仓库
const baseRepoOptions = ref<any[]>([]);
const baseRepoLoading = ref(false);
let baseRepoSearchTimer: any = null;

// 初始化已有的基座仓库到选项列表（编辑模式回显用）
function initBaseRepoOptions() {
  const existing = subProjects.value
    .filter((s: any) => s.baseRepoUrl)
    .map((s: any) => s.baseRepoUrl);
  const unique = [...new Set(existing)];
  if (unique.length > 0) {
    baseRepoOptions.value = unique.map((url: string) => ({
      label: url,
      value: url,
    }));
  }
}

async function loadBaseRepoList(search?: string) {
  baseRepoLoading.value = true;
  try {
    const result: any = await getGitLabProjects({ search: search || undefined, perPage: 50 });
    const searchResults = (Array.isArray(result) ? result : []).map((p: any) => ({
      label: `${p.nameWithNamespace || p.name}`,
      value: p.httpUrlToRepo || p.webUrl,
    }));
    // 合并已有子项目的 baseRepoUrl（保证编辑回显）
    const existingUrls = subProjects.value.filter((s: any) => s.baseRepoUrl).map((s: any) => s.baseRepoUrl);
    const allValues = new Set(searchResults.map((o: any) => o.value));
    const merged = [...searchResults];
    for (const url of existingUrls) {
      if (!allValues.has(url)) merged.push({ label: url, value: url });
    }
    baseRepoOptions.value = merged;
  } catch {
    // keep existing
  } finally {
    baseRepoLoading.value = false;
  }
}

function handleBaseRepoSearch(value: string) {
  if (baseRepoSearchTimer) clearTimeout(baseRepoSearchTimer);
  baseRepoSearchTimer = setTimeout(() => loadBaseRepoList(value), 300);
}

function handleBaseRepoDropdownOpen(open: boolean) {
  if (open && baseRepoOptions.value.length === 0) {
    loadBaseRepoList();
  }
}

function addSubProject() {
  subProjects.value.push({
    id: 0,
    name: '',
    code: '',
    description: '',
    baseRepoUrl: undefined,
    gitlabProjectId: undefined,
    gitlabProjectPath: '',
    gitlabProjectUrl: '',
    defaultBranch: 'main',
    localPath: '',
    sort: subProjects.value.length,
  });
}

function removeSubProject(index: number) {
  subProjects.value.splice(index, 1);
}

// 名称变化时自动生成编码和仓库地址
function onSubProjectNameChange(index: number) {
  const item = subProjects.value[index]!;
  const name = item.name?.trim() || '';
  if (!name) return;

  // 自动生成编码
  const code = name.toLowerCase().replace(/[^a-z0-9\u4e00-\u9fa5-]/g, '-').replace(/[\u4e00-\u9fa5]+/g, (match: string) => {
    // 简单拼音首字母或直接用 kebab
    return match;
  }).replace(/[^a-z0-9-]/g, '-').replace(/-+/g, '-').replace(/^-|-$/g, '');

  // 仅在编码为空或之前是自动生成的情况下才覆盖
  if (!item._codeManual) {
    item.code = code || name;
  }

  // 自动拼接仓库地址和本地路径
  generateRepoUrl(index);
  if (!item._localPathManual) {
    item.localPath = calcSubLocalPath(item.code);
  }
}

function onSubProjectCodeChange(index: number) {
  const item = subProjects.value[index]!;
  item._codeManual = true;
  generateRepoUrl(index);
  if (!item._localPathManual) {
    item.localPath = calcSubLocalPath(item.code);
  }
}

function generateRepoUrl(index: number) {
  const item = subProjects.value[index]!;
  const groupUrl = gitlabGroupInfo.value.url;
  const groupPath = gitlabGroupInfo.value.path || '';
  const code = item.code?.trim();
  if (groupUrl && code) {
    // 群组 webUrl 形如 https://gitlab.saibo.club/groups/saibo/apps —— 含 /groups/ 段,
    // 而项目 clone 地址是 {根域}/{群组fullPath}/{code}.git(不带 groups)。
    // 直接拿 webUrl 拼会得到 .../groups/saibo/{code}.git(带 groups 且丢子组层级),clone 必失败。
    // 正确做法:从 webUrl 剥出根域,再用群组 fullPath 重新拼。
    const origin = groupUrl.replace(/\/groups\/.*$/, '').replace(/\/$/, '');
    item.gitlabProjectUrl = `${origin}/${groupPath}/${code}.git`;
    item.gitlabProjectPath = `${groupPath}/${code}`;
  }
}

// 选择已有仓库
function handleSelectExistingRepo(index: number, value: any) {
  const item = subProjects.value[index]!;
  if (!value) {
    item.gitlabProjectId = undefined;
    item.gitlabProjectPath = '';
    item.gitlabProjectUrl = '';
    return;
  }
  const repo = groupProjects.value.find((p) => p.value === value);
  if (repo) {
    item.gitlabProjectId = repo.id;
    item.gitlabProjectPath = repo.path;
    item.gitlabProjectUrl = repo.httpUrlToRepo || repo.webUrl || value;
    if (!item.name?.trim()) {
      const repoName = repo.label?.split('  (')[0] || '';
      item.name = repoName;
    }
  }
}

// 创建 GitLab 仓库
const creatingProject = ref<number | null>(null);
async function handleCreateRepo(index: number) {
  const item = subProjects.value[index]!;
  if (!item.name?.trim()) return message.warning('请先填写子项目名称');

  const groupId = gitlabGroupInfo.value.id;
  if (!groupId) return message.warning('请先选择 GitLab 群组');

  const path = item.code?.trim() || item.name.trim().toLowerCase().replace(/[^a-z0-9-]/g, '-').replace(/-+/g, '-');
  creatingProject.value = index;
  try {
    const result: any = await postGitLabCreateProject({
      namespaceId: groupId,
      name: item.name.trim(),
      path,
    });
    item.gitlabProjectId = result.id;
    item.gitlabProjectPath = result.pathWithNamespace;
    item.gitlabProjectUrl = result.httpUrlToRepo || result.webUrl;
    message.success(`仓库 ${result.name} 创建成功`);
    loadGroupProjects(groupId);
  } catch {
    // error handled by api wrapper
  } finally {
    creatingProject.value = null;
  }
}

// ========== Step 3: 项目配置 ==========

const categoryOptions = ref<{ label: string; value: number }[]>([]);
const configItems = ref<any[]>([]);

async function loadCategories() {
  try {
    const result: any = await getConfigCategoryList();
    const items = Array.isArray(result) ? result : (result?.items || []);
    categoryOptions.value = items.map((item: any) => ({ label: item.name, value: item.id }));
  } catch { /* ignore */ }
}

function addConfigItem() {
  configItems.value.push({ categoryId: undefined, configKey: '', configName: '', configValue: '', isSensitive: false });
}

function removeConfigItem(index: number) {
  configItems.value.splice(index, 1);
}

// ========== 步骤导航 ==========

async function handleNext() {
  if (currentStep.value === 0) {
    const values: any = await baseFormApi.validateAndSubmitForm();
    if (!values) return;
    values.description = editorContent.value;
    values.logoUrl = projectLogoUrl.value || null;
    values.gitlabGroupId = gitlabGroupInfo.value.id || null;
    values.gitlabGroupPath = gitlabGroupInfo.value.path || '';
    values.gitlabGroupUrl = gitlabGroupInfo.value.url || '';
    values.localPath = localPath.value || null;
    // 编辑已有项目时本地路径必然已存在，允许直接修正；仅新建时拦截
    if (!isEdit.value && localPathExists.value && !localPathForce.value) {
      message.warning('路径已存在，请勾选「强制在本路径创建」后继续');
      return;
    }
    values.forceCreate = localPathForce.value;
    saving.value = true;
    try {
      if (id.value > 0) {
        values.id = id.value;
        await postEdit(values);
      } else {
        const data: any = await postAdd(values);
        id.value = data.id;
      }
      if (isEdit.value && id.value > 0) {
        const subResult: any = await getSubProjectList({ projectId: id.value });
        subProjects.value = subResult?.items || [];
        initBaseRepoOptions();
      }
      currentStep.value = 1;
      // 新建项目且有群组：进入 step2 后异步加载并自动填充
      if (!isEdit.value && gitlabGroupInfo.value.id) {
        autoPopulateFromGroup();
      } else if (gitlabGroupInfo.value.id && groupProjects.value.length === 0) {
        loadGroupProjects(gitlabGroupInfo.value.id);
      }
    } catch { /* API wrapper handles error */ } finally {
      saving.value = false;
    }
  } else if (currentStep.value === 1) {
    // 自动创建未有仓库的子项目
    const groupId = gitlabGroupInfo.value.id;
    for (let i = 0; i < subProjects.value.length; i++) {
      const item = subProjects.value[i]!;
      if (item.name?.trim() && !item.gitlabProjectId && groupId) {
        await handleCreateRepo(i);
      }
    }
    // 保存子项目
    const validSubs = subProjects.value.filter((s: any) => s.name?.trim());
    if (validSubs.length > 0) {
      saving.value = true;
      try {
        await postSubProjectSaveList(id.value, validSubs);
      } catch { return; } finally {
        saving.value = false;
      }
    }
    await loadCategories();
    currentStep.value = 2;
  } else if (currentStep.value === 2) {
    const validConfigs = configItems.value.filter((item: any) => item.configKey && item.configName);
    if (validConfigs.length > 0) {
      saving.value = true;
      try {
        await postConfigSave(validConfigs.map((item: any) => ({ ...item, projectId: id.value })));
      } catch { return; } finally {
        saving.value = false;
      }
    }
    currentStep.value = 3;
  }
}

function handlePrev() { currentStep.value--; }
function goToDetail() { router.push({ path: '/project/detail', query: { id: String(id.value) } }); }
function goToList() { router.push({ path: '/project/list' }); }

// ========== 初始化 ==========

onMounted(async () => {
  id.value = Number(route.query.id) || 0;
  loadGitLabGroups();
  try {
    const setting: any = await getSystemSettingByKey('workspace_root');
    workspaceRoot.value = setting?.dataContent || '';
    console.log('[onMounted] workspace_root 加载结果:', setting, '→ workspaceRoot=', workspaceRoot.value);
  } catch { /* workspace_root 未配置时静默 */ }
  if (id.value > 0) {
    try {
      const result: any = await getDetail(id.value);
      baseFormApi.setValues(result);
      projectCode.value = result.code || '';
      editorContent.value = result.description || '';
      projectLogoUrl.value = result.logoUrl || '';
      if (result.localPath) {
        localPath.value = result.localPath;
        localPathManual.value = true;
      }
      if (result.gitlabGroupId) {
        selectedGroupId.value = result.gitlabGroupId;
        gitlabGroupInfo.value = { id: result.gitlabGroupId, path: result.gitlabGroupPath, url: result.gitlabGroupUrl };
        loadGroupProjects(result.gitlabGroupId);
      }
    } catch { message.error('加载项目信息失败'); }
  }
});
</script>

<template>
  <Card :title="isEdit ? '编辑项目' : '新增项目'" style="margin: 10px">
    <Steps
      :current="currentStep"
      :items="[{ title: '基本信息' }, { title: '子项目' }, { title: '项目配置' }, { title: '完成' }]"
      style="max-width: 700px; margin: 0 auto 32px"
    />

    <!-- Step 1: 基本信息 -->
    <div v-show="currentStep === 0">
      <!-- 项目 Logo -->
      <div class="logo-section">
        <div class="logo-preview">
          <img v-if="logoPreviewUrl" :src="logoPreviewUrl" alt="项目 Logo" class="logo-img" />
          <div v-else class="logo-placeholder">LOGO</div>
        </div>
        <div class="logo-actions">
          <div class="logo-title">项目 Logo</div>
          <div class="logo-hint">支持 PNG / JPG / WEBP 等，自动处理为 512×512</div>
          <Space>
            <Upload
              :show-upload-list="false"
              accept="image/*"
              :before-upload="handleLogoUpload"
            >
              <Button type="primary" :loading="logoUploading">
                {{ logoPreviewUrl ? '更换 Logo' : '上传 Logo' }}
              </Button>
            </Upload>
            <Tooltip title="PM 现有 AI 仅文本模型，文生图能力待接入">
              <Button @click="handleAiGenerateLogo">AI 生成</Button>
            </Tooltip>
            <Button v-if="logoPreviewUrl" danger @click="removeLogo">移除</Button>
          </Space>
        </div>
      </div>

      <BaseForm />
      <div style="max-width: 900px; padding: 0 12px; margin: 0 auto">
        <!-- 本地路径 -->
        <div class="form-row">
          <label class="form-label">本地路径：</label>
          <Input
            v-model:value="localPath"
            placeholder="项目在本机的绝对路径"
            style="flex: 1"
            @input="localPathManual = true"
            @change="(e: any) => { if (!e.target?.value) localPathManual = false; }"
          />
        </div>
        <div v-if="localPath && !localPathManual" class="form-hint">
          已根据工程根目录自动计算，可手动修改
        </div>
        <div v-if="localPathExists" style="margin: 6px 0">
          <Alert
            type="warning"
            show-icon
            :message="`路径已存在：${localPath}`"
          >
            <template #description>
              <Checkbox v-model:checked="localPathForce">强制在本路径创建</Checkbox>
            </template>
          </Alert>
        </div>
        <!-- 选择群组 -->
        <div class="form-row">
          <label class="form-label">GitLab 群组：</label>
          <Select
            v-model:value="selectedGroupId"
            show-search
            :filter-option="(input: string, option: any) => option.label?.toLowerCase().includes(input.toLowerCase())"
            placeholder="选择已有的 GitLab 群组"
            :loading="groupLoading"
            :options="gitlabGroups"
            style="flex: 1"
            allow-clear
            @change="handleGroupChange"
          />
        </div>
        <!-- 新建群组 -->
        <div class="form-row">
          <label class="form-label">新建群组：</label>
          <Input v-model:value="newGroupName" placeholder="输入新群组名称" style="flex: 1" @press-enter="handleCreateGroup" />
          <Button :loading="creatingGroup" :disabled="!newGroupName?.trim()" @click="handleCreateGroup">创建</Button>
        </div>
        <div v-if="gitlabGroupInfo.url" class="form-hint">
          当前群组: <a :href="gitlabGroupInfo.url" target="_blank">{{ gitlabGroupInfo.path }}</a>
        </div>
      </div>
      <!-- 项目描述 MdEditor 独占一行在最下面 -->
      <div style="margin-top: 16px;">
        <div style="margin-bottom: 8px; font-weight: 500;">项目描述 (Markdown)</div>
        <MdEditor v-model="editorContent" :theme="mdTheme" :preview="false" style="height: 350px;" />
      </div>
    </div>

    <!-- Step 2: 子项目管理 -->
    <div v-show="currentStep === 1">
      <Spin v-if="autoPopulatingSubProjects" tip="正在从 GitLab 群组加载子项目..." style="width: 100%; padding: 48px 0; display: flex; justify-content: center" />
      <template v-else>
      <div class="step-header">
        <span class="step-desc">管理项目下的子项目，每个子项目对应一个 GitLab 仓库</span>
        <Space>
          <Button v-if="gitlabGroupInfo.id" :loading="autoPopulatingSubProjects" @click="handleSyncFromGroup">从 GitLab 群组同步</Button>
          <Button type="primary" @click="addSubProject">添加子项目</Button>
        </Space>
      </div>

      <div v-if="subProjects.length === 0" class="empty-hint">
        暂无子项目，点击「添加子项目」开始添加
      </div>

      <div v-for="(item, index) in subProjects" :key="index" class="sub-project-card">
        <div style="display: flex; align-items: center; justify-content: space-between; margin-bottom: 12px">
          <Tag color="blue">子项目 {{ index + 1 }}</Tag>
          <Popconfirm title="确定删除该子项目？" @confirm="removeSubProject(index)">
            <Button danger size="small">删除</Button>
          </Popconfirm>
        </div>

        <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 12px; margin-bottom: 12px">
          <div>
            <div class="field-label">名称 *</div>
            <Input v-model:value="item.name" placeholder="子项目名称" @change="onSubProjectNameChange(index)" />
          </div>
          <div>
            <div class="field-label">编码</div>
            <Input v-model:value="item.code" placeholder="自动生成，可手动修改" @change="onSubProjectCodeChange(index)" />
          </div>
        </div>

        <div style="margin-bottom: 12px">
          <div class="field-label">基座仓库（搜索 GitLab 仓库作为基础版本二次开发）</div>
          <Select
            v-model:value="item.baseRepoUrl"
            show-search
            :filter-option="false"
            placeholder="点击展开或输入关键词搜索 GitLab 仓库"
            :options="baseRepoOptions"
            :loading="baseRepoLoading"
            allow-clear
            style="width: 100%"
            @search="handleBaseRepoSearch"
            @dropdownVisibleChange="handleBaseRepoDropdownOpen"
          />
        </div>

        <div>
          <div class="field-label">子项目仓库</div>
          <div style="display: flex; gap: 12px">
            <Select
              v-model:value="item.gitlabProjectUrl"
              show-search
              :filter-option="(input: string, option: any) => option.label?.toLowerCase().includes(input.toLowerCase())"
              placeholder="选择已有仓库或手动填写"
              :options="groupProjects"
              :loading="groupProjectsLoading"
              allow-clear
              style="flex: 1"
              @change="(val: any) => handleSelectExistingRepo(index, val)"
            />
            <Button
              type="primary"
              ghost
              :loading="creatingProject === index"
              :disabled="!item.name?.trim() || !gitlabGroupInfo.id"
              @click="handleCreateRepo(index)"
            >
              创建仓库
            </Button>
          </div>
        </div>

        <div v-if="item.gitlabProjectPath" class="field-hint" style="margin-top: 8px">
          GitLab 路径: {{ item.gitlabProjectPath }}
        </div>

        <div style="margin-top: 12px">
          <div class="field-label">本地路径</div>
          <Input
            v-model:value="item.localPath"
            placeholder="自动根据项目路径拼接，可手动修改"
            @input="item._localPathManual = true"
            @change="(e: any) => { if (!e.target?.value) item._localPathManual = false; }"
          />
        </div>
      </div>
      </template>
    </div>

    <!-- Step 3: 项目配置 -->
    <div v-show="currentStep === 2">
      <div class="step-header">
        <span class="step-desc">为项目添加特有的配置属性（如公钥、密钥等），也可跳过此步骤</span>
        <Button type="primary" @click="addConfigItem">添加配置</Button>
      </div>

      <div v-if="configItems.length === 0" class="empty-hint">
        暂无配置项，点击「添加配置」开始添加
      </div>

      <div v-for="(item, index) in configItems" :key="index" class="sub-project-card">
        <div style="display: grid; grid-template-columns: 150px 1fr 1fr 2fr auto auto; gap: 12px; align-items: center">
          <Select v-model:value="item.categoryId" placeholder="选择分类" :options="categoryOptions" allow-clear />
          <Input v-model:value="item.configKey" placeholder="配置键" />
          <Input v-model:value="item.configName" placeholder="配置名称" />
          <Input v-model:value="item.configValue" placeholder="配置值" />
          <Switch v-model:checked="item.isSensitive" checked-children="敏感" un-checked-children="普通" />
          <Button danger size="small" @click="removeConfigItem(index)">删除</Button>
        </div>
      </div>
    </div>

    <!-- Step 4: 完成 -->
    <div v-if="currentStep === 3">
      <Result :status="'success'" :title="isEdit ? '项目更新成功！' : '项目创建成功！'" sub-title="你可以前往项目详情查看，或返回列表">
        <template #extra>
          <Space>
            <Button type="primary" @click="goToDetail">查看项目详情</Button>
            <Button @click="goToList">返回列表</Button>
          </Space>
        </template>
      </Result>
    </div>

    <!-- 底部按钮 -->
    <div v-if="currentStep < 3" class="step-footer">
      <Space>
        <Button v-if="currentStep > 0" @click="handlePrev">上一步</Button>
        <Button type="primary" :loading="saving" @click="handleNext">
          {{ currentStep === 2 ? '完成' : '下一步' }}
        </Button>
      </Space>
    </div>
  </Card>
</template>

<style scoped>
.logo-section {
  display: flex;
  gap: 20px;
  align-items: center;
  max-width: 900px;
  padding: 16px;
  margin: 0 auto 20px;
  background: var(--ant-color-fill-quaternary);
  border: 1px solid var(--ant-color-border-secondary);
  border-radius: 8px;
}

.logo-preview {
  display: flex;
  flex-shrink: 0;
  align-items: center;
  justify-content: center;
  width: 88px;
  height: 88px;
  overflow: hidden;
  background: var(--ant-color-bg-container);
  border: 1px dashed var(--ant-color-border);
  border-radius: 8px;
}

.logo-img {
  width: 100%;
  height: 100%;
  object-fit: contain;
}

.logo-placeholder {
  font-size: 13px;
  font-weight: 600;
  letter-spacing: 1px;
  color: var(--ant-color-text-quaternary);
}

.logo-actions {
  flex: 1;
}

.logo-title {
  margin-bottom: 4px;
  font-weight: 500;
  color: var(--ant-color-text);
}

.logo-hint {
  margin-bottom: 10px;
  font-size: 12px;
  color: var(--ant-color-text-tertiary);
}

.form-row {
  display: flex;
  gap: 12px;
  align-items: center;
  margin-bottom: 12px;
}

.form-label {
  flex-shrink: 0;
  min-width: 90px;
  color: var(--ant-color-text);
  text-align: right;
}

.form-hint {
  margin: -4px 0 12px 102px;
  font-size: 12px;
  color: var(--ant-color-text-tertiary);
}

.step-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 16px;
}

.step-desc {
  color: var(--ant-color-text-secondary);
}

.empty-hint {
  padding: 48px 0;
  color: var(--ant-color-text-tertiary);
  text-align: center;
}

.sub-project-card {
  padding: 16px;
  margin-bottom: 12px;
  background: var(--ant-color-fill-quaternary);
  border: 1px solid var(--ant-color-border);
  border-radius: 8px;
}

.field-label {
  margin-bottom: 4px;
  font-size: 13px;
  color: var(--ant-color-text-secondary);
}

.field-hint {
  margin-top: 4px;
  font-size: 12px;
  color: var(--ant-color-text-tertiary);
}

.step-footer {
  padding-top: 16px;
  margin-top: 24px;
  text-align: right;
  border-top: 1px solid var(--ant-color-border-secondary);
}
</style>

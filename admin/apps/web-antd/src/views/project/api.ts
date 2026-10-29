import { api } from '#/api/request';

export async function getCheckPathExists(path: string) {
  return api.get('/Project/CheckPathExists', { params: { path } });
}

/**
 * 上传项目 Logo：后端处理成 512x512 PNG 存 storage，返回 { url }
 */
export async function postUploadProjectLogo(file: File): Promise<{ url: string }> {
  const formData = new FormData();
  formData.append('file', file);
  return api.post('/ImageProcessing/UploadProjectLogo', formData, { quiet: true });
}

export async function getList(params: any) {
  return api.get('/Project/List', { params });
}

export async function getListWithStats(params: any) {
  return api.get('/Project/ListWithStats', { params });
}

export async function getDetail(id: number) {
  return api.get('/Project/Detail', { params: { id } });
}

export async function getOverview(id: number) {
  return api.get('/Project/Overview', { params: { id } });
}

export async function postAdd(data: any) {
  return api.post('/Project/Add', data);
}

export async function postEdit(params: any) {
  if (params.id) {
    return api.post('/Project/Edit', params);
  }
  return postAdd(params);
}

export async function postDel(id: number) {
  return api.post('/Project/Del', { id });
}

// Environment
export async function getEnvironmentList(params: any) {
  return api.get('/ProjectEnvironment/List', { params });
}

export async function postEnvironmentAdd(data: any) {
  return api.post('/ProjectEnvironment/Add', data);
}

export async function postEnvironmentEdit(data: any) {
  return api.post('/ProjectEnvironment/Edit', data);
}

export async function postEnvironmentDel(id: number) {
  return api.post('/ProjectEnvironment/Del', { id });
}

// Template
export async function getTemplateList(params: any) {
  return api.get('/ProjectTemplate/List', { params });
}

export async function getTemplateDetail(id: number) {
  return api.get('/ProjectTemplate/Detail', { params: { id } });
}

export async function postTemplateAdd(data: any) {
  return api.post('/ProjectTemplate/Add', data);
}

export async function postTemplateEdit(params: any) {
  if (params.id) {
    return api.post('/ProjectTemplate/Edit', params);
  }
  return postTemplateAdd(params);
}

export async function postTemplateDel(id: number) {
  return api.post('/ProjectTemplate/Del', { id });
}

// Activity
export async function getActivityList(params: any) {
  return api.get('/ProjectActivity/List', { params });
}

// Config Category
export async function getConfigCategoryList() {
  return api.get('/ProjectConfigCategory/List', {
    params: { page: 1, pageSize: 100 },
  });
}

// Config
export async function getConfigList(params: any) {
  return api.get('/ProjectConfig/List', { params });
}

export async function postConfigSave(data: any) {
  return api.post('/ProjectConfig/Save', data, { json: true });
}

export async function postConfigInitFromTemplate(data: any) {
  return api.post('/ProjectConfig/InitFromTemplate', data);
}

export async function postConfigDecryptValue(id: number) {
  return api.get('/ProjectConfig/DecryptValue', { params: { id } });
}

// SubProject
export async function getSubProjectList(params: any) {
  return api.get('/SubProject/List', { params });
}

export async function postSubProjectSaveList(projectId: number, data: any) {
  return api.post(`/SubProject/SaveList?projectId=${projectId}`, { items: data });
}

export async function getSubProjectCommits(subProjectId: number, limit = 5) {
  return api.get('/SubProject/Commits', { params: { subProjectId, limit } });
}

export async function postSubProjectSyncCode(id: number) {
  return api.post('/SubProject/SyncCode', { id });
}

export async function getSubProjectGitStatus(id: number) {
  return api.get('/SubProject/GitStatus', { params: { id } });
}

export async function getSubProjectDetail(id: number) {
  return api.get('/SubProject/Detail', { params: { id } });
}

export async function postSubProjectGitCommit(id: number, commitMessage: string) {
  return api.post('/SubProject/GitCommit', { id, commitMessage });
}

export async function postSubProjectGitPush(id: number) {
  return api.post('/SubProject/GitPush', { id });
}

// ProjectServer (部署配置)
export async function getProjectServerList(params: any) {
  return api.get('/ProjectServer/List', { params });
}

export async function postProjectServerAdd(data: any) {
  return api.post('/ProjectServer/Add', data);
}

export async function postProjectServerEdit(data: any) {
  return api.post('/ProjectServer/Edit', data);
}

export async function postProjectServerDel(id: number) {
  return api.post('/ProjectServer/Del', { id });
}

export async function getServerSimpleList() {
  return api.get('/Server/SimpleList');
}

// GitLab
export async function getGitLabGroups(params?: any) {
  return api.get('/GitLab/Groups', { params });
}

export async function getGitLabGroupProjects(groupId: number, params?: any) {
  return api.get('/GitLab/GroupProjects', { params: { groupId, ...params } });
}

export async function postGitLabCreateGroup(data: any) {
  return api.post('/GitLab/CreateGroup', data, { json: true });
}

export async function postGitLabCreateProject(data: any) {
  return api.post('/GitLab/CreateProject', data, { json: true });
}

export async function getGitLabProjects(params?: any) {
  return api.get('/GitLab/Projects', { params });
}

// 子项目一键部署
export async function getSchemesBySubProject(subProjectId: number) {
  return api.get('/PipelineProfile/BySubProject', { params: { subProjectId } });
}

export async function postOneClickDeploy(data: {
  branch?: string;
  profileId: number;
  version?: string;
}) {
  return api.post('/PipelineProfile/OneClickDeploy', data, { json: true });
}

export async function getDeployStatus(params: {
  buildId?: number;
  deployId?: number;
}) {
  return api.get('/PipelineProfile/Status', { params });
}


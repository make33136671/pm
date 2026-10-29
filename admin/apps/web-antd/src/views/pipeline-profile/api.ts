import { api } from '#/api/request';

export async function getList(params: any) {
  return api.get('/PipelineProfile/List', { params });
}

export async function getDetail(id: number) {
  return api.get('/PipelineProfile/Detail', { params: { id } });
}

export async function postAdd(data: any) {
  return api.post('/PipelineProfile/Add', data, { json: true });
}

export async function postEdit(data: any) {
  return api.post('/PipelineProfile/Edit', data, { json: true });
}

export async function postDel(id: number) {
  return api.post('/PipelineProfile/Del', { id }, { json: true });
}

export async function postTriggerBuild(data: any) {
  return api.post('/PipelineProfile/TriggerBuild', data, { json: true });
}

// 一键运行(LocalBuild 纯构建 / SbpmDeploy 直发都走这里)
export async function postOneClickDeploy(data: {
  branch?: string;
  profileId: number;
  version?: string;
}) {
  return api.post('/PipelineProfile/OneClickDeploy', data, { json: true });
}

export async function postApproveBuild(data: any) {
  return api.post('/PipelineProfile/ApproveBuild', data, { json: true });
}

export async function postRejectBuild(data: any) {
  return api.post('/PipelineProfile/RejectBuild', data, { json: true });
}

// 部署
export async function postTriggerDeploy(data: any) {
  return api.post('/PipelineProfile/TriggerDeploy', data, { json: true });
}

export async function postApproveDeploy(data: any) {
  return api.post('/PipelineProfile/ApproveDeploy', data, { json: true });
}

export async function postRejectDeploy(data: any) {
  return api.post('/PipelineProfile/RejectDeploy', data, { json: true });
}

// 回滚
export async function postTriggerRollback(data: any) {
  return api.post('/PipelineProfile/TriggerRollback', data, { json: true });
}

// 查询
export async function getVersions(profileId: number, limit = 10) {
  return api.get('/PipelineProfile/Versions', { params: { profileId, limit } });
}

export async function getStatus(params: any) {
  return api.get('/PipelineProfile/Status', { params });
}

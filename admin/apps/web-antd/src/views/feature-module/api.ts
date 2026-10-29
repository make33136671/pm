import { api } from '#/api/request';

// 功能模块树
export async function getTree(projectId: number) {
  return api.get('/FeatureModule/Tree', { params: { projectId } });
}

export async function getList(projectId: number) {
  return api.get('/FeatureModule/List', { params: { projectId } });
}

export async function getDetail(id: number) {
  return api.get('/FeatureModule/Detail', { params: { id } });
}

export async function postAdd(data: any) {
  return api.post('/FeatureModule/Add', data);
}

export async function postEdit(data: any) {
  return api.post('/FeatureModule/Edit', data);
}

export async function postDel(id: number) {
  return api.post('/FeatureModule/Del', { id });
}

// 需求关联
export async function saveRelations(requirementId: number, featureModuleIds: number[]) {
  return api.post(`/FeatureModule/SaveRelations?requirementId=${requirementId}`, featureModuleIds, { json: true });
}

export async function getRelations(requirementId: number) {
  return api.get('/FeatureModule/GetRelations', { params: { requirementId } });
}

// AI 生成需求（非流式）
export async function postGenerateRequirements(data: any) {
  return api.post('/FeatureModule/GenerateRequirements', data, { json: true });
}

// 需求确认
export async function getPendingConfirm(batchId: string) {
  return api.get('/Requirement/PendingConfirm', { params: { batchId } });
}

export async function postConfirmBatch(batchId: string) {
  return api.post('/Requirement/ConfirmBatch', { batchId });
}

export async function postDiscardBatch(batchId: string) {
  return api.post('/Requirement/DiscardBatch', { batchId });
}

export async function postConfirmSingle(id: number) {
  return api.post('/Requirement/ConfirmSingle', { id });
}

// 导图同步
export async function postSyncMap(projectId: number) {
  return api.post('/FeatureModule/SyncMap', { projectId });
}

export async function postSaveFromMap(projectId: number, mapData: string) {
  return api.post(`/FeatureModule/SaveFromMap?projectId=${projectId}`, JSON.stringify(mapData), { json: true });
}

import { api } from '#/api/request';

// CRUD
export async function getList(params: any) {
  return api.get('/ApiEndpoint/List', { params });
}

export async function getDetail(id: number) {
  return api.get('/ApiEndpoint/Detail', { params: { id } });
}

export async function postAdd(data: any) {
  return api.post('/ApiEndpoint/Add', data);
}

export async function postEdit(data: any) {
  if (data.id) {
    return api.post('/ApiEndpoint/Edit', data);
  }
  return postAdd(data);
}

export async function postDel(id: number) {
  return api.post('/ApiEndpoint/Del', { id });
}

// 功能模块关联（多对多）
export async function saveRelations(apiEndpointId: number, featureModuleIds: number[]) {
  return api.post(`/ApiEndpoint/SaveRelations?apiEndpointId=${apiEndpointId}`, featureModuleIds, { json: true });
}

export async function getRelations(apiEndpointId: number) {
  return api.get('/ApiEndpoint/GetRelations', { params: { apiEndpointId } });
}

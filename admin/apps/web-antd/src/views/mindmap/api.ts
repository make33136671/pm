import { api } from '#/api/request';

export async function getList(params: any) {
  return api.get('/MindMap/List', { params });
}

export async function getDetail(id: number) {
  return api.get('/MindMap/Detail', { params: { id } });
}

export async function postAdd(data: any) {
  return api.post('/MindMap/Add', data);
}

export async function postEdit(data: any) {
  if (data.id) {
    return api.post('/MindMap/Edit', data);
  }
  return postAdd(data);
}

export async function postDel(id: number) {
  return api.post('/MindMap/Del', { id });
}

export async function postGenerateRequirementMap(projectId: number) {
  return api.post('/MindMap/GenerateRequirementMap', { projectId });
}

export async function getMain(projectId: number) {
  return api.get('/MindMap/GetMain', { params: { projectId } });
}

export async function postSyncMain(projectId: number) {
  return api.post('/MindMap/SyncMain', { projectId });
}

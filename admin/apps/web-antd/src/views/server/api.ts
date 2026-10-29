import { api } from '#/api/request';

// Server CRUD
export async function getList(params: any) {
  return api.get('/Server/List', { params });
}

export async function getDetail(id: number) {
  return api.get('/Server/Detail', { params: { id } });
}

export async function getSimpleList() {
  return api.get('/Server/SimpleList');
}

export async function postAdd(data: any) {
  return api.post('/Server/Add', data);
}

export async function postEdit(data: any) {
  return api.post('/Server/Edit', data);
}

export async function postDel(id: number) {
  return api.post('/Server/Del', { id });
}

export async function decryptField(serverId: number, fieldName: string) {
  return api.get('/Server/DecryptField', { params: { serverId, fieldName } });
}

export async function postDiscover(id: number) {
  return api.post('/Server/Discover', { id });
}

export async function postTestConnection(id: number) {
  return api.post('/Server/TestConnection', { id });
}

// ProjectServer CRUD
export async function getProjectServerList(params: any) {
  return api.get('/ProjectServer/List', { params });
}

export async function getProjectServerDetail(id: number) {
  return api.get('/ProjectServer/Detail', { params: { id } });
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

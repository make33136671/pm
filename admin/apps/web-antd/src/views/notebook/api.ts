import { api } from '#/api/request';

export async function getList(params: any) {
  return api.get('/Notebook/List', { params });
}

export async function postAdd(data: any) {
  return api.post('/Notebook/Add', data);
}

export async function postEdit(params: any) {
  if (params.id) {
    return api.post('/Notebook/Edit', params);
  }
  return postAdd(params);
}

export async function postDel(id: number) {
  return api.post('/Notebook/Del', { id });
}

export async function postPin(id: number) {
  return api.post('/Notebook/Pin', { id });
}

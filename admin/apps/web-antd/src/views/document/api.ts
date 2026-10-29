import { api } from '#/api/request';

export async function getList(params: any) {
  return api.get('/ProjectDocument/List', { params });
}

export async function getDetail(id: number) {
  return api.get('/ProjectDocument/Detail', { params: { id } });
}

export async function postAdd(data: any) {
  return api.post('/ProjectDocument/Add', data);
}

export async function postEdit(data: any) {
  if (data.id) {
    return api.post('/ProjectDocument/Edit', data);
  }
  return postAdd(data);
}

export async function postDel(id: number) {
  return api.post('/ProjectDocument/Del', { id });
}

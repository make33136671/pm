import { api } from '#/api/request';

export async function getList(params: any) {
  return api.get('/Changelog/List', { params });
}

export async function getDetail(id: number) {
  return api.get('/Changelog/Detail', { params: { id } });
}

export async function postAdd(data: any) {
  return api.post('/Changelog/Add', data);
}

export async function postEdit(data: any) {
  if (data.id) {
    return api.post('/Changelog/Edit', data);
  }
  return postAdd(data);
}

export async function postDel(id: number) {
  return api.post('/Changelog/Del', { id });
}

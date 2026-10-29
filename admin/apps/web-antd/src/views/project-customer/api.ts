import { api } from '#/api/request';

export async function getList(params: any) {
  return api.get('/ProjectCustomer/List', { params });
}

export async function getDetail(id: number) {
  return api.get('/ProjectCustomer/Detail', { params: { id } });
}

export async function postAdd(data: any) {
  return api.post('/ProjectCustomer/Add', data);
}

export async function postEdit(params: any) {
  if (params.id) {
    return api.post('/ProjectCustomer/Edit', params);
  }
  return postAdd(params);
}

export async function postDel(id: number) {
  return api.post('/ProjectCustomer/Del', { id });
}

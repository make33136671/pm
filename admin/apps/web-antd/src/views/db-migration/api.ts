import { api } from '#/api/request';

export async function getList(params: any) {
  return api.get('/DbMigration/List', { params });
}

export async function getDetail(id: number) {
  return api.get('/DbMigration/Detail', { params: { id } });
}

export async function postAdd(data: any) {
  return api.post('/DbMigration/Add', data);
}

export async function postEdit(data: any) {
  if (data.id) {
    return api.post('/DbMigration/Edit', data);
  }
  return postAdd(data);
}

export async function postDel(id: number) {
  return api.post('/DbMigration/Del', { id });
}

export async function postExecute(id: number) {
  return api.post('/DbMigration/Execute', { id });
}

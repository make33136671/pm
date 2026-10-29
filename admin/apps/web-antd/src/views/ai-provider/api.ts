import { api } from '#/api/request';

export async function getList(params: any) {
  return api.get('/AiProvider/List', { params });
}

export async function getDetail(id: number) {
  return api.get('/AiProvider/Detail', { params: { id } });
}

export async function postAdd(data: any) {
  return api.post('/AiProvider/Add', data);
}

export async function postEdit(params: any) {
  if (params.id) {
    return api.post('/AiProvider/Edit', params);
  }
  return postAdd(params);
}

export async function postDel(id: number) {
  return api.post('/AiProvider/Del', { id });
}

// 获取已注册的适配器类型
export async function getAdapterTypes() {
  return api.get('/AiProvider/AdapterTypes');
}

// 获取可用的 AI 提供商（Active + 有适配器）
export async function getAvailable() {
  return api.get('/AiProvider/Available');
}

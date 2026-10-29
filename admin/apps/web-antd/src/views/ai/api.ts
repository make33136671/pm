import { api } from '#/api/request';

// AI Memory
export async function getMemoryList(params: any) {
  return api.get('/AiMemory/List', { params });
}

export async function getMemoryDetail(id: number) {
  return api.get('/AiMemory/Detail', { params: { id } });
}

export async function postMemoryAdd(data: any) {
  return api.post('/AiMemory/Add', data);
}

export async function postMemoryEdit(data: any) {
  if (data.id) {
    return api.post('/AiMemory/Edit', data);
  }
  return postMemoryAdd(data);
}

export async function postMemoryDel(id: number) {
  return api.post('/AiMemory/Del', { id });
}

export async function getMemorySearch(params: any) {
  return api.get('/AiMemory/Search', { params });
}

// AI Task
export async function getTaskList(params: any) {
  return api.get('/AiTask/List', { params });
}

export async function getTaskDetail(id: number) {
  return api.get('/AiTask/Detail', { params: { id } });
}

export async function postTaskCreate(data: any) {
  return api.post('/AiTask/Create', data);
}

export async function postTaskCancel(id: number) {
  return api.post('/AiTask/Cancel', { id });
}

// AI Service（聊天/分析/编码/设计/Review）
export async function postAiExecute(data: any) {
  return api.post('/Ai/Execute', data, { json: true });
}

export async function postAiChat(data: any) {
  return api.post('/Ai/Chat', data, { json: true });
}

export async function postAiAnalyze(data: any) {
  return api.post('/Ai/AnalyzeCode', data, { json: true });
}

export async function postAiGenerate(data: any) {
  return api.post('/Ai/GenerateCode', data, { json: true });
}

export async function postAiDesign(data: any) {
  return api.post('/Ai/DesignFeature', data, { json: true });
}

export async function postAiReview(data: any) {
  return api.post('/Ai/ReviewCode', data, { json: true });
}

export async function getAiTaskHistory(params: any) {
  return api.get('/Ai/TaskHistory', { params });
}

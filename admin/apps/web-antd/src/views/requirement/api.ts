import { api } from '#/api/request';

export async function getList(params: any) {
  return api.get('/Requirement/List', { params });
}

export async function getDetail(id: number) {
  return api.get('/Requirement/Detail', { params: { id } });
}

export async function postAdd(data: any) {
  return api.post('/Requirement/Add', data);
}

export async function postEdit(params: any) {
  if (params.id) {
    return api.post('/Requirement/Edit', params);
  }
  return postAdd(params);
}

export async function postDel(id: number) {
  return api.post('/Requirement/Del', { id });
}

export async function postBatchUpdateStatus(data: any) {
  return api.post('/Requirement/BatchUpdateStatus', data);
}

export async function getKanban(params: any) {
  return api.get('/Requirement/Kanban', { params });
}

// AI 分析拆分
export async function postAnalyzeAndSplit(projectId: number, content: string) {
  return api.post('/Requirement/AnalyzeAndSplit', { projectId, content });
}

export async function getStagingList(batchId: string) {
  return api.get('/Requirement/StagingList', { params: { batchId } });
}

export async function postEditStaging(data: any) {
  return api.post('/Requirement/EditStaging', data);
}

export async function postConfirmSingle(id: number) {
  return api.post('/Requirement/ConfirmSingle', { id });
}

export async function postConfirmAll(batchId: string) {
  return api.post('/Requirement/ConfirmAll', { batchId });
}

export async function postRejectSingle(id: number) {
  return api.post('/Requirement/RejectSingle', { id });
}

export async function postRestoreSingle(id: number) {
  return api.post('/Requirement/RestoreSingle', { id });
}

// AI Analysis Batches
export async function getAnalysisBatchList(params: any) {
  return api.get('/Requirement/AnalysisBatchList', { params });
}

export async function postAddToBatch(data: any) {
  return api.post('/Requirement/AddToBatch', data);
}

export async function postDiscardBatch(batchId: string) {
  return api.post('/Requirement/DiscardBatch', { batchId });
}

// AI 任务拆解(#9)
export async function postAiSplitTasks(requirementId: number) {
  return api.post('/Requirement/AiSplitTasks', { requirementId });
}
export async function postAiConfirmTasks(batchId: string) {
  return api.post('/Requirement/AiConfirmTasks', { batchId });
}

// AI 搜索意图解析(#18)
export async function getAiParseSearchIntent(query: string, scope = 'requirement') {
  return api.get('/Ai/ParseSearchIntent', { params: { query, scope } });
}

// Comments
export async function getCommentList(params: any) {
  return api.get('/RequirementComment/List', { params });
}

export async function postCommentAdd(data: any) {
  return api.post('/RequirementComment/Add', data);
}

export async function postCommentDel(id: number) {
  return api.post('/RequirementComment/Del', { id });
}

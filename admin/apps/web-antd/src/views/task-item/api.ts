import { api } from '#/api/request';

// CRUD
export async function getList(params: any) {
  return api.get('/TaskItem/List', { params });
}

export async function getDetail(id: number) {
  return api.get('/TaskItem/Detail', { params: { id } });
}

export async function postAdd(data: any) {
  return api.post('/TaskItem/Add', data);
}

export async function postEdit(params: any) {
  if (params.id) {
    return api.post('/TaskItem/Edit', params);
  }
  return postAdd(params);
}

export async function postDel(id: number) {
  return api.post('/TaskItem/Del', { id });
}

// 看板
export async function getKanban(params: any) {
  return api.get('/TaskItem/Kanban', { params });
}

// 恢复 / 取消
export async function postResume(id: number) {
  return api.post('/TaskItem/Resume', { id });
}

export async function postCancel(id: number) {
  return api.post('/TaskItem/Cancel', { id });
}

// 日志
export async function getLogs(params: any) {
  return api.get('/TaskItem/Logs', { params });
}

// 从需求生成
export async function postGenerateFromRequirements(data: any) {
  return api.post('/TaskItem/GenerateFromRequirements', data, { json: true });
}

export async function postConfirmCreate(data: any) {
  return api.post('/TaskItem/ConfirmCreate', data, { json: true });
}

// AI 执行（非流式）
export async function postExecByAi(data: any) {
  return api.post('/TaskItem/ExecByAi', data);
}

// 验收流程 (#102 #107)
export async function postReviewApprove(data: any) {
  return api.post('/TaskItem/ReviewApprove', data);
}

export async function postReviewReject(data: any) {
  return api.post('/TaskItem/ReviewReject', data);
}

export async function postAutoVerify(taskItemId: number) {
  return api.post('/TaskItem/AutoVerify', { taskItemId });
}

// 附件 (#103)
export async function getAttachments(taskItemId: number, stage?: string) {
  return api.get('/TaskItem/GetAttachments', { params: { taskItemId, stage } });
}

// AI Code Review (#128-131)
export async function postAiCodeReview(data: { gitlabProjectId: number; mrIid: number }) {
  return api.post('/AiCodeReview/ReviewMr', data);
}

// AI 代码生成器 (#133-138)
export async function postAiCodeGenerate(data: { taskItemId: number; gitlabProjectId: number; targetBranch?: string }) {
  return api.post('/AiCodeGenerator/Generate', data);
}

// GitLab MR 列表 (#132)
export async function getMergeRequests(params: { projectId: number; state?: string; page?: number }) {
  return api.get('/GitLab/MergeRequests', { params });
}

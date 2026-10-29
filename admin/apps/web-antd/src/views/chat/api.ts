import { api } from '#/api/request';

// ==================== 账号管理 ====================
export const getAccountList = (params: any) => api.get('/ChatAccount/List', { params });
export const getAccountDetail = (id: number) => api.get('/ChatAccount/Detail', { params: { id } });
export const postAccountAdd = (data: any) => api.post('/ChatAccount/Add', data);
export const postAccountEdit = (data: any) => data.id ? api.post('/ChatAccount/Edit', data) : postAccountAdd(data);
export const postAccountDel = (id: number) => api.post('/ChatAccount/Del', { id });
export const postAccountConnect = (id: number) => api.post('/ChatAccount/Connect', { id });
export const postAccountDisconnect = (id: number) => api.post('/ChatAccount/Disconnect', { id });
export const postSetWebhook = (id: number, webhookUrl: string) => api.post('/ChatAccount/SetWebhook', { id, webhookUrl });
export const postDeleteWebhook = (id: number) => api.post('/ChatAccount/DeleteWebhook', { id });
export const getConnectionStatus = (id: number) => api.get('/ChatAccount/ConnectionStatus', { params: { id } });

// ==================== 群组绑定 ====================
export const getGroupList = (params: any) => api.get('/ChatGroup/List', { params });
export const getGroupDetail = (id: number) => api.get('/ChatGroup/Detail', { params: { id } });
export const postGroupAdd = (data: any) => api.post('/ChatGroup/Add', data);
export const postGroupEdit = (data: any) => api.post('/ChatGroup/Edit', data);
export const postGroupDel = (id: number) => api.post('/ChatGroup/Del', { id });
export const postSendNotification = (projectId: number, title: string, content: string) =>
  api.post('/ChatGroup/SendNotification', { projectId, title, content });

// ==================== 用户绑定 ====================
export const getUserList = (params: any) => api.get('/ChatUser/List', { params });
export const getUserDetail = (id: number) => api.get('/ChatUser/Detail', { params: { id } });
export const postGenerateVerifyCode = (adminId: number, projectId: number, platformType: string = 'Telegram') =>
  api.post('/ChatUser/GenerateVerifyCode', { adminId, projectId, platformType });
export const postUserDel = (id: number) => api.post('/ChatUser/Del', { id });

// ==================== 消息记录 ====================
export const getMessageList = (params: any) => api.get('/ChatMessage/List', { params });
export const getMessageDetail = (id: number) => api.get('/ChatMessage/Detail', { params: { id } });
export const postMessageSend = (groupBindingId: number, content: string) =>
  api.post('/ChatMessage/Send', { groupBindingId, content });
export const postMessageExportMd = (messageIds: string) =>
  api.postBlob('/ChatMessage/ExportMd', { messageIds });
export const postMessageAnalyzeSelected = (messageIds: string) =>
  api.post('/ChatMessage/AnalyzeSelected', { messageIds });

// ==================== 需求管理 ====================
export const getRequirementList = (params: any) => api.get('/ChatRequirement/List', { params });
export const getRequirementDetail = (id: number) => api.get('/ChatRequirement/Detail', { params: { id } });
export const postRequirementConfirm = (id: number, confirmedBy: number) =>
  api.post('/ChatRequirement/Confirm', { id, confirmedBy });
export const postRequirementReject = (id: number) => api.post('/ChatRequirement/Reject', { id });
export const postRequirementDel = (id: number) => api.post('/ChatRequirement/Del', { id });
export const postRequirementAnalyze = (groupBindingId: number) =>
  api.post('/ChatRequirement/Analyze', { groupBindingId });
export const postRequirementSendToGroup = (groupBindingId: number, requirementIds?: string) =>
  api.post('/ChatRequirement/SendToGroup', { groupBindingId, requirementIds });

// ==================== 分析批次 ====================
export const getBatchList = (params: any) => api.get('/ChatAnalysisBatch/List', { params });
export const getBatchDetail = (id: number) => api.get('/ChatAnalysisBatch/Detail', { params: { id } });
export const postBatchCreate = (projectId: number, groupBindingId: number | null, title: string) =>
  api.post('/ChatAnalysisBatch/Create', { projectId, groupBindingId, title });
export const postBatchImport = (id: number, messageIds: string) =>
  api.post('/ChatAnalysisBatch/ImportMessages', { id, messageIds });
export const postBatchAnalyze = (id: number) =>
  api.post('/ChatAnalysisBatch/Analyze', { id });
export const postBatchExportMd = (id: number) =>
  api.postBlob('/ChatAnalysisBatch/ExportMd', { id });
export const postBatchImportMd = (id: number, file: File) => {
  const form = new FormData();
  form.append('id', id.toString());
  form.append('file', file);
  return api.post('/ChatAnalysisBatch/ImportMd', form);
};
export const postBatchDel = (id: number) =>
  api.post('/ChatAnalysisBatch/Del', { id });

// ==================== AI 管线 ====================
export const getPipelineByGroup = (groupBindingId: number) =>
  api.get('/AiPipeline/GetByGroup', { params: { groupBindingId } });
export const postPipelineToggleEnabled = (pipelineId: number, enabled: boolean) =>
  api.post('/AiPipeline/ToggleEnabled', { pipelineId, enabled });
export const postPipelineToggleRealtime = (pipelineId: number, realtimeEnabled: boolean) =>
  api.post('/AiPipeline/ToggleRealtime', { pipelineId, realtimeEnabled });
export const postPipelineAddStep = (data: any) => api.post('/AiPipeline/AddStep', data);
export const postPipelineEditStep = (data: any) => api.post('/AiPipeline/EditStep', data);
export const postPipelineDeleteStep = (stepId: number) => api.post('/AiPipeline/DeleteStep', { stepId });

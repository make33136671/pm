import { api } from '#/api/request';

export async function getInfo(options?: { silent?: boolean }) {
  return api.get('/App/Info', options);
}

export async function postRestart() {
  return api.post('/App/Restart', {}, { silent: true });
}

/**
 * 统一上传：上传 api 或前端项目的 zip 包，返回 jobId。
 * 上传后需调用 postDeploy(jobId) 执行部署。
 */
export async function postUpload(target: string, file: File) {
  const formData = new FormData();
  formData.append('target', target);
  formData.append('file', file);
  return api.post('/App/Upload', formData, { silent: true });
}

export async function getDeployTargets() {
  return api.get('/App/DeployTargets');
}

export async function getPreviewDeploy(
  target: string,
  filePath: string,
  fileName?: string,
) {
  return api.get('/App/PreviewDeploy', {
    params: { target, filePath, fileName },
  });
}

export async function postCancelUpload(filePath: string) {
  const formData = new FormData();
  formData.append('filePath', filePath);
  return api.post('/App/CancelUpload', formData, { silent: true });
}

/**
 * 按路径部署 API：文件已通过分片上传到服务器（filePath 为服务器相对路径），
 * 直接部署，无需重新上传文件。
 */
export async function postDeployApiByPath(
  filePath: string,
  fileName: string,
  fileSize: number,
  clearCache = false,
  keepConfig = true,
  keepLock = true,
) {
  const formData = new FormData();
  formData.append('filePath', filePath);
  formData.append('fileName', fileName);
  formData.append('fileSize', String(fileSize));
  formData.append('clearCache', String(clearCache));
  formData.append('keepConfig', String(keepConfig));
  formData.append('keepLock', String(keepLock));
  return api.post('/App/DeployApiByPath', formData, { silent: true });
}

/** 按路径部署前端项目（admin/h5）：文件已分片上传，直接部署。 */
export async function postDeployProjectByPath(
  key: string,
  filePath: string,
  fileName: string,
  fileSize: number,
) {
  const formData = new FormData();
  formData.append('key', key);
  formData.append('filePath', filePath);
  formData.append('fileName', fileName);
  formData.append('fileSize', String(fileSize));
  return api.post('/App/DeployProjectByPath', formData);
}

export async function getDeployRecords(params: { target?: string; page?: number; pageSize?: number }) {
  return api.get('/App/DeployRecords', { params });
}

export async function postRollback(id: number) {
  const formData = new FormData();
  formData.append('id', String(id));
  return api.post('/App/Rollback', formData);
}

export async function postDeploy(
  uploadId: number,
  keepConfig = true,
  clearCache = false,
  keepLock = true,
) {
  const formData = new FormData();
  formData.append('uploadId', String(uploadId));
  formData.append('keepConfig', String(keepConfig));
  formData.append('clearCache', String(clearCache));
  formData.append('keepLock', String(keepLock));
  return api.post('/App/Deploy', formData, { silent: true });
}

export async function getUploadRecords(params: {
  target?: string;
  status?: string;
  page?: number;
  pageSize?: number;
}) {
  return api.get('/App/UploadRecords', { params });
}

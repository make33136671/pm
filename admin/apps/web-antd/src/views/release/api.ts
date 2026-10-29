import { api } from '#/api/request';

export async function getList(params: any) {
  return api.get('/Release/List', { params });
}

export async function getDetail(id: number) {
  return api.get('/Release/Detail', { params: { id } });
}

export async function postCreate(data: any) {
  return api.post('/Release/Create', data);
}

export async function postApprove(id: number, comment?: string) {
  return api.post('/Release/Approve', { id, comment });
}

export async function postReject(id: number, comment?: string) {
  return api.post('/Release/Reject', { id, comment });
}

export async function postDeploy(id: number) {
  return api.post('/Release/Deploy', { id });
}

export async function postRollback(id: number) {
  return api.post('/Release/Rollback', { releaseId: id });
}

// Backup
export async function getBackupList(params: any) {
  return api.get('/ReleaseBackup/List', { params });
}

export async function postBackupDel(id: number) {
  return api.post('/ReleaseBackup/Del', { id });
}

import { api } from '#/api/request';

export async function getDocVersions(sourceType: string, sourceId: number) {
  return api.get('/DocVersion/Versions', { params: { sourceType, sourceId } });
}

export async function getDocLatest(sourceType: string, sourceId: number) {
  return api.get('/DocVersion/Latest', { params: { sourceType, sourceId } });
}

export async function getDocDetail(id: number) {
  return api.get('/DocVersion/Detail', { params: { id } });
}

export async function postDocRollback(data: any) {
  return api.post('/DocVersion/Rollback', data);
}

export function getDocDownloadUrl(sourceType: string, sourceId: number) {
  const baseUrl = (import.meta as any).env.VITE_GLOB_API_URL || '';
  return `${baseUrl}/admin/DocVersion/Download?sourceType=${sourceType}&sourceId=${sourceId}`;
}

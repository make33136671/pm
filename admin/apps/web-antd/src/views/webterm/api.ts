import { useAppConfig } from '@vben/hooks';
import { useAccessStore } from '@vben/stores';

import { api } from '#/api/request';

const { apiURL } = useAppConfig(import.meta.env, import.meta.env.PROD);

export async function getConfig() {
  return api.get('/CliConfig/Get');
}

export async function saveConfig(data: any) {
  return api.post('/CliConfig/Edit', data);
}

export async function getSessionList(params: any) {
  return api.get('/CliSession/List', { params });
}

export async function getSessionDetail(id: number) {
  return api.get('/CliSession/Detail', { params: { id } });
}

export async function createSession(data: {
  name?: string;
  projectId?: number;
  subProjectId?: number;
  cols?: number;
  rows?: number;
  cwd?: string;
}) {
  return api.post('/CliSession/Create', data);
}

export async function getProjectTree() {
  return api.get('/Project/Tree');
}

export async function destroySession(id: number) {
  return api.post('/CliSession/Destroy', { id });
}

export async function clearSessionLog(id: number) {
  return api.post('/CliSession/ClearLog', { id });
}

export async function deleteSession(id: number) {
  return api.post('/CliSession/Del', { id });
}

/**
 * 下载会话日志文件。带 JWT 鉴权走 blob —— 不能用 <a href>:
 * 相对路径会被前端 SPA 路由吞掉跳到兜底页,且 <a> 跳转不带 token 必 401。
 */
export async function downloadSessionLog(id: number) {
  const accessStore = useAccessStore();
  const res = await fetch(`${apiURL}/CliSession/DownloadLog?id=${id}`, {
    headers: { Authorization: `Bearer ${accessStore.accessToken}` },
  });
  if (!res.ok) throw new Error('下载失败');
  const blob = await res.blob();
  const a = document.createElement('a');
  a.href = URL.createObjectURL(blob);
  a.download = `webterm-${id}.log`;
  a.click();
  URL.revokeObjectURL(a.href);
}

/**
 * 拉取会话日志原始字节,用于已结束会话的只读终端回放。
 * 文件不存在(404)或为空时返回 null。
 */
export async function fetchSessionLog(id: number): Promise<ArrayBuffer | null> {
  const accessStore = useAccessStore();
  const res = await fetch(`${apiURL}/CliSession/DownloadLog?id=${id}`, {
    headers: { Authorization: `Bearer ${accessStore.accessToken}` },
  });
  if (!res.ok) return null;
  const buf = await res.arrayBuffer();
  return buf.byteLength > 0 ? buf : null;
}

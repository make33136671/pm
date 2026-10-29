import { useAppConfig } from '@vben/hooks';
import { useAccessStore } from '@vben/stores';

import { api } from '#/api/request';

const { apiURL } = useAppConfig(import.meta.env, import.meta.env.PROD);

/** 目录条目 */
export interface FileEntry {
  name: string;
  path: string;
  isDirectory: boolean;
  isSymlink: boolean;
  size: number;
  lastModified: string;
  lastAccessed: string;
  mode: string;
  owner: string;
  group: string;
  editable: boolean;
  extension: string;
}

export interface Breadcrumb {
  name: string;
  path: string;
}

export interface DirectoryListing {
  path: string;
  parentPath: string;
  breadcrumbs: Breadcrumb[];
  directories: FileEntry[];
  files: FileEntry[];
  truncated: boolean;
  total: number;
  self: FileEntry | null;
}

export interface FileContent {
  path: string;
  name: string;
  content: string;
  size: number;
  isBinary: boolean;
  tooLarge: boolean;
  editable: boolean;
  lineEnding: 'CRLF' | 'LF' | 'MIXED' | 'NONE';
  lineCount: number;
  lastModified: string;
  mode: string;
  hash: string;
}

export interface DiskUsage {
  path: string;
  totalBytes: number;
  freeBytes: number;
  usedBytes: number;
  usedPercent: number;
}

/** 列举目录 */
export async function listDirectory(path: string, showHidden = false) {
  return api.get('/FileManager/List', {
    params: { path, showHidden },
  }) as Promise<DirectoryListing>;
}

/** 只列子目录（目录树用） */
export async function listDirectories(path: string) {
  return api.get('/FileManager/Directories', {
    params: { path },
  }) as Promise<FileEntry[]>;
}

/** 可浏览的根目录 */
export async function getRoots() {
  return api.get('/FileManager/Roots') as Promise<Breadcrumb[]>;
}

/** 磁盘用量 */
export async function getDiskUsage(path: string) {
  return api.get('/FileManager/DiskUsage', { params: { path } }) as Promise<DiskUsage>;
}

/** 读取文本内容 */
export async function readFile(path: string) {
  return api.get('/FileManager/Read', { params: { path } }) as Promise<FileContent>;
}

/** 保存文本内容 */
export async function saveFile(payload: {
  path: string;
  content: string;
  expectedHash?: string;
  createBackup?: boolean;
}) {
  return api.post('/FileManager/Save', payload, { quiet: true });
}

/** 新建文件或目录 */
export async function createEntry(path: string, isDirectory: boolean) {
  return api.post('/FileManager/Create', { path, isDirectory });
}

/** 重命名 */
export async function renameEntry(path: string, newName: string) {
  return api.post('/FileManager/Rename', { path, newName });
}

/** 删除 */
export async function deleteEntry(path: string, recursive = false) {
  return api.post('/FileManager/Delete', { path, recursive });
}

/** 修改权限 */
export async function chmodEntry(path: string, mode: string) {
  return api.post('/FileManager/Chmod', { path, mode });
}

/** 上传文件到目录 */
export async function uploadFile(dirPath: string, file: File, overwrite = false) {
  const formData = new FormData();
  formData.append('dirPath', dirPath);
  formData.append('file', file);
  formData.append('overwrite', String(overwrite));
  return api.post('/FileManager/Upload', formData, { quiet: true });
}

/**
 * 下载文件。
 *
 * 走 fetch 而不是 <a href>，因为下载接口需要 Authorization 头；
 * 拿到 blob 后用临时 a 标签触发保存。
 */
export async function downloadFile(path: string) {
  const accessStore = useAccessStore();
  const res = await fetch(
    `${apiURL}/FileManager/Download?path=${encodeURIComponent(path)}`,
    { headers: { Authorization: `Bearer ${accessStore.accessToken}` } },
  );
  if (!res.ok) throw new Error('下载失败');
  const blob = await res.blob();
  const disposition = res.headers.get('content-disposition') || '';
  // 优先取 RFC 5987 的 filename*=，其次普通 filename=
  const utf8Match = disposition.match(/filename\*=UTF-8''([^;]+)/i);
  const plainMatch = disposition.match(/filename="?([^";]+)"?/i);
  const fileName = utf8Match?.[1]
    ? decodeURIComponent(utf8Match[1])
    : plainMatch?.[1] || path.split('/').pop() || 'download';

  const blobUrl = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = blobUrl;
  a.download = fileName;
  a.click();
  URL.revokeObjectURL(blobUrl);
}

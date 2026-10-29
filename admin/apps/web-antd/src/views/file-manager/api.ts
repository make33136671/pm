import { useAppConfig } from '@vben/hooks';
import { useAccessStore } from '@vben/stores';

import { api } from '#/api/request';

const { apiURL } = useAppConfig(import.meta.env, import.meta.env.PROD);

// ==================== 类型 ====================

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

export interface RecycleItem {
  id: string;
  originalPath: string;
  name: string;
  size: number;
  isDirectory: boolean;
  deletedAt: string;
  deletedBy: string;
}

export interface RecycleList {
  items: RecycleItem[];
  totalSize: number;
  count: number;
  quotaMb: number;
}

export interface ArchiveEntry {
  name: string;
  size: number;
  compressedSize: number;
  isDirectory: boolean;
  lastModified: string;
  encrypted: boolean;
}

export interface ArchivePreview {
  path: string;
  format: string;
  totalEntries: number;
  totalSize: number;
  truncated: boolean;
  entries: ArchiveEntry[];
}

export interface BatchItemResult {
  path: string;
  success: boolean;
  message: string;
}

export interface BatchResult {
  total: number;
  succeeded: number;
  failed: number;
  items: BatchItemResult[];
}

export interface PathSize {
  path: string;
  size: number;
  fileCount: number;
  dirCount: number;
  truncated: boolean;
}

export interface SearchResult {
  items: FileEntry[];
  truncated: boolean;
  scanned: number;
  elapsedMs: number;
}

export interface FileNote {
  path: string;
  note: string;
  favorite: boolean;
  updatedTime: string;
}

export interface SystemUser {
  name: string;
  uid: number;
  gid: number;
}

// ==================== 浏览 ====================

export async function listDirectory(path: string, showHidden = false) {
  return api.get('/FileManager/List', {
    params: { path, showHidden },
  }) as Promise<DirectoryListing>;
}

export async function listDirectories(path: string) {
  return api.get('/FileManager/Directories', {
    params: { path },
  }) as Promise<FileEntry[]>;
}

export async function getRoots() {
  return api.get('/FileManager/Roots') as Promise<Breadcrumb[]>;
}

export async function getDiskUsage(path: string) {
  return api.get('/FileManager/DiskUsage', {
    params: { path },
  }) as Promise<DiskUsage>;
}

// ==================== 读写 ====================

export async function readFile(path: string) {
  return api.get('/FileManager/Read', { params: { path } }) as Promise<FileContent>;
}

export async function saveFile(payload: {
  path: string;
  content: string;
  expectedHash?: string;
  createBackup?: boolean;
}) {
  return api.post('/FileManager/Save', payload, { quiet: true });
}

export async function createEntry(path: string, isDirectory: boolean) {
  return api.post('/FileManager/Create', { path, isDirectory });
}

export async function renameEntry(path: string, newName: string) {
  return api.post('/FileManager/Rename', { path, newName });
}

export async function deleteEntry(path: string, recursive = false) {
  return api.post('/FileManager/Delete', { path, recursive });
}

export async function chmodEntry(path: string, mode: string) {
  return api.post('/FileManager/Chmod', { path, mode });
}

export async function uploadFile(dirPath: string, file: File, overwrite = false) {
  const formData = new FormData();
  formData.append('dirPath', dirPath);
  formData.append('file', file);
  formData.append('overwrite', String(overwrite));
  return api.post('/FileManager/Upload', formData, { quiet: true });
}

// ==================== 复制 / 移动 ====================

export async function copyEntry(source: string, target: string, overwrite = false) {
  return api.post('/FileManager/Copy', { source, target, overwrite });
}

export async function moveEntry(source: string, target: string, overwrite = false) {
  return api.post('/FileManager/Move', { source, target, overwrite });
}

// ==================== 批量 ====================

/** 批量接口统一用换行分隔多路径（后端 SplitPaths 解析）。 */
const joinPaths = (paths: string[]) => paths.join('\n');

export async function batchDelete(paths: string[], recursive = true) {
  return api.post('/FileManager/BatchDelete', {
    paths: joinPaths(paths),
    recursive,
  }) as Promise<BatchResult>;
}

export async function batchCopyMove(
  paths: string[],
  targetDir: string,
  move = false,
  overwrite = false,
) {
  return api.post('/FileManager/BatchCopyMove', {
    paths: joinPaths(paths),
    targetDir,
    move,
    overwrite,
  }) as Promise<BatchResult>;
}

export async function batchChmod(paths: string[], mode: string) {
  return api.post('/FileManager/BatchChmod', {
    paths: joinPaths(paths),
    mode,
  }) as Promise<BatchResult>;
}

// ==================== 回收站 ====================

export async function recycleList() {
  return api.get('/FileManager/RecycleList') as Promise<RecycleList>;
}

export async function recycleRestore(id: string, restoreTo?: string) {
  return api.post('/FileManager/RecycleRestore', { id, restoreTo });
}

export async function recyclePurge(id: string) {
  return api.post('/FileManager/RecyclePurge', { id });
}

export async function recyclePurgeAll() {
  return api.post('/FileManager/RecyclePurgeAll');
}

// ==================== 压缩 / 解压 ====================

export async function compressPaths(payload: {
  paths: string[];
  outputDir?: string;
  name?: string;
  format?: string;
  level?: number;
  encrypt?: boolean;
  password?: string;
}) {
  return api.post('/FileManager/Compress', {
    paths: joinPaths(payload.paths),
    outputDir: payload.outputDir,
    name: payload.name,
    format: payload.format ?? 'zip',
    level: payload.level ?? 6,
    encrypt: payload.encrypt ?? false,
    password: payload.password,
  });
}

export async function archivePreview(path: string) {
  return api.get('/FileManager/ArchivePreview', {
    params: { path },
  }) as Promise<ArchivePreview>;
}

export async function extractArchive(payload: {
  path: string;
  outputDir?: string;
  password?: string;
  overwrite?: boolean;
  useGbkNames?: boolean;
}) {
  return api.post('/FileManager/Extract', payload);
}

export async function archiveFormats() {
  return api.get('/FileManager/ArchiveFormats') as Promise<string[]>;
}

// ==================== 搜索 / 统计 ====================

export async function searchFiles(params: {
  path: string;
  pattern?: string;
  recursive?: boolean;
  contentContains?: string;
  maxResults?: number;
}) {
  return api.get('/FileManager/Search', { params }) as Promise<SearchResult>;
}

export async function pathSize(path: string) {
  return api.get('/FileManager/PathSize', {
    params: { path },
  }) as Promise<PathSize>;
}

// ==================== 属主 / 备注 ====================

export async function chownEntry(payload: {
  path: string;
  owner: string;
  group?: string;
  recursive?: boolean;
}) {
  return api.post('/FileManager/Chown', payload);
}

export async function systemUsers() {
  return api.get('/FileManager/SystemUsers') as Promise<SystemUser[]>;
}

export async function getNote(path: string) {
  return api.get('/FileManager/GetNote', { params: { path } }) as Promise<FileNote>;
}

export async function saveNote(path: string, note: string, favorite: boolean) {
  return api.post('/FileManager/SaveNote', {
    path,
    note,
    favorite,
  }) as Promise<FileNote>;
}

export async function favorites() {
  return api.get('/FileManager/Favorites') as Promise<FileNote[]>;
}

// ==================== 下载 ====================

function parseFileName(disposition: string, fallback: string): string {
  const utf8 = /filename\*=UTF-8''([^;]+)/i.exec(disposition);
  if (utf8?.[1]) return decodeURIComponent(utf8[1]);
  const plain = /filename="?([^";]+)"?/i.exec(disposition);
  return plain?.[1] ?? fallback;
}

/** 下载（带 Authorization 头，所以走 fetch 再转 blob）。 */
export async function downloadFile(path: string) {
  const accessStore = useAccessStore();
  const res = await fetch(
    `${apiURL}/FileManager/Download?path=${encodeURIComponent(path)}`,
    { headers: { Authorization: `Bearer ${accessStore.accessToken}` } },
  );
  if (!res.ok) throw new Error('下载失败');

  const blob = await res.blob();
  const fileName = parseFileName(
    res.headers.get('content-disposition') ?? '',
    path.split('/').pop() ?? 'download',
  );

  const blobUrl = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = blobUrl;
  a.download = fileName;
  a.click();
  URL.revokeObjectURL(blobUrl);
}

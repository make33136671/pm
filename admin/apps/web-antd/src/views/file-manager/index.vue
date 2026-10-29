<script lang="ts" setup>
import type { FileContent, FileEntry, RecycleItem } from './api';

import { computed, onBeforeUnmount, onMounted, reactive, ref, watch } from 'vue';

import { Page } from '@vben/common-ui';

import {
  Alert,
  Button,
  Card,
  Checkbox,
  Drawer,
  Dropdown,
  Empty,
  Input,
  message,
  Modal,
  Progress,
  Select,
  Space,
  Spin,
  Switch,
  Table,
  Tag,
  Tree,
} from 'ant-design-vue';

import {
  archiveFormats,
  archivePreview,
  batchCopyMove,
  batchDelete,
  chmodEntry,
  chownEntry,
  compressPaths,
  copyEntry,
  createEntry,
  deleteEntry,
  downloadFile,
  extractArchive,
  favorites,
  getDiskUsage,
  getNote,
  getDefaultPath,
  getRoots,
  listDirectories,
  listDirectory,
  pathSize,
  readFile,
  recycleList,
  recyclePurge,
  recyclePurgeAll,
  recycleRestore,
  renameEntry,
  saveFile,
  saveNote,
  searchFiles,
  systemUsers,
  uploadFile,
} from './api';

defineOptions({ name: 'FileManager' });

// ==================== 全局状态 ====================

const loading = ref(false);
const listing = ref<Awaited<ReturnType<typeof listDirectory>> | null>(null);
const currentPath = ref('/');
const showHidden = ref(false);
const disk = ref<Awaited<ReturnType<typeof getDiskUsage>> | null>(null);
const selectedPaths = ref<string[]>([]);

const treeData = ref<any[]>([]);
const treeExpanded = ref<string[]>([]);
const treeLoading = ref(false);

// 视图模式：列表 / 搜索
const viewMode = ref<'list' | 'search'>('list');

// ==================== 列目录 ====================

async function loadDir(path?: string) {
  loading.value = true;
  try {
    listing.value = await listDirectory(path ?? currentPath.value, showHidden.value);
    currentPath.value = listing.value?.path ?? '/';
    selectedPaths.value = [];
    if (!treeExpanded.value.includes(currentPath.value)) {
      treeExpanded.value = [...treeExpanded.value, currentPath.value];
    }
    void loadDisk();
  } catch {
    // 拦截器已提示
  } finally {
    loading.value = false;
  }
}

async function loadDisk() {
  try {
    disk.value = await getDiskUsage(currentPath.value);
  } catch {
    disk.value = null;
  }
}

async function loadRoots() {
  try {
    const roots = (await getRoots()) ?? [];
    if (roots.length > 0) {
      treeData.value = roots.map((r) => ({
        title: r.name,
        key: r.path,
        isLeaf: false,
      }));
    }

    // 优先用系统设置里的默认打开路径；取不到再退回第一个根
    let initial = roots[0]?.path ?? '/';
    try {
      const def = await getDefaultPath();
      if (def?.path) initial = def.path;
      if (def?.fallback && def.message) {
        message.warning(def.message);
      }
    } catch {
      // 默认路径读取失败不阻断，用 roots[0]
    }
    currentPath.value = initial;
  } catch {
    rootsInit();
  }
}

/** 根目录加载失败时的兜底：直接给 / */
function rootsInit() {
  currentPath.value = '/';
  treeData.value = [{ title: '/', key: '/', isLeaf: false }];
}

// ==================== 目录树 ====================

async function onLoadTreeData(node: any) {
  treeLoading.value = true;
  try {
    const dirs = await listDirectories(node.event.node.key);
    const children = (dirs ?? []).map((d: FileEntry) => ({
      title: d.name,
      key: d.path,
      isLeaf: false,
    }));
    const attach = (nodes: any[]): any[] =>
      nodes.map((n) =>
        n.key === node.event.node.key
          ? { ...n, children }
          : n.children
            ? { ...n, children: attach(n.children) }
            : n,
      );
    treeData.value = attach(treeData.value);
  } catch {
    treeData.value = treeData.value.map((n: any) =>
      n.key === node.event.node.key ? { ...n, isLeaf: true } : n,
    );
  } finally {
    treeLoading.value = false;
  }
}

function onTreeSelect(keys: any[]) {
  const p = keys[0];
  if (p && p !== currentPath.value) {
    viewMode.value = 'list';
    void loadDir(String(p));
  }
}

// ==================== 表格 ====================

const columns = [
  { title: '名称', dataIndex: 'name', key: 'name', ellipsis: true },
  { title: '大小', dataIndex: 'sizeText', key: 'size', width: 100, align: 'right' },
  { title: '权限', dataIndex: 'mode', key: 'mode', width: 80 },
  { title: '属主', dataIndex: 'ownerText', key: 'owner', width: 100 },
  { title: '修改时间', dataIndex: 'mtimeText', key: 'mtime', width: 165 },
  { title: '操作', key: 'action', width: 240, fixed: 'right' },
];

const tableData = computed(() => {
  if (viewMode.value === 'search') return searchRows.value;
  const l = listing.value;
  if (!l) return [];
  const mk = (e: FileEntry) => decorate(e);
  return [...(l.directories ?? []).map(mk), ...(l.files ?? []).map(mk)];
});

function decorate(e: FileEntry) {
  return {
    ...e,
    sizeText: e.isDirectory ? '-' : formatSize(e.size),
    ownerText: e.owner && e.owner !== '-' ? `${e.owner}:${e.group}` : '-',
    mtimeText: formatTime(e.lastModified),
    canExtract: !e.isDirectory && isArchiveName(e.name),
  };
}

function formatSize(bytes: number) {
  if (!bytes) return '0 B';
  const units = ['B', 'KB', 'MB', 'GB', 'TB'];
  let v = bytes;
  let i = 0;
  while (v >= 1024 && i < units.length - 1) {
    v /= 1024;
    i += 1;
  }
  return `${v.toFixed(v >= 10 || i === 0 ? 0 : 1)} ${units[i]}`;
}

function formatTime(iso: string) {
  if (!iso) return '-';
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return iso;
  const p = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())} ${p(d.getHours())}:${p(d.getMinutes())}:${p(d.getSeconds())}`;
}

const ARCHIVE_RE = /\.(zip|tar|tar\.gz|tgz|tar\.bz2|tar\.xz|7z|rar|gz|bz2|xz)$/i;
function isArchiveName(name: string) {
  return ARCHIVE_RE.test(name);
}

const selectedRows = computed(() =>
  tableData.value.filter((r) => selectedPaths.value.includes(r.path)),
);

const rowSelection = computed(() => ({
  selectedRowKeys: selectedPaths.value,
  onChange: (keys: (string | number)[]) => {
    selectedPaths.value = keys.map(String);
  },
  columnWidth: 40,
}));

function onRowDblClick(record: any) {
  if (record.isDirectory) {
    viewMode.value = 'list';
    void loadDir(record.path);
  } else {
    void openEditor(record);
  }
}

function goUp() {
  const p = listing.value?.parentPath;
  if (p) void loadDir(p);
}

function goCrumb(path: string) {
  viewMode.value = 'list';
  if (path !== currentPath.value) void loadDir(path);
}

// ==================== 编辑器 ====================

const editorOpen = ref(false);
const editorLoading = ref(false);
const saving = ref(false);
const current = ref<FileContent | null>(null);
const editorContent = ref('');
const dirty = computed(
  () => current.value !== null && editorContent.value !== current.value.content,
);

async function openEditor(record: any) {
  if (record.isDirectory) return;
  editorOpen.value = true;
  editorLoading.value = true;
  current.value = null;
  try {
    const data = await readFile(record.path);
    current.value = data;
    editorContent.value = data.content;
  } catch {
    editorOpen.value = false;
  } finally {
    editorLoading.value = false;
  }
}

async function handleSave(createBackup = true) {
  if (!current.value) return;
  saving.value = true;
  try {
    await saveFile({
      path: current.value.path,
      content: editorContent.value,
      expectedHash: current.value.hash,
      createBackup,
    });
    const fresh = await readFile(current.value.path);
    current.value = fresh;
    editorContent.value = fresh.content;
    await loadDir();
  } catch {
    // 拦截器已提示（含「文件已被他人修改」）
  } finally {
    saving.value = false;
  }
}

function closeEditor() {
  if (dirty.value) {
    Modal.confirm({
      title: '放弃未保存的修改？',
      content: '当前文件有未保存的改动，关闭后会丢失。',
      okText: '放弃并关闭',
      okType: 'danger',
      cancelText: '继续编辑',
      onOk: () => {
        editorOpen.value = false;
        current.value = null;
      },
    });
    return;
  }
  editorOpen.value = false;
  current.value = null;
}

function onBeforeUnload(e: BeforeUnloadEvent) {
  if (dirty.value) {
    e.preventDefault();
    e.returnValue = '';
  }
}

/** 编辑器里 Tab 插入两空格，而不是切走焦点 */
function onTab(e: KeyboardEvent) {
  const el = e.target as HTMLTextAreaElement | null;
  if (!el) return;
  const { selectionStart: s, selectionEnd: end, value } = el;
  el.value = `${value.slice(0, s)}  ${value.slice(end)}`;
  el.selectionStart = el.selectionEnd = s + 2;
  el.dispatchEvent(new Event('input'));
}

// ==================== 新建 / 重命名 ====================

const createModal = reactive({ open: false, isDirectory: false, name: '' });
const renameModal = reactive({ open: false, path: '', name: '' });

function openCreate(isDirectory: boolean) {
  Object.assign(createModal, { open: true, isDirectory, name: '' });
}

async function doCreate() {
  const name = createModal.name.trim();
  if (!name) {
    message.error('请输入名称');
    return;
  }
  const base = currentPath.value === '/' ? '' : currentPath.value;
  const target = `${base}/${name}`.replaceAll('//', '/');
  try {
    await createEntry(target, createModal.isDirectory);
    createModal.open = false;
    await loadDir();
  } catch {
    // 拦截器已提示
  }
}

function openRename(record: any) {
  Object.assign(renameModal, { open: true, path: record.path, name: record.name });
}

async function doRename() {
  const name = renameModal.name.trim();
  if (!name) {
    message.error('请输入名称');
    return;
  }
  try {
    await renameEntry(renameModal.path, name);
    renameModal.open = false;
    await loadDir();
  } catch {
    // 拦截器已提示
  }
}

// ==================== 删除 ====================

async function doDelete(record: any) {
  Modal.confirm({
    title: `确认删除「${record.name}」？`,
    content: record.isDirectory
      ? '目录及其全部内容将移入回收站（可在回收站恢复）。'
      : '文件将移入回收站（可在回收站恢复）。',
    okText: '移入回收站',
    okType: 'danger',
    cancelText: '取消',
    onOk: async () => {
      try {
        await deleteEntry(record.path, record.isDirectory);
        await loadDir();
      } catch {
        // 拦截器已提示
      }
    },
  });
}

async function doBatchDelete() {
  const n = selectedPaths.value.length;
  if (n === 0) return;
  Modal.confirm({
    title: `确认删除选中的 ${n} 项？`,
    content: '全部移入回收站，可恢复。',
    okText: '移入回收站',
    okType: 'danger',
    cancelText: '取消',
    onOk: async () => {
      try {
        const r = await batchDelete(selectedPaths.value);
        message.success(`成功 ${r.succeeded} 项，失败 ${r.failed} 项`);
        await loadDir();
      } catch {
        // 拦截器已提示
      }
    },
  });
}

// ==================== 权限 ====================

const chmodModal = reactive({ open: false, path: '', mode: '', recursive: false });
const chownModal = reactive({
  open: false,
  path: '',
  owner: '',
  group: '',
  recursive: false,
});
const userOptions = ref<{ label: string; value: string }[]>([]);

function openChmod(record: any) {
  Object.assign(chmodModal, {
    open: true,
    path: record.path,
    mode: (record.mode || '0644').replace(/^0/, ''),
    recursive: false,
  });
}

async function doChmod() {
  try {
    await chmodEntry(chmodModal.path, chmodModal.mode);
    chmodModal.open = false;
    await loadDir();
  } catch {
    // 拦截器已提示
  }
}

async function openChown(record: any) {
  Object.assign(chownModal, {
    open: true,
    path: record.path,
    owner: record.owner && record.owner !== '-' ? record.owner : 'root',
    group: '',
    recursive: false,
  });
  if (userOptions.value.length === 0) {
    try {
      const users = await systemUsers();
      userOptions.value = users.map((u) => ({
        label: `${u.name} (${u.uid})`,
        value: u.name,
      }));
    } catch {
      // 拉不到就允许手输
    }
  }
}

async function doChown() {
  try {
    await chownEntry({
      path: chownModal.path,
      owner: chownModal.owner,
      group: chownModal.group || undefined,
      recursive: chownModal.recursive,
    });
    chownModal.open = false;
    await loadDir();
  } catch {
    // 拦截器已提示
  }
}

// ==================== 上传 / 下载 ====================

const uploadRef = ref<HTMLInputElement | null>(null);

function pickUpload() {
  uploadRef.value?.click();
}

async function onUploadChange(e: Event) {
  const input = e.target as HTMLInputElement;
  const files = input.files;
  if (!files || files.length === 0) return;
  let ok = 0;
  let fail = 0;
  for (const file of Array.from(files)) {
    try {
      await uploadFile(currentPath.value, file, true);
      ok += 1;
    } catch {
      fail += 1;
    }
  }
  input.value = '';
  message.success(`上传完成：成功 ${ok} 个${fail > 0 ? `，失败 ${fail} 个` : ''}`);
  await loadDir();
}

async function handleDownload(record: any) {
  try {
    await downloadFile(record.path);
  } catch {
    message.error('下载失败');
  }
}

// ==================== 复制 / 移动 / 粘贴 ====================

const clipboard = ref<{ mode: 'copy' | 'cut'; paths: string[] }>({
  mode: 'copy',
  paths: [],
});

function setClipboard(mode: 'copy' | 'cut', paths: string[]) {
  if (paths.length === 0) return;
  clipboard.value = { mode, paths };
  message.success(`已${mode === 'copy' ? '复制' : '剪切'} ${paths.length} 项，进入目标目录后点「粘贴」`);
}

async function doPaste() {
  const { mode, paths } = clipboard.value;
  if (paths.length === 0) {
    message.warning('剪贴板为空');
    return;
  }
  try {
    const r = await batchCopyMove(paths, currentPath.value, mode === 'cut');
    message.success(`成功 ${r.succeeded} 项，失败 ${r.failed} 项`);
    if (mode === 'cut') setClipboard('copy', []);
    await loadDir();
  } catch {
    // 拦截器已提示
  }
}

const moveTargetModal = reactive({ open: false, target: '' });

function openMoveTo() {
  Object.assign(moveTargetModal, { open: true, target: currentPath.value });
}

async function doMoveTo() {
  const paths = selectedPaths.value.length > 0
    ? selectedPaths.value
    : copyModal.src
      ? [copyModal.src]
      : [];
  if (paths.length === 0) {
    message.warning('请先选择条目');
    return;
  }
  try {
    await batchCopyMove(paths, moveTargetModal.target, false);
    moveTargetModal.open = false;
    await loadDir();
  } catch {
    // 拦截器已提示
  }
}

/// 单个「复制到」弹窗（不用浏览器 prompt：lint 禁用，且样式不受控）
const copyModal = reactive({ open: false, src: '', target: '' });

function doCopyTo(record: any) {
  Object.assign(copyModal, { open: true, src: record.path, target: record.path });
}

async function doCopyToConfirm() {
  if (!copyModal.target) {
    message.error('请输入目标路径');
    return;
  }
  try {
    await copyEntry(copyModal.src, copyModal.target);
    copyModal.open = false;
    await loadDir();
  } catch {
    // 拦截器已提示
  }
}
// ==================== 压缩 / 解压 ====================

const formats = ref<string[]>(['zip', 'tar.gz']);

const compressModal = reactive({
  open: false,
  name: '',
  format: 'zip',
  level: 6,
  encrypt: false,
  password: '',
});

function openCompress(paths: string[]) {
  const first = paths[0]?.split('/').pop() ?? 'archive';
  Object.assign(compressModal, {
    open: true,
    name: paths.length === 1 ? first : 'archive',
    format: 'zip',
    level: 6,
    encrypt: false,
    password: '',
  });
}

async function doCompress() {
  const paths = selectedPaths.value;
  try {
    await compressPaths({
      paths,
      name: compressModal.name,
      format: compressModal.format,
      level: compressModal.level,
      encrypt: compressModal.encrypt,
      password: compressModal.password || undefined,
    });
    compressModal.open = false;
    await loadDir();
  } catch {
    // 拦截器已提示
  }
}

const extractModal = reactive({
  open: false,
  path: '',
  preview: null as Awaited<ReturnType<typeof archivePreview>> | null,
  outputDir: '',
  overwrite: false,
  password: '',
  useGbkNames: false,
});

async function openExtract(record: any) {
  Object.assign(extractModal, {
    open: true,
    path: record.path,
    preview: null,
    outputDir: '',
    overwrite: false,
    password: '',
    useGbkNames: false,
  });
  try {
    extractModal.preview = await archivePreview(record.path);
  } catch {
    // 预览失败不影响解压
  }
}

async function doExtract() {
  try {
    await extractArchive({
      path: extractModal.path,
      outputDir: extractModal.outputDir || undefined,
      overwrite: extractModal.overwrite,
      password: extractModal.password || undefined,
      useGbkNames: extractModal.useGbkNames,
    });
    extractModal.open = false;
    await loadDir();
  } catch {
    // 拦截器已提示
  }
}

// ==================== 搜索 ====================

const searchForm = reactive({
  pattern: '',
  content: '',
  recursive: true,
});
const searchRows = ref<any[]>([]);
const searchInfo = ref<{ scanned: number; elapsedMs: number; truncated: boolean } | null>(null);
const searching = ref(false);

async function doSearch() {
  if (!searchForm.pattern && !searchForm.content) {
    message.warning('请输入文件名关键词或内容关键词');
    return;
  }
  searching.value = true;
  try {
    const r = await searchFiles({
      path: currentPath.value,
      pattern: searchForm.pattern || '*',
      recursive: searchForm.recursive,
      contentContains: searchForm.content || undefined,
    });
    searchRows.value = r.items.map(decorate);
    searchInfo.value = {
      scanned: r.scanned,
      elapsedMs: r.elapsedMs,
      truncated: r.truncated,
    };
    viewMode.value = 'search';
  } catch {
    // 拦截器已提示
  } finally {
    searching.value = false;
  }
}

function clearSearch() {
  viewMode.value = 'list';
  searchRows.value = [];
  searchInfo.value = null;
}

// ==================== 回收站 ====================

const recycleOpen = ref(false);
const recycleLoading = ref(false);
const recycleData = ref<Awaited<ReturnType<typeof recycleList>> | null>(null);

async function openRecycle() {
  recycleOpen.value = true;
  await loadRecycle();
}

async function loadRecycle() {
  recycleLoading.value = true;
  try {
    recycleData.value = await recycleList();
  } catch {
    recycleData.value = null;
  } finally {
    recycleLoading.value = false;
  }
}

async function doRestore(item: RecycleItem) {
  try {
    await recycleRestore(item.id);
    await loadRecycle();
    await loadDir();
  } catch {
    // 拦截器已提示
  }
}

async function doPurge(item: RecycleItem) {
  Modal.confirm({
    title: `永久删除「${item.name}」？`,
    content: '不可恢复。',
    okText: '永久删除',
    okType: 'danger',
    onOk: async () => {
      try {
        await recyclePurge(item.id);
        await loadRecycle();
      } catch {
        // 拦截器已提示
      }
    },
  });
}

function doPurgeAll() {
  Modal.confirm({
    title: '清空回收站？',
    content: '回收站中全部条目将被永久删除，不可恢复。',
    okText: '清空',
    okType: 'danger',
    onOk: async () => {
      try {
        await recyclePurgeAll();
        await loadRecycle();
      } catch {
        // 拦截器已提示
      }
    },
  });
}

// ==================== 属性 / 备注 ====================

const propsModal = reactive({
  open: false,
  path: '',
  size: null as Awaited<ReturnType<typeof pathSize>> | null,
  note: '',
  favorite: false,
  loading: false,
});

async function openProps(record: any) {
  Object.assign(propsModal, {
    open: true,
    path: record.path,
    size: null,
    note: '',
    favorite: false,
    loading: true,
  });
  try {
    const [s, n] = await Promise.all([
      pathSize(record.path),
      getNote(record.path).catch(() => null),
    ]);
    propsModal.size = s;
    if (n) {
      propsModal.note = n.note;
      propsModal.favorite = n.favorite;
    }
  } finally {
    propsModal.loading = false;
  }
}

async function doSaveNote() {
  try {
    await saveNote(propsModal.path, propsModal.note, propsModal.favorite);
    propsModal.open = false;
  } catch {
    // 拦截器已提示
  }
}

// ==================== 收藏夹 ====================

const favList = ref<Awaited<ReturnType<typeof favorites>>>([]);

async function loadFavorites() {
  try {
    favList.value = await favorites();
  } catch {
    favList.value = [];
  }
}

function gotoFavorite(path: string) {
  viewMode.value = 'list';
  void loadDir(path);
}

// ==================== 右键菜单 ====================

const ctxMenu = reactive({
  open: false,
  x: 0,
  y: 0,
  record: null as any,
});

function onRowContextMenu(e: MouseEvent, record: any) {
  e.preventDefault();
  if (!selectedPaths.value.includes(record.path)) {
    selectedPaths.value = [record.path];
  }
  Object.assign(ctxMenu, { open: true, x: e.clientX, y: e.clientY, record });
}

function closeCtxMenu() {
  ctxMenu.open = false;
}

const ctxItems = computed(() => {
  const r = ctxMenu.record;
  const multi = selectedPaths.value.length > 1;
  if (!r) return [];
  const items: any[] = [
    { key: 'open', label: r.isDirectory ? '打开' : '编辑' },
    { key: 'download', label: '下载', disabled: r.isDirectory },
    { type: 'divider' },
    { key: 'copy', label: multi ? `复制 ${selectedPaths.value.length} 项` : '复制' },
    { key: 'cut', label: multi ? `剪切 ${selectedPaths.value.length} 项` : '剪切' },
    { key: 'paste', label: '粘贴到此处', disabled: r.isDirectory === false },
    { key: 'rename', label: '重命名', disabled: multi },
    { type: 'divider' },
    { key: 'compress', label: '压缩' },
    { key: 'extract', label: '解压', disabled: !isArchiveName(r.name) },
    { type: 'divider' },
    { key: 'chmod', label: '权限', disabled: multi },
    { key: 'chown', label: '属主', disabled: multi },
    { key: 'props', label: '属性 / 备注', disabled: multi },
    { type: 'divider' },
    { key: 'delete', label: '删除（移入回收站）', danger: true },
  ];
  return items;
});

async function onCtxClick({ key }: { key: string }) {
  const r = ctxMenu.record;
  closeCtxMenu();
  if (!r) return;

  const paths = selectedPaths.value.length > 0 ? selectedPaths.value : [r.path];

  switch (key) {
    case 'open': {
      if (r.isDirectory) {
        viewMode.value = 'list';
        await loadDir(r.path);
      } else {
        await openEditor(r);
      }
      break;
    }
    case 'download': {
      await handleDownload(r);
      break;
    }
    case 'copy': {
      setClipboard('copy', paths);
      break;
    }
    case 'cut': {
      setClipboard('cut', paths);
      break;
    }
    case 'paste': {
      const prev = currentPath.value;
      await loadDir(r.path);
      await doPaste();
      if (prev !== currentPath.value) void loadDir(prev);
      break;
    }
    case 'rename': {
      openRename(r);
      break;
    }
    case 'compress': {
      openCompress(paths);
      break;
    }
    case 'extract': {
      await openExtract(r);
      break;
    }
    case 'chmod': {
      openChmod(r);
      break;
    }
    case 'chown': {
      await openChown(r);
      break;
    }
    case 'props': {
      await openProps(r);
      break;
    }
    case 'delete': {
      if (paths.length > 1) await doBatchDelete();
      else await doDelete(r);
      break;
    }
    default:
      break;
  }
}

// ==================== 生命周期 ====================

watch(showHidden, () => loadDir());

onMounted(async () => {
  window.addEventListener('beforeunload', onBeforeUnload);
  window.addEventListener('click', closeCtxMenu);
  await loadRoots();
  await loadDir();
  void loadFavorites();
  archiveFormats()
    .then((f) => {
      if (Array.isArray(f) && f.length > 0) formats.value = f;
    })
    .catch(() => {});
});

onBeforeUnmount(() => {
  window.removeEventListener('beforeunload', onBeforeUnload);
  window.removeEventListener('click', closeCtxMenu);
});

const rowKey = (r: any) => r.path;
</script>

<template>
  <Page auto-content-height>
    <div class="fm-root">
      <!-- 左侧：目录树 + 收藏夹 -->
      <Card
        title="目录"
        class="fm-tree-card"
        :body-style="{ flex: 1, overflow: 'auto', minHeight: 0, padding: '8px' }"
      >
        <template #extra>
          <Button size="small" @click="loadDir()">刷新</Button>
        </template>
        <Spin :spinning="treeLoading">
          <Tree
            v-model:expanded-keys="treeExpanded"
            :tree-data="treeData"
            :selected-keys="[currentPath]"
            :load-data="onLoadTreeData"
            show-icon
            block-node
            @select="onTreeSelect"
          />
        </Spin>

        <div v-if="favList.length > 0" class="fm-fav">
          <div class="fm-fav-title">收藏夹</div>
          <div
            v-for="f in favList"
            :key="f.path"
            class="fm-fav-item"
            :title="f.path"
            @click="gotoFavorite(f.path)"
          >
            ⭐ {{ f.path.split('/').pop() || f.path }}
          </div>
        </div>
      </Card>

      <!-- 右侧：文件区 -->
      <Card class="fm-main-card" :body-style="{ flex: 1, display: 'flex', flexDirection: 'column', minHeight: 0, padding: '0 16px 12px' }">
        <template #title>
          <Space wrap>
            <Button size="small" :disabled="!listing?.parentPath" @click="goUp">上级</Button>
            <Button size="small" @click="loadDir()">刷新</Button>
            <Dropdown :trigger="['click']">
              <Button size="small">新建 ▾</Button>
              <template #overlay>
                <div class="fm-menu-box">
                  <div class="fm-menu-item" @click="openCreate(false)">📄 新建文件</div>
                  <div class="fm-menu-item" @click="openCreate(true)">📁 新建目录</div>
                </div>
              </template>
            </Dropdown>
            <Button size="small" @click="pickUpload">上传</Button>
            <Button size="small" :disabled="clipboard.paths.length === 0" @click="doPaste">
              粘贴<span v-if="clipboard.paths.length > 0">({{ clipboard.paths.length }})</span>
            </Button>
            <Button size="small" @click="openMoveTo">移动到…</Button>
            <Button size="small" :disabled="selectedPaths.length === 0" @click="openCompress(selectedPaths)">
              压缩
            </Button>
            <Button size="small" @click="openRecycle">回收站</Button>
            <input ref="uploadRef" type="file" multiple class="fm-hidden" @change="onUploadChange" />
          </Space>
        </template>

        <template #extra>
          <Space size="small">
            <span v-if="disk" class="fm-dim fm-xs">磁盘</span>
            <Progress
              v-if="disk"
              :percent="disk.usedPercent"
              size="small"
              :show-info="false"
              class="fm-disk-bar"
            />
            <span v-if="disk" class="fm-dim fm-xs">
              {{ formatSize(disk.freeBytes) }} 可用 / {{ formatSize(disk.totalBytes) }}
            </span>
            <span class="fm-dim fm-xs">显示隐藏文件</span>
            <Switch v-model:checked="showHidden" size="small" />
          </Space>
        </template>

        <!-- 搜索栏 -->
        <div class="fm-search">
          <Input
            v-model:value="searchForm.pattern"
            placeholder="文件名（支持 * ?）"
            allow-clear
            style="width: 200px"
            @press-enter="doSearch"
          />
          <Input
            v-model:value="searchForm.content"
            placeholder="内容包含（可选）"
            allow-clear
            style="width: 180px"
            @press-enter="doSearch"
          />
          <Checkbox v-model:checked="searchForm.recursive">含子目录</Checkbox>
          <Button size="small" type="primary" :loading="searching" @click="doSearch">搜索</Button>
          <Button v-if="viewMode === 'search'" size="small" @click="clearSearch">返回列表</Button>
          <span v-if="searchInfo" class="fm-dim fm-xs">
            扫描 {{ searchInfo.scanned }} 项，{{ searchInfo.elapsedMs }}ms
            <Tag v-if="searchInfo.truncated" color="warning">结果已截断</Tag>
          </span>
        </div>

        <!-- 面包屑 -->
        <div class="fm-crumbs">
          <template v-for="(c, i) in listing?.breadcrumbs ?? []" :key="c.path">
            <span class="fm-crumb" @click="goCrumb(c.path)">{{ c.name }}</span>
            <span v-if="i < (listing?.breadcrumbs?.length ?? 0) - 1" class="fm-dim">/</span>
          </template>
          <Tag v-if="selectedPaths.length > 0" color="processing" class="fm-ml">
            已选 {{ selectedPaths.length }} 项
          </Tag>
          <Button
            v-if="selectedPaths.length > 0"
            size="small"
            danger
            class="fm-ml"
            @click="doBatchDelete"
          >
            批量删除
          </Button>
          <Button
            v-if="selectedPaths.length > 1"
            size="small"
            class="fm-ml"
            @click="openCompress(selectedPaths)"
          >
            批量压缩
          </Button>
        </div>

        <Alert
          v-if="listing?.truncated && viewMode === 'list'"
          type="warning"
          :message="`目录条目过多，仅显示前 ${listing?.total} 条中的一部分`"
          class="fm-mb"
          show-icon
        />

        <div class="fm-table-wrap">
          <Spin :spinning="loading">
            <Table
              :columns="columns as any"
              :data-source="tableData"
              :row-key="rowKey"
              :row-selection="rowSelection as any"
              size="small"
              :pagination="false"
              :scroll="{ x: 1000 }"
              :custom-row="(record: any) => ({
                onDblclick: () => onRowDblClick(record),
                onContextmenu: (e: MouseEvent) => onRowContextMenu(e, record),
              })"
            >
              <template #bodyCell="{ column, record }">
                <template v-if="column.key === 'name'">
                  <Space>
                    <span class="fm-icon">{{ record.isDirectory ? '📁' : '📄' }}</span>
                    <span
                      :class="{ 'fm-link': record.isDirectory }"
                      @click="record.isDirectory && loadDir(record.path)"
                    >
                      {{ record.name }}
                    </span>
                    <Tag v-if="record.isSymlink" color="purple">链接</Tag>
                    <Tag v-if="record.canExtract" color="blue">压缩包</Tag>
                  </Space>
                </template>
                <template v-else-if="column.key === 'action'">
                  <Space size="small" wrap>
                    <Button
                      v-if="!record.isDirectory"
                      size="small"
                      :disabled="!record.editable"
                      @click="openEditor(record)"
                    >
                      编辑
                    </Button>
                    <Button v-if="record.isDirectory" size="small" @click="loadDir(record.path)">
                      打开
                    </Button>
                    <Button v-if="!record.isDirectory" size="small" @click="handleDownload(record)">
                      下载
                    </Button>
                    <Button v-if="record.canExtract" size="small" @click="openExtract(record)">
                      解压
                    </Button>
                    <Button size="small" danger @click="doDelete(record)">删除</Button>
                    <Dropdown :trigger="['click']">
                      <Button size="small">更多 ▾</Button>
                      <template #overlay>
                        <div class="fm-menu-box">
                          <div class="fm-menu-item" @click="openRename(record)">重命名</div>
                          <div class="fm-menu-item" @click="setClipboard('copy', [record.path])">复制</div>
                          <div class="fm-menu-item" @click="setClipboard('cut', [record.path])">剪切</div>
                          <div class="fm-menu-item" @click="doCopyTo(record)">复制到…</div>
                          <div class="fm-menu-item" @click="openChmod(record)">权限</div>
                          <div class="fm-menu-item" @click="openChown(record)">属主</div>
                          <div class="fm-menu-item" @click="openProps(record)">属性 / 备注</div>
                        </div>
                      </template>
                    </Dropdown>
                  </Space>
                </template>
              </template>
              <template #emptyText>
                <Empty :description="viewMode === 'search' ? '没有匹配的文件' : '空目录'" />
              </template>
            </Table>
          </Spin>
        </div>
      </Card>
    </div>

    <!-- 右键菜单 -->
    <div
      v-if="ctxMenu.open"
      class="fm-ctx"
      :style="{ left: `${ctxMenu.x}px`, top: `${ctxMenu.y}px` }"
      @click.stop
    >
      <template v-for="(it, i) in ctxItems" :key="i">
        <div v-if="it.type === 'divider'" class="fm-ctx-divider"></div>
        <div
          v-else
          class="fm-ctx-item"
          :class="{ 'fm-ctx-danger': it.danger, 'fm-ctx-disabled': it.disabled }"
          @click="!it.disabled && onCtxClick(it)"
        >
          {{ it.label }}
        </div>
      </template>
    </div>

    <!-- 编辑器 -->
    <Modal
      v-model:open="editorOpen"
      :title="current?.path ?? '打开文件'"
      width="90vw"
      :footer="null"
      :mask-closable="false"
      destroy-on-close
      :body-style="{ padding: '0 16px 16px' }"
      @cancel="closeEditor"
    >
      <Spin :spinning="editorLoading">
        <template v-if="current">
          <div class="fm-editor-bar">
            <Tag>大小 {{ formatSize(current.size) }}</Tag>
            <Tag>行数 {{ current.lineCount }}</Tag>
            <Tag>换行 {{ current.lineEnding }}</Tag>
            <Tag>权限 {{ current.mode }}</Tag>
            <Tag v-if="dirty" color="warning">未保存</Tag>
            <span class="fm-flex1"></span>
            <Button
              size="small"
              type="primary"
              :loading="saving"
              :disabled="!current.editable"
              @click="handleSave(true)"
            >
              保存（自动 .bak）
            </Button>
            <Button size="small" :loading="saving" :disabled="!current.editable" @click="handleSave(false)">
              直接保存
            </Button>
            <Button size="small" @click="closeEditor">关闭</Button>
          </div>

          <Alert
            v-if="current.isBinary"
            type="info"
            message="这是二进制文件，不支持在线编辑，请下载后处理。"
            show-icon
            class="fm-mb"
          />
          <Alert
            v-else-if="current.tooLarge"
            type="warning"
            :message="`文件超过在线编辑上限（${formatSize(current.size)}），请下载后处理。`"
            show-icon
            class="fm-mb"
          />

          <textarea
            v-model="editorContent"
            :readonly="!current.editable"
            spellcheck="false"
            class="fm-editor"
            @keydown.tab.prevent="onTab"
          ></textarea>
        </template>
      </Spin>
    </Modal>

    <!-- 新建 -->
    <Modal
      v-model:open="createModal.open"
      :title="createModal.isDirectory ? '新建目录' : '新建文件'"
      @ok="doCreate"
    >
      <div class="fm-dim fm-mb">将在 {{ currentPath }} 下创建</div>
      <Input v-model:value="createModal.name" placeholder="名称，如 appsettings.json" @press-enter="doCreate" />
    </Modal>

    <!-- 重命名 -->
    <Modal v-model:open="renameModal.open" title="重命名" @ok="doRename">
      <Input v-model:value="renameModal.name" placeholder="新名称" @press-enter="doRename" />
    </Modal>

    <!-- 权限 -->
    <Modal v-model:open="chmodModal.open" title="修改权限" @ok="doChmod">
      <div class="fm-dim fm-mb">{{ chmodModal.path }}</div>
      <Input v-model:value="chmodModal.mode" placeholder="八进制，如 644 / 755 / 600" @press-enter="doChmod" />
    </Modal>

    <!-- 属主 -->
    <Modal v-model:open="chownModal.open" title="修改属主" @ok="doChown">
      <div class="fm-dim fm-mb">{{ chownModal.path }}</div>
      <Space direction="vertical" style="width: 100%">
        <Select
          v-model:value="chownModal.owner"
          :options="userOptions"
          placeholder="用户名或 UID（可手输）"
          show-search
          style="width: 100%"
        />
        <Input v-model:value="chownModal.group" placeholder="属组（可选）" />
        <Checkbox v-model:checked="chownModal.recursive">递归应用到子目录</Checkbox>
      </Space>
    </Modal>

    <!-- 压缩 -->
    <Modal v-model:open="compressModal.open" title="压缩" @ok="doCompress">
      <Space direction="vertical" style="width: 100%">
        <div class="fm-dim">将压缩已选 {{ selectedPaths.length }} 项</div>
        <Input v-model:value="compressModal.name" placeholder="压缩包名称（不含扩展名）" />
        <Select v-model:value="compressModal.format" :options="formats.map((f) => ({ label: f, value: f }))" style="width: 100%" />
        <div>
          <span class="fm-dim">压缩等级：</span>
          <Select
            v-model:value="compressModal.level"
            :options="[
              { label: '0 仅存储', value: 0 },
              { label: '1 最快', value: 1 },
              { label: '6 默认', value: 6 },
              { label: '9 最小', value: 9 },
            ]"
            style="width: 140px"
          />
        </div>
        <Checkbox v-model:checked="compressModal.encrypt">加密（zip/7z）</Checkbox>
        <Input
          v-if="compressModal.encrypt"
          v-model:value="compressModal.password"
          type="password"
          placeholder="压缩口令"
        />
      </Space>
    </Modal>

    <!-- 解压 -->
    <Modal v-model:open="extractModal.open" title="解压" @ok="doExtract" width="640px">
      <Space direction="vertical" style="width: 100%">
        <div class="fm-dim">{{ extractModal.path }}</div>
        <div v-if="extractModal.preview">
          <Tag color="blue">{{ extractModal.preview.format }}</Tag>
          <Tag>{{ extractModal.preview.totalEntries }} 个条目</Tag>
          <Tag>{{ formatSize(extractModal.preview.totalSize) }}</Tag>
          <Tag v-if="extractModal.preview.truncated" color="warning">列表已截断</Tag>
        </div>
        <Input v-model:value="extractModal.outputDir" placeholder="解压到（留空 = 同目录下新建同名目录）" />
        <Input v-model:value="extractModal.password" type="password" placeholder="口令（加密包需要）" />
        <Checkbox v-model:checked="extractModal.overwrite">覆盖同名文件</Checkbox>
        <Checkbox v-model:checked="extractModal.useGbkNames">中文文件名乱码时勾选（GBK 解码）</Checkbox>
        <div v-if="extractModal.preview && extractModal.preview.entries.length > 0" class="fm-preview">
          <div v-for="e in extractModal.preview.entries.slice(0, 200)" :key="e.name" class="fm-preview-row">
            <span>{{ e.isDirectory ? '📁' : '📄' }} {{ e.name }}</span>
            <span class="fm-dim">{{ formatSize(e.size) }}</span>
          </div>
        </div>
      </Space>
    </Modal>

    <!-- 复制到 -->
    <Modal v-model:open="copyModal.open" title="复制到" @ok="doCopyToConfirm">
      <Space direction="vertical" style="width: 100%">
        <div class="fm-dim">源：{{ copyModal.src }}</div>
        <Input v-model:value="copyModal.target" placeholder="目标完整路径（含新文件名）" @press-enter="doCopyToConfirm" />
      </Space>
    </Modal>

    <!-- 移动到 -->
    <Modal v-model:open="moveTargetModal.open" title="移动到 / 复制到" @ok="doMoveTo">
      <Space direction="vertical" style="width: 100%">
        <div class="fm-dim">目标目录（把已选项复制到这里）</div>
        <Input v-model:value="moveTargetModal.target" placeholder="/opt/app" />
      </Space>
    </Modal>

    <!-- 属性 / 备注 -->
    <Modal v-model:open="propsModal.open" title="属性 / 备注" @ok="doSaveNote">
      <Spin :spinning="propsModal.loading">
        <Space direction="vertical" style="width: 100%">
          <div class="fm-dim">{{ propsModal.path }}</div>
          <div v-if="propsModal.size">
            <Tag>总大小 {{ formatSize(propsModal.size.size) }}</Tag>
            <Tag>文件 {{ propsModal.size.fileCount }}</Tag>
            <Tag>目录 {{ propsModal.size.dirCount }}</Tag>
            <Tag v-if="propsModal.size.truncated" color="warning">统计被截断</Tag>
          </div>
          <Input.TextArea
            v-model:value="propsModal.note"
            :rows="4"
            placeholder="备注（例如：这是 Nginx 主配置，改完要 reload）"
          />
          <Checkbox v-model:checked="propsModal.favorite">加入收藏夹</Checkbox>
        </Space>
      </Spin>
    </Modal>

    <!-- 回收站抽屉 -->
    <Drawer v-model:open="recycleOpen" title="回收站" width="720" placement="right">
      <template #extra>
        <Space>
          <Button size="small" @click="loadRecycle">刷新</Button>
          <Button
            size="small"
            danger
            :disabled="!recycleData || recycleData.items.length === 0"
            @click="doPurgeAll"
          >
            清空
          </Button>
        </Space>
      </template>

      <Spin :spinning="recycleLoading">
        <Alert
          v-if="recycleData"
          type="info"
          :message="`共 ${recycleData.count} 项，占用 ${formatSize(recycleData.totalSize)}${recycleData.quotaMb > 0 ? ` / 上限 ${recycleData.quotaMb}MB` : ''}`"
          show-icon
          class="fm-mb"
        />
        <Empty v-if="recycleData && recycleData.items.length === 0" description="回收站是空的" />
        <div v-for="it in recycleData?.items ?? []" :key="it.id" class="fm-recycle-row">
          <div class="fm-flex1">
            <div>
              <span class="fm-icon">{{ it.isDirectory ? '📁' : '📄' }}</span>
              <span>{{ it.name }}</span>
              <Tag class="fm-ml">{{ formatSize(it.size) }}</Tag>
            </div>
            <div class="fm-dim fm-xs">{{ it.originalPath }}</div>
            <div class="fm-dim fm-xs">{{ formatTime(it.deletedAt) }} · {{ it.deletedBy }}</div>
          </div>
          <Space>
            <Button size="small" @click="doRestore(it)">恢复</Button>
            <Button size="small" danger @click="doPurge(it)">彻底删除</Button>
          </Space>
        </div>
      </Spin>
    </Drawer>
  </Page>
</template>

<style scoped>
.fm-root {
  display: flex;
  gap: 16px;
  height: 100%;
  min-height: 0;
}

.fm-tree-card {
  display: flex;
  flex: 0 0 260px;
  flex-direction: column;
  overflow: hidden;
}

.fm-main-card {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-width: 0;
  overflow: hidden;
}

.fm-fav {
  margin-top: 12px;
  border-top: 1px solid hsl(var(--border));
  padding-top: 8px;
}

.fm-fav-title {
  margin-bottom: 4px;
  color: hsl(var(--muted-foreground));
  font-size: 12px;
}

.fm-fav-item {
  padding: 4px 6px;
  overflow: hidden;
  border-radius: 4px;
  font-size: 12px;
  text-overflow: ellipsis;
  white-space: nowrap;
  cursor: pointer;
}

.fm-fav-item:hover {
  background: hsl(var(--accent));
}

.fm-search {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  align-items: center;
  margin-bottom: 8px;
}

.fm-crumbs {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
  align-items: center;
  margin-bottom: 8px;
  font-size: 13px;
}

.fm-crumb {
  color: hsl(var(--primary));
  cursor: pointer;
}

.fm-table-wrap {
  flex: 1;
  min-height: 0;
  overflow: auto;
}

.fm-link {
  color: hsl(var(--primary));
  cursor: pointer;
}

.fm-icon {
  font-size: 15px;
}

.fm-dim {
  color: hsl(var(--muted-foreground));
}

.fm-xs {
  font-size: 12px;
}

.fm-ml {
  margin-left: 8px;
}

.fm-mb {
  margin-bottom: 8px;
}

.fm-flex1 {
  flex: 1;
}

.fm-disk-bar {
  width: 140px;
}

.fm-hidden {
  display: none;
}

.fm-menu-box {
  min-width: 140px;
  padding: 4px 0;
  background: hsl(var(--popover));
  border: 1px solid hsl(var(--border));
  border-radius: 6px;
  box-shadow: 0 6px 16px rgb(0 0 0 / 12%);
}

.fm-menu-item {
  padding: 6px 12px;
  font-size: 13px;
  cursor: pointer;
}

.fm-menu-item:hover {
  background: hsl(var(--accent));
}

.fm-ctx {
  position: fixed;
  z-index: 2000;
  min-width: 180px;
  padding: 4px 0;
  background: hsl(var(--popover));
  border: 1px solid hsl(var(--border));
  border-radius: 6px;
  box-shadow: 0 6px 16px rgb(0 0 0 / 15%);
}

.fm-ctx-item {
  padding: 6px 14px;
  font-size: 13px;
  cursor: pointer;
}

.fm-ctx-item:hover {
  background: hsl(var(--accent));
}

.fm-ctx-danger {
  color: hsl(var(--destructive));
}

.fm-ctx-disabled {
  color: hsl(var(--muted-foreground));
  cursor: not-allowed;
}

.fm-ctx-divider {
  height: 1px;
  margin: 4px 0;
  background: hsl(var(--border));
}

.fm-editor-bar {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  align-items: center;
  margin-bottom: 8px;
}

.fm-editor {
  width: 100%;
  height: 70vh;
  padding: 12px;
  background: hsl(var(--muted));
  border: 1px solid hsl(var(--border));
  border-radius: 6px;
  outline: none;
  font-family: 'JetBrains Mono', 'Fira Code', Consolas, monospace;
  font-size: 13px;
  line-height: 1.6;
  tab-size: 4;
  resize: vertical;
}

.fm-preview {
  max-height: 220px;
  padding: 8px;
  overflow: auto;
  background: hsl(var(--muted));
  border-radius: 6px;
  font-family: Consolas, monospace;
  font-size: 12px;
}

.fm-preview-row {
  display: flex;
  justify-content: space-between;
  padding: 2px 0;
}

.fm-recycle-row {
  display: flex;
  gap: 12px;
  align-items: center;
  padding: 10px 0;
  border-bottom: 1px solid hsl(var(--border));
}
</style>

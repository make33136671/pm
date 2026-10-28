<script lang="ts" setup>
import type { FileContent, FileEntry } from './api';

import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue';

import { Page } from '@vben/common-ui';

import {
  Alert,
  Button,
  Card,
  Empty,
  Input,
  message,
  Modal,
  Progress,
  Space,
  Spin,
  Table,
  Tag,
  Tree,
} from 'ant-design-vue';

import {
  chmodEntry,
  createEntry,
  deleteEntry,
  downloadFile,
  getDiskUsage,
  getRoots,
  listDirectories,
  listDirectory,
  readFile,
  renameEntry,
  saveFile,
  uploadFile,
} from './api';

defineOptions({ name: 'FileManager' });

// ==================== 状态 ====================

const loading = ref(false);
const listing = ref<Awaited<ReturnType<typeof listDirectory>> | null>(null);
const roots = ref<{ name: string; path: string }[]>([]);
const currentPath = ref('/');
const showHidden = ref(false);
const disk = ref<any>(null);

const treeData = ref<any[]>([]);
const treeExpandedKeys = ref<string[]>([]);
const treeLoading = ref(false);

// 编辑器
const editorOpen = ref(false);
const editorLoading = ref(false);
const saving = ref(false);
const current = ref<FileContent | null>(null);
const editorContent = ref('');
const dirty = computed(
  () => current.value !== null && editorContent.value !== current.value.content,
);

const uploadRef = ref<HTMLInputElement | null>(null);

// ==================== 列目录 ====================

async function loadDir(path?: string) {
  loading.value = true;
  try {
    listing.value = await listDirectory(path ?? currentPath.value, showHidden.value);
    currentPath.value = listing.value?.path ?? '/';
    // 目录加载后同步高亮树节点
    if (!treeExpandedKeys.value.includes(currentPath.value)) {
      treeExpandedKeys.value = [...treeExpandedKeys.value, currentPath.value];
    }
    loadDisk();
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
    roots.value = (await getRoots()) ?? [];
    if (roots.value.length > 0 && roots.value[0]) {
      currentPath.value = roots.value[0].path;
      treeData.value = roots.value.map((r) => ({
        title: r.name,
        key: r.path,
        isLeaf: false,
      }));
    }
  } catch {
    roots.value = [];
  }
}

// ==================== 目录树（懒加载） ====================

async function onLoadTreeData(node: any) {
  treeLoading.value = true;
  try {
    const dirs = await listDirectories(node.event.node.key);
    const children = (dirs ?? []).map((d: FileEntry) => ({
      title: d.name,
      key: d.path,
      isLeaf: false,
    }));
    // 把子节点挂到对应节点上
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

function onTreeSelect(keys: string[]) {
  if (keys.length > 0 && keys[0] && keys[0] !== currentPath.value) {
    loadDir(keys[0]);
  }
}

// ==================== 表格 ====================

const columns = [
  { title: '名称', dataIndex: 'name', key: 'name', ellipsis: true },
  { title: '大小', dataIndex: 'sizeText', key: 'size', width: 110, align: 'right' },
  { title: '权限', dataIndex: 'mode', key: 'mode', width: 90 },
  { title: '属主', dataIndex: 'ownerText', key: 'owner', width: 110 },
  { title: '修改时间', dataIndex: 'lastModified', key: 'mtime', width: 180 },
  { title: '操作', key: 'action', width: 260, fixed: 'right' },
] as const;

const tableData = computed(() => {
  const l = listing.value;
  if (!l) return [];
  const mk = (e: FileEntry) => ({
    ...e,
    sizeText: e.isDirectory ? '-' : formatSize(e.size),
    ownerText: e.owner && e.owner !== '-' ? `${e.owner}:${e.group}` : '-',
    lastModified: formatTime(e.lastModified),
  });
  return [...(l.directories ?? []).map(mk), ...(l.files ?? []).map(mk)];
});

function formatSize(bytes: number) {
  if (!bytes) return '0 B';
  const units = ['B', 'KB', 'MB', 'GB', 'TB'];
  let v = bytes;
  let i = 0;
  while (v >= 1024 && i < units.length - 1) {
    v /= 1024;
    i++;
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

/** 行双击：目录进入，文本文件打开编辑器 */
function onRowDblClick(record: any) {
  if (record.isDirectory) loadDir(record.path);
  else void openEditor(record);
}

// ==================== 编辑器 ====================

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
    message.success('保存成功');
    // 重新读取以刷新 hash / 行数
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
      content: '当前文件有未保存的改动，关闭后这些改动会丢失。',
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

// 关闭浏览器标签页时提醒未保存
function onBeforeUnload(e: BeforeUnloadEvent) {
  if (dirty.value) {
    e.preventDefault();
    e.returnValue = '';
  }
}
onMounted(() => window.addEventListener('beforeunload', onBeforeUnload));
onBeforeUnmount(() => window.removeEventListener('beforeunload', onBeforeUnload));

/** 编辑器里按 Tab 插入两个空格而不是切走焦点（改代码时很关键） */
function onTab(e: KeyboardEvent) {
  const el = e.target as HTMLTextAreaElement | null;
  if (!el) return;
  const start = el.selectionStart;
  const end = el.selectionEnd;
  const value = el.value;
  el.value = `${value.slice(0, start)}  ${value.slice(end)}`;
  el.selectionStart = el.selectionEnd = start + 2;
  el.dispatchEvent(new Event('input'));
}

// ==================== 新建 / 重命名 / 删除 / chmod ====================

const createModal = ref({ open: false, isDirectory: false, name: '' });
const renameModal = ref({ open: false, path: '', name: '' });
const chmodModal = ref({ open: false, path: '', mode: '' });

function openCreate(isDirectory: boolean) {
  createModal.value = { open: true, isDirectory, name: '' };
}

async function doCreate() {
  const name = createModal.value.name.trim();
  if (!name) {
    message.error('请输入名称');
    return;
  }
  const base = currentPath.value === '/' ? '' : currentPath.value;
  const target = `${base}/${name}`.replaceAll('//', '/');
  try {
    await createEntry(target, createModal.value.isDirectory);
    createModal.value.open = false;
    await loadDir();
  } catch {
    // 拦截器已提示
  }
}

function openRename(record: any) {
  renameModal.value = { open: true, path: record.path, name: record.name };
}

async function doRename() {
  const name = renameModal.value.name.trim();
  if (!name) {
    message.error('请输入名称');
    return;
  }
  try {
    await renameEntry(renameModal.value.path, name);
    renameModal.value.open = false;
    await loadDir();
  } catch {
    // 拦截器已提示
  }
}

function doDelete(record: any) {
  Modal.confirm({
    title: `确认删除「${record.name}」？`,
    content: record.isDirectory
      ? '该目录及其全部内容将被递归删除，此操作不可撤销。'
      : '该文件将被永久删除，此操作不可撤销。',
    okText: '删除',
    okType: 'danger',
    cancelText: '取消',
    onOk: async () => {
      try {
        await deleteEntry(record.path, record.isDirectory);
        await loadDir();
      } catch {
        // 拦截器已提示（非空目录会要求确认递归）
      }
    },
  });
}

function openChmod(record: any) {
  chmodModal.value = {
    open: true,
    path: record.path,
    mode: (record.mode || '0644').replace(/^0/, ''),
  };
}

async function doChmod() {
  try {
    await chmodEntry(chmodModal.value.path, chmodModal.value.mode);
    chmodModal.value.open = false;
    await loadDir();
  } catch {
    // 拦截器已提示
  }
}

// ==================== 上传 / 下载 ====================

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
      ok++;
    } catch {
      fail++;
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

// ==================== 其它 ====================

function goUp() {
  if (listing.value?.parentPath && listing.value.parentPath !== currentPath.value) {
    loadDir(listing.value.parentPath);
  }
}

function goCrumb(path: string) {
  if (path !== currentPath.value) loadDir(path);
}

watch(showHidden, () => loadDir());

onMounted(async () => {
  await loadRoots();
  await loadDir();
});

const rowKey = (r: any) => r.path;
</script>

<template>
  <Page auto-content-height>
    <div style="display: flex; gap: 16px; height: 100%; min-height: 0">
      <!-- 左侧目录树 -->
      <Card
        title="目录"
        style="display: flex; flex: 0 0 280px; flex-direction: column; overflow: hidden"
        :body-style="{ flex: 1, overflow: 'auto', minHeight: 0 }"
      >
        <Spin :spinning="treeLoading">
          <Tree
            v-model:expanded-keys="treeExpandedKeys"
            :tree-data="treeData"
            :selected-keys="[currentPath]"
            :load-data="onLoadTreeData"
            show-icon
            block-node
            @select="onTreeSelect"
          />
        </Spin>
      </Card>

      <!-- 右侧文件区 -->
      <Card
        style="display: flex; flex: 1; flex-direction: column; overflow: hidden; min-width: 0"
        :body-style="{ flex: 1, display: 'flex', flexDirection: 'column', minHeight: 0 }"
      >
        <template #title>
          <Space wrap>
            <Button size="small" :disabled="!listing?.parentPath" @click="goUp">
              上级
            </Button>
            <Button size="small" @click="loadDir()">刷新</Button>
            <Button size="small" @click="openCreate(false)">新建文件</Button>
            <Button size="small" @click="openCreate(true)">新建目录</Button>
            <Button size="small" @click="pickUpload">上传</Button>
            <input
              ref="uploadRef"
              type="file"
              multiple
              style="display: none"
              @change="onUploadChange"
            />
            <Space
              size="small"
              style="font-size: 12px; color: hsl(var(--muted-foreground))"
            >
              <span>显示隐藏文件</span>
              <input v-model="showHidden" type="checkbox" />
            </Space>
          </Space>
        </template>

        <template #extra>
          <Space v-if="disk" size="small" style="font-size: 12px">
            <span style="color: hsl(var(--muted-foreground))">磁盘</span>
            <Progress
              :percent="disk.usedPercent"
              size="small"
              style="width: 140px"
              :show-info="false"
            />
            <span style="color: hsl(var(--muted-foreground))">
              {{ formatSize(disk.freeBytes) }} 可用 /
              {{ formatSize(disk.totalBytes) }}
            </span>
          </Space>
        </template>

        <!-- 面包屑 -->
        <div
          style="
            display: flex;
            flex-wrap: wrap;
            gap: 4px;
            align-items: center;
            margin-bottom: 8px;
            font-size: 13px;
          "
        >
          <template v-for="(c, i) in listing?.breadcrumbs ?? []" :key="c.path">
            <span
              style="cursor: pointer; color: hsl(var(--primary))"
              @click="goCrumb(c.path)"
              >{{ c.name }}</span
            >
            <span
              v-if="i < (listing?.breadcrumbs?.length ?? 0) - 1"
              style="color: hsl(var(--muted-foreground))"
              >/</span
            >
          </template>
        </div>

        <Alert
          v-if="listing?.truncated"
          type="warning"
          :message="`目录条目过多，仅显示前 ${listing?.total} 条中的一部分`"
          style="margin-bottom: 8px"
          show-icon
        />

        <div style="flex: 1; min-height: 0; overflow: auto">
          <Spin :spinning="loading">
            <Table
              :columns="columns as any"
              :data-source="tableData"
              :row-key="rowKey"
              size="small"
              :pagination="false"
              :scroll="{ x: 1000 }"
              :custom-row="(record: any) => ({ onDblclick: () => onRowDblClick(record) })"
            >
              <template #bodyCell="{ column, record }">
                <template v-if="column.key === 'name'">
                  <Space>
                    <span style="font-size: 15px">{{ record.isDirectory ? '📁' : '📄' }}</span>
                    <span
                      :style="{
                        cursor: record.isDirectory ? 'pointer' : 'default',
                        color: record.isDirectory ? 'hsl(var(--primary))' : '',
                      }"
                      @click="record.isDirectory && loadDir(record.path)"
                      >{{ record.name }}</span
                    >
                    <Tag v-if="record.isSymlink" color="purple">链接</Tag>
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
                    <Button size="small" @click="openRename(record)">重命名</Button>
                    <Button size="small" @click="openChmod(record)">权限</Button>
                    <Button size="small" danger @click="doDelete(record)">删除</Button>
                  </Space>
                </template>
              </template>
              <template #emptyText>
                <Empty description="空目录" />
              </template>
            </Table>
          </Spin>
        </div>
      </Card>
    </div>

    <!-- 编辑器抽屉（用 Modal 全屏更利于改代码） -->
    <Modal
      v-model:open="editorOpen"
      :title="current ? `${current.path}` : '打开文件'"
      width="90vw"
      :footer="null"
      :mask-closable="false"
      destroy-on-close
      :body-style="{ padding: '0 16px 16px' }"
      @cancel="closeEditor"
    >
      <Spin :spinning="editorLoading">
        <template v-if="current">
          <div
            style="
              display: flex;
              flex-wrap: wrap;
              gap: 8px;
              align-items: center;
              margin-bottom: 8px;
            "
          >
            <Tag>大小 {{ formatSize(current.size) }}</Tag>
            <Tag>行数 {{ current.lineCount }}</Tag>
            <Tag>换行 {{ current.lineEnding }}</Tag>
            <Tag>权限 {{ current.mode }}</Tag>
            <Tag v-if="dirty" color="warning">未保存</Tag>
            <span style="flex: 1"></span>
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
            style="margin-bottom: 8px"
          />
          <Alert
            v-else-if="current.tooLarge"
            type="warning"
            :message="`文件超过在线编辑上限（${formatSize(current.size)}），请下载后处理。`"
            show-icon
            style="margin-bottom: 8px"
          />

          <textarea
            v-model="editorContent"
            :readonly="!current.editable"
            spellcheck="false"
            style="
              width: 100%;
              height: 70vh;
              padding: 12px;
              font-family: 'JetBrains Mono', 'Fira Code', Consolas, monospace;
              font-size: 13px;
              line-height: 1.6;
              tab-size: 4;
              resize: vertical;
              background: hsl(var(--muted));
              border: 1px solid hsl(var(--border));
              border-radius: 6px;
              outline: none;
            "
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
      <div style="margin-bottom: 8px; color: hsl(var(--muted-foreground)); font-size: 12px">
        将在 <b>{{ currentPath }}</b> 下创建
      </div>
      <Input v-model:value="createModal.name" placeholder="名称，如 appsettings.json" @press-enter="doCreate" />
    </Modal>

    <!-- 重命名 -->
    <Modal v-model:open="renameModal.open" title="重命名" @ok="doRename">
      <Input v-model:value="renameModal.name" placeholder="新名称" @press-enter="doRename" />
    </Modal>

    <!-- chmod -->
    <Modal v-model:open="chmodModal.open" title="修改权限" @ok="doChmod">
      <div style="margin-bottom: 8px; color: hsl(var(--muted-foreground)); font-size: 12px">
        {{ chmodModal.path }}
      </div>
      <Input v-model:value="chmodModal.mode" placeholder="八进制，如 644 / 755 / 600" @press-enter="doChmod" />
    </Modal>
  </Page>
</template>

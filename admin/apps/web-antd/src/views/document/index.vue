<script lang="ts" setup>
import type { VxeGridProps } from '#/adapter/vxe-table';
import type { VbenFormProps } from '#/adapter/form';

import { ref } from 'vue';

import { Page } from '@vben/common-ui';

import {
  Tag,
  Space,
  Button,
  Popconfirm,
  Modal,
  message,
  Image,
  Segmented,
  Upload,
  Progress,
  Select,
} from 'ant-design-vue';

import { useVbenVxeGrid } from '#/adapter/vxe-table';
import { useVbenForm } from '#/adapter/form';

import { uploadFileApi } from '#/api/core/admin';

import { getList, getDetail, postEdit, postDel, postAdd } from './api';
import { getList as getProjectList } from '../project/api';
import { useAppConfig } from '@vben/hooks';

const { apiURL } = useAppConfig(import.meta.env, import.meta.env.PROD);

interface RowType {
  id: number;
  title: string;
  project: { name: string };
  category: string;
  filePath: string;
  fileType: string;
  fileSize: number;
  operatorName: string;
  createdTime: string;
}

// ---- 弹窗状态 ----
const editModalVisible = ref(false);
const editId = ref<number>(0);

const uploadModalVisible = ref(false);
const uploadProjectId = ref<number | undefined>(undefined);
const uploadCategory = ref<string>('Inbox');
const projectOptions = ref<{ label: string; value: number }[]>([]);

interface UploadFileItem {
  file: File;
  status: 'pending' | 'uploading' | 'done' | 'error';
  progress: number;
  error?: string;
}
const uploadFiles = ref<UploadFileItem[]>([]);
const isUploading = ref(false);

const activeTab = ref<string>('all');

const categoryOptions = [
  { label: '待分类', value: 'Inbox' },
  { label: '设计图', value: 'Design' },
  { label: 'Logo', value: 'Logo' },
  { label: '修改文稿', value: 'Revision' },
  { label: '算法文档', value: 'Algorithm' },
  { label: '公告文档', value: 'Announcement' },
  { label: '合同', value: 'Contract' },
  { label: '其他', value: 'Other' },
];

const categoryColorMap: Record<string, string> = {
  Inbox: 'orange',
  Design: 'purple',
  Logo: 'magenta',
  Revision: 'blue',
  Algorithm: 'cyan',
  Announcement: 'green',
  Contract: 'gold',
  Other: 'default',
};

const categoryLabelMap: Record<string, string> = Object.fromEntries(
  categoryOptions.map((o) => [o.value, o.label]),
);

const tabOptions = [
  { label: '全部', value: 'all' },
  { label: '待分类', value: 'Inbox' },
  ...categoryOptions.filter((o) => o.value !== 'Inbox'),
];

const imageExts = new Set(['jpg', 'jpeg', 'png', 'gif', 'webp', 'svg', 'bmp']);

function isImage(row: RowType) {
  return imageExts.has((row.fileType || '').toLowerCase());
}

function getFileUrl(row: RowType) {
  if (!row.filePath) return '';
  if (row.filePath.startsWith('http')) return row.filePath;
  const serverBase = apiURL.replace(/\/admin$/, '');
  return `${serverBase}${row.filePath}`;
}

// ---- 列表 ----
const formOptions: VbenFormProps = {
  schema: [
    {
      label: '项目',
      fieldName: 'projectId',
      component: 'ApiSelect',
      componentProps: {
        placeholder: '请选择项目',
        api: getProjectList,
        afterFetch: (data: any) =>
          data.items.map((item: any) => ({
            label: item.name,
            value: item.id,
          })),
        allowClear: true,
        filterOption: true,
        showSearch: true,
      },
    },
    {
      label: '关键词',
      fieldName: 'keyword',
      component: 'Input',
      componentProps: { placeholder: '请输入关键词' },
    },
  ],
  wrapperClass: 'grid-cols-2',
  showCollapseButton: false,
  submitOnChange: false,
  submitOnEnter: true,
};

const gridOptions: VxeGridProps<RowType> = {
  columns: [
    { fixed: 'left', field: 'id', title: 'Id', width: 50 },
    {
      field: 'filePath',
      title: '预览',
      width: 70,
      slots: { default: 'preview' },
    },
    { field: 'title', title: '标题', minWidth: 200 },
    { field: 'project.name', title: '项目', width: 140 },
    {
      field: 'category',
      title: '分类',
      width: 110,
      slots: { default: 'category' },
    },
    { field: 'fileType', title: '类型', width: 80 },
    {
      field: 'fileSize',
      title: '大小',
      width: 100,
      slots: { default: 'fileSize' },
    },
    { field: 'operatorName', title: '来源', width: 100 },
    {
      field: 'createdTime',
      title: '创建时间',
      width: 170,
      slots: { default: 'createdTime' },
    },
    {
      field: 'action',
      fixed: 'right',
      align: 'center',
      slots: { default: 'action' },
      title: '操作',
      width: 180,
    },
  ],
  height: 'auto',
  keepSource: true,
  pagerConfig: {},
  proxyConfig: {
    ajax: {
      query: async ({ page }, formValues) => {
        const category =
          activeTab.value !== 'all' ? activeTab.value : undefined;
        return await getList({
          page: page.currentPage,
          pageSize: page.pageSize,
          category,
          ...formValues,
        });
      },
    },
  },
  showOverflow: false,
  rowConfig: { isHover: true },
  toolbarConfig: { refresh: true, custom: true },
};

const [Grid, gridApi] = useVbenVxeGrid({ formOptions, gridOptions });

// ---- 编辑弹窗 ----
const [EditForm, editFormApi] = useVbenForm({
  commonConfig: {
    colon: true,
    componentProps: { class: 'w-full' },
  },
  handleSubmit: onEditSubmit,
  layout: 'horizontal',
  schema: [
    {
      fieldName: 'projectId',
      component: 'ApiSelect',
      label: '项目',
      rules: 'required',
      componentProps: {
        placeholder: '请选择项目',
        api: getProjectList,
        afterFetch: (data: any) =>
          data.items.map((item: any) => ({
            label: item.name,
            value: item.id,
          })),
        allowClear: true,
        filterOption: true,
        showSearch: true,
      },
    },
    {
      fieldName: 'title',
      component: 'Input',
      label: '标题',
      rules: 'required',
      componentProps: { placeholder: '请输入文档标题' },
    },
    {
      fieldName: 'category',
      component: 'Select',
      label: '分类',
      componentProps: {
        placeholder: '请选择分类',
        options: categoryOptions,
      },
    },
    {
      fieldName: 'content',
      component: 'Textarea',
      label: '内容',
      componentProps: {
        placeholder: '请输入文档内容（Markdown）',
        rows: 6,
      },
    },
  ],
  wrapperClass: 'grid-cols-1',
});

// ---- 上传弹窗（批量） ----
async function handleUpload() {
  uploadFiles.value = [];
  uploadProjectId.value = undefined;
  uploadCategory.value = 'Inbox';
  isUploading.value = false;
  // 加载项目列表
  if (projectOptions.value.length === 0) {
    const data = await getProjectList({ pageSize: 100 });
    projectOptions.value = data.items.map((item: any) => ({
      label: item.name,
      value: item.id,
    }));
  }
  uploadModalVisible.value = true;
}

function handleBeforeUpload(file: File) {
  uploadFiles.value.push({
    file,
    status: 'pending',
    progress: 0,
  });
  return false; // 阻止自动上传
}

function removeFile(index: number) {
  uploadFiles.value.splice(index, 1);
}

async function startBatchUpload() {
  if (!uploadProjectId.value) {
    message.warning('请先选择项目');
    return;
  }
  if (uploadFiles.value.length === 0) {
    message.warning('请先添加文件');
    return;
  }

  isUploading.value = true;
  let successCount = 0;

  for (const item of uploadFiles.value) {
    if (item.status === 'done') {
      successCount++;
      continue;
    }
    item.status = 'uploading';
    item.progress = 30;
    try {
      // 上传文件
      const result: any = await uploadFileApi(item.file);
      item.progress = 70;

      // 创建文档记录
      const ext = item.file.name.split('.').pop()?.toLowerCase() || '';
      await postAdd({
        projectId: uploadProjectId.value,
        title: item.file.name,
        category: uploadCategory.value,
        filePath: result.fileName,
        fileSize: item.file.size,
        fileType: ext,
      });

      item.status = 'done';
      item.progress = 100;
      successCount++;
    } catch (e: any) {
      item.status = 'error';
      item.error = e?.message || '上传失败';
    }
  }

  isUploading.value = false;
  if (successCount > 0) {
    message.success(`成功上传 ${successCount} 个文件`);
    gridApi.query();
  }
}

// ---- 编辑 ----
async function handleEdit(id: number) {
  editId.value = id;
  editFormApi.resetForm();
  const detail = await getDetail(id);
  editFormApi.setValues(detail);
  editModalVisible.value = true;
}

function handleDel(id: number) {
  postDel(id).then(() => {
    message.success('删除成功');
    gridApi.query();
  });
}

async function handleClassify(row: RowType, category: string) {
  await postEdit({ id: row.id, title: row.title, category });
  message.success('分类成功');
  gridApi.query();
}

function handlePreview(row: RowType) {
  const url = getFileUrl(row);
  if (url) window.open(url, '_blank');
}

function onEditSubmit(values: Record<string, any>) {
  values.id = editId.value;
  postEdit(values).then(() => {
    message.success('保存成功');
    editModalVisible.value = false;
    gridApi.query();
  });
}

function onTabChange(val: string | number) {
  activeTab.value = val as string;
  gridApi.query();
}

function formatSize(bytes: number) {
  if (!bytes && bytes !== 0) return '-';
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  if (bytes < 1024 * 1024 * 1024)
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  return `${(bytes / (1024 * 1024 * 1024)).toFixed(2)} GB`;
}

function formatDateTime(dateStr: string) {
  if (!dateStr) return '-';
  return new Date(dateStr).toLocaleString('zh-CN');
}

function getFileIcon(name: string) {
  const ext = name.split('.').pop()?.toLowerCase() || '';
  if (imageExts.has(ext)) return '🖼️';
  if (['pdf'].includes(ext)) return '📄';
  if (['doc', 'docx'].includes(ext)) return '📝';
  if (['xls', 'xlsx'].includes(ext)) return '📊';
  if (['zip', 'rar', '7z'].includes(ext)) return '📦';
  if (['mp4', 'mov', 'avi'].includes(ext)) return '🎬';
  return '📎';
}
</script>

<template>
  <Page auto-content-height>
    <Grid>
      <template #toolbar-tools>
        <Space>
          <Segmented
            :value="activeTab"
            :options="tabOptions"
            size="small"
            @change="onTabChange"
          />
          <Button type="primary" @click="handleUpload">上传资源</Button>
        </Space>
      </template>

      <template #preview="{ row }">
        <Image
          v-if="isImage(row) && row.filePath"
          :src="getFileUrl(row)"
          :width="40"
          :height="40"
          style=" cursor: pointer;object-fit: cover; border-radius: 4px"
          :preview="{ src: getFileUrl(row) }"
        />
        <span
          v-else-if="row.filePath"
          style="
            display: inline-flex;
            align-items: center;
            justify-content: center;
            width: 40px;
            height: 40px;
            font-size: 11px;
            color: #999;
            cursor: pointer;
            background: #f5f5f5;
            border-radius: 4px;
          "
          @click="handlePreview(row)"
        >
          {{ (row.fileType || 'FILE').toUpperCase() }}
        </span>
        <span v-else style="color: #ccc">-</span>
      </template>

      <template #category="{ row }">
        <Tag :color="categoryColorMap[row.category] || 'default'">
          {{ categoryLabelMap[row.category] || row.category || '-' }}
        </Tag>
      </template>

      <template #fileSize="{ row }">
        {{ formatSize(row.fileSize) }}
      </template>

      <template #createdTime="{ row }">
        {{ formatDateTime(row.createdTime) }}
      </template>

      <template #action="{ row }">
        <Space wrap>
          <Button
            v-if="row.filePath"
            type="link"
            @click="handlePreview(row)"
          >
            预览
          </Button>
          <Popconfirm
            v-if="row.category === 'Inbox'"
            title="选择分类"
            :show-cancel="false"
            ok-text="关闭"
          >
            <template #description>
              <Space wrap style="max-width: 280px; padding: 8px 0">
                <Button
                  v-for="opt in categoryOptions.filter(
                    (o) => o.value !== 'Inbox',
                  )"
                  :key="opt.value"
                  size="small"
                  @click="handleClassify(row, opt.value)"
                >
                  {{ opt.label }}
                </Button>
              </Space>
            </template>
            <Button type="primary">分类</Button>
          </Popconfirm>
          <Button @click="handleEdit(row.id)">编辑</Button>
          <Popconfirm
            title="确定要删除该文档吗？"
            @confirm="handleDel(row.id)"
          >
            <Button danger>删除</Button>
          </Popconfirm>
        </Space>
      </template>
    </Grid>

    <!-- 批量上传弹窗 -->
    <Modal
      v-model:open="uploadModalVisible"
      title="上传资源"
      :footer="null"
      width="600px"
      :mask-closable="!isUploading"
      :closable="!isUploading"
    >
      <!-- 项目 & 分类 -->
      <div style="display: flex; gap: 12px; margin-bottom: 16px">
        <Select
          v-model:value="uploadProjectId"
          placeholder="请选择项目"
          :options="projectOptions"
          style="flex: 1"
          show-search
          :filter-option="
            (input: string, option: any) =>
              option.label.toLowerCase().includes(input.toLowerCase())
          "
          :disabled="isUploading"
        />
        <Select
          v-model:value="uploadCategory"
          placeholder="分类"
          :options="categoryOptions"
          style="width: 140px"
          :disabled="isUploading"
        />
      </div>

      <!-- 拖拽上传区 -->
      <Upload.Dragger
        :before-upload="handleBeforeUpload"
        :show-upload-list="false"
        :multiple="true"
        :disabled="isUploading"
      >
        <div style="padding: 16px 0">
          <p style="font-size: 36px; color: #1890ff">+</p>
          <p style="font-size: 14px; color: rgb(0 0 0 / 85%)">
            点击或拖拽文件到此区域（支持批量）
          </p>
        </div>
      </Upload.Dragger>

      <!-- 文件列表 -->
      <div
        v-if="uploadFiles.length > 0"
        style="
          max-height: 300px;
          margin-top: 16px;
          overflow-y: auto;
          border: 1px solid #f0f0f0;
          border-radius: 6px;
        "
      >
        <div
          v-for="(item, idx) in uploadFiles"
          :key="idx"
          style="
            display: flex;
            gap: 8px;
            align-items: center;
            padding: 8px 12px;
            border-bottom: 1px solid #f5f5f5;
          "
        >
          <span style="font-size: 18px">{{ getFileIcon(item.file.name) }}</span>
          <div style="flex: 1; min-width: 0">
            <div
              style="
                overflow: hidden;
                text-overflow: ellipsis;
                font-size: 13px;
                white-space: nowrap;
              "
            >
              {{ item.file.name }}
            </div>
            <div style="font-size: 11px; color: #999">
              {{ formatSize(item.file.size) }}
            </div>
            <Progress
              v-if="item.status === 'uploading'"
              :percent="item.progress"
              size="small"
              :show-info="false"
              style="margin: 2px 0 0"
            />
          </div>
          <Tag v-if="item.status === 'done'" color="success">完成</Tag>
          <Tag v-else-if="item.status === 'error'" color="error">
            {{ item.error || '失败' }}
          </Tag>
          <Tag v-else-if="item.status === 'uploading'" color="processing">
            上传中
          </Tag>
          <Button
            v-if="item.status === 'pending'"
            type="text"
            size="small"
            danger
            @click="removeFile(idx)"
          >
            移除
          </Button>
        </div>
      </div>

      <!-- 操作按钮 -->
      <div
        v-if="uploadFiles.length > 0"
        style=" display: flex; gap: 8px; justify-content: flex-end;margin-top: 16px"
      >
        <Button
          v-if="!isUploading"
          @click="uploadFiles = []"
        >
          清空列表
        </Button>
        <Button
          type="primary"
          :loading="isUploading"
          :disabled="uploadFiles.every((f) => f.status === 'done')"
          @click="startBatchUpload"
        >
          {{
            isUploading
              ? '上传中...'
              : `开始上传（${uploadFiles.filter((f) => f.status !== 'done').length} 个文件）`
          }}
        </Button>
      </div>
    </Modal>

    <!-- 编辑弹窗 -->
    <Modal
      v-model:open="editModalVisible"
      title="编辑文档"
      :footer="null"
      :destroy-on-close="false"
      width="700px"
    >
      <EditForm />
    </Modal>
  </Page>
</template>

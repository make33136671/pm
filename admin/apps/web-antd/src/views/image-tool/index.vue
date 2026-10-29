<script lang="ts" setup>
import type { UploadProps } from 'ant-design-vue';

import { computed, onBeforeUnmount, reactive, ref } from 'vue';

import { Page } from '@vben/common-ui';

import {
  Button,
  Card,
  Checkbox,
  Empty,
  Input,
  InputNumber,
  message,
  Select,
  Slider,
  Space,
  Tabs,
  Tag,
  Upload,
} from 'ant-design-vue';

import {
  convert,
  downloadBlob,
  favicon,
  multiResolution,
  resize,
} from './api';

// ---------------- 上传与预览 ----------------

const selectedFile = ref<File | null>(null);
const previewUrl = ref('');
const imageInfo = reactive({ width: 0, height: 0 });

/** 拦截上传，只保存文件本身、不自动上传 */
const beforeUpload: UploadProps['beforeUpload'] = (file) => {
  if (!file.type?.startsWith('image/')) {
    message.error('请选择图片文件');
    return Upload.LIST_IGNORE;
  }
  setFile(file as unknown as File);
  return false;
};

function setFile(file: File) {
  if (previewUrl.value) URL.revokeObjectURL(previewUrl.value);
  selectedFile.value = file;
  previewUrl.value = URL.createObjectURL(file);
  imageInfo.width = 0;
  imageInfo.height = 0;
  const img = new Image();
  img.addEventListener('load', () => {
    imageInfo.width = img.naturalWidth;
    imageInfo.height = img.naturalHeight;
  });
  img.src = previewUrl.value;
}

function clearFile() {
  if (previewUrl.value) URL.revokeObjectURL(previewUrl.value);
  selectedFile.value = null;
  previewUrl.value = '';
  imageInfo.width = 0;
  imageInfo.height = 0;
}

const fileSizeText = computed(() => {
  const f = selectedFile.value;
  if (!f) return '';
  if (f.size < 1024) return `${f.size} B`;
  if (f.size < 1024 ** 2) return `${(f.size / 1024).toFixed(1)} KB`;
  return `${(f.size / 1024 ** 2).toFixed(2)} MB`;
});

const hasFile = computed(() => !!selectedFile.value);

onBeforeUnmount(() => {
  if (previewUrl.value) URL.revokeObjectURL(previewUrl.value);
});

// ---------------- 公共选项 ----------------

const FORMAT_OPTIONS = [
  { label: 'PNG', value: 'png' },
  { label: 'JPG', value: 'jpg' },
  { label: 'WebP', value: 'webp' },
  { label: 'BMP', value: 'bmp' },
  { label: 'GIF', value: 'gif' },
];

const FIT_OPTIONS = [
  { label: '留白 (contain)', value: 'contain' },
  { label: '裁剪 (cover)', value: 'cover' },
  { label: '拉伸 (stretch)', value: 'stretch' },
];

/** 包一层：执行 blob 下载，统一 loading + 成功/失败提示 */
async function runDownload(
  loadingRef: { value: boolean },
  task: () => Promise<Blob>,
  filename: string,
) {
  if (!selectedFile.value) return;
  loadingRef.value = true;
  try {
    const blob = await task();
    downloadBlob(blob, filename);
    // blob 下载不走 api 拦截器的成功 toast，这里手动提示
    message.success('已下载');
  } catch (error: any) {
    message.error(error?.message || '处理失败');
  } finally {
    loadingRef.value = false;
  }
}

/** 去掉原文件扩展名，便于拼输出文件名 */
const baseName = computed(() => {
  const name = selectedFile.value?.name ?? 'image';
  return name.replace(/\.[^./\\]+$/, '');
});

// ---------------- 多分辨率 ----------------

const mr = reactive({
  sizes: ['16', '32', '64', '128', '256', '512'] as string[],
  inputValue: '',
  format: 'png',
  quality: 90,
  fit: 'contain',
  background: '',
});
const mrLoading = ref(false);

function mrAddSize() {
  const v = mr.inputValue.trim();
  if (v && /^\d+$/.test(v) && !mr.sizes.includes(v)) {
    mr.sizes.push(v);
  }
  mr.inputValue = '';
}
function mrRemoveSize(s: string) {
  mr.sizes = mr.sizes.filter((x) => x !== s);
}

async function runMultiResolution() {
  if (mr.sizes.length === 0) {
    message.error('请至少添加一个尺寸');
    return;
  }
  await runDownload(
    mrLoading,
    () =>
      multiResolution(selectedFile.value as File, {
        sizes: mr.sizes.join(','),
        format: mr.format,
        quality: mr.quality,
        fit: mr.fit,
        background: mr.background || undefined,
      }),
    `${baseName.value}_multi.zip`,
  );
}

// ---------------- 格式转换 ----------------

const cv = reactive({
  format: 'png',
  quality: 90,
  width: undefined as number | undefined,
  height: undefined as number | undefined,
  fit: 'contain',
  background: '',
});
const cvLoading = ref(false);

async function runConvert() {
  await runDownload(
    cvLoading,
    () =>
      convert(selectedFile.value as File, {
        format: cv.format,
        quality: cv.quality,
        width: cv.width,
        height: cv.height,
        fit: cv.fit,
        background: cv.background || undefined,
      }),
    `${baseName.value}.${cv.format}`,
  );
}

// ---------------- Favicon ----------------

const FAVICON_SIZE_OPTIONS = ['16', '32', '48', '64', '128', '256'];
const fav = reactive({
  sizes: [...FAVICON_SIZE_OPTIONS] as string[],
});
const favLoading = ref(false);

async function runFavicon() {
  if (fav.sizes.length === 0) {
    message.error('请至少选择一个尺寸');
    return;
  }
  await runDownload(
    favLoading,
    () => favicon(selectedFile.value as File, fav.sizes.join(',')),
    'favicon.ico',
  );
}

// ---------------- 缩放裁剪 ----------------

const rz = reactive({
  width: 256 as number | undefined,
  height: 256 as number | undefined,
  fit: 'contain',
  background: '',
  format: 'png',
  quality: 90,
});
const rzLoading = ref(false);

async function runResize() {
  if (!rz.width || !rz.height) {
    message.error('请输入宽和高');
    return;
  }
  await runDownload(
    rzLoading,
    () =>
      resize(selectedFile.value as File, {
        width: rz.width as number,
        height: rz.height as number,
        fit: rz.fit,
        background: rz.background || undefined,
        format: rz.format,
        quality: rz.quality,
      }),
    `${baseName.value}_${rz.width}x${rz.height}.${rz.format}`,
  );
}
</script>

<template>
  <Page>
    <!-- 上传区 -->
    <Card class="mb-4" :body-style="{ padding: '16px' }">
      <Upload.Dragger
        v-if="!hasFile"
        accept="image/*"
        :show-upload-list="false"
        :before-upload="beforeUpload"
      >
        <p class="upload-hint-icon">+</p>
        <p class="upload-hint-text">点击或拖拽图片到此处</p>
        <p class="upload-hint-sub">支持 PNG / JPG / WebP / BMP / GIF</p>
      </Upload.Dragger>

      <div v-else class="preview-row">
        <div class="preview-thumb">
          <img :src="previewUrl" alt="预览" />
        </div>
        <div class="preview-meta">
          <div class="preview-name">{{ selectedFile?.name }}</div>
          <div class="preview-sub">
            <span v-if="imageInfo.width">
              {{ imageInfo.width }} × {{ imageInfo.height }} px ·
            </span>
            {{ fileSizeText }}
          </div>
          <Space class="mt-2">
            <Upload
              accept="image/*"
              :show-upload-list="false"
              :before-upload="beforeUpload"
            >
              <Button type="default" size="small">更换图片</Button>
            </Upload>
            <Button type="default" size="small" danger @click="clearFile">
              移除
            </Button>
          </Space>
        </div>
      </div>
    </Card>

    <!-- 功能页签 -->
    <Card :body-style="{ padding: '16px' }">
      <Empty
        v-if="!hasFile"
        description="请先选择一张图片"
        class="empty-tip"
      />

      <Tabs v-show="hasFile">
        <!-- 多分辨率 -->
        <Tabs.TabPane key="multi" tab="多分辨率">
          <div class="form-grid">
            <div class="form-item form-item-full">
              <label class="form-label">尺寸（像素）</label>
              <Space wrap class="size-tags">
                <Tag
                  v-for="s in mr.sizes"
                  :key="s"
                  closable
                  color="processing"
                  @close="mrRemoveSize(s)"
                >
                  {{ s }}
                </Tag>
                <Input
                  v-model:value="mr.inputValue"
                  class="size-input"
                  size="small"
                  placeholder="新增"
                  @press-enter="mrAddSize"
                  @blur="mrAddSize"
                />
              </Space>
            </div>

            <div class="form-item">
              <label class="form-label">输出格式</label>
              <Select v-model:value="mr.format" :options="FORMAT_OPTIONS" />
            </div>

            <div class="form-item">
              <label class="form-label">适配方式</label>
              <Select v-model:value="mr.fit" :options="FIT_OPTIONS" />
            </div>

            <div class="form-item">
              <label class="form-label">质量：{{ mr.quality }}</label>
              <Slider v-model:value="mr.quality" :min="1" :max="100" />
            </div>

            <div class="form-item">
              <label class="form-label">背景色（留空透明）</label>
              <div class="color-row">
                <input
                  type="color"
                  class="color-swatch"
                  :value="mr.background || '#ffffff'"
                  @input="mr.background = ($event.target as HTMLInputElement).value"
                />
                <Input
                  v-model:value="mr.background"
                  placeholder="留空 = 透明"
                  allow-clear
                />
              </div>
            </div>
          </div>

          <Button
            type="primary"
            class="mt-4"
            :disabled="!hasFile"
            :loading="mrLoading"
            @click="runMultiResolution"
          >
            生成并下载 (zip)
          </Button>
        </Tabs.TabPane>

        <!-- 格式转换 -->
        <Tabs.TabPane key="convert" tab="格式转换">
          <div class="form-grid">
            <div class="form-item">
              <label class="form-label">目标格式</label>
              <Select v-model:value="cv.format" :options="FORMAT_OPTIONS" />
            </div>

            <div class="form-item">
              <label class="form-label">适配方式</label>
              <Select v-model:value="cv.fit" :options="FIT_OPTIONS" />
            </div>

            <div class="form-item">
              <label class="form-label">宽度（可选）</label>
              <InputNumber
                v-model:value="cv.width"
                class="full-w"
                :min="1"
                placeholder="原始宽度"
              />
            </div>

            <div class="form-item">
              <label class="form-label">高度（可选）</label>
              <InputNumber
                v-model:value="cv.height"
                class="full-w"
                :min="1"
                placeholder="原始高度"
              />
            </div>

            <div class="form-item">
              <label class="form-label">质量：{{ cv.quality }}</label>
              <Slider v-model:value="cv.quality" :min="1" :max="100" />
            </div>

            <div class="form-item">
              <label class="form-label">背景色（留空透明）</label>
              <div class="color-row">
                <input
                  type="color"
                  class="color-swatch"
                  :value="cv.background || '#ffffff'"
                  @input="cv.background = ($event.target as HTMLInputElement).value"
                />
                <Input
                  v-model:value="cv.background"
                  placeholder="留空 = 透明"
                  allow-clear
                />
              </div>
            </div>
          </div>

          <Button
            type="primary"
            class="mt-4"
            :disabled="!hasFile"
            :loading="cvLoading"
            @click="runConvert"
          >
            转换并下载
          </Button>
        </Tabs.TabPane>

        <!-- Favicon -->
        <Tabs.TabPane key="favicon" tab="生成 Favicon">
          <div class="form-item form-item-full">
            <label class="form-label">包含尺寸</label>
            <Checkbox.Group
              v-model:value="fav.sizes"
              :options="
                FAVICON_SIZE_OPTIONS.map((s) => ({ label: `${s}px`, value: s }))
              "
            />
          </div>

          <Button
            type="primary"
            class="mt-4"
            :disabled="!hasFile"
            :loading="favLoading"
            @click="runFavicon"
          >
            生成并下载 favicon.ico
          </Button>
        </Tabs.TabPane>

        <!-- 缩放裁剪 -->
        <Tabs.TabPane key="resize" tab="缩放裁剪">
          <div class="form-grid">
            <div class="form-item">
              <label class="form-label">宽度</label>
              <InputNumber
                v-model:value="rz.width"
                class="full-w"
                :min="1"
                placeholder="宽度"
              />
            </div>

            <div class="form-item">
              <label class="form-label">高度</label>
              <InputNumber
                v-model:value="rz.height"
                class="full-w"
                :min="1"
                placeholder="高度"
              />
            </div>

            <div class="form-item">
              <label class="form-label">适配方式</label>
              <Select v-model:value="rz.fit" :options="FIT_OPTIONS" />
            </div>

            <div class="form-item">
              <label class="form-label">输出格式</label>
              <Select v-model:value="rz.format" :options="FORMAT_OPTIONS" />
            </div>

            <div class="form-item">
              <label class="form-label">质量：{{ rz.quality }}</label>
              <Slider v-model:value="rz.quality" :min="1" :max="100" />
            </div>

            <div class="form-item">
              <label class="form-label">背景色（留空透明）</label>
              <div class="color-row">
                <input
                  type="color"
                  class="color-swatch"
                  :value="rz.background || '#ffffff'"
                  @input="rz.background = ($event.target as HTMLInputElement).value"
                />
                <Input
                  v-model:value="rz.background"
                  placeholder="留空 = 透明"
                  allow-clear
                />
              </div>
            </div>
          </div>

          <Button
            type="primary"
            class="mt-4"
            :disabled="!hasFile"
            :loading="rzLoading"
            @click="runResize"
          >
            处理并下载
          </Button>
        </Tabs.TabPane>
      </Tabs>
    </Card>
  </Page>
</template>

<style scoped>
.mb-4 {
  margin-bottom: 16px;
}
.mt-2 {
  margin-top: 8px;
}
.mt-4 {
  margin-top: 16px;
}

.upload-hint-icon {
  font-size: 40px;
  font-weight: 300;
  line-height: 1;
  color: var(--ant-color-primary);
}
.upload-hint-text {
  margin-top: 8px;
  font-size: 15px;
  color: var(--ant-color-text);
}
.upload-hint-sub {
  margin-top: 4px;
  font-size: 12px;
  color: var(--ant-color-text-tertiary);
}

.preview-row {
  display: flex;
  gap: 16px;
  align-items: center;
}
.preview-thumb {
  display: flex;
  flex: none;
  align-items: center;
  justify-content: center;
  width: 88px;
  height: 88px;
  overflow: hidden;
  background: var(--ant-color-fill-tertiary);
  border: 1px solid var(--ant-color-border-secondary);
  border-radius: 8px;
}
.preview-thumb img {
  max-width: 100%;
  max-height: 100%;
  object-fit: contain;
}
.preview-meta {
  min-width: 0;
}
.preview-name {
  overflow: hidden;
  font-size: 14px;
  font-weight: 500;
  color: var(--ant-color-text);
  text-overflow: ellipsis;
  white-space: nowrap;
}
.preview-sub {
  margin-top: 4px;
  font-size: 12px;
  color: var(--ant-color-text-secondary);
}

.empty-tip {
  padding: 40px 0;
}

.form-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 16px 24px;
}
.form-item {
  display: flex;
  flex-direction: column;
}
.form-item-full {
  grid-column: 1 / -1;
}
.form-label {
  margin-bottom: 6px;
  font-size: 13px;
  color: var(--ant-color-text-secondary);
}

.full-w {
  width: 100%;
}

.size-tags {
  width: 100%;
}
.size-input {
  width: 72px;
}

.color-row {
  display: flex;
  gap: 8px;
  align-items: center;
}
.color-swatch {
  flex: none;
  width: 36px;
  height: 32px;
  padding: 2px;
  cursor: pointer;
  background: var(--ant-color-bg-container);
  border: 1px solid var(--ant-color-border);
  border-radius: 6px;
}
</style>

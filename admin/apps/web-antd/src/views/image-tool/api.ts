import { useAppConfig } from '@vben/hooks';
import { useAccessStore } from '@vben/stores';

const { apiURL } = useAppConfig(import.meta.env, import.meta.env.PROD);

/**
 * 后端图片处理接口返回的是二进制文件流（zip / 图片 / favicon），
 * 不走统一 api 包装器（那一套会按 JSON 解包 body.code，blob 会被误判为失败）。
 * 这里直接用 fetch 携带 token 拿 Blob，与项目里 database / backup-receive 的下载实现保持一致。
 */
async function postForBlob(
  path: string,
  formData: FormData,
): Promise<Blob> {
  const accessStore = useAccessStore();
  const token = accessStore.accessToken;
  const resp = await fetch(`${apiURL}${path}`, {
    method: 'POST',
    headers: token ? { Authorization: `Bearer ${token}` } : {},
    body: formData,
  });

  if (!resp.ok) {
    // 失败时后端通常返回 JSON 错误体，尽量读出 message 抛给调用方
    let msg = `请求失败 (HTTP ${resp.status})`;
    try {
      const data = await resp.clone().json();
      msg = data?.error ?? data?.message ?? msg;
    } catch {
      // 非 JSON 错误体，保留默认 msg
    }
    throw new Error(msg);
  }

  return resp.blob();
}

/** 触发浏览器下载一个 Blob */
export function downloadBlob(blob: Blob, filename: string) {
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = filename;
  document.body.append(a);
  a.click();
  a.remove();
  URL.revokeObjectURL(url);
}

export interface MultiResolutionParams {
  /** 逗号分隔的尺寸，如 "16,32,64" */
  sizes: string;
  /** png/jpg/webp/bmp/gif */
  format: string;
  /** 1-100 */
  quality: number;
  /** contain/cover/stretch */
  fit: string;
  /** 颜色 如 #ffffff，留空表示透明背景 */
  background?: string;
}

/** 多分辨率生成，返回 zip */
export async function multiResolution(
  file: File,
  params: MultiResolutionParams,
): Promise<Blob> {
  const fd = new FormData();
  fd.append('file', file);
  fd.append('sizes', params.sizes);
  fd.append('format', params.format);
  fd.append('quality', String(params.quality));
  fd.append('fit', params.fit);
  if (params.background) fd.append('background', params.background);
  return postForBlob('/ImageProcessing/MultiResolution', fd);
}

export interface ConvertParams {
  format: string;
  quality: number;
  width?: number;
  height?: number;
  fit: string;
  background?: string;
}

/** 格式转换，返回转换后的图片 */
export async function convert(
  file: File,
  params: ConvertParams,
): Promise<Blob> {
  const fd = new FormData();
  fd.append('file', file);
  fd.append('format', params.format);
  fd.append('quality', String(params.quality));
  if (params.width) fd.append('width', String(params.width));
  if (params.height) fd.append('height', String(params.height));
  fd.append('fit', params.fit);
  if (params.background) fd.append('background', params.background);
  return postForBlob('/ImageProcessing/Convert', fd);
}

/** 生成 favicon.ico，sizes 可选（逗号分隔） */
export async function favicon(file: File, sizes?: string): Promise<Blob> {
  const fd = new FormData();
  fd.append('file', file);
  if (sizes) fd.append('sizes', sizes);
  return postForBlob('/ImageProcessing/Favicon', fd);
}

export interface ResizeParams {
  width: number;
  height: number;
  fit: string;
  background?: string;
  format: string;
  quality: number;
}

/** 缩放裁剪，返回处理后图片 */
export async function resize(
  file: File,
  params: ResizeParams,
): Promise<Blob> {
  const fd = new FormData();
  fd.append('file', file);
  fd.append('width', String(params.width));
  fd.append('height', String(params.height));
  fd.append('fit', params.fit);
  if (params.background) fd.append('background', params.background);
  fd.append('format', params.format);
  fd.append('quality', String(params.quality));
  return postForBlob('/ImageProcessing/Resize', fd);
}

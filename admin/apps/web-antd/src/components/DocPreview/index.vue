<script lang="ts" setup>
import { ref } from 'vue';
import { Modal, Drawer, Button, Space, Tag, Timeline, message, Popconfirm } from 'ant-design-vue';
import DocRenderer from './DocRenderer.vue';
import { getDocLatest, getDocVersions, getDocDetail, postDocRollback } from './api';

const props = defineProps<{
  sourceType: string;
}>();

// 预览
const previewVisible = ref(false);
const previewRawContent = ref('');
const previewTitle = ref('');
const previewSourceId = ref(0);

async function openPreview(sourceId: number, title: string) {
  previewTitle.value = title;
  previewSourceId.value = sourceId;
  try {
    const doc: any = await getDocLatest(props.sourceType, sourceId);
    previewRawContent.value = doc?.content || '';
    previewVisible.value = true;
  } catch {
    message.warning('暂无文档');
  }
}

// 公开链接
function copyLink(sourceId: number) {
  const url = `${window.location.origin}/api/doc/${props.sourceType}/${sourceId}`;
  navigator.clipboard.writeText(url).then(() => {
    message.success('文档链接已复制');
  }).catch(() => {
    const input = document.createElement('input');
    input.value = url;
    document.body.appendChild(input);
    input.select();
    document.execCommand('copy');
    document.body.removeChild(input);
    message.success('文档链接已复制');
  });
}

// 下载
function doDownload(content: string, title: string) {
  const blob = new Blob([content], { type: 'text/markdown;charset=utf-8' });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = `${title}.md`;
  a.click();
  URL.revokeObjectURL(url);
}

async function downloadDirect(sourceId: number, title: string) {
  try {
    const doc: any = await getDocLatest(props.sourceType, sourceId);
    doDownload(doc?.content || '暂无内容', title);
  } catch {
    message.warning('暂无文档');
  }
}

// 版本历史
const historyVisible = ref(false);
const historyList = ref<any[]>([]);
const historySourceId = ref(0);
const historyTitle = ref('');
const versionContent = ref('');
const versionViewVisible = ref(false);
const versionViewTitle = ref('');

async function openHistory(sourceId: number, title: string) {
  historySourceId.value = sourceId;
  historyTitle.value = title;
  const list: any = await getDocVersions(props.sourceType, sourceId);
  historyList.value = Array.isArray(list) ? list : [];
  historyVisible.value = true;
}

async function viewVersion(id: number, version: number) {
  const doc: any = await getDocDetail(id);
  versionContent.value = doc?.content || '';
  versionViewTitle.value = `v${version}`;
  versionViewVisible.value = true;
}

async function rollback(versionId: number) {
  await postDocRollback({
    sourceType: props.sourceType,
    sourceId: historySourceId.value,
    versionId,
  });
  message.success('回档成功');
  historyVisible.value = false;
}

defineExpose({ openPreview, download: downloadDirect, openHistory, copyLink });
</script>

<template>
  <!-- 预览弹窗 -->
  <Modal v-model:open="previewVisible" :title="previewTitle" width="820px" :footer="null">
    <div style="max-height: 70vh; padding: 4px 0; overflow-y: auto;">
      <DocRenderer :content="previewRawContent" :source-type="sourceType" />
    </div>
    <div style=" display: flex; gap: 8px; justify-content: flex-end;margin-top: 14px;">
      <Button @click="copyLink(previewSourceId)">复制链接</Button>
      <Button @click="doDownload(previewRawContent, previewTitle)">下载 MD</Button>
      <Button @click="previewVisible = false">关闭</Button>
    </div>
  </Modal>

  <!-- 版本历史抽屉 -->
  <Drawer v-model:open="historyVisible" :title="`${historyTitle} - 历史版本`" :width="480" placement="right">
    <Timeline>
      <Timeline.Item v-for="v in historyList" :key="v.id">
        <div style="display: flex; align-items: center; justify-content: space-between;">
          <div>
            <Tag color="blue">v{{ v.version }}</Tag>
            <span style="font-size: 12px; color: #999;">{{ v.createdTime }}</span>
          </div>
          <Space size="small">
            <Button size="small" @click="viewVersion(v.id, v.version)">查看</Button>
            <Popconfirm title="确定回档到此版本？" @confirm="rollback(v.id)">
              <Button size="small" type="primary" ghost>回档</Button>
            </Popconfirm>
          </Space>
        </div>
        <div style=" margin-top: 2px;font-size: 12px; color: #666;">
          {{ v.changeNote || '-' }} · {{ v.operator || '-' }}
        </div>
      </Timeline.Item>
    </Timeline>
    <div v-if="historyList.length === 0" style=" padding: 40px 0; color: #999;text-align: center;">暂无版本记录</div>
  </Drawer>

  <!-- 版本内容查看 — 和预览用同一个 DocRenderer -->
  <Modal v-model:open="versionViewVisible" :title="versionViewTitle" width="820px" :footer="null">
    <div style="max-height: 70vh; padding: 4px 0; overflow-y: auto;">
      <DocRenderer :content="versionContent" :source-type="sourceType" />
    </div>
  </Modal>
</template>

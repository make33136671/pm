<script lang="ts" setup>
import { ref, onMounted, computed } from 'vue';
import { VueFlow, Handle, Position } from '@vue-flow/core';
import '@vue-flow/core/dist/style.css';
import '@vue-flow/core/dist/theme-default.css';

import { Spin, Tag, Button, message } from 'ant-design-vue';
import html2canvas from 'html2canvas';
import { getBatchDetail } from '../api';

const props = defineProps<{ batchId: number }>();
const emit = defineEmits<{ close: [] }>();

const loading = ref(true);
const detail = ref<any>(null);

const reqStatusLabels: Record<string, string> = {
  Draft: '草稿', PendingConfirm: '待确认', Confirmed: '已确认', Rejected: '已拒绝',
};
const reqStatusColors: Record<string, string> = {
  Draft: 'default', PendingConfirm: 'orange', Confirmed: 'green', Rejected: 'red',
};

// ============ 高亮交互 ============
const hoveredMsgId = ref<number | null>(null);
const hoveredReqId = ref<number | null>(null);
// 点击锁定：类型 + id
const clickedType = ref<'msg' | 'req' | null>(null);
const clickedId = ref<number | null>(null);

function onClickMsg(id: number) {
  if (clickedType.value === 'msg' && clickedId.value === id) {
    clickedType.value = null; clickedId.value = null; // 再次点击取消
  } else {
    clickedType.value = 'msg'; clickedId.value = id;
  }
}
function onClickReq(id: number) {
  if (clickedType.value === 'req' && clickedId.value === id) {
    clickedType.value = null; clickedId.value = null;
  } else {
    clickedType.value = 'req'; clickedId.value = id;
  }
}

// 构建关联索引：msg→reqs, req→msgs
const reqMsgIndex = computed(() => {
  const msg2reqs: Record<number, number[]> = {};
  const req2msgs: Record<number, number[]> = {};
  if (!detail.value) return { msg2reqs, req2msgs };
  for (const req of detail.value.chatRequirements || []) {
    if (!req.sourceMessageIds) continue;
    let ids: number[] = [];
    try { ids = JSON.parse(req.sourceMessageIds); } catch { continue; }
    req2msgs[req.id] = ids;
    for (const mid of ids) {
      (msg2reqs[mid] ||= []).push(req.id);
    }
  }
  return { msg2reqs, req2msgs };
});

// BFS 找所有间接关联节点
function bfsConnected(startType: 'msg' | 'req', startId: number): { msgs: Set<number>; reqs: Set<number> } {
  const { msg2reqs, req2msgs } = reqMsgIndex.value;
  const visitedMsgs = new Set<number>();
  const visitedReqs = new Set<number>();
  const queue: Array<{ type: 'msg' | 'req'; id: number }> = [{ type: startType, id: startId }];

  while (queue.length > 0) {
    const { type, id } = queue.shift()!;
    if (type === 'msg') {
      if (visitedMsgs.has(id)) continue;
      visitedMsgs.add(id);
      for (const rid of msg2reqs[id] || []) {
        if (!visitedReqs.has(rid)) queue.push({ type: 'req', id: rid });
      }
    } else {
      if (visitedReqs.has(id)) continue;
      visitedReqs.add(id);
      for (const mid of req2msgs[id] || []) {
        if (!visitedMsgs.has(mid)) queue.push({ type: 'msg', id: mid });
      }
    }
  }
  return { msgs: visitedMsgs, reqs: visitedReqs };
}

// 活跃的 seed：优先 click，其次 hover
const activeSeed = computed<{ type: 'msg' | 'req'; id: number } | null>(() => {
  // 点击锁定优先，锁定时忽略 hover
  if (clickedType.value && clickedId.value != null) return { type: clickedType.value, id: clickedId.value };
  if (hoveredMsgId.value != null) return { type: 'msg', id: hoveredMsgId.value };
  if (hoveredReqId.value != null) return { type: 'req', id: hoveredReqId.value };
  return null;
});

const connectedResult = computed(() => {
  if (!activeSeed.value) return null;
  return bfsConnected(activeSeed.value.type, activeSeed.value.id);
});

const highlightedMsgIds = computed(() => connectedResult.value?.msgs ?? new Set<number>());
const highlightedReqIds = computed(() => connectedResult.value?.reqs ?? new Set<number>());

const highlightedEdgeKeys = computed(() => {
  const keys = new Set<string>();
  if (!connectedResult.value) return keys;
  const { msg2reqs } = reqMsgIndex.value;
  for (const mid of highlightedMsgIds.value) {
    for (const rid of msg2reqs[mid] || []) {
      if (highlightedReqIds.value.has(rid)) keys.add(`msg-${mid}-req-${rid}`);
    }
  }
  return keys;
});

// ============ 发送人颜色 ============
const senderColors = ['#1677ff', '#eb2f96', '#fa8c16', '#52c41a', '#722ed1', '#13c2c2'];
const senderColorMap = computed(() => {
  if (!detail.value) return {};
  const map: Record<string, string> = {};
  let idx = 0;
  for (const msg of detail.value.messages || []) {
    if (!(msg.senderName in map)) {
      map[msg.senderName] = senderColors[idx % senderColors.length];
      idx++;
    }
  }
  return map;
});

// 判断是否为客户：senderRoles 里 role=Client 或没有绑定记录的都算客户
function isClient(senderId: string): boolean {
  if (!detail.value?.senderRoles) return true; // 没有角色数据默认客户
  const role = detail.value.senderRoles[senderId];
  return !role || role === 'Client';
}

// ============ 三列布局: 左=客户  中=需求  右=团队 ============
const MSG_WIDTH = 280;
const REQ_WIDTH = 260;
const COL_GAP = 160; // 列间距（留给连线）
const LEFT_X = 0;
const CENTER_X = MSG_WIDTH + COL_GAP;
const RIGHT_X = CENTER_X + REQ_WIDTH + COL_GAP;
const NODE_GAP = 12;
const TOP_OFFSET = 20;

const msgHeights = ref<Record<number, number>>({});
const reqHeights = ref<Record<number, number>>({});

function setMsgHeight(id: number, el: HTMLElement | null) {
  if (el) msgHeights.value[id] = el.offsetHeight;
}
function setReqHeight(id: number, el: HTMLElement | null) {
  if (el) reqHeights.value[id] = el.offsetHeight;
}

const nodes = computed(() => {
  if (!detail.value) return [];
  const msgs = detail.value.messages || [];
  const reqs = detail.value.chatRequirements || [];
  const result: any[] = [];

  // 消息分两列，共享时间线（y 轴递增），记录每条消息的 y 中心点
  const msgYCenter: Record<number, number> = {};
  let yMsg = TOP_OFFSET;
  for (const msg of msgs) {
    const client = isClient(msg.senderId);
    const h = msgHeights.value[msg.id] || 68;
    result.push({
      id: `msg-${msg.id}`,
      type: client ? 'msgLeft' : 'msgRight',
      position: { x: client ? LEFT_X : RIGHT_X, y: yMsg },
      data: msg,
      sourcePosition: client ? Position.Right : Position.Left,
      style: { width: `${MSG_WIDTH}px` },
    });
    msgYCenter[msg.id] = yMsg + h / 2;
    yMsg += h + NODE_GAP;
  }

  // 需求 Y 轴跟随关联消息的平均位置，并避免重叠
  const reqPositions: { req: any; targetY: number }[] = [];
  for (const req of reqs) {
    let ids: number[] = [];
    try { ids = req.sourceMessageIds ? JSON.parse(req.sourceMessageIds) : []; } catch { /* */ }
    const validYs = ids.map(id => msgYCenter[id]).filter(y => y !== undefined);
    const targetY = validYs.length > 0
      ? validYs.reduce((a, b) => a + b, 0) / validYs.length
      : TOP_OFFSET;
    reqPositions.push({ req, targetY });
  }
  // 按目标 Y 排序后，防止重叠
  reqPositions.sort((a, b) => a.targetY - b.targetY);
  let lastReqBottom = -Infinity;
  for (const item of reqPositions) {
    const h = reqHeights.value[item.req.id] || 90;
    let y = item.targetY - h / 2; // 居中对齐
    if (y < lastReqBottom + NODE_GAP) {
      y = lastReqBottom + NODE_GAP; // 避免重叠
    }
    result.push({
      id: `req-${item.req.id}`,
      type: 'req',
      position: { x: CENTER_X, y },
      data: item.req,
      targetPosition: Position.Left,
      sourcePosition: Position.Right,
      style: { width: `${REQ_WIDTH}px` },
    });
    lastReqBottom = y + h;
  }
  return result;
});

const edges = computed(() => {
  if (!detail.value) return [];
  const result: any[] = [];
  const msgs = detail.value.messages || [];
  // 构建 msgId -> senderId 映射
  const msgSenderMap: Record<number, string> = {};
  for (const m of msgs) msgSenderMap[m.id] = m.senderId;

  for (const req of detail.value.chatRequirements || []) {
    if (!req.sourceMessageIds) continue;
    let ids: number[] = [];
    try { ids = JSON.parse(req.sourceMessageIds); } catch { continue; }
    for (const mid of ids) {
      const key = `msg-${mid}-req-${req.id}`;
      const isHighlighted = highlightedEdgeKeys.value.size > 0 && highlightedEdgeKeys.value.has(key);
      const isDimmed = highlightedEdgeKeys.value.size > 0 && !isHighlighted;
      const client = isClient(msgSenderMap[mid] || '');
      if (client) {
        // 客户消息右侧 → 需求左侧
        result.push({
          id: key,
          source: `msg-${mid}`,
          sourceHandle: 'right',
          target: `req-${req.id}`,
          targetHandle: 'left',
          type: 'default',
          animated: isHighlighted,
          style: {
            stroke: isHighlighted ? '#1677ff' : isDimmed ? 'rgba(255,255,255,0.08)' : 'rgba(255,255,255,0.2)',
            strokeWidth: isHighlighted ? 2.5 : 1.5,
            transition: 'stroke 0.2s, stroke-width 0.2s',
          },
        });
      } else {
        // 需求右侧 → 团队消息左侧
        result.push({
          id: key,
          source: `req-${req.id}`,
          sourceHandle: 'right',
          target: `msg-${mid}`,
          targetHandle: 'left',
          type: 'default',
          animated: isHighlighted,
          style: {
            stroke: isHighlighted ? '#1677ff' : isDimmed ? 'rgba(255,255,255,0.08)' : 'rgba(255,255,255,0.2)',
            strokeWidth: isHighlighted ? 2.5 : 1.5,
            transition: 'stroke 0.2s, stroke-width 0.2s',
          },
        });
      }
    }
  }
  return result;
});

function formatTime(dateStr: string) {
  if (!dateStr) return '';
  const d = new Date(dateStr);
  return `${d.getMonth() + 1}-${d.getDate()} ${d.toTimeString().slice(0, 5)}`;
}

function getMediaUrl(mediaId: number) {
  return `/api/chat/media/${mediaId}`;
}

const flowContainer = ref<HTMLElement>();
const exporting = ref(false);

async function exportAsImage() {
  if (!flowContainer.value) return;
  exporting.value = true;
  try {
    const viewport = flowContainer.value.querySelector('.vue-flow__viewport') as HTMLElement;
    const wrapper = flowContainer.value.querySelector('.vue-flow') as HTMLElement;
    if (!viewport || !wrapper) { message.error('未找到画布'); return; }

    // 计算所有节点的完整边界
    const nodesEl = flowContainer.value.querySelectorAll('.vue-flow__node');
    let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
    nodesEl.forEach((el: any) => {
      const transform = el.style.transform;
      const match = /translate\(([^,]+)px,\s*([^)]+)px\)/.exec(transform);
      if (!match) return;
      const x = parseFloat(match[1]);
      const y = parseFloat(match[2]);
      minX = Math.min(minX, x);
      minY = Math.min(minY, y);
      maxX = Math.max(maxX, x + el.offsetWidth);
      maxY = Math.max(maxY, y + el.offsetHeight);
    });

    const padding = 40;
    const fullWidth = maxX - minX + padding * 2;
    const fullHeight = maxY - minY + padding * 2;

    // 保存原始样式
    const origWrapperStyle = wrapper.style.cssText;
    const origContainerStyle = flowContainer.value.style.cssText;
    const origViewportTransform = viewport.style.transform;

    // 临时撑大容器，移动视口让所有节点可见
    flowContainer.value.style.width = `${fullWidth}px`;
    flowContainer.value.style.height = `${fullHeight}px`;
    flowContainer.value.style.overflow = 'visible';
    wrapper.style.width = `${fullWidth}px`;
    wrapper.style.height = `${fullHeight}px`;
    wrapper.style.overflow = 'visible';
    viewport.style.transform = `translate(${-minX + padding}px, ${-minY + padding}px)`;

    // 等待渲染
    await new Promise(r => setTimeout(r, 300));

    const canvas = await html2canvas(wrapper, {
      width: fullWidth,
      height: fullHeight,
      backgroundColor: '#141414',
      useCORS: true,
      allowTaint: true,
      scale: 2,
    });

    // 恢复原始样式
    flowContainer.value.style.cssText = origContainerStyle;
    wrapper.style.cssText = origWrapperStyle;
    viewport.style.transform = origViewportTransform;

    const link = document.createElement('a');
    link.download = `batch-${props.batchId}-detail.png`;
    link.href = canvas.toDataURL('image/png');
    link.click();
    message.success('导出成功');
  } catch (e: any) {
    message.error('导出失败: ' + (e?.message || ''));
  } finally {
    exporting.value = false;
  }
}

onMounted(async () => {
  try {
    detail.value = await getBatchDetail(props.batchId);
  } catch (e: any) {
    message.error(e?.message || '加载失败');
  } finally {
    loading.value = false;
  }
});
</script>

<template>
  <div class="batch-detail-root" ref="flowContainer">
    <Button class="export-img-btn" size="small" :loading="exporting" @click="exportAsImage">导出长图</Button>
    <div v-if="loading" style="display: flex; align-items: center; justify-content: center; height: 100%;">
      <Spin />
    </div>
    <VueFlow
      v-else-if="detail"
      @pane-click="clickedType = null; clickedId = null"
      :nodes="nodes"
      :edges="edges"
      :nodes-draggable="false"
      :nodes-connectable="false"
      :zoom-on-scroll="false"
      :pan-on-scroll="true"
      :pan-on-drag="true"
      :zoom-on-double-click="false"
      :prevent-scrolling="false"
      :default-viewport="{ x: 0, y: 0, zoom: 1 }"
      :min-zoom="1"
      :max-zoom="1"
    >
      <!-- 左侧消息（客户） -->
      <template #node-msgLeft="{ data }">
        <Handle type="source" :position="Position.Right" id="right" />
        <div
          :ref="(el: any) => setMsgHeight(data.id, el)"
          class="msg-bubble msg-left"
          :class="{ highlighted: highlightedMsgIds.size > 0 && highlightedMsgIds.has(data.id), dimmed: highlightedMsgIds.size > 0 && !highlightedMsgIds.has(data.id), clicked: clickedType === 'msg' && clickedId === data.id }"
          @mouseenter="hoveredMsgId = data.id"
          @mouseleave="hoveredMsgId = null"
          @click="onClickMsg(data.id)"
        >
          <div class="msg-sender">
            <span :style="{ color: senderColorMap[data.senderName] }">{{ data.senderName }}</span>
            <span class="msg-time">{{ formatTime(data.createdTime) }}</span>
          </div>
          <div class="msg-content">
            <template v-if="data.mediaList?.length">
              <template v-for="media in data.mediaList" :key="media.id">
                <img v-if="media.mimeType?.startsWith('image/')" :src="getMediaUrl(media.id)" class="msg-media-img" />
                <video v-else-if="media.mimeType?.startsWith('video/')" :src="getMediaUrl(media.id)" class="msg-media-img" autoplay loop muted playsinline />
                <div v-else class="msg-media-file">📎 {{ media.fileName || '文件' }}</div>
              </template>
            </template>
            <div v-if="data.content">{{ data.content }}</div>
          </div>
        </div>
      </template>

      <!-- 右侧消息（团队） -->
      <template #node-msgRight="{ data }">
        <Handle type="target" :position="Position.Left" id="left" />
        <div
          :ref="(el: any) => setMsgHeight(data.id, el)"
          class="msg-bubble msg-right"
          :class="{ highlighted: highlightedMsgIds.size > 0 && highlightedMsgIds.has(data.id), dimmed: highlightedMsgIds.size > 0 && !highlightedMsgIds.has(data.id), clicked: clickedType === 'msg' && clickedId === data.id }"
          @mouseenter="hoveredMsgId = data.id"
          @mouseleave="hoveredMsgId = null"
          @click="onClickMsg(data.id)"
        >
          <div class="msg-sender">
            <span :style="{ color: senderColorMap[data.senderName] }">{{ data.senderName }}</span>
            <span class="msg-time">{{ formatTime(data.createdTime) }}</span>
          </div>
          <div class="msg-content">
            <template v-if="data.mediaList?.length">
              <template v-for="media in data.mediaList" :key="media.id">
                <img v-if="media.mimeType?.startsWith('image/')" :src="getMediaUrl(media.id)" class="msg-media-img" />
                <video v-else-if="media.mimeType?.startsWith('video/')" :src="getMediaUrl(media.id)" class="msg-media-img" autoplay loop muted playsinline />
                <div v-else class="msg-media-file">📎 {{ media.fileName || '文件' }}</div>
              </template>
            </template>
            <div v-if="data.content">{{ data.content }}</div>
          </div>
        </div>
      </template>

      <!-- 需求卡片（中间） -->
      <template #node-req="{ data }">
        <Handle type="target" :position="Position.Left" id="left" />
        <Handle type="source" :position="Position.Right" id="right" />
        <div
          :ref="(el: any) => setReqHeight(data.id, el)"
          class="req-card"
          :class="{ highlighted: highlightedReqIds.size > 0 && highlightedReqIds.has(data.id), dimmed: highlightedReqIds.size > 0 && !highlightedReqIds.has(data.id), clicked: clickedType === 'req' && clickedId === data.id }"
          @mouseenter="hoveredReqId = data.id"
          @mouseleave="hoveredReqId = null"
          @click="onClickReq(data.id)"
        >
          <div class="req-title">
            <span>{{ data.title }}</span>
            <Tag :color="reqStatusColors[data.status] || 'default'" style="margin-left: 6px; font-size: 11px;">{{ reqStatusLabels[data.status] || data.status }}</Tag>
          </div>
          <div v-if="data.description" class="req-desc">{{ data.description }}</div>
        </div>
      </template>
    </VueFlow>
  </div>
</template>

<style scoped>
/* 消息气泡 */
.msg-bubble {
  max-width: 280px;
  padding: 10px 14px;
  cursor: default;
  background: var(--ant-color-bg-elevated);
  border: 2px solid rgb(255 255 255 / 15%);
  box-shadow: 0 2px 8px rgb(0 0 0 / 15%);
  transition: all 0.2s;
}

.msg-left {
  border-left: 4px solid #fa8c16;
  border-radius: 4px 12px 12px;
}

.msg-right {
  border-right: 4px solid #1677ff;
  border-radius: 12px 4px 12px 12px;
}

.msg-bubble.highlighted {
  border-color: #1677ff !important;
  box-shadow: 0 0 0 2px rgb(22 119 255 / 30%), 0 2px 8px rgb(22 119 255 / 20%);
}

.msg-bubble.clicked {
  border-color: #faad14 !important;
  box-shadow: 0 0 0 3px rgb(250 173 20 / 40%), 0 2px 8px rgb(250 173 20 / 20%);
}

.msg-bubble.dimmed {
  opacity: 0.25;
}

.msg-sender {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 4px;
  font-size: 12px;
  font-weight: 600;
}

.msg-time {
  font-size: 11px;
  font-weight: 400;
  color: var(--ant-color-text-quaternary);
}

.msg-content {
  font-size: 13px;
  line-height: 1.5;
  color: var(--ant-color-text);
  overflow-wrap: break-word;
}

.msg-media-img {
  display: block;
  max-width: 240px;
  max-height: 180px;
  margin: 4px 0;
  border-radius: 4px;
}

.msg-media-file {
  margin: 4px 0;
  font-size: 12px;
  color: var(--ant-color-text-secondary);
}

/* 需求卡片 */
.req-card {
  max-width: 260px;
  padding: 10px 14px;
  cursor: default;
  background: var(--ant-color-bg-elevated);
  border: 2px solid rgb(255 255 255 / 15%);
  border-top: 4px solid #52c41a;
  border-radius: 8px;
  box-shadow: 0 2px 8px rgb(0 0 0 / 15%);
  transition: all 0.2s;
}

.req-card.highlighted {
  border-color: #1677ff !important;
  box-shadow: 0 0 0 2px rgb(22 119 255 / 30%), 0 2px 8px rgb(22 119 255 / 20%);
}

.req-card.clicked {
  border-color: #faad14 !important;
  box-shadow: 0 0 0 3px rgb(250 173 20 / 40%), 0 2px 8px rgb(250 173 20 / 20%);
}

.req-card.dimmed {
  opacity: 0.25;
}

.req-title {
  display: flex;
  align-items: center;
  font-size: 13px;
  font-weight: 600;
  color: var(--ant-color-text);
}

.req-desc {
  margin-top: 6px;
  font-size: 12px;
  line-height: 1.5;
  color: var(--ant-color-text-secondary);
  overflow-wrap: break-word;
}

/* 导出按钮 */
.export-img-btn {
  position: absolute;
  top: 12px;
  right: 12px;
  z-index: 10;
}

/* 根容器 */
.batch-detail-root {
  position: relative;
  width: 100%;
  height: calc(100vh - 57px);
}

/* Vue Flow 覆盖 */
:deep(.vue-flow__node) {
  padding: 0;
  background: transparent;
  border: none;
  border-radius: 0;
  box-shadow: none;
}

:deep(.vue-flow__handle) {
  width: 6px;
  height: 6px;
  opacity: 0;
}
</style>

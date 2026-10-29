<script lang="ts" setup>
import { ref, computed, onMounted, onUnmounted } from 'vue';

const props = defineProps<{
  startDate?: string;
  createdTime?: string;
  dueDate?: string;
}>();

const now = ref(Date.now());
let timer: ReturnType<typeof setInterval> | null = null;

onMounted(() => {
  timer = setInterval(() => { now.value = Date.now(); }, 1000);
});

onUnmounted(() => {
  if (timer) clearInterval(timer);
});

const startMs = computed(() => new Date(props.startDate || props.createdTime || '').getTime() || 0);
const endMs = computed(() => props.dueDate ? new Date(props.dueDate).getTime() : 0);
const hasRange = computed(() => startMs.value > 0 && endMs.value > 0 && endMs.value > startMs.value);

const percent = computed(() => {
  if (!hasRange.value) return 0;
  const p = Math.round(((now.value - startMs.value) / (endMs.value - startMs.value)) * 100);
  return Math.max(0, Math.min(100, p));
});

const overdue = computed(() => hasRange.value && now.value > endMs.value);

const strokeColor = computed(() => {
  if (overdue.value) return '#ff4d4f';
  if (percent.value > 80) return '#faad14';
  return '#1677ff';
});

const remainingText = computed(() => {
  if (!hasRange.value) return '';
  const diff = Math.abs(endMs.value - now.value);
  const days = Math.floor(diff / 86400000);
  const hours = Math.floor((diff % 86400000) / 3600000);
  const minutes = Math.floor((diff % 3600000) / 60000);
  const seconds = Math.floor((diff % 60000) / 1000);
  const pad = (n: number) => String(n).padStart(2, '0');
  const time = `${pad(days)}天${pad(hours)}时${pad(minutes)}分${pad(seconds)}秒`;
  return overdue.value ? `逾期 ${time}` : time;
});

function shortDate(dateStr?: string) {
  if (!dateStr) return '';
  const d = new Date(dateStr);
  const m = String(d.getMonth() + 1).padStart(2, '0');
  const day = String(d.getDate()).padStart(2, '0');
  return `${m}/${day}`;
}
</script>

<template>
  <div v-if="hasRange" class="time-bar">
    <span class="time-bar-date">{{ shortDate(startDate || createdTime) }}</span>
    <div class="time-bar-track">
      <div class="time-bar-fill" :style="{ width: percent + '%', backgroundColor: strokeColor }" />
      <span class="time-bar-text" :class="{ overdue }">{{ remainingText }}</span>
    </div>
    <span class="time-bar-date">{{ shortDate(dueDate) }}</span>
  </div>
</template>

<style>
.time-bar {
  display: flex;
  gap: 8px;
  align-items: center;
}

.time-bar-date {
  flex-shrink: 0;
  font-size: 11px;
  color: var(--ant-color-text-tertiary);
  white-space: nowrap;
}

.time-bar-track {
  position: relative;
  flex: 1;
  height: 22px;
  overflow: hidden;
  background: #e5e7eb;
  border-radius: 11px;
}

html.dark .time-bar-track {
  background: #3a3a3a;
}

.time-bar-fill {
  position: absolute;
  top: 0;
  left: 0;
  height: 100%;
  background-image: linear-gradient(
    -45deg,
    rgb(255 255 255 / 20%) 25%,
    transparent 25%,
    transparent 50%,
    rgb(255 255 255 / 20%) 50%,
    rgb(255 255 255 / 20%) 75%,
    transparent 75%,
    transparent
  );
  background-size: 40px 40px;
  border-radius: 11px;
  animation: time-bar-flow 1.5s linear infinite;
}

@keyframes time-bar-flow {
  0% { background-position: 0 0; }

  100% { background-position: 40px 0; }
}

.time-bar-text {
  position: absolute;
  inset: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 11px;
  font-weight: 500;
  color: #fff;
  letter-spacing: 0.5px;
  text-shadow: 0 1px 2px rgb(0 0 0 / 40%);
  pointer-events: none;
}

.time-bar-text.overdue {
  color: #fff;
}
</style>

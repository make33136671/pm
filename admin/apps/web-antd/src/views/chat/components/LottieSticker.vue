<script lang="ts" setup>
import { ref, onMounted, onUnmounted, watch } from 'vue';
import lottie from 'lottie-web';
import { ungzip } from 'pako';

const props = withDefaults(defineProps<{
  src: string;
  size?: number;
}>(), { size: 200 });

const container = ref<HTMLDivElement>();
let animInstance: ReturnType<typeof lottie.loadAnimation> | null = null;
const failed = ref(false);

async function loadTgs() {
  if (!container.value) return;

  // 清理旧动画
  if (animInstance) {
    animInstance.destroy();
    animInstance = null;
  }
  failed.value = false;

  try {
    const resp = await fetch(props.src);
    if (!resp.ok) throw new Error(`HTTP ${resp.status}`);

    const buf = await resp.arrayBuffer();
    const jsonStr = new TextDecoder().decode(ungzip(new Uint8Array(buf)));
    const animationData = JSON.parse(jsonStr);

    animInstance = lottie.loadAnimation({
      container: container.value,
      renderer: 'svg',
      loop: true,
      autoplay: true,
      animationData,
    });
  } catch {
    failed.value = true;
  }
}

onMounted(loadTgs);
watch(() => props.src, loadTgs);
onUnmounted(() => animInstance?.destroy());
</script>

<template>
  <div v-if="!failed"
       ref="container"
       class="lottie-sticker"
       :style="{ width: `${size}px`, height: `${size}px` }" />
  <div v-else class="lottie-sticker-fallback"
       :style="{ width: `${size}px`, height: `${size}px` }">
    [Sticker]
  </div>
</template>

<style scoped>
.lottie-sticker {
  display: inline-block;
}

.lottie-sticker :deep(svg) {
  display: block;
}

.lottie-sticker-fallback {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  font-size: 13px;
  color: #999;
  background: rgb(128 128 128 / 10%);
  border-radius: 8px;
}
</style>

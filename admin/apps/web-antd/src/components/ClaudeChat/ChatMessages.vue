<script lang="ts" setup>
import { nextTick, ref, watch } from 'vue';
import type { ChatMessage } from './types';
import MessageBubble from './MessageBubble.vue';

const props = defineProps<{ messages: ChatMessage[] }>();

const scrollContainer = ref<HTMLDivElement>();

function scrollToBottom() {
  nextTick(() => {
    const el = scrollContainer.value;
    if (el) {
      el.scrollTop = el.scrollHeight;
    }
  });
}

// auto scroll on new messages or content changes
watch(
  () => {
    const msgs = props.messages;
    const last = msgs[msgs.length - 1];
    return last ? `${msgs.length}-${last.content.length}` : '0';
  },
  () => scrollToBottom(),
);
</script>

<template>
  <div ref="scrollContainer" class="chat-messages">
    <div v-if="messages.length === 0" class="empty-hint">
      <div class="empty-icon">AI</div>
      <p>你好！我是 Claude 助手，有什么可以帮到你的？</p>
    </div>
    <MessageBubble
      v-for="msg in messages"
      :key="msg.id"
      :message="msg"
    />
  </div>
</template>

<style scoped>
.chat-messages {
  flex: 1;
  padding: 16px;
  overflow-y: auto;
}

.empty-hint {
  display: flex;
  flex-direction: column;
  gap: 12px;
  align-items: center;
  justify-content: center;
  height: 100%;
  color: #999;
}

.empty-icon {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 48px;
  height: 48px;
  font-size: 16px;
  font-weight: 700;
  color: #fff;
  background: #722ed1;
  border-radius: 50%;
}
</style>

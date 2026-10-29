<script lang="ts" setup>
import type { ChatMessage } from './types';
import MarkdownRenderer from './MarkdownRenderer.vue';

defineProps<{ message: ChatMessage }>();
</script>

<template>
  <div
    class="message-bubble"
    :class="{ 'is-user': message.role === 'user', 'is-assistant': message.role === 'assistant' }"
  >
    <div class="bubble-avatar">
      {{ message.role === 'user' ? 'U' : 'AI' }}
    </div>
    <div class="bubble-content">
      <div v-if="message.role === 'user'" class="user-text">
        {{ message.content }}
      </div>
      <div v-else-if="!message.content" class="thinking-dots">
        <span></span><span></span><span></span>
      </div>
      <MarkdownRenderer v-else :content="message.content" />
    </div>
  </div>
</template>

<style scoped>
.message-bubble {
  display: flex;
  gap: 10px;
  margin-bottom: 16px;
}

.message-bubble.is-user {
  flex-direction: row-reverse;
}

.bubble-avatar {
  display: flex;
  flex-shrink: 0;
  align-items: center;
  justify-content: center;
  width: 32px;
  height: 32px;
  font-size: 12px;
  font-weight: 600;
  color: #fff;
  border-radius: 50%;
}

.is-user .bubble-avatar {
  background: #1677ff;
}

.is-assistant .bubble-avatar {
  background: #722ed1;
}

.bubble-content {
  max-width: 85%;
  padding: 10px 14px;
  line-height: 1.5;
  border-radius: 12px;
}

.is-user .bubble-content {
  color: #fff;
  background: #1677ff;
  border-top-right-radius: 4px;
}

.is-assistant .bubble-content {
  color: #333;
  background: #f5f5f5;
  border-top-left-radius: 4px;
}

html.dark .is-assistant .bubble-content {
  color: #e0e0e0;
  background: #2a2a2a;
}

.user-text {
  overflow-wrap: break-word;
  white-space: pre-wrap;
}

.thinking-dots {
  display: flex;
  gap: 4px;
  align-items: center;
  height: 20px;
}

.thinking-dots span {
  width: 7px;
  height: 7px;
  background: #999;
  border-radius: 50%;
  animation: dot-bounce 1.4s ease-in-out infinite;
}

html.dark .thinking-dots span {
  background: #666;
}

.thinking-dots span:nth-child(2) {
  animation-delay: 0.16s;
}

.thinking-dots span:nth-child(3) {
  animation-delay: 0.32s;
}

@keyframes dot-bounce {
  0%,
  80%,
  100% {
    opacity: 0.4;
    transform: scale(0.6);
  }

  40% {
    opacity: 1;
    transform: scale(1);
  }
}
</style>

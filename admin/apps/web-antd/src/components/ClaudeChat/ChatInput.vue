<script lang="ts" setup>
import { ref } from 'vue';
import { Button, Textarea } from 'ant-design-vue';

const props = defineProps<{ loading: boolean }>();
const emit = defineEmits<{
  send: [message: string];
  stop: [];
}>();

const inputText = ref('');

function handleSend() {
  const text = inputText.value.trim();
  if (!text || props.loading) return;
  emit('send', text);
  inputText.value = '';
}

function handleKeydown(e: KeyboardEvent) {
  if (e.key === 'Enter' && !e.shiftKey) {
    e.preventDefault();
    handleSend();
  }
}
</script>

<template>
  <div class="chat-input">
    <Textarea
      v-model:value="inputText"
      placeholder="输入消息，Enter 发送，Shift+Enter 换行..."
      :disabled="loading"
      :rows="2"
      :auto-size="{ minRows: 2, maxRows: 5 }"
      style="flex: 1"
      @keydown="handleKeydown"
    />
    <Button v-if="loading" danger size="small" @click="$emit('stop')">停止</Button>
    <Button v-else type="primary" size="small" :disabled="!inputText.trim()" @click="handleSend">发送</Button>
  </div>
</template>

<style scoped>
.chat-input {
  --border-color: #f0f0f0;

  display: flex;
  gap: 8px;
  align-items: flex-end;
  padding: 12px 16px;
  border-top: 1px solid var(--border-color);
}

html.dark .chat-input {
  --border-color: #303030;
}
</style>

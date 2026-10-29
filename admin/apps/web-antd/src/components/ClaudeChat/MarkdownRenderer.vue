<script lang="ts" setup>
import { computed } from 'vue';
import { marked } from 'marked';

const props = defineProps<{ content: string }>();

marked.setOptions({
  breaks: true,
  gfm: true,
});

const html = computed(() => {
  if (!props.content) return '';
  return marked.parse(props.content) as string;
});
</script>

<template>
  <div class="markdown-body" v-html="html" />
</template>

<style scoped>
.markdown-body {
  font-size: 14px;
  line-height: 1.6;
  overflow-wrap: break-word;
}

.markdown-body :deep(p) {
  margin: 0 0 8px;
}

.markdown-body :deep(p:last-child) {
  margin-bottom: 0;
}

.markdown-body :deep(pre) {
  padding: 12px;
  margin: 8px 0;
  overflow-x: auto;
  background: #f5f5f5;
  border-radius: 6px;
}

html.dark .markdown-body :deep(pre) {
  background: #1e1e1e;
}

.markdown-body :deep(code) {
  font-family: 'SF Mono', SFMono-Regular, Consolas, 'Liberation Mono', Menlo,
    monospace;
  font-size: 13px;
}

.markdown-body :deep(:not(pre) > code) {
  padding: 2px 4px;
  background: rgb(0 0 0 / 6%);
  border-radius: 4px;
}

html.dark .markdown-body :deep(:not(pre) > code) {
  background: rgb(255 255 255 / 10%);
}

.markdown-body :deep(ul),
.markdown-body :deep(ol) {
  padding-left: 20px;
  margin: 4px 0;
}

.markdown-body :deep(blockquote) {
  padding-left: 12px;
  margin: 8px 0;
  color: #666;
  border-left: 3px solid #d9d9d9;
}

html.dark .markdown-body :deep(blockquote) {
  color: #999;
  border-left-color: #424242;
}

.markdown-body :deep(table) {
  width: 100%;
  margin: 8px 0;
  border-collapse: collapse;
}

.markdown-body :deep(th),
.markdown-body :deep(td) {
  padding: 6px 12px;
  text-align: left;
  border: 1px solid #d9d9d9;
}

html.dark .markdown-body :deep(th),
html.dark .markdown-body :deep(td) {
  border-color: #424242;
}

.markdown-body :deep(th) {
  background: #fafafa;
}

html.dark .markdown-body :deep(th) {
  background: #2a2a2a;
}
</style>

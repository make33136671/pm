<script lang="ts" setup>
import { computed } from 'vue';
import { Descriptions, DescriptionsItem } from 'ant-design-vue';
import { MdPreview } from 'md-editor-v3';
import 'md-editor-v3/lib/preview.css';
import { usePreferences } from '@vben/preferences';

const props = defineProps<{
  content: string;
  sourceType: string;
}>();

const { isDark } = usePreferences();
const mdTheme = computed(() => isDark.value ? 'dark' : 'light');

// 解析基本信息表格
const meta = computed(() => {
  const lines = props.content.split('\n');
  const result: { key: string; value: string }[] = [];
  for (const line of lines) {
    const m = line.match(/^\|\s*(.+?)\s*\|\s*(.+?)\s*\|$/);
    if (m && m[1] !== '属性' && m[1] !== '---') {
      result.push({ key: m[1]!.trim(), value: m[2]!.trim() });
    }
  }
  return result;
});

// 提取描述内容（## 需求描述 / ## 任务描述 之后）
const descContent = computed(() => {
  const match = props.content.match(/##\s*(需求描述|任务描述)\s*\n([\s\S]*)$/);
  return match ? match[2]!.trim() : '';
});

const descLabel = computed(() => props.sourceType === 'Requirement' ? '需求描述' : '任务描述');
</script>

<template>
  <div class="doc-renderer">
    <!-- 基本信息 -->
    <Descriptions v-if="meta.length > 0" bordered size="small" :column="3" class="doc-renderer-meta">
      <DescriptionsItem v-for="item in meta" :key="item.key" :label="item.key">
        {{ item.value }}
      </DescriptionsItem>
    </Descriptions>

    <!-- 描述内容 -->
    <div v-if="descContent" class="doc-renderer-desc">
      <div class="doc-renderer-desc-label">{{ descLabel }}</div>
      <div class="doc-renderer-desc-body" :class="{ dark: isDark }">
        <MdPreview :model-value="descContent" :theme="mdTheme" />
      </div>
    </div>

    <!-- 无法解析时，直接渲染全部 -->
    <div v-else-if="!meta.length" class="doc-renderer-desc-body" :class="{ dark: isDark }">
      <MdPreview :model-value="content || '暂无内容'" :theme="mdTheme" />
    </div>
  </div>
</template>

<style scoped>
.doc-renderer-meta {
  margin-bottom: 16px;
}

.doc-renderer-desc {
  margin-top: 4px;
}

.doc-renderer-desc-label {
  padding-left: 2px;
  margin-bottom: 8px;
  font-size: 15px;
  font-weight: 600;
}

.doc-renderer-desc-body {
  background: #f6f8fa;
  border: 1px solid #e5e7eb;
  border-radius: 8px;
}

.doc-renderer-desc-body.dark {
  background: #161b22;
  border-color: #30363d;
}
</style>

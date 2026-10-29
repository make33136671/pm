<script lang="ts" setup>
/**
 * 需求信息单元格 — 统一渲染组件
 * 用于：独立需求列表、项目详情内嵌需求列表
 * 修改此处，所有地方自动生效
 */
import { Tag, Progress } from 'ant-design-vue';
import { requirementStatusLabels, requirementTypeLabels, complexityLabels } from '#/utils/enum-labels';
import { reqStatusColorMap, reqTypeColorMap, reqComplexityColorMap, reqProgressMap } from '#/utils/shared-columns';

defineProps<{
  record: any;
  showProject?: boolean;
  clickTitle?: boolean;
  /** 分组显示：info=标题/项目/批次, status=类型/复杂度/状态, 不传=全部 */
  group?: 'info' | 'status';
}>();

const emit = defineEmits<{
  (e: 'clickTitle', id: number): void;
}>();
</script>

<template>
  <table style=" width: 100%;font-size: 13px; border-collapse: collapse;">
    <!-- info 组: 标题/项目/批次 -->
    <tr v-if="!group || group === 'info'">
      <td style=" width: 76px; padding: 4px 12px; vertical-align: top;color: var(--ant-color-text-quaternary); white-space: nowrap; background: var(--ant-color-fill-quaternary);">标题</td>
      <td style="padding: 4px 12px;">
        <a v-if="clickTitle" style="color: var(--ant-color-primary); cursor: pointer;" @click="emit('clickTitle', record.id)">{{ record.title }}</a>
        <span v-else>{{ record.title }}</span>
      </td>
    </tr>
    <tr v-if="(!group || group === 'info') && showProject && record.project?.name">
      <td style=" width: 76px; padding: 4px 12px;color: var(--ant-color-text-quaternary); background: var(--ant-color-fill-quaternary);">项目</td>
      <td style="padding: 4px 12px;">{{ record.project.name }}</td>
    </tr>
    <!-- 批次归入 info 组 -->
    <tr v-if="(!group || group === 'info') && record.batchId">
      <td style=" width: 76px; padding: 4px 12px;color: var(--ant-color-text-quaternary); background: var(--ant-color-fill-quaternary);">批次</td>
      <td style="padding: 4px 12px;"><Tag size="small" color="blue">#{{ record.batchId }}</Tag> {{ record.batchLabel || '' }}</td>
    </tr>
    <!-- status 组: 类型/复杂度/状态 -->
    <tr v-if="!group || group === 'status'">
      <td style=" width: 76px; padding: 4px 12px;color: var(--ant-color-text-quaternary); background: var(--ant-color-fill-quaternary);">类型</td>
      <td style="padding: 4px 12px;"><Tag :color="reqTypeColorMap[record.type] || 'default'">{{ requirementTypeLabels[record.type] || record.type }}</Tag></td>
    </tr>
    <tr v-if="!group || group === 'status'">
      <td style=" width: 76px; padding: 4px 12px;color: var(--ant-color-text-quaternary); background: var(--ant-color-fill-quaternary);">复杂度</td>
      <td style="padding: 4px 12px;"><Tag :color="reqComplexityColorMap[record.complexity] || 'default'">{{ complexityLabels[record.complexity] || record.complexity }}</Tag></td>
    </tr>
    <tr v-if="!group || group === 'status'">
      <td style=" width: 76px; padding: 4px 12px;color: var(--ant-color-text-quaternary); background: var(--ant-color-fill-quaternary);">状态</td>
      <td style="padding: 4px 12px;"><Tag :color="reqStatusColorMap[record.status] || 'default'">{{ requirementStatusLabels[record.status] || record.status }}</Tag></td>
    </tr>
    <!-- 以下字段仅在无 group（完整显示）时展示 -->
    <tr v-if="!group">
      <td style=" width: 76px; padding: 4px 12px;color: var(--ant-color-text-quaternary); background: var(--ant-color-fill-quaternary);">负责人</td>
      <td style="padding: 4px 12px;">{{ record.assignee || '-' }}</td>
    </tr>
    <tr v-if="!group">
      <td style=" width: 76px; padding: 4px 12px;color: var(--ant-color-text-quaternary); background: var(--ant-color-fill-quaternary);">进度</td>
      <td style="padding: 4px 12px;"><Progress :percent="reqProgressMap[record.status] ?? 0" :stroke-color="record.status === 'Done' || record.status === 'Closed' ? '#52c41a' : '#1890ff'" size="small" /></td>
    </tr>
    <tr v-if="!group && record.dueDate">
      <td style=" width: 76px; padding: 4px 12px;color: var(--ant-color-text-quaternary); background: var(--ant-color-fill-quaternary);">截止</td>
      <td style="padding: 4px 12px;">{{ record.dueDate }}</td>
    </tr>
  </table>
</template>

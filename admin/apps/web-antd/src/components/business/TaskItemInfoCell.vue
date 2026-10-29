<script lang="ts" setup>
/**
 * 任务信息单元格 — 统一渲染组件
 * 用于：独立任务列表、项目详情内嵌任务列表
 * 修改此处，所有地方自动生效
 */
import { Tag } from 'ant-design-vue';
import { taskItemTypeLabels, taskItemClaimStatusLabels, taskItemExecStatusLabels, complexityLabels } from '#/utils/enum-labels';
import { taskTypeColorMap, taskComplexityColorMap, taskClaimColorMap, taskExecColorMap } from '#/utils/shared-columns';

defineProps<{
  record: any;
  showProject?: boolean;
  showRequirement?: boolean;
  clickTitle?: boolean;
  /** 分组显示：info=标题/项目/需求/批次, status=类型/复杂度/接单/执行, 不传=全部 */
  group?: 'info' | 'status';
}>();

const emit = defineEmits<{
  (e: 'clickTitle', id: number): void;
}>();
</script>

<template>
  <table style=" width: 100%;font-size: 13px; border-collapse: collapse;">
    <!-- info 组: 标题/项目/需求/批次 -->
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
    <tr v-if="(!group || group === 'info') && showRequirement && record.requirement?.title">
      <td style=" width: 76px; padding: 4px 12px;color: var(--ant-color-text-quaternary); background: var(--ant-color-fill-quaternary);">需求</td>
      <td style="padding: 4px 12px;">{{ record.requirement.title }}</td>
    </tr>
    <!-- status 组: 类型/复杂度/接单/执行 -->
    <tr v-if="!group || group === 'status'">
      <td style=" width: 76px; padding: 4px 12px;color: var(--ant-color-text-quaternary); background: var(--ant-color-fill-quaternary);">类型</td>
      <td style="padding: 4px 12px;"><Tag :color="taskTypeColorMap[record.type] || 'default'">{{ taskItemTypeLabels[record.type] || record.type }}</Tag></td>
    </tr>
    <tr v-if="!group || group === 'status'">
      <td style=" width: 76px; padding: 4px 12px;color: var(--ant-color-text-quaternary); background: var(--ant-color-fill-quaternary);">复杂度</td>
      <td style="padding: 4px 12px;"><Tag :color="taskComplexityColorMap[record.complexity] || 'default'">{{ complexityLabels[record.complexity] || record.complexity }}</Tag></td>
    </tr>
    <tr v-if="!group || group === 'status'">
      <td style=" width: 76px; padding: 4px 12px;color: var(--ant-color-text-quaternary); background: var(--ant-color-fill-quaternary);">接单</td>
      <td style="padding: 4px 12px;"><Tag :color="taskClaimColorMap[record.claimStatus] || 'default'">{{ taskItemClaimStatusLabels[record.claimStatus] || record.claimStatus }}</Tag> {{ record.claimedBy || '' }}</td>
    </tr>
    <tr v-if="!group || group === 'status'">
      <td style=" width: 76px; padding: 4px 12px;color: var(--ant-color-text-quaternary); background: var(--ant-color-fill-quaternary);">执行</td>
      <td style="padding: 4px 12px;">
        <Tag :color="taskExecColorMap[record.execStatus] || 'default'">{{ taskItemExecStatusLabels[record.execStatus] || record.execStatus }}</Tag>
        <span v-if="record.failCount > 0" style=" margin-left: 6px;color: #ff4d4f;">失败 {{ record.failCount }}/{{ record.maxRetryCount }}</span>
      </td>
    </tr>
    <!-- 批次归入 info 组 -->
    <tr v-if="(!group || group === 'info') && record.batchId">
      <td style=" width: 76px; padding: 4px 12px;color: var(--ant-color-text-quaternary); background: var(--ant-color-fill-quaternary);">批次</td>
      <td style="padding: 4px 12px;"><Tag size="small" color="blue">#{{ record.batchId }}</Tag></td>
    </tr>
  </table>
</template>

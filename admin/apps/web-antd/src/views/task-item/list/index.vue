<script lang="ts" setup>
import type { VxeGridProps } from '#/adapter/vxe-table';
import type { VbenFormProps } from '#/adapter/form';

import { ref, computed } from 'vue';
import { useRouter } from 'vue-router';

import { Page } from '@vben/common-ui';

import { Tag, Space, Button, Popconfirm, Drawer, Segmented, Card, message, Modal, Input, Textarea } from 'ant-design-vue';

import { useVbenVxeGrid } from '#/adapter/vxe-table';

import { getList, postDel, postResume, postCancel, getLogs, getKanban, postReviewApprove, postReviewReject, postAutoVerify, postAiCodeGenerate, getDetail, getAttachments } from '../api';
import { getList as getProjectList } from '../../project/api';
import {
  taskItemTypeLabels, taskItemClaimStatusLabels,
  taskItemExecStatusLabels, complexityLabels, taskSourceLabels, toOptions,
} from '#/utils/enum-labels';
import DocPreview from '#/components/DocPreview/index.vue';
import TaskItemInfoCell from '#/components/business/TaskItemInfoCell.vue';

const router = useRouter();
const docRef = ref<InstanceType<typeof DocPreview>>();

// 视图模式
const viewMode = ref<string>('table');

// 看板数据
const kanbanData = ref<Record<string, any[]>>({});
const kanbanProjectId = ref<number | null>(null);

// 日志抽屉
const logVisible = ref(false);
const logData = ref<any[]>([]);
const logTaskId = ref(0);

const claimStatusOptions = toOptions(taskItemClaimStatusLabels);
const execStatusOptions = toOptions(taskItemExecStatusLabels);
const typeOptions = toOptions(taskItemTypeLabels);
const sourceOptions = toOptions(taskSourceLabels);
const execStatusColorMap: Record<string, string> = {
  Idle: 'default',
  Running: 'processing',
  PendingReview: 'warning',
  Rejected: 'error',
  Success: 'green',
  Failed: 'red',
  Timeout: 'orange',
  Hibernated: 'purple',
};

const claimStatusColorMap: Record<string, string> = {
  Pending: 'default',
  Claimed: 'blue',
  Delivered: 'green',
};

const typeColorMap: Record<string, string> = {
  Development: 'blue',
  Bug: 'red',
  Design: 'purple',
  Testing: 'orange',
  Docs: 'green',
  Other: 'default',
};

const complexityColorMap: Record<string, string> = {
  Low: 'green',
  Medium: 'blue',
  High: 'orange',
  VeryHigh: 'red',
};

const formOptions: VbenFormProps = {
  schema: [
    {
      label: '标题',
      fieldName: 'keyword',
      component: 'Input',
      componentProps: { placeholder: '请输入标题关键词' },
    },
    {
      label: '项目',
      fieldName: 'projectId',
      component: 'ApiSelect',
      componentProps: {
        placeholder: '请选择项目',
        api: getProjectList,
        afterFetch: (data: any) => data.items.map((item: any) => ({ label: item.name, value: item.id })),
        allowClear: true,
        showSearch: true,
        filterOption: true,
      },
    },
    {
      label: '接单状态',
      fieldName: 'claimStatus',
      component: 'Select',
      componentProps: { placeholder: '请选择', options: claimStatusOptions, allowClear: true },
    },
    {
      label: '执行状态',
      fieldName: 'execStatus',
      component: 'Select',
      componentProps: { placeholder: '请选择', options: execStatusOptions, allowClear: true },
    },
    {
      label: '类型',
      fieldName: 'type',
      component: 'Select',
      componentProps: { placeholder: '请选择', options: typeOptions, allowClear: true },
    },
    {
      label: '来源',
      fieldName: 'source',
      component: 'Select',
      componentProps: { placeholder: '请选择', options: sourceOptions, allowClear: true },
    },
  ],
  wrapperClass: 'grid-cols-3',
  showCollapseButton: true,
  submitOnChange: false,
  submitOnEnter: true,
};

const gridOptions: VxeGridProps = {
  columns: [
    { fixed: 'left', field: 'id', title: 'Id', width: 50, visible: false },
    { field: 'title', title: '信息', slots: { default: 'info' }, minWidth: 300 },
    { field: 'type', title: '状态', slots: { default: 'status' }, width: 280 },
    { field: 'source', title: '来源', slots: { default: 'source' }, width: 90 },
    { field: 'action', fixed: 'right', slots: { default: 'action' }, title: '操作', width: 200, align: 'center' },
  ],
  height: 'auto',
  keepSource: true,
  pagerConfig: {},
  proxyConfig: {
    ajax: {
      query: async ({ page }, formValues) => {
        return await getList({ page: page.currentPage, pageSize: page.pageSize, ...formValues });
      },
    },
  },
  showOverflow: false,
  rowConfig: { isHover: true },
  toolbarConfig: { refresh: true, custom: true },
};

const [Grid, gridApi] = useVbenVxeGrid({ formOptions, gridOptions });

const handleAdd = () => router.push({ path: '/project/task-item/form' });
const handleEdit = (id: number) => router.push({ path: '/project/task-item/form', query: { id } });
const handleDel = (id: number) => postDel(id).then(() => gridApi.query());

const handleResume = (id: number) => {
  postResume(id).then(() => { message.success('任务已恢复'); gridApi.query(); });
};

const handleCancel = (id: number) => {
  postCancel(id).then(() => { message.success('任务已取消'); gridApi.query(); });
};

const handleViewLogs = (taskItemId: number) => {
  logTaskId.value = taskItemId;
  getLogs({ taskItemId, pageSize: 50 }).then((res: any) => {
    logData.value = res.items || [];
    logVisible.value = true;
  });
};

const loadKanban = async () => {
  if (!kanbanProjectId.value) return;
  const res = await getKanban({ projectId: kanbanProjectId.value });
  kanbanData.value = res || {};
};

const actionLogLabels: Record<string, string> = {
  Created: '创建',
  Claimed: '接单',
  ExecStart: '开始执行',
  ExecSuccess: '执行成功',
  ExecFailed: '执行失败',
  ExecTimeout: '执行超时',
  Hibernated: '休眠',
  Resumed: '恢复',
  Cancelled: '取消',
  SubmittedReview: '提交验收',
  ReviewApproved: '验收通过',
  ReviewRejected: '退回重修',
};

// AI 流程详情抽屉
const detailVisible = ref(false);
const detailData = ref<any>(null);
const detailAttachments = ref<any[]>([]);
const detailLoading = ref(false);

// 解析 commit 改动文件 JSON
const changedFiles = computed(() => {
  const raw = detailData.value?.changedFilesList;
  if (!raw) return [];
  try {
    const arr = JSON.parse(raw);
    return Array.isArray(arr) ? arr : [];
  } catch {
    return [];
  }
});
const fileStatusColor = (status: string) => {
  const map: Record<string, string> = { added: 'green', deleted: 'red', renamed: 'orange', modified: 'blue' };
  return map[status] || 'default';
};

const handleViewDetail = async (id: number) => {
  detailVisible.value = true;
  detailLoading.value = true;
  detailData.value = null;
  detailAttachments.value = [];
  try {
    const [detail, attachments] = await Promise.all([
      getDetail(id),
      getAttachments(id),
    ]);
    detailData.value = detail;
    detailAttachments.value = Array.isArray(attachments) ? attachments : [];
  } finally {
    detailLoading.value = false;
  }
};

// 验收/AI 操作
const reviewLoading = ref<Record<number, boolean>>({});
const aiGenLoading = ref<Record<number, boolean>>({});

// 任务已提交过（执行结束有产出）即可进入验收流程，不必停在 PendingReview。
// 即使已完成（Success）也允许操作员提问题退回，让其他人重新接单。
const reviewableStatuses = ['PendingReview', 'Success', 'Failed', 'Timeout', 'Hibernated'];
const canReview = (execStatus: string) => reviewableStatuses.includes(execStatus);

// 退回弹窗
const rejectModalVisible = ref(false);
const rejectTaskId = ref(0);
const rejectReason = ref('');

const handleReviewApprove = (id: number) => {
  reviewLoading.value[id] = true;
  postReviewApprove({ taskItemId: id, operatorName: 'admin' })
    .then(() => gridApi.query())
    .finally(() => { reviewLoading.value[id] = false; });
};

const handleReviewReject = (id: number) => {
  rejectTaskId.value = id;
  rejectReason.value = '';
  rejectModalVisible.value = true;
};

const confirmReject = () => {
  if (!rejectReason.value.trim()) { message.warning('请输入退回原因'); return; }
  reviewLoading.value[rejectTaskId.value] = true;
  postReviewReject({ taskItemId: rejectTaskId.value, reason: rejectReason.value, operatorName: 'admin' })
    .then(() => { rejectModalVisible.value = false; gridApi.query(); })
    .finally(() => { reviewLoading.value[rejectTaskId.value] = false; });
};

const handleAutoVerify = (id: number) => {
  reviewLoading.value[id] = true;
  postAutoVerify(id)
    .then(() => gridApi.query())
    .finally(() => { reviewLoading.value[id] = false; });
};

// AI 代码生成弹窗
const aiGenModalVisible = ref(false);
const aiGenRow = ref<any>(null);
const aiGenProjectId = ref('');

const handleAiCodeGen = (row: any) => {
  aiGenRow.value = row;
  aiGenProjectId.value = row.project?.gitlabGroupId ? String(row.project.gitlabGroupId) : '';
  aiGenModalVisible.value = true;
};

const confirmAiCodeGen = () => {
  if (!aiGenProjectId.value.trim()) { message.warning('请输入 GitLab Project ID'); return; }
  const id = aiGenRow.value.id;
  aiGenLoading.value[id] = true;
  postAiCodeGenerate({ taskItemId: id, gitlabProjectId: Number(aiGenProjectId.value) })
    .then((res: any) => {
      aiGenModalVisible.value = false;
      message.success(`代码生成完成，分支: ${res?.branchName || '已提交'}`);
      gridApi.query();
    })
    .finally(() => { aiGenLoading.value[id] = false; });
};
</script>

<template>
  <Page auto-content-height>
    <div style="margin-bottom: 12px">
      <Segmented v-model:value="viewMode" :options="[{ label: '表格', value: 'table' }, { label: '看板', value: 'kanban' }]" />
    </div>

    <!-- 表格视图 -->
    <Grid v-show="viewMode === 'table'">
      <template #toolbar-tools>
        <Button type="primary" @click="handleAdd">新增任务</Button>
      </template>

      <template #info="{ row }">
        <TaskItemInfoCell :record="row" group="info" show-project show-requirement click-title @click-title="handleEdit" />
      </template>

      <template #status="{ row }">
        <TaskItemInfoCell :record="row" group="status" />
      </template>

      <template #source="{ row }">
        <Tag v-if="row.source === 'hermes'" color="gold">🤖 Hermes</Tag>
        <Tag v-else-if="row.source === 'tg_bot'" color="cyan">TG</Tag>
        <Tag v-else-if="row.source === 'ai_auto'" color="purple">AI</Tag>
        <Tag v-else-if="row.source" color="default">{{ taskSourceLabels[row.source] || row.source }}</Tag>
        <span v-else style="color: #ccc;">—</span>
      </template>

      <template #action="{ row }">
        <Space wrap>
          <Button size="small" type="primary" @click="handleEdit(row.id)">编辑</Button>
          <Button size="small" @click="handleViewDetail(row.id)">详情</Button>
          <Button size="small" @click="handleViewLogs(row.id)">日志</Button>
          <!-- 验收流程操作 (#110)：任务提交过（待验收/已完成/失败/超时/休眠）都可验收/退回 -->
          <template v-if="canReview(row.execStatus)">
            <Button v-if="row.execStatus !== 'Success' || !row.verified" size="small" type="primary" :loading="reviewLoading[row.id]" @click="handleReviewApprove(row.id)">✅ 通过</Button>
            <Button size="small" danger :loading="reviewLoading[row.id]" @click="handleReviewReject(row.id)">↩ 退回</Button>
            <Button size="small" :loading="reviewLoading[row.id]" @click="handleAutoVerify(row.id)">🤖 AI 验证</Button>
          </template>
          <!-- AI 代码生成 (#138): 仅 Running 状态可触发 -->
          <Button v-if="row.execStatus === 'Running'" size="small" :loading="aiGenLoading[row.id]" @click="handleAiCodeGen(row)">⚡ AI 生成代码</Button>
          <Button v-if="row.execStatus === 'Hibernated'" size="small" type="primary" ghost @click="handleResume(row.id)">恢复</Button>
          <Popconfirm title="确定取消吗？" @confirm="handleCancel(row.id)">
            <Button v-if="row.execStatus === 'Idle' || row.execStatus === 'Running'" size="small" danger>取消</Button>
          </Popconfirm>
          <Popconfirm title="确定删除吗？" @confirm="handleDel(row.id)">
            <Button size="small" type="primary" danger>删除</Button>
          </Popconfirm>
        </Space>
      </template>
    </Grid>

    <DocPreview ref="docRef" source-type="TaskItem" />

    <!-- 退回重修弹窗 (#110) -->
    <Modal v-model:open="rejectModalVisible" title="退回重修" @ok="confirmReject" ok-text="确认退回" cancel-text="取消">
      <p style="margin-bottom: 8px; color: var(--ant-color-text-secondary);">请填写退回原因，Agent 重新执行时可参考：</p>
      <Textarea v-model:value="rejectReason" placeholder="退回原因（必填）" :rows="3" />
    </Modal>

    <!-- AI 代码生成弹窗 (#138) -->
    <Modal v-model:open="aiGenModalVisible" :title="`AI 生成代码 — #${aiGenRow?.id} ${aiGenRow?.title}`" @ok="confirmAiCodeGen" ok-text="开始生成" cancel-text="取消" :confirm-loading="aiGenLoading[aiGenRow?.id]">
      <p style="margin-bottom: 8px; color: var(--ant-color-text-secondary);">AI 将根据任务描述生成代码，自动建分支并创建 MR。</p>
      <div style="margin-bottom: 8px;">
        <span style="font-size: 13px;">GitLab Project ID：</span>
        <Input v-model:value="aiGenProjectId" placeholder="请输入 GitLab Project ID（数字）" style="margin-top: 4px;" />
      </div>
    </Modal>

    <!-- 看板视图 -->
    <div v-show="viewMode === 'kanban'" style="padding: 0 16px;">
      <div style=" display: flex; gap: 12px; align-items: center;margin-bottom: 16px;">
        <span>选择项目查看看板：</span>
        <!-- 简单使用 projectId 输入 -->
        <input v-model.number="kanbanProjectId" type="number" placeholder="项目ID" style="width: 120px; padding: 4px 8px; border: 1px solid #d9d9d9; border-radius: 4px;" />
        <Button type="primary" size="small" @click="loadKanban">加载</Button>
      </div>
      <div style="display: flex; gap: 16px; overflow-x: auto;">
        <Card v-for="(tasks, status) in kanbanData" :key="status" :title="taskItemExecStatusLabels[status] || status" style=" flex: 1;min-width: 260px;">
          <div v-for="task in tasks" :key="task.id" style=" padding: 8px;margin-bottom: 8px; cursor: pointer; border: 1px solid #f0f0f0; border-radius: 4px;" @click="handleEdit(task.id)">
            <div style="font-weight: 500;">{{ task.title }}</div>
            <div style=" display: flex; gap: 4px;margin-top: 4px;">
              <Tag :color="typeColorMap[task.type] || 'default'" size="small">{{ taskItemTypeLabels[task.type] }}</Tag>
            </div>
            <div v-if="task.claimedBy" style="margin-top: 4px; font-size: 12px; color: #999;">{{ task.claimedBy }}</div>
          </div>
          <div v-if="!tasks || tasks.length === 0" style="color: #999; text-align: center;">暂无任务</div>
        </Card>
      </div>
    </div>

    <!-- AI 流程详情抽屉 (#110) -->
    <Drawer v-model:open="detailVisible" title="任务详情 / AI 流程" :width="560" placement="right">
      <div v-if="detailLoading" style="padding: 40px 0; text-align: center; color: var(--ant-color-text-secondary);">加载中...</div>
      <template v-else-if="detailData">
        <!-- 基本信息 -->
        <Card size="small" title="基本信息" style="margin-bottom: 12px;">
          <table style="width: 100%; font-size: 13px; border-collapse: collapse;">
            <tr v-for="(item, i) in [
              ['标题', detailData.title],
              ['类型', taskItemTypeLabels[detailData.type] || detailData.type],
              ['接单状态', taskItemClaimStatusLabels[detailData.claimStatus] || detailData.claimStatus],
              ['执行状态', taskItemExecStatusLabels[detailData.execStatus] || detailData.execStatus],
              ['执行模式', detailData.execMode],
              ['负责人', detailData.assignee || '—'],
              ['接单者', detailData.claimedBy || '—'],
              ['最大重试', detailData.maxRetryCount],
              ['超时(秒)', detailData.timeoutSeconds],
              ['截止日期', detailData.dueDate || '—'],
            ]" :key="i" style='border-bottom: 1px solid var(--ant-color-split);'>
              <td style='padding: 6px 8px; color: var(--ant-color-text-secondary); width: 90px; white-space: nowrap;'>{{ item[0] }}</td>
              <td style='padding: 6px 8px;'>{{ item[1] }}</td>
            </tr>
          </table>
        </Card>

        <!-- AI 流程状态 -->
        <Card size="small" title="AI 验收流程" style="margin-bottom: 12px;">
          <div style="display: flex; flex-direction: column; gap: 10px; font-size: 13px;">
            <!-- 验收状态 -->
            <div style="display: flex; align-items: center; gap: 8px;">
              <span style="color: var(--ant-color-text-secondary); width: 80px;">验收状态</span>
              <Tag v-if="detailData.verified" color="success">✅ 已验收通过</Tag>
              <Tag v-else-if="detailData.execStatus === 'PendingReview'" color="warning">⏳ 待验收</Tag>
              <Tag v-else color="default">未验收</Tag>
            </div>

            <!-- 修复说明 -->
            <div v-if="detailData.fixedDescription">
              <div style="color: var(--ant-color-text-secondary); margin-bottom: 4px;">修复说明（Agent 提交）</div>
              <div style="
                padding: 8px 12px;
                background: var(--ant-color-fill-tertiary);
                border-radius: 6px;
                white-space: pre-wrap;
                line-height: 1.6;
              ">{{ detailData.fixedDescription }}</div>
            </div>

            <!-- 退回原因 -->
            <div v-if="detailData.rejectReason">
              <div style="color: var(--ant-color-error); margin-bottom: 4px;">退回原因</div>
              <div style="
                padding: 8px 12px;
                background: var(--ant-color-error-bg);
                border-radius: 6px;
                color: var(--ant-color-error-text);
                white-space: pre-wrap;
                line-height: 1.6;
              ">{{ detailData.rejectReason }}</div>
            </div>

            <!-- 位置提示 -->
            <div v-if="detailData.routeHint || detailData.filePathHint || detailData.lineHint">
              <div style="color: var(--ant-color-text-secondary); margin-bottom: 4px;">代码位置提示</div>
              <div style="padding: 8px 12px; background: var(--ant-color-fill-tertiary); border-radius: 6px; font-size: 12px; font-family: monospace;">
                <div v-if="detailData.routeHint">路由：{{ detailData.routeHint }}</div>
                <div v-if="detailData.filePathHint">文件：{{ detailData.filePathHint }}</div>
                <div v-if="detailData.lineHint">行号：{{ detailData.lineHint }}</div>
              </div>
            </div>

            <!-- 空提示 -->
            <div v-if="!detailData.fixedDescription && !detailData.rejectReason && !detailData.routeHint"
              style="color: var(--ant-color-text-tertiary); text-align: center; padding: 12px 0;">
              暂无验收流程信息
            </div>
          </div>
        </Card>

        <!-- Git 提交关联 (#任务交付追溯) -->
        <Card v-if="detailData.commitSha" size="small" title="Git 提交" style="margin-bottom: 12px;">
          <div style="font-size: 13px; display: flex; flex-direction: column; gap: 8px;">
            <div style="display: flex; align-items: center; gap: 8px;">
              <span style="color: var(--ant-color-text-secondary); width: 70px;">Commit</span>
              <Tag color="blue" style="font-family: monospace;">{{ detailData.commitSha.slice(0, 8) }}</Tag>
              <span v-if="detailData.commitBranch" style="color: var(--ant-color-text-secondary);">@{{ detailData.commitBranch }}</span>
            </div>
            <div v-if="detailData.commitAuthor" style="display: flex; align-items: center; gap: 8px;">
              <span style="color: var(--ant-color-text-secondary); width: 70px;">提交者</span>
              <span>{{ detailData.commitAuthor }}</span>
            </div>
            <div v-if="changedFiles.length > 0">
              <div style="color: var(--ant-color-text-secondary); margin-bottom: 4px;">改动文件（{{ changedFiles.length }}）</div>
              <div style="padding: 8px 12px; background: var(--ant-color-fill-tertiary); border-radius: 6px; font-family: monospace; font-size: 12px; max-height: 200px; overflow-y: auto;">
                <div v-for="(f, i) in changedFiles" :key="i" style="display: flex; gap: 6px; padding: 2px 0;">
                  <Tag :color="fileStatusColor(f.status)" size="small" style="margin: 0; min-width: 60px; text-align: center;">{{ f.status }}</Tag>
                  <span>{{ f.path }}</span>
                </div>
              </div>
            </div>
          </div>
        </Card>

        <!-- 附件列表 -->
        <Card v-if="detailAttachments.length > 0" size="small" title="附件" style="margin-bottom: 12px;">
          <div v-for="att in detailAttachments" :key="att.id"
            style="display: flex; align-items: center; justify-content: space-between; padding: 6px 0; border-bottom: 1px solid var(--ant-color-split);">
            <span style="font-size: 13px;">{{ att.fileName }}</span>
            <a :href="att.fileUrl" target="_blank">
              <Button size="small">下载</Button>
            </a>
          </div>
        </Card>

        <!-- 操作按钮：任务提交过即可验收/退回 -->
        <div v-if="canReview(detailData.execStatus)" style="display: flex; gap: 8px; margin-top: 8px; flex-wrap: wrap;">
          <Button v-if="detailData.execStatus !== 'Success' || !detailData.verified" type="primary" :loading="reviewLoading[detailData.id]" @click="handleReviewApprove(detailData.id); detailVisible = false">✅ 验收通过</Button>
          <Button danger :loading="reviewLoading[detailData.id]" @click="handleReviewReject(detailData.id)">↩ 退回重修</Button>
          <Button :loading="reviewLoading[detailData.id]" @click="handleAutoVerify(detailData.id); detailVisible = false">🤖 AI 自动验证</Button>
        </div>
      </template>
    </Drawer>

    <!-- 日志抽屉 -->
    <Drawer v-model:open="logVisible" title="任务日志" :width="520" placement="right">
      <div v-for="log in logData" :key="log.id" style=" padding: 8px;margin-bottom: 12px; border-bottom: 1px solid #f0f0f0;">
        <div style="display: flex; align-items: center; justify-content: space-between;">
          <Tag :color="log.action === 'ExecFailed' || log.action === 'ExecTimeout' ? 'red' : log.action === 'ExecSuccess' ? 'green' : 'blue'">
            {{ actionLogLabels[log.action] || log.action }}
          </Tag>
          <span style="font-size: 12px; color: #999;">{{ log.createdTime }}</span>
        </div>
        <div v-if="log.operator" style=" margin-top: 4px;font-size: 12px; color: #666;">操作者: {{ log.operator }}</div>
        <div v-if="log.duration" style="font-size: 12px; color: #666;">耗时: {{ log.duration }}s</div>
        <div v-if="log.message" style="margin-top: 4px; font-size: 13px; white-space: pre-wrap;">{{ log.message }}</div>
      </div>
      <div v-if="logData.length === 0" style=" padding: 40px 0; color: #999;text-align: center;">暂无日志</div>
    </Drawer>
  </Page>
</template>

<script lang="ts" setup>
import { onBeforeUnmount, ref } from 'vue';
import { useRouter } from 'vue-router';

import {
  Button,
  Drawer,
  Dropdown,
  Empty,
  Menu,
  MenuDivider,
  MenuItem,
  Modal,
  Space,
  Spin,
  Tag,
} from 'ant-design-vue';

import {
  getDeployStatus,
  getSchemesBySubProject,
  postOneClickDeploy,
} from '../api';

const props = defineProps<{
  subProject: any;
}>();

const router = useRouter();

// ===== 方案下拉 =====
const schemes = ref<any[]>([]);
const schemesLoading = ref(false);

async function loadSchemes(open: boolean) {
  if (!open) return;
  schemesLoading.value = true;
  try {
    schemes.value = (await getSchemesBySubProject(props.subProject.id)) || [];
  } finally {
    schemesLoading.value = false;
  }
}

// 部署模式标签
const buildSourceLabels: Record<string, string> = {
  GitLabCi: 'CI 产物',
  LocalBuild: '本机构建',
  LastArtifact: '复用上次',
};
const strategyLabels: Record<string, string> = {
  Replace: '原地覆盖',
  BlueGreen: '蓝绿',
  Rolling: '滚动',
  Canary: '灰度',
};
const buildStatusMeta: Record<string, { color: string; text: string }> = {
  PendingApproval: { color: 'warning', text: '待审批' },
  Approved: { color: 'processing', text: '已批准' },
  Building: { color: 'processing', text: '构建中' },
  Success: { color: 'success', text: '构建成功' },
  Failed: { color: 'error', text: '构建失败' },
  Rejected: { color: 'default', text: '已驳回' },
};
const deployStatusMeta: Record<string, { color: string; text: string }> = {
  Pending: { color: 'warning', text: '待审批' },
  Approved: { color: 'processing', text: '已批准' },
  Deploying: { color: 'processing', text: '部署中' },
  Success: { color: 'success', text: '部署成功' },
  Failed: { color: 'error', text: '部署失败' },
  Rollback: { color: 'default', text: '已回滚' },
};

// ===== 进度抽屉 + 轮询 =====
const drawerOpen = ref(false);
const currentBuildId = ref<number | undefined>();
const currentScheme = ref<any>(null);
const status = ref<any>(null);
let pollTimer: ReturnType<typeof setInterval> | null = null;

const BUILD_TERMINAL = new Set(['Failed', 'Rejected']);
const DEPLOY_TERMINAL = new Set(['Success', 'Failed', 'Rollback']);

function stopPoll() {
  if (pollTimer) {
    clearInterval(pollTimer);
    pollTimer = null;
  }
}

async function poll() {
  if (!currentBuildId.value) return;
  try {
    const res: any = await getDeployStatus({ buildId: currentBuildId.value });
    status.value = res;
    const buildDone = BUILD_TERMINAL.has(res.status) || res.status === 'Success';
    const deployDone = res.deploy_status && DEPLOY_TERMINAL.has(res.deploy_status);
    // 构建失败/驳回 → 结束;构建成功且部署到终态 → 结束
    if (BUILD_TERMINAL.has(res.status) || (buildDone && deployDone)) {
      stopPoll();
    }
  } catch {
    // 轮询失败忽略,下一拍再试
  }
}

function confirmDeploy(scheme: any) {
  if (!scheme.is_enabled) return;
  Modal.confirm({
    title: `一键部署:${scheme.name}`,
    content: `子项目「${props.subProject.name}」将按【${strategyLabels[scheme.deploy_strategy] || scheme.deploy_strategy}】策略,从【${buildSourceLabels[scheme.build_source] || scheme.build_source}】出产物,部署到 ${scheme.server_count} 台服务器。是否继续?`,
    okText: '开始部署',
    cancelText: '取消',
    onOk: () => triggerDeploy(scheme),
  });
}

async function triggerDeploy(scheme: any) {
  // 触发成功的 toast 由 axios 拦截器自动弹出,这里不重复
  const res: any = await postOneClickDeploy({
    profileId: scheme.id,
    branch: scheme.default_branch,
  });
  currentScheme.value = scheme;
  currentBuildId.value = res.build_id;
  status.value = { status: 'Building', version: res.version, branch: res.branch };
  drawerOpen.value = true;
  stopPoll();
  pollTimer = setInterval(poll, 2000);
  poll();
}

function onDrawerClose() {
  drawerOpen.value = false;
  stopPoll();
}

function goManageSchemes() {
  router.push({
    path: '/build-release/pipeline-profile',
    query: {
      action: 'add',
      projectId: props.subProject.projectId,
      subProjectId: props.subProject.id,
    },
  });
}

onBeforeUnmount(stopPoll);
</script>

<template>
  <span>
    <Dropdown :trigger="['click']" @open-change="loadSchemes">
      <Button type="primary" size="small">🚀 部署</Button>
      <template #overlay>
        <Menu style="min-width: 240px">
          <Spin :spinning="schemesLoading">
            <template v-if="schemes.length > 0">
              <MenuItem
                v-for="s in schemes"
                :key="s.id"
                :disabled="!s.is_enabled"
                @click="confirmDeploy(s)"
              >
                <div style="display: flex; flex-direction: column; gap: 2px; padding: 2px 0">
                  <div style="display: flex; gap: 6px; align-items: center">
                    <span style="font-weight: 500">{{ s.name }}</span>
                    <Tag v-if="s.environment" color="blue" style="margin: 0">{{ s.environment }}</Tag>
                  </div>
                  <div style="display: flex; gap: 6px; align-items: center; font-size: 11px; color: var(--ant-color-text-tertiary)">
                    <span>{{ buildSourceLabels[s.build_source] || s.build_source }}</span>
                    <span>·</span>
                    <span>{{ strategyLabels[s.deploy_strategy] || s.deploy_strategy }}</span>
                    <span>·</span>
                    <span>{{ s.server_count }} 台</span>
                    <Tag
                      v-if="s.last_deploy_status"
                      :color="deployStatusMeta[s.last_deploy_status]?.color || 'default'"
                      style="margin: 0"
                    >
                      {{ deployStatusMeta[s.last_deploy_status]?.text || s.last_deploy_status }}
                    </Tag>
                  </div>
                </div>
              </MenuItem>
            </template>
            <Empty
              v-else-if="!schemesLoading"
              :image="Empty.PRESENTED_IMAGE_SIMPLE"
              description="暂无部署方案"
              style="padding: 12px 8px"
            />
          </Spin>
          <MenuDivider />
          <MenuItem key="__manage" @click="goManageSchemes">
            <span style="color: var(--ant-color-primary)">⚙ 管理部署方案…</span>
          </MenuItem>
        </Menu>
      </template>
    </Dropdown>

    <!-- 部署进度抽屉 -->
    <Drawer
      :open="drawerOpen"
      :title="`部署进度 · ${currentScheme?.name || ''}`"
      width="640"
      @close="onDrawerClose"
    >
      <div v-if="status">
        <!-- 概要 -->
        <Space style="margin-bottom: 16px">
          <Tag color="blue">{{ status.branch }}</Tag>
          <span>版本 <code>{{ status.version }}</code></span>
        </Space>

        <!-- 阶段:构建 -->
        <div style="margin-bottom: 12px; padding: 12px; border: 1px solid var(--ant-color-border); border-radius: 6px">
          <div style="display: flex; gap: 8px; align-items: center; margin-bottom: 8px">
            <span style="font-weight: 600">① 构建</span>
            <Tag :color="buildStatusMeta[status.status]?.color || 'default'">
              {{ buildStatusMeta[status.status]?.text || status.status }}
            </Tag>
            <span v-if="status.duration" style="font-size: 12px; color: var(--ant-color-text-tertiary)">{{ status.duration }}s</span>
          </div>
          <pre v-if="status.build_log" style="max-height: 200px; overflow: auto; margin: 0; padding: 8px; font-size: 12px; background: var(--ant-color-fill-quaternary); border-radius: 4px; white-space: pre-wrap">{{ status.build_log }}</pre>
        </div>

        <!-- 阶段:部署 -->
        <div style="padding: 12px; border: 1px solid var(--ant-color-border); border-radius: 6px">
          <div style="display: flex; gap: 8px; align-items: center; margin-bottom: 8px">
            <span style="font-weight: 600">② 部署</span>
            <Tag v-if="status.deploy_status" :color="deployStatusMeta[status.deploy_status]?.color || 'default'">
              {{ deployStatusMeta[status.deploy_status]?.text || status.deploy_status }}
            </Tag>
            <span v-else style="font-size: 12px; color: var(--ant-color-text-tertiary)">
              {{ status.status === 'Success' ? '准备中…' : '等待构建完成' }}
            </span>
          </div>
          <pre v-if="status.deploy_log" style="max-height: 240px; overflow: auto; margin: 0; padding: 8px; font-size: 12px; background: var(--ant-color-fill-quaternary); border-radius: 4px; white-space: pre-wrap">{{ status.deploy_log }}</pre>
        </div>
      </div>
      <Empty v-else description="暂无进度信息" />
    </Drawer>
  </span>
</template>

<script lang="ts" setup>
import { onMounted, ref } from 'vue';

import { Page } from '@vben/common-ui';

import {
  Alert,
  Button,
  Card,
  Form,
  FormItem,
  Input,
  InputNumber,
  RadioButton,
  RadioGroup,
  Select,
  SelectOption,
  Space,
  Switch,
  Textarea,
} from 'ant-design-vue';

import { getConfig, saveConfig } from './api';

interface CliConfig {
  id?: number;
  runAsUser?: string;
  defaultShell?: string;
  defaultCwd?: string;
  defaultEnvJson?: string;
  sshPrivateKey?: string;
  sshPrivateKeyPassphrase?: string;
  scrollbackBytes?: number;
  autoCleanDays?: number;
  totalCapMb?: number;
  idleSessionDays?: number;
  // 视觉
  fontFamily?: string;
  fontSize?: number;
  lineHeight?: number;
  letterSpacing?: number;
  theme?: string;
  customBgColor?: string;
  customFgColor?: string;
  customCursorColor?: string;
  cursorStyle?: string;
  cursorBlink?: boolean;
  xtermScrollback?: number;
}

// 字体家族预设
const FONT_FAMILY_PRESETS = [
  { label: 'Menlo (macOS 默认)', value: 'Menlo, Consolas, "Liberation Mono", monospace' },
  { label: 'Consolas (Windows)', value: 'Consolas, Menlo, monospace' },
  { label: 'JetBrains Mono', value: '"JetBrains Mono", Menlo, Consolas, monospace' },
  { label: 'Fira Code', value: '"Fira Code", Menlo, Consolas, monospace' },
  { label: 'Source Code Pro', value: '"Source Code Pro", Menlo, Consolas, monospace' },
  { label: 'Cascadia Code', value: '"Cascadia Code", Menlo, Consolas, monospace' },
];

// 主题预设
const THEME_PRESETS = [
  { label: '深色 (默认)', value: 'dark' },
  { label: '浅色', value: 'light' },
  { label: 'Solarized Dark', value: 'solarized-dark' },
  { label: 'Solarized Light', value: 'solarized-light' },
  { label: 'Monokai', value: 'monokai' },
  { label: '自定义', value: 'custom' },
];

// 一键全套预设(字体 + 字号 + 主题 + 光标 + scrollback 一起换)
interface AppearancePreset {
  name: string;
  desc: string;
  config: Partial<CliConfig>;
}
const APPEARANCE_PRESETS: AppearancePreset[] = [
  {
    name: '经典',
    desc: 'Menlo 14px / 深色 / 方块光标 — 出厂默认',
    config: {
      fontFamily: 'Menlo, Consolas, "Liberation Mono", monospace',
      fontSize: 14,
      lineHeight: 1.2,
      letterSpacing: 0,
      theme: 'dark',
      cursorStyle: 'block',
      cursorBlink: true,
      xtermScrollback: 5000,
    },
  },
  {
    name: 'VSCode 风',
    desc: 'Consolas 14px / 深色 / 竖线光标',
    config: {
      fontFamily: 'Consolas, Menlo, monospace',
      fontSize: 14,
      lineHeight: 1.2,
      letterSpacing: 0,
      theme: 'dark',
      cursorStyle: 'bar',
      cursorBlink: true,
      xtermScrollback: 10_000,
    },
  },
  {
    name: 'JetBrains 紧凑',
    desc: 'JetBrains Mono 13px / Monokai / 不闪烁',
    config: {
      fontFamily: '"JetBrains Mono", Menlo, Consolas, monospace',
      fontSize: 13,
      lineHeight: 1.15,
      letterSpacing: 0,
      theme: 'monokai',
      cursorStyle: 'block',
      cursorBlink: false,
      xtermScrollback: 10_000,
    },
  },
  {
    name: 'Solarized 护眼',
    desc: 'Source Code Pro 15px / Solarized Dark / 下划线光标',
    config: {
      fontFamily: '"Source Code Pro", Menlo, Consolas, monospace',
      fontSize: 15,
      lineHeight: 1.3,
      letterSpacing: 0.5,
      theme: 'solarized-dark',
      cursorStyle: 'underline',
      cursorBlink: true,
      xtermScrollback: 5000,
    },
  },
  {
    name: '浅色 (Light)',
    desc: 'Menlo 14px / 浅色背景 / 适合屏幕亮的环境',
    config: {
      fontFamily: 'Menlo, Consolas, "Liberation Mono", monospace',
      fontSize: 14,
      lineHeight: 1.2,
      letterSpacing: 0,
      theme: 'light',
      cursorStyle: 'block',
      cursorBlink: true,
      xtermScrollback: 5000,
    },
  },
  {
    name: '大字 (移动端)',
    desc: 'Menlo 16px / 深色 / 行高松一点,手机阅读友好',
    config: {
      fontFamily: 'Menlo, Consolas, "Liberation Mono", monospace',
      fontSize: 16,
      lineHeight: 1.35,
      letterSpacing: 0.5,
      theme: 'dark',
      cursorStyle: 'block',
      cursorBlink: true,
      xtermScrollback: 3000,
    },
  },
];

function applyAppearancePreset(preset: AppearancePreset) {
  // 只覆盖预设包含的字段,不动其他(如 SSH key / shell / cwd 等运维字段)
  Object.assign(form.value, preset.config);
}

const form = ref<CliConfig>({});
const loading = ref(false);

async function load() {
  loading.value = true;
  try {
    form.value = (await getConfig()) || {};
    // 后端把敏感字段返回为 '***',前端展示为占位
    if (form.value.sshPrivateKey === '***')
      form.value.sshPrivateKey = '';
    if (form.value.sshPrivateKeyPassphrase === '***')
      form.value.sshPrivateKeyPassphrase = '';
  } finally {
    loading.value = false;
  }
}

async function onSave() {
  // 前端把空 SSH key 字段转为 '***'(代表"不修改"),非空提交后端会加密
  const payload = { ...form.value };
  if (!payload.sshPrivateKey) payload.sshPrivateKey = '***';
  if (!payload.sshPrivateKeyPassphrase)
    payload.sshPrivateKeyPassphrase = '***';
  await saveConfig(payload);
  await load();
}

onMounted(load);
</script>

<template>
  <Page>
    <Card title="网页终端配置" :loading="loading">
      <Alert
        message="敏感字段(SSH 私钥/口令)留空提交不会清空旧值,如需清空请手动设为单个英文空格再保存"
        type="info"
        show-icon
        class="mb-4"
      />
      <Form layout="vertical">
        <FormItem
          label="runAsUser(留空则以 pm 服务用户身份运行)"
          help="设了之后启动 PTY 会用 sudo -H -E -u <user> 切身份。需运维提前在 sudoers 配 NOPASSWD"
        >
          <Input v-model:value="form.runAsUser" placeholder="如 deploy" />
        </FormItem>
        <FormItem label="默认 Shell">
          <Input v-model:value="form.defaultShell" placeholder="/bin/bash" />
        </FormItem>
        <FormItem label="默认工作目录(留空 → runAsUser 的 HOME 或 /)">
          <Input v-model:value="form.defaultCwd" placeholder="如 /www/projects" />
        </FormItem>
        <FormItem
          label="默认环境变量(JSON)"
          help='格式:[{"key":"X","value":"Y","encrypted":false}, ...] encrypted=true 时 value 会加密存,启动时解密注入'
        >
          <Textarea
            v-model:value="form.defaultEnvJson"
            :rows="6"
            placeholder='[{"key":"PATH","value":"/usr/local/bin:/usr/bin"}]'
          />
        </FormItem>
        <FormItem
          label="Git SSH 私钥"
          help="启动会话时会写到 tmpfs 临时文件 + 设 GIT_SSH_COMMAND,会话退出时擦除"
        >
          <Textarea
            v-model:value="form.sshPrivateKey"
            :rows="6"
            placeholder="-----BEGIN OPENSSH PRIVATE KEY-----..."
          />
        </FormItem>
        <FormItem label="SSH 私钥口令(可选)">
          <Input
            v-model:value="form.sshPrivateKeyPassphrase"
            type="password"
            placeholder="无口令保留为空"
          />
        </FormItem>

        <FormItem label="环形缓冲(bytes,网页重连时 replay 上限)">
          <InputNumber
            v-model:value="form.scrollbackBytes"
            :min="65_536"
            :step="1024"
            class="w-full"
          />
        </FormItem>
        <FormItem label="日志自动清理天数(已结束会话的日志超过 N 天删除)">
          <InputNumber
            v-model:value="form.autoCleanDays"
            :min="1"
            class="w-full"
          />
        </FormItem>
        <FormItem label="日志总容量上限(MB,触发后按 LastActiveAt 升序删非 Running 会话)">
          <InputNumber
            v-model:value="form.totalCapMb"
            :min="100"
            class="w-full"
          />
        </FormItem>
        <FormItem label="空闲会话回收天数(Running 但 N 天无连接 → 自动 kill)">
          <InputNumber
            v-model:value="form.idleSessionDays"
            :min="1"
            class="w-full"
          />
        </FormItem>

        <!-- 终端外观 (xterm.js 视觉) -->
        <div class="section-title">终端外观</div>
        <div class="preset-row">
          <span class="preset-label">一键预设:</span>
          <Button
            v-for="p in APPEARANCE_PRESETS"
            :key="p.name"
            size="small"
            :title="p.desc"
            @click="applyAppearancePreset(p)"
          >
            {{ p.name }}
          </Button>
          <span class="preset-hint">
            (覆盖下方外观项,**不**会自动保存,可继续微调再点保存)
          </span>
        </div>
        <FormItem label="字体" help="预设字体或填自定义 fontFamily 值">
          <Select
            v-model:value="form.fontFamily"
            placeholder="选择或输入自定义"
            allow-clear
            show-search
          >
            <SelectOption
              v-for="p in FONT_FAMILY_PRESETS"
              :key="p.value"
              :value="p.value"
            >
              {{ p.label }}
            </SelectOption>
          </Select>
        </FormItem>
        <div class="form-row">
          <FormItem label="字号 (px)">
            <InputNumber
              v-model:value="form.fontSize"
              :min="8"
              :max="32"
              class="w-full"
            />
          </FormItem>
          <FormItem label="行高">
            <InputNumber
              v-model:value="form.lineHeight"
              :min="0.8"
              :max="2.5"
              :step="0.05"
              class="w-full"
            />
          </FormItem>
          <FormItem label="字间距 (px)">
            <InputNumber
              v-model:value="form.letterSpacing"
              :min="0"
              :max="5"
              :step="0.5"
              class="w-full"
            />
          </FormItem>
        </div>
        <FormItem label="主题">
          <RadioGroup v-model:value="form.theme">
            <RadioButton
              v-for="t in THEME_PRESETS"
              :key="t.value"
              :value="t.value"
            >
              {{ t.label }}
            </RadioButton>
          </RadioGroup>
        </FormItem>
        <div v-if="form.theme === 'custom'" class="form-row">
          <FormItem label="背景色">
            <Input
              v-model:value="form.customBgColor"
              type="color"
              class="color-input"
            />
          </FormItem>
          <FormItem label="前景色 (文字)">
            <Input
              v-model:value="form.customFgColor"
              type="color"
              class="color-input"
            />
          </FormItem>
          <FormItem label="光标色">
            <Input
              v-model:value="form.customCursorColor"
              type="color"
              class="color-input"
            />
          </FormItem>
        </div>
        <div class="form-row">
          <FormItem label="光标形状">
            <RadioGroup v-model:value="form.cursorStyle">
              <RadioButton value="block">方块</RadioButton>
              <RadioButton value="underline">下划线</RadioButton>
              <RadioButton value="bar">竖线</RadioButton>
            </RadioGroup>
          </FormItem>
          <FormItem label="光标闪烁">
            <Switch v-model:checked="form.cursorBlink" />
          </FormItem>
          <FormItem label="xterm 回滚行数(客户端历史)">
            <InputNumber
              v-model:value="form.xtermScrollback"
              :min="100"
              :max="100_000"
              :step="500"
              class="w-full"
            />
          </FormItem>
        </div>

        <Alert
          message="外观改动保存后,在网页终端 reload / 新开会话生效(不实时热更)"
          type="info"
          show-icon
          class="mb-4"
        />

        <Space>
          <Button type="primary" @click="onSave">保存</Button>
          <Button @click="load">重置</Button>
        </Space>
      </Form>
    </Card>
  </Page>
</template>

<style scoped>
.mb-4 {
  margin-bottom: 16px;
}

.w-full {
  width: 100%;
}

.section-title {
  margin: 20px 0 12px;
  padding-top: 16px;
  font-size: 15px;
  font-weight: 600;
  color: var(--ant-color-text);
  border-top: 1px solid var(--ant-color-split);
}

.form-row {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
  gap: 16px;
}

.color-input {
  width: 80px;
  height: 32px;
  padding: 2px;
}

.preset-row {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  align-items: center;
  margin-bottom: 16px;
  padding: 10px 12px;
  background: var(--ant-color-fill-quaternary);
  border: 1px dashed var(--ant-color-border-secondary);
  border-radius: 4px;
}

.preset-label {
  font-weight: 500;
  color: var(--ant-color-text);
}

.preset-hint {
  flex-basis: 100%;
  font-size: 12px;
  color: var(--ant-color-text-tertiary);
}
</style>

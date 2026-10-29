<script lang="ts" setup>
import { computed } from 'vue';

import { useAntdDesignTokens } from '@vben/hooks';
import { preferences, usePreferences } from '@vben/preferences';

import { App, ConfigProvider, theme } from 'ant-design-vue';

import { antdLocale } from '#/locales';

defineOptions({ name: 'App' });

const { isDark } = usePreferences();
const { tokens } = useAntdDesignTokens();

const tokenTheme = computed(() => {
  const algorithm = isDark.value
    ? [theme.darkAlgorithm]
    : [theme.defaultAlgorithm];

  // antd 紧凑模式算法
  if (preferences.app.compact) {
    algorithm.push(theme.compactAlgorithm);
  }

  return {
    algorithm,
    token: tokens,
  };
});
</script>

<template>
  <ConfigProvider :locale="antdLocale" :theme="tokenTheme">
    <App>
      <RouterView />
    </App>
  </ConfigProvider>
</template>

<!-- 全局样式(非 scoped):移动端隐藏右下角「偏好设置」浮动齿轮按钮。
     它由 layouts 基座以 `fixed bottom-20 right-0` 渲染,移动端通常用不到且挡内容。
     仅按窗口宽度隐藏,不动基座包,避免派生漂移。 -->
<style>
@media (max-width: 768px) {
  .fixed.bottom-20.right-0 {
    display: none !important;
  }
}
</style>

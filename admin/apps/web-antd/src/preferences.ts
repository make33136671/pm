import { defineOverridesPreferences } from '@vben/preferences';

/**
 * @description 项目配置文件
 * 只需要覆盖项目中的一部分配置，不需要的配置不用覆盖，会自动使用默认配置
 * !!! 更改配置后请清空缓存，否则可能不生效
 */
export const overridesPreferences = defineOverridesPreferences({
  // overrides
  app: {
    name: import.meta.env.VITE_APP_TITLE,
    accessMode: 'mixed', //菜单权限模式 前端模式 'frontend' | 后端模式 'backend' | 混合模式 'mixed'
    defaultHomePath: '/profile', // 默认首页路径，避免根路由递归
    defaultAvatar: '/storage/images/logo.png',
  },
  copyright: {
    companyName: import.meta.env.VITE_APP_TITLE,
    companySiteLink: '',
    date: '2025',
  },
  logo: {
    source: '/storage/images/logo.png',
  },
  widget: {
    // 顶部全局搜索框用不到，关闭；改为顶部「进入网页终端」按钮（见 layouts/basic.vue）
    globalSearch: false,
  },
});

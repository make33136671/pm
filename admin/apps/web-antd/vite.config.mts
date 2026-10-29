import { defineConfig } from '@vben/vite-config';

export default defineConfig(async () => {
  return {
    application: {},
    vite: {
      server: {
        proxy: {
          '/api': {
            changeOrigin: true,
            rewrite: (path) => path.replace(/^\/api/, ''),
            target: 'http://localhost:6020/api',
            ws: true,
          },
          // 后端品牌资源（Logo/Favicon/app-info）走 6020
          // 由安装向导生成到 Sb.Apps.Pm.WebApi/wwwroot/storage/，前端不再保留副本
          '/storage': {
            changeOrigin: true,
            target: 'http://localhost:6020',
          },
          '/upload': {
            changeOrigin: true,
            target: 'http://localhost:6020',
          },
        },
      },
    },
  };
});

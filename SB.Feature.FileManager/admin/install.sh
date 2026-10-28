#!/usr/bin/env bash
# 把文件管理器的前端页面接入 sb-framework-admin（Vben Admin fork）。
#
# 用法：
#   bash SB.Feature.FileManager/admin/install.sh ~/saibo/sb/sb-framework-admin
#
# 做三件事：
#   1. 复制 views/file-manager/{List.vue,api.ts} 到 apps/web-antd/src/views/file-manager/
#   2. 幂等写入菜单定义（后端 IModuleInitializer 也可声明，二选一）
#      —— 本脚本只打印提示，不自动改菜单表；菜单由后端 GetMenus() 声明时无需任何操作。
#   3. 提示手动核对点（vben admin 的 views 自动扫描路由，通常无需改 router）。
set -euo pipefail

ADMIN_DIR="${1:-$HOME/saibo/sb/sb-framework-admin}"
SRC_DIR="$(cd "$(dirname "$0")" && pwd)/views/file-manager"
DEST_DIR="$ADMIN_DIR/apps/web-antd/src/views/file-manager"

if [ ! -d "$ADMIN_DIR/apps/web-antd" ]; then
  echo "!! 目标不是 sb-framework-admin 仓库: $ADMIN_DIR" >&2
  exit 1
fi

mkdir -p "$DEST_DIR"
cp "$SRC_DIR/List.vue" "$SRC_DIR/api.ts" "$DEST_DIR/"
echo "==> 已复制前端页面到 $DEST_DIR"

cat <<'EOF'

下一步：
  1. 确认后端已引用 SB.Feature.FileManager 包，并在 appsettings 配好 Features:FileManager。
  2. 菜单：若未用后端 IModuleInitializer.GetMenus() 声明，在管理后台「菜单管理」手动加一条：
       name:        文件管理
       path:        /file-manager
       component:   file-manager/List
       title:       文件管理
       icon:        lucide:folder-search
  3. 构建前端：cd sb-framework-admin && pnpm install && pnpm build
  4. 路由约定：vben admin 按 views 目录自动注册，component 写 "file-manager/List" 即可。
EOF

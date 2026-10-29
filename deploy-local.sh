#!/bin/bash
# 本机发布脚本
#   ./deploy-local.sh          → 构建并部署 api + admin
#   ./deploy-local.sh api      → 仅 api
#   ./deploy-local.sh admin    → 仅 admin
#
# 规则：
#   - /opt/api/appsettings*.json、client.key、logs/、installed.lock 不覆盖不删除
#   - /opt/api/installed.lock 不弄丢
set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
API_SRC="$SCRIPT_DIR/api"
ADMIN_SRC="$SCRIPT_DIR/admin"
BUILD_DIR="$SCRIPT_DIR/dist/api"
API_DEST="/opt/api"
ADMIN_DEST="/opt/admin"

TARGET="${1:-all}"

# ─────────────────────────── API ───────────────────────────
build_api() {
  echo "=== 构建 API ==="
  cd "$API_SRC"

  PREFIX=${VERSION_PREFIX:-1.0}
  PATCH=$(git rev-list --count HEAD 2>/dev/null || echo 0)
  VERSION="$PREFIX.$PATCH"
  echo "版本: $VERSION"

  rm -rf "$BUILD_DIR"
  mkdir -p "$BUILD_DIR"

  dotnet publish Sb.Apps.Pm.WebApi/Sb.Apps.Pm.WebApi.csproj \
    -c Release \
    -o "$BUILD_DIR" \
    -p:Version="$VERSION" \
    -p:IncludeSourceRevisionInInformationalVersion=false \
    --nologo \
    -v minimal

  # 发布产物里不带 appsettings（build-publish.sh 同款做法）
  rm -f "$BUILD_DIR"/appsettings.json "$BUILD_DIR"/appsettings.*.json

  echo ""
  echo "=== 部署 API → $API_DEST ==="
  rsync -a --delete \
    --exclude='appsettings.json' \
    --exclude='appsettings.*.json' \
    --exclude='appsettings-history' \
    --exclude='installed.lock' \
    --exclude='client.key' \
    --exclude='logs' \
    "$BUILD_DIR/" "$API_DEST/"

  echo "✓ API 部署完成 (v$VERSION)"
}

# ─────────────────────────── Admin ─────────────────────────
build_admin() {
  echo "=== 构建 Admin ==="
  cd "$ADMIN_SRC"
  pnpm build:antd

  echo ""
  echo "=== 部署 Admin → $ADMIN_DEST ==="
  rsync -a --delete "$ADMIN_SRC/apps/web-antd/dist/" "$ADMIN_DEST/"

  echo "✓ Admin 部署完成"
}

# ─────────────────────────── 入口 ──────────────────────────
case "$TARGET" in
  api)   build_api ;;
  admin) build_admin ;;
  all)   build_api; echo ""; build_admin ;;
  *)
    echo "用法: $0 [api|admin|all]"
    exit 1
    ;;
esac

echo ""
echo "✓ 全部完成"

#!/usr/bin/env bash
# PM 一键构建发布产物（API + Admin）
#
# 用法：
#   ./build-release.sh              # 构建全部
#   ./build-release.sh api          # 只构建后端
#   ./build-release.sh admin        # 只构建前端
#   VERSION_PREFIX=1.1 ./build-release.sh
#
# 产物：
#   dist/api/                      后端发布目录（可直接 rsync 到服务器）
#   dist/admin/                    前端静态目录（可直接放 nginx 根目录）
#   release/pm-api-<ver>.zip       后端压缩包
#   release/pm-admin-<ver>.zip     前端压缩包
#   release/SHA256SUMS.txt         校验值
#
# 注意事项（都是实际踩过的）：
#   · 版本号取 git commit 数；容器内无 git 时会退化为 0，需手工传 VERSION
#   · dotnet publish 会清空输出目录 → appsettings 单独保留
#   · 前端构建产物文件名带 hash，部署后必须让用户强刷或清 CDN 缓存
#   · 必须用 -c Release，且建议先删 obj/Release 避免用到陈旧编译结果
set -euo pipefail

cd "$(dirname "$0")"

TARGET="${1:-all}"
PREFIX="${VERSION_PREFIX:-1.0}"

# 版本号：git commit 数
PATCH=$(git rev-list --count HEAD 2>/dev/null || echo 0)
VERSION="${VERSION:-$PREFIX.$PATCH}"
SHORT_SHA=$(git rev-parse --short HEAD 2>/dev/null || echo "unknown")

echo "=========================================="
echo " PM 构建发布  版本 $VERSION  ($SHORT_SHA)"
echo "=========================================="
echo ""

mkdir -p dist release

# ── 后端 ─────────────────────────────────────
build_api() {
  echo "=== [API] 清理并构建 ==="
  rm -rf dist/api api/Sb.Apps.Pm.WebApi/obj/Release api/Sb.Apps.Pm.WebApi/bin/Release
  mkdir -p dist/api

  dotnet publish api/Sb.Apps.Pm.WebApi/Sb.Apps.Pm.WebApi.csproj \
    -c Release \
    -o dist/api \
    -p:Version="$VERSION" \
    -p:IncludeSourceRevisionInInformationalVersion=false \
    --nologo -v minimal

  # 按官方 build-publish.sh 惯例：产物不带 appsettings，避免覆盖线上配置
  rm -f dist/api/appsettings.json dist/api/appsettings.*.json

  echo "  ✓ dist/api ($(ls dist/api | wc -l) 文件, $(du -sh dist/api | cut -f1))"
}

# ── 前端 ─────────────────────────────────────
build_admin() {
  echo "=== [Admin] 构建 ==="
  cd admin
  NODE_OPTIONS=--max-old-space-size=8192 pnpm run build:antd
  cd ..

  rm -rf dist/admin
  cp -a admin/apps/web-antd/dist dist/admin
  echo "  ✓ dist/admin ($(du -sh dist/admin | cut -f1))"
}

# ── 打包 ─────────────────────────────────────
pack() {
  echo "=== 打包 ==="
  if command -v zip >/dev/null 2>&1; then
    ( cd dist/api   && zip -qr "../../release/pm-api-$VERSION.zip"   . )
    ( cd dist/admin && zip -qr "../../release/pm-admin-$VERSION.zip" . )
  else
    echo "  !! 未找到 zip 命令，跳过压缩（dist/ 目录已可直接使用）"
    echo "     Ubuntu/Debian: apt-get install -y zip"
    return 0
  fi
  ( cd release && sha256sum pm-*-"$VERSION".zip > SHA256SUMS.txt )
  echo "  ✓ release/pm-api-$VERSION.zip"
  echo "  ✓ release/pm-admin-$VERSION.zip"
  echo "  ✓ release/SHA256SUMS.txt"
}

case "$TARGET" in
  api)   build_api ;;
  admin) build_admin ;;
  all)   build_api; echo; build_admin; echo; pack ;;
  *)     echo "用法: $0 [api|admin|all]"; exit 1 ;;
esac

echo ""
echo "=========================================="
echo " 构建完成  版本 $VERSION"
echo "=========================================="
echo " 产物: dist/api  dist/admin"
[ -f "release/pm-api-$VERSION.zip" ] && echo " 压缩: release/*.zip"
echo ""
echo "部署说明见: dist/DEPLOY.md"

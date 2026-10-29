#!/bin/bash
# PM API 构建发布脚本
#
# 版本号规则（语义化）：
#   Version = <PREFIX>.<git commit 数>     例: 1.0.157
#
# 每次新 commit → patch 自动 +1
# bump minor/major: VERSION_PREFIX=1.1 ./build-publish.sh
set -e

cd "$(dirname "$0")"

PREFIX=${VERSION_PREFIX:-1.0}
PATCH=$(git rev-list --count HEAD 2>/dev/null || echo 0)
VERSION="$PREFIX.$PATCH"

echo "=== PM publish version $VERSION ==="
echo ""

DIST=../dist/api
rm -rf $DIST
mkdir -p $DIST

dotnet publish Sb.Apps.Pm.WebApi/Sb.Apps.Pm.WebApi.csproj \
  -c Release \
  -o $DIST \
  -p:Version=$VERSION \
  -p:IncludeSourceRevisionInInformationalVersion=false \
  --nologo \
  -v minimal

echo ""
echo "=== 移除 appsettings + wwwroot ==="
cd $DIST
rm -f appsettings.json appsettings.*.json
rm -rf wwwroot

echo ""
echo "=== 打 zip ==="
cd ..
rm -f api.zip
cd api && zip -qr ../api.zip .
ls -lh ../api.zip

echo ""
echo "✓ Done. Version: $VERSION"

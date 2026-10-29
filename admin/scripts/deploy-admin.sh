#!/bin/bash
# 构建产物已生成:apps/web-antd/dist.zip
# 通过 PM「部署管理」一步式接口 /admin/App/UploadProject 上传 + 部署 admin 目标。
#
# 用法:
#   PM_URL=https://pm.saibo.club PM_TOKEN=<FullAccess key 或 登录 JWT> ./scripts/deploy-admin.sh
#
# 认证:Authorization: Bearer <token>。token 需是 FullAccess 访问令牌或管理员登录 JWT
#   (UploadProject 无 [Scope] 声明,受限 scoped key 会被拒)。
set -euo pipefail

cd "$(dirname "$0")/.."

ZIP="apps/web-antd/dist.zip"
KEY="admin"

: "${PM_URL:?需要设置 PM_URL,例 https://pm.saibo.club}"
: "${PM_TOKEN:?需要设置 PM_TOKEN(Bearer 令牌)}"

[ -f "$ZIP" ] || { echo "找不到 $ZIP,请先 pnpm build:antd" >&2; exit 1; }

echo "=== 上传 + 部署 admin → ${PM_URL} ($(du -h "$ZIP" | cut -f1)) ==="
curl -fsS -X POST "${PM_URL%/}/admin/App/UploadProject" \
  -H "Authorization: Bearer ${PM_TOKEN}" \
  -F "key=${KEY}" \
  -F "file=@${ZIP};type=application/zip" \
  -w '\nHTTP %{http_code}\n'
echo "✓ 完成"

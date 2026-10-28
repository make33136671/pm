#!/usr/bin/env bash
# 把 GitLab(origin) 的 main 定期同步到 GitHub(github)。
# 用法: bash scripts/sync-github.sh [分支名]   # 默认 main
set -euo pipefail

BRANCH="${1:-main}"
cd "$(dirname "$0")/.."

echo "==> 仓库: $(pwd)"
echo "==> 同步分支: $BRANCH"

# 0. 防御：GitHub 远程必须存在
if ! git remote get-url github >/dev/null 2>&1; then
  echo "!! 缺少 github 远程，请先: git remote add github https://github.com/make33136671/pm.git" >&2
  exit 1
fi

# 1. 工作区必须是干净的，避免把半成品推出去
if [ -n "$(git status --porcelain)" ]; then
  echo "!! 工作区有未提交改动，先 commit 或 stash 再同步" >&2
  git status --short
  exit 1
fi

# 2. 先拉 GitLab，保证推出去的是最新版
echo "==> git pull --ff-only origin $BRANCH"
git pull --ff-only origin "$BRANCH" || echo "   (pull 跳过或已是最新)"

# 3. 推 GitHub（网络抖动重试 3 次）
for i in 1 2 3; do
  echo "==> 第 $i 次 push github $BRANCH"
  if git -c http.version=HTTP/1.1 push github "$BRANCH" "$BRANCH"; then
    echo "==> 同步完成: github/$BRANCH"
    git log --oneline -1
    exit 0
  fi
  [ "$i" -lt 3 ] && { echo "   网络失败，10 秒后重试..."; sleep 10; }
done

echo "!! 三次推送均失败，检查网络或 GitHub 凭据" >&2
exit 1

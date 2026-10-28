#!/usr/bin/env bash
# 把 GitLab(origin) 的 main 定期同步到 GitHub(github)。
#
# 用法：
#   bash scripts/sync-github.sh [分支名]     # 默认 main
#
# 依赖：GitHub 推送凭据。本机没有 GitHub Token 时会在最后一步失败，
# 请先按 README 的「GitHub 推送凭据」一节配置 ~/.git-credentials。
#
# 退出码：0 同步成功 / 1 工作区脏 / 2 缺 github 远程 / 3 推送失败
set -uo pipefail

BRANCH="${1:-main}"
cd "$(dirname "$0")/.." || exit 2

GITHUB_URL="https://github.com/make33136671/pm.git"

echo "[$(date '+%F %T')] ==> 仓库: $(pwd)  分支: $BRANCH"

# 0. 防御：GitHub 远程必须存在，缺了就补回来（「始终保留 github 地址」）
if ! git remote get-url github >/dev/null 2>&1; then
  echo "[$(date '+%F %T')] !! 缺少 github 远程，自动补回: $GITHUB_URL"
  git remote add github "$GITHUB_URL"
fi

# 1. 工作区必须干净，否则可能把半成品推出去
if [ -n "$(git status --porcelain)" ]; then
  echo "[$(date '+%F %T')] !! 工作区有未提交改动，跳过本次同步"
  git status --short
  exit 1
fi

# 2. 先拉 GitLab，保证推出去的是最新版
git pull --ff-only origin "$BRANCH" >/dev/null 2>&1 \
  && echo "[$(date '+%F %T')] ==> GitLab 已是最新" \
  || echo "[$(date '+%F %T')] ==> GitLab pull 跳过（离线或已最新）"

# 3. 推 GitHub，网络抖动重试 3 次
for i in 1 2 3; do
  echo "[$(date '+%F %T')] ==> push github/$BRANCH（第 $i 次）"
  if git -c http.version=HTTP/1.1 push github "$BRANCH:$BRANCH" 2>/tmp/sync-gh-$PID.err; then
    echo "[$(date '+%F %T')] ==> 同步完成: github/$BRANCH  $(git log --oneline -1)"
    rm -f /tmp/sync-gh-$PID.err
    exit 0
  fi
  # 只对网络类错误重试；认证失败重试也没用，直接退出并给出提示
  if grep -qiE 'could not read Username|Authentication failed|403|Permission denied' /tmp/sync-gh-$PID.err; then
    echo "[$(date '+%F %T')] !! GitHub 认证失败，需要配置凭据（见 README「GitHub 推送凭据」）"
    cat /tmp/sync-gh-$PID.err
    rm -f /tmp/sync-gh-$PID.err
    exit 3
  fi
  [ "$i" -lt 3 ] && { echo "   网络失败，30 秒后重试..."; sleep 30; }
done

echo "[$(date '+%F %T')] !! 三次推送均失败"
cat /tmp/sync-gh-$PID.err 2>/dev/null
rm -f /tmp/sync-gh-$PID.err
exit 3

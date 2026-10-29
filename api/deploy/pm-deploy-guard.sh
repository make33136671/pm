#!/bin/bash
# ============================================================================
# pm-deploy-guard —— 部署看门狗（独立于 API 进程，由 supervisord 单独拉起）
#
# 作用：解决"部署后 API 崩溃访问不通 → 必须 SSH"的死锁。它跟 dotnet 进程完全解耦,
#       API 死光它还活着,自动把上一份健康版本恢复回去并重启,替你做掉去终端的动作。
#       见 framework docs/AI_Deploy_Resilience.md。
#
# 机制（DB-free,纯文件快照）:
#   - 平时:API 健康且无部署进行中时,把 /app 二进制快照到 SNAPSHOT_DIR(排除配置/日志)。
#           这就是 "last-good 回滚弹药"。
#   - 部署后:框架 Deploy 会在 /app 落 .deploying 标记。看门狗据此进入观察窗口,
#           WAIT 秒内 /health 未恢复 → 从快照回滚 /app + supervisorctl restart pm-api,
#           记 auto-rollback.log。
#
# 注意:这是 *代码* 回滚。若新版已跑过 DB 迁移再崩,回滚后是"新库+旧码",看门狗只回滚代码并
#       大声告警,不反向改 schema(见文档"数据库迁移红线")。
# ============================================================================
set -uo pipefail

HEALTH_URL="${SB_GUARD_HEALTH_URL:-http://127.0.0.1:6020/health}"
APP_DIR="${SB_GUARD_APP_DIR:-/app}"
SNAPSHOT_DIR="${SB_GUARD_SNAPSHOT_DIR:-/var/sb-lastgood}"
SERVICE="${SB_GUARD_SERVICE:-pm-api}"
WAIT="${SB_GUARD_WAIT:-90}"          # 部署后等待恢复的最长秒数
POLL="${SB_GUARD_POLL:-5}"           # 平时轮询间隔
MARKER="$APP_DIR/.deploying"
LOG="$APP_DIR/logs/auto-rollback.log"

# 快照/回滚时排除的运行态文件(配置/锁/密钥/日志/归档/临时目录)
EXCLUDES=(
  --exclude 'appsettings*.json'
  --exclude 'installed.lock'
  --exclude 'client.key'
  --exclude 'logs/'
  --exclude 'logs'
  --exclude 'deploy_archive/'
  --exclude 'deploy_archive'
  --exclude '.deploying'
  --exclude '.smoke-*'
  --exclude 'appsettings-history/'
)

log() { echo "[$(date -u +%FT%TZ)] $*"; }
alog() { mkdir -p "$(dirname "$LOG")"; echo "[$(date -u +%FT%TZ)] $*" | tee -a "$LOG"; }

healthy() {
  local code
  code=$(curl -s -o /dev/null -w '%{http_code}' --max-time 4 "$HEALTH_URL" 2>/dev/null || echo 000)
  [ "$code" = "200" ]
}

take_snapshot() {
  mkdir -p "$SNAPSHOT_DIR"
  # rsync --delete 让快照精确等于当前 /app 二进制集
  rsync -a --delete "${EXCLUDES[@]}" "$APP_DIR/" "$SNAPSHOT_DIR/" 2>/dev/null
}

restore_snapshot() {
  # 回滚:快照覆盖回 /app,但保留线上配置/锁/密钥/日志(EXCLUDES 保证不动它们)
  rsync -a "${EXCLUDES[@]}" "$SNAPSHOT_DIR/" "$APP_DIR/" 2>/dev/null
}

rollback() {
  alog "‼ 部署后 ${WAIT}s 内 /health 未恢复,触发自动回滚。"
  if [ ! -d "$SNAPSHOT_DIR" ] || [ -z "$(ls -A "$SNAPSHOT_DIR" 2>/dev/null)" ]; then
    alog "✗ 无 last-good 快照可回滚(可能容器刚启动还没来得及快照)。请人工介入。"
    return 1
  fi
  restore_snapshot
  alog "已恢复 last-good 二进制,重启 $SERVICE ..."
  supervisorctl restart "$SERVICE" >/dev/null 2>&1 || alog "✗ supervisorctl restart 失败"
  # 等回滚后的恢复
  local ok=false
  for _ in $(seq 1 18); do sleep 5; if healthy; then ok=true; break; fi; done
  if $ok; then
    alog "✓ 回滚成功,服务已恢复(运行的是上一版健康代码)。"
    alog "⚠ 若新版曾执行过 DB 迁移,现在是『新库+旧码』,请人工核对数据库 schema。"
  else
    alog "✗ 回滚后仍不健康,可能是 DB/配置/环境问题(非代码)。请人工介入查日志。"
  fi
  return 0
}

log "pm-deploy-guard 启动: health=$HEALTH_URL app=$APP_DIR snapshot=$SNAPSHOT_DIR wait=${WAIT}s"
command -v rsync >/dev/null || { log "ERROR 未找到 rsync(看门狗依赖它做快照/回滚)"; }

while true; do
  if [ -f "$MARKER" ]; then
    # —— 部署观察窗口 ——
    log "检测到部署标记,进入观察窗口(最长 ${WAIT}s)..."
    deadline=$(( $(date +%s) + WAIT ))
    recovered=false
    while [ "$(date +%s)" -lt "$deadline" ]; do
      if healthy; then recovered=true; break; fi
      sleep "$POLL"
    done
    if $recovered; then
      rm -f "$MARKER"
      log "部署后服务健康,刷新 last-good 快照。"
      take_snapshot
    else
      rollback
      rm -f "$MARKER"
    fi
  else
    # —— 平时监控:健康时维护 last-good 快照 ——
    if healthy; then
      # 没有快照,或距上次快照较久,则刷新(避免每轮都 rsync)
      if [ ! -d "$SNAPSHOT_DIR" ] || [ -z "$(ls -A "$SNAPSHOT_DIR" 2>/dev/null)" ]; then
        log "首次健康,建立 last-good 快照。"
        take_snapshot
      fi
    fi
    sleep "$POLL"
  fi
done

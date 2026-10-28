#!/usr/bin/env bash
# 安装「定时同步 GitHub」任务。支持两种方式，自动选可用的：
#   1. 有 crontab 命令 → 写用户 crontab
#   2. 否则 → 写 /etc/cron.d/pm-github-sync（Debian/Ubuntu 风格）
#
# 用法：
#   bash scripts/setup-sync-cron.sh              # 默认每天 03:30
#   bash scripts/setup-sync-cron.sh "0 3 * * 1"  # 自定义 cron 表达式
#   bash scripts/setup-sync-cron.sh --remove     # 卸载
#
# ⚠️ 本机（110）当前没有运行 cron 守护进程。若脚本判定 cron 不可用，
#    会打印 systemd timer / n8n 两种替代方案的现成文件，需由你决定走哪条。
set -euo pipefail

REPO_DIR="$(cd "$(dirname "$0")/.." && pwd)"
SCRIPT="$REPO_DIR/scripts/sync-github.sh"
LOG_DIR="$REPO_DIR/logs"
MARKER="# pm-github-sync"
CRON_D_FILE="/etc/cron.d/pm-github-sync"

cron_running() {
  for p in /proc/[0-9]*/comm; do
    case "$(cat "$p" 2>/dev/null)" in cron*|crond*) return 0;; esac
  done
  return 1
}

if [ "${1:-}" = "--remove" ]; then
  command -v crontab >/dev/null && { crontab -l 2>/dev/null | grep -v "$MARKER" | crontab - || true; }
  [ -f "$CRON_D_FILE" ] && rm -f "$CRON_D_FILE" && echo "==> 已删除 $CRON_D_FILE"
  echo "==> 已移除定时同步"
  exit 0
fi

SCHEDULE="${1:-30 3 * * *}"
[ -f "$SCRIPT" ] || { echo "!! 找不到 $SCRIPT" >&2; exit 1; }
chmod +x "$SCRIPT"
mkdir -p "$LOG_DIR"

if ! cron_running; then
  echo "!! 本机没有运行中的 cron 守护进程。"
  echo "   已生成替代方案文件，请从下面二选一后启用："
  echo
  cat <<SYSTEMD
── 方案 A：systemd timer ─────────────────────────────────
# /etc/systemd/system/pm-github-sync.service
[Unit]
Description=PM sync to GitHub
[Service]
Type=oneshot
User=root
ExecStart=/bin/bash $SCRIPT
StandardOutput=append:$LOG_DIR/github-sync.log
StandardError=append:$LOG_DIR/github-sync.log

# /etc/systemd/system/pm-github-sync.timer
[Unit]
Description=Daily PM GitHub sync
[Timer]
OnCalendar=*-*-* 03:30:00
Persistent=true
[Install]
WantedBy=timers.target

启用：systemctl daemon-reload && systemctl enable --now pm-github-sync.timer

── 方案 B：n8n（https://n8n.saibo.club）─────────────────
Cron 节点 03:30 → Execute Command 节点：
  /bin/bash $SCRIPT

── 方案 C：直接装 cron ─────────────────────────────────
  apt-get install -y cron && service cron start
  然后重跑本脚本
─────────────────────────────────────────────────────────
SYSTEMD
  exit 0
fi

if command -v crontab >/dev/null; then
  {
    crontab -l 2>/dev/null | grep -v "$MARKER" || true
    echo "$SCHEDULE /bin/bash $SCRIPT >> $LOG_DIR/github-sync.log 2>&1 $MARKER"
  } | crontab -
  echo "==> 已写入用户 crontab：$SCHEDULE"
else
  {
    echo "SHELL=/bin/bash"
    echo "PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin"
    echo "$SCHEDULE root /bin/bash $SCRIPT >> $LOG_DIR/github-sync.log 2>&1 $MARKER"
  } > "$CRON_D_FILE"
  chmod 644 "$CRON_D_FILE"
  echo "==> 已写入 $CRON_D_FILE：$SCHEDULE"
fi

echo "==> 脚本: $SCRIPT"
echo "==> 日志: $LOG_DIR/github-sync.log"

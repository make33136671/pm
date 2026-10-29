#!/bin/sh
# PM 启动脚本（容器内）
# 用法: sh /opt/pm/start.sh [端口]
set -u
PORT="${1:-6200}"
APP_DIR=/opt/pm/api
PIDFILE=/opt/pm/pm.pid

if [ -f "$PIDFILE" ]; then
  OLD=$(cat "$PIDFILE" 2>/dev/null)
  if [ -n "${OLD:-}" ] && kill -0 "$OLD" 2>/dev/null; then
    echo "PM 已在运行 (pid $OLD)，先停止"
    kill "$OLD" 2>/dev/null
    sleep 5
  fi
fi

cd "$APP_DIR" || exit 1
nohup dotnet Sb.Apps.Pm.WebApi.dll --urls="http://0.0.0.0:$PORT" \
  >> "$APP_DIR/run.log" 2>&1 &
NEWPID=$!
echo "$NEWPID" > "$PIDFILE"
echo "PM 启动中 pid=$NEWPID port=$PORT"

i=0
while [ "$i" -lt 60 ]; do
  if curl -s -m 2 -o /dev/null "http://127.0.0.1:$PORT/admin/Setup/Status" 2>/dev/null; then
    echo "PM 就绪: http://127.0.0.1:$PORT  (pid $NEWPID)"
    exit 0
  fi
  if ! kill -0 "$NEWPID" 2>/dev/null; then
    echo "PM 进程已退出，日志尾部："
    tail -20 "$APP_DIR/run.log"
    exit 1
  fi
  i=$((i + 1))
  sleep 2
done
echo "PM 120 秒内未就绪，日志尾部："
tail -20 "$APP_DIR/run.log"
exit 1

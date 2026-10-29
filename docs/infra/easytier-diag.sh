#!/usr/bin/env bash
# EasyTier 连接问题自测脚本
#
# 用途：收集上报 EasyTier 项目组所需的全部证据（版本/参数/延迟/MTU/日志统计）
# 用法：bash easytier-diag.sh [对端IP]
#
# 输出：控制台 + /tmp/easytier-diag-<时间>.txt

set -u
PEER="${1:-192.168.10.199}"
OUT="/tmp/easytier-diag-$(date +%Y%m%d-%H%M%S).txt"
TEE() { "$@" 2>&1 | tee -a "$OUT"; }

echo "===== EasyTier 诊断报告 =====" | tee "$OUT"
echo "生成时间: $(date -Is)" | tee -a "$OUT"
echo "对端: $PEER" | tee -a "$OUT"
echo "主机: $(hostname)" | tee -a "$OUT"
echo | tee -a "$OUT"

echo "--- 1. 版本与容器 ---" | tee -a "$OUT"
for c in $(docker ps --format '{{.Names}}' | grep -i easy); do
  echo "[$c]" | tee -a "$OUT"
  docker inspect "$c" --format '  Image: {{.Config.Image}}' | tee -a "$OUT"
  docker inspect "$c" --format '  NetworkMode: {{.HostConfig.NetworkMode}}' | tee -a "$OUT"
  docker inspect "$c" --format '  Uptime: {{.State.StartedAt}}' | tee -a "$OUT"
  docker exec "$c" easytier-core --version 2>&1 | sed 's/^/  /' | tee -a "$OUT"
  echo "  Cmd: $(docker inspect "$c" --format '{{json .Config.Cmd}}' | sed 's/--network-secret [^,]*/--network-secret <REDACTED>/')" | tee -a "$OUT"
  echo | tee -a "$OUT"
done

echo "--- 2. 虚拟网卡 ---" | tee -a "$OUT"
for i in $(ip -br link show 2>/dev/null | awk '$1 ~ /^tun|^utun|^wg/ {print $1}'); do
  ip addr show "$i" 2>/dev/null | grep -E '^[0-9]+:|inet |mtu' | tee -a "$OUT"
done
echo | tee -a "$OUT"

echo "--- 3. 延迟与丢包（60 秒）---" | tee -a "$OUT"
TEE ping -c 60 -i 1 "$PEER" | tail -4
echo | tee -a "$OUT"

echo "--- 4. MTU 探测 ---" | tee -a "$OUT"
IFACE=$(ip route get "$PEER" 2>/dev/null | grep -oE 'dev [^ ]+' | awk '{print $2}')
echo "出口网卡: ${IFACE:-未知}" | tee -a "$OUT"
[ -n "${IFACE:-}" ] && ip link show "$IFACE" | head -1 | tee -a "$OUT"
# 注意：tun 接口会丢弃总长 > 自身 MTU 的 IP 包，故能通的 payload 上限 = 接口MTU - 28。
# 若接口 MTU=1300，则 payload 1300(=1328字节) 必然不通，最大可通 payload 为 1272。
echo "（提示：payload 1300 通 = 接口 MTU >=1328；payload 1272 通 = 接口 MTU >=1300）" | tee -a "$OUT"
for s in 1472 1400 1380 1360 1340 1328 1320 1300 1272 1200; do
  if ping -c 2 -W 2 -M do -s "$s" "$PEER" >/dev/null 2>&1; then
    echo "  payload $s => 通 (MTU=$((s+28)))" | tee -a "$OUT"
  else
    echo "  payload $s => 丢包" | tee -a "$OUT"
  fi
done
echo | tee -a "$OUT"

echo "--- 5. 长连接存活测试（裸 TCP，无数据）---" | tee -a "$OUT"
TEE timeout 180 node -e "
const net=require('net');
const t0=Date.now();
const s=net.connect({host:'$PEER',port:8080},()=>s.write('GET / HTTP/1.1\r\nHost:$PEER\r\nConnection: keep-alive\r\n\r\n'));
s.on('close',()=>{console.log('[close] 存活 '+Math.round((Date.now()-t0)/1000)+'s');process.exit(0)});
s.on('error',e=>{console.log('[error] '+e.message+' 存活 '+Math.round((Date.now()-t0)/1000)+'s');process.exit(0)});
setTimeout(()=>{console.log('[180s] 仍存活');process.exit(0)},175000);
" || echo "  (node 不可用，跳过)" | tee -a "$OUT"
echo | tee -a "$OUT"

echo "--- 6. 连接错误统计 ---" | tee -a "$OUT"
for c in $(docker ps --format '{{.Names}}' | grep -i easy); do
  echo "[$c]" | tee -a "$OUT"
  N=$(docker logs "$c" 2>&1 | grep -c 'connection error')
  echo "  connection error 总数: $N" | tee -a "$OUT"
  docker logs "$c" 2>&1 | grep -oE 'err="[^"]*"' | sed 's/remote=.*//' | sort | uniq -c | sort -rn | head -6 | sed 's/^/  /' | tee -a "$OUT"
  echo "  日志首行: $(docker logs "$c" 2>&1 | head -1 | cut -c1-30)" | tee -a "$OUT"
  echo "  日志末行: $(docker logs "$c" 2>&1 | tail -1 | cut -c1-30)" | tee -a "$OUT"
  echo | tee -a "$OUT"
done

echo "--- 7. 近期连接抖动样本 ---" | tee -a "$OUT"
for c in $(docker ps --format '{{.Names}}' | grep -i easy | head -1); do
  docker logs "$c" 2>&1 | grep -E 'connection error|new connection accepted' | tail -20 | cut -c1-160 | tee -a "$OUT"
done

echo | tee -a "$OUT"
echo "===== 完成，报告已保存: $OUT =====" | tee -a "$OUT"

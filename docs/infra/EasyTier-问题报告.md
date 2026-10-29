# EasyTier 2.6.4 连接异常报告

> 报告日期：2026-10-29
> 版本：`easytier-core 2.6.4-8428a89d`
> 部署：Debian 12 宿主机，同时存在 systemd 服务 + Docker 容器

---

## 一、拓扑与配置（关键信息）

### 同机运行了 3 个 EasyTier 实例

```
PID 926   /usr/local/bin/easytier-core -c /opt/swiftdesk/config.toml
          -> systemd: swiftdesk-a-side.service（真正在用的客户端）

PID 2168  easytier-core --network-name swiftdesk ... -l tcp://0.0.0.0:11020
          -> Docker: easytier-relay

PID 2183  easytier-core --network-name swiftdesk ... -l tcp://0.0.0.0:11010
          -> Docker: easytier-signal
```

三个实例属于同一个 network_name = "swiftdesk"，并监听 11010/11011/11020/11021。

### A 侧（本机 niuma / 10.126.126.2）

/opt/swiftdesk/config.toml：

```toml
instance_name  = "swiftdesk-a-side"
hostname       = "a-side"
dhcp           = true
network_name   = "swiftdesk"
network_secret = <REDACTED>

[[peer]]
uri = "wss://et.saibo.club/etws"

listeners  = ["udp://0.0.0.0:22010"]
rpc_portal = "127.0.0.1:15889"
```

### B 侧（对端 199 / 10.126.126.5）

局域网 192.168.10.199，通过隧道访问其 :8080。

---

## 二、问题 1：同机多实例互相连接导致连接风暴（最严重，根因）

### 现象

swiftdesk-a-side（PID 926）日志中，每秒持续出现「建立连接 -> 立即删除」循环：

```
new peer connection added  tunnel_type: "tcp"  local=tcp://172.19.0.1:40679  remote=tcp://172.19.0.1:11010
peer connection removed    tunnel_type: "tcp"  local=tcp://172.19.0.1:40679  remote=tcp://172.19.0.1:11010
new peer connection added  tunnel_type: "tcp"  local=tcp://172.20.0.1:35677  remote=tcp://172.20.0.1:11010
peer connection removed    tunnel_type: "tcp"  local=tcp://172.20.0.1:35677  remote=tcp://172.20.0.1:11010
new peer connection added  tunnel_type: "tcp"  local=tcp://172.17.0.1:35769  remote=tcp://172.17.0.1:11010
peer connection removed    tunnel_type: "tcp"  local=tcp://172.17.0.1:35769  remote=tcp://172.17.0.1:11010
（循环）
```

### 远端地址统计（journalctl -u swiftdesk-a-side 最近 2000 行）

| 连接目标 | 次数 |
|---|---|
| tcp://172.20.0.1:11010 | 548 |
| tcp://172.17.0.1:11010 | 544 |
| tcp://172.19.0.1:11010 | 544 |
| ws://172.20.0.1:11011/ | 544 |
| ws://172.19.0.1:11011/ | 544 |
| ws://172.17.0.1:11011/ | 544 |
| udp://45.119.135.17:xxxx（真实对端） | 16 |

172.17.0.1 / 172.19.0.1 / 172.20.0.1 全是本机 Docker 网桥：

```
docker0          172.17.0.1/16  UP
br-7f67ff5e7894  172.19.0.1/16  UP
br-77339a2e93ac  172.20.0.1/16  UP
br-363748419b23  172.18.0.1/16  DOWN
```

即：a-side 在同机另外两个 EasyTier 实例（监听 11010/11011）通过 Docker 网桥
把自己发现成了 peer，并持续重连。

3260+ 次无效连接 vs 16 次真实连接 —— 真实连接只占 0.5%。

### 疑问

1. 同机同 network_name 的多个实例，是否应该互相连接？如果设计上允许（用于中继），
   为何连接建立后立即被删除并反复重试？
2. 是否有本机实例 / 自身 peer_id 识别机制，避免连自己的其它实例？
3. 是否有 --no-listener 之外的方式，限制 EasyTier 不去连接指定网段
   （如 Docker 网桥 172.16.0.0/12）？文档未提及。

---

## 三、问题 2：too many direct conns 持续刷屏

### 数据

easytier-signal 容器 72 小时日志（157,499 行）：

| 错误 | 次数 | 占比 |
|---|---|---|
| anyhow error: too many direct conns, cur: 3, max: 3 | 56,896 | 84.1% |
| Timeout error: deadline has elapsed | 10,604 | 15.7% |
| wait resp error: conn closed during wait handshake response | 132 | 0.2% |
| connection error 合计 | 67,632 | — |

平均每分钟 15.7 次，持续 72 小时。

### 疑问

1. max: 3 是硬编码还是可配置？如何调整？
2. 达到上限时，是拒绝新连接还是踢掉旧连接？日志显示之后又 new connection accepted，
   形成抖动循环。
3. 该错误与问题 1 的「同机互连」是否为同一根因？

---

## 四、问题 3：跨隧道 TCP 长连接每 ~120 秒被断开

### 数据

通过隧道访问对端 192.168.10.199:8080：

| 测试 | 存活时间 |
|---|---|
| WebSocket（每秒 ping） #1 | 118 s |
| WebSocket #2 | 126 s |
| WebSocket #3（MTU 1360） | 115 s |
| WebSocket #4（MTU 1300） | 132 s |
| WebSocket #5（MTU 1300） | 110 s |
| 裸 TCP（无数据） | 66 s |

全部集中在 110–132 秒，code=1006（abnormal closure，无 close frame）。

对比：同样 WebSocket 直连后端（不经隧道）150 秒无断连。

### 已排除

| 假设 | 验证 |
|---|---|
| 应用层 WS 超时 | 后端无空闲超时逻辑 |
| Caddy 反代超时 | 无 read_timeout / idle_timeout 配置 |
| Cloudflare 100s 空闲 | 有持续 ping，且直连 150s 未断 |
| 本机 conntrack | tcp_timeout_established = 432000（5 天） |
| MTU 不匹配 | 1300 vs 1360 对比无差异 |

### 疑问

1. EasyTier 是否有 ~120 秒的会话 / NAT 老化？该值是否可配置？
2. 长连接应用（SSH/VNC/RDP/WebSocket）是否需要专门的保活配置？文档未提及。

---

## 五、问题 4：MTU 声明与实际不符且会漂移

### 数据

tun0 声明 MTU 1360（后手动改 1300），但实测：

```bash
# 第一次探测（接口 MTU 1360）
ping -M do -s 1400   -> 100% 丢包
ping -M do -s 1300   -> 通 (MTU≈1328)

# 数小时后复测（接口已改 1300）
payload 1300/1320/1340/1360/1380/1400/1472  -> 全部丢包
payload 1200 -> 通 (MTU≈1228)
```

路径 MTU 不是固定值，会随链路状况漂移。静态配置 MTU 不可靠。

### 影响

- MTU 1360 时：连续 10 次 HTTP 请求 1 次失败（10%）
- 手动降到 1300：连续 20 次请求 0 失败

说明 1328 < packet < 1360 的包被静默黑洞丢弃，引发 TCP 重传与长尾延迟。

### 疑问

1. tun MTU 的计算依据是什么？是否考虑了底层承载链路（UDP/WSS over 公网）实际 MTU？
2. 是否支持手动指定 tun MTU 的配置项？
3. 是否会做 PMTUD？默认值是否应更保守（如 1280）？

---

## 六、问题 5：延迟与丢包异常

| 目标 | 平均延迟 | 丢包 | mdev |
|---|---|---|---|
| 本机回环 192.168.2.110 | 0.036 ms | 0% | 0.008 ms |
| VPN 网关 10.126.126.1 | 0.8–14.8 ms | 0% | 27.7 ms |
| 对端 10.126.126.5 | 431 ms | 3.3% ~ 16.7% | 20 ms |

60 秒持续 ping 两次采样：丢包 3.33% -> 16.67%，延迟稳定在 431ms。

### 疑问

1. --latency-first（帮助中 "latency first mode"）是否适用于本场景？
2. 是否有内建的路径质量检测 / 自动选优机制？
3. 同机 3 实例的拓扑是否导致流量绕行、放大延迟？

---

## 七、问题 6：日志可观测性

所有 connection error 均为 INFO 级别，72 小时刷 15.7 万行，导致：

- 真正的故障被淹没
- docker logs --since 24h 因 json-file 轮转返回 0 条

### 建议

1. 高频重连类错误降级为 WARN 并限流（如每分钟 1 条 + 累计计数）
2. 提供 --log-ratelimit 或类似配置
3. 提供连接指标（peer 数 / 重连次数 / RTT）供 Prometheus 采集

---

## 八、复现脚本

见同目录 easytier-diag.sh：

```bash
bash easytier-diag.sh <对端IP>
```

自动采集：容器版本/参数、虚拟网卡、60 秒延迟丢包、MTU 逐级探测、
裸 TCP 长连接存活、连接错误分类统计、近期抖动样本。

---

## 九、期望

1. 明确同机多实例行为：是否应互连、如何避免「自己连自己」、能否按网段排除
2. 明确 max direct conns 来源与配置方式
3. 明确是否存在 ~120s 会话老化，及长连接应用的推荐配置
4. tun MTU 计算方式与手动指定支持
5. 高频错误日志限流
6. 文档补充多实例部署与长连接场景章节

---

## 附：原始统计

```
# easytier-signal（72h）
Total lines:            157,499
Time range:             2026-10-26T10:25:57Z ~ 2026-10-29T16:17:46Z
connection error:       67,632
  |- too many direct conns, cur: 3, max: 3      56,896  (84.1%)
  |- Timeout error: deadline has elapsed        10,604  (15.7%)
  |- conn closed during wait handshake             132  (0.2%)

# easytier-relay
Total lines:            228
connection error:       21
  |- too many direct conns                     20
  |- conn closed during wait handshake          1

# swiftdesk-a-side（最近 2000 行）
真实对端连接 (45.119.135.17:udp)      16
本机 Docker 网桥连接 (172.17/19/20)  3264
```

# EasyTier 2.6.4 连接异常报告

> 报告日期：2026-10-29
> 版本：`easytier-core 2.6.4-8428a89d`
> 部署：Docker（`network_mode: host`），Debian 12 / Ubuntu 宿主机

---

## 摘要

EasyTier 2.6.4 在两节点（110 ↔ 199）组网中，72 小时内产生 **67,632 次连接错误**，
其中 **56,896 次是 `too many direct conns, cur: 3, max: 3`**（占 84%）。

同时观察到 **跨隧道 TCP 长连接每 ~120 秒被强制断开**（应用层 web terminal 的
WebSocket 以 `code=1006` abnormal closure 断开），严重影响长连接类应用（VNC / SSH / WebSocket）。

节点间延迟异常：VPN 网关 `10.126.126.1` 仅 1ms，但对端节点 `10.126.126.5` 达 **370–430ms**，
且 3–5% 丢包。

---

## 环境

| 项 | 值 |
|---|---|
| EasyTier 版本 | `easytier-core 2.6.4-8428a89d` |
| 部署方式 | Docker，`network_mode: host` |
| 网络名 | `swiftdesk` |
| 节点 A（110） | `10.126.126.2`，hostname `relay-110`，公网出口 `27.214.240.74` |
| 节点 B（199） | `10.126.126.5`，局域网 `192.168.10.199` |
| 运行时长 | 72 小时（uptime 38h 后重启过一次） |

### 节点 A（110）启动参数

```bash
easytier-core \
  --network-name swiftdesk \
  --network-secret <REDACTED> \
  --relay-network-whitelist swiftdesk default \
  -l tcp://0.0.0.0:11020 \
  -l udp://0.0.0.0:11020 \
  -l ws://0.0.0.0:11021 \
  --hostname relay-110 \
  --instance-name swiftdesk-relay \
  --peers tcp://192.168.2.110:11010 \
  --peers ws://192.168.2.110:11011 \
  --rpc-portal 127.0.0.1:15900
```

### 节点 A 上的第二个实例（signal）

```bash
easytier-core \
  --network-name swiftdesk \
  --network-secret <REDACTED> \
  --relay-network-whitelist \
  --relay-all-peer-rpc \
  -l tcp://0.0.0.0:11010 \
  -l udp://0.0.0.0:11010 \
  -l ws://0.0.0.0:11011 \
  --mapped-listeners udp://27.214.240.74:11010 \
  --mapped-listeners tcp://27.214.240.74:11010
```

> ⚠️ 注意：**同一台机器上跑两个 EasyTier 实例**（11010/11011 与 11020/11021），
> 且 relay 实例的 `--peers` 指向**同机的 signal 实例**（`192.168.2.110:11010/11011`）。
> 这可能与下述「连接数打满」有关，但报告不对此下定论，请项目组判断。

---

## 问题 1：`too many direct conns` 持续刷屏（最严重）

### 现象

`easytier-signal` 实例 72 小时日志（157,499 行）中的错误分布：

| 错误 | 次数 | 占比 |
|---|---|---|
| `anyhow error: too many direct conns, cur: 3, max: 3` | **56,896** | 84.1% |
| `Timeout error: deadline has elapsed` | **10,604** | 15.7% |
| `wait resp error: conn closed during wait handshake response` | 132 | 0.2% |
| **合计 connection error** | **67,632** | — |

平均 **每分钟 ~15.7 次**连接错误，72 小时从未停止。

### 原始日志片段

```
INFO CORE::INSTANCE::CONNECTION: [280ed3b0-...] connection error
    local="udp://0.0.0.0:11010" remote="udp://192.168.2.110:51796"
    err="anyhow error: too many direct conns, cur: 3, max: 3"
```

```
INFO CORE::INSTANCE::CONNECTION: [7c905fdb-...] connection error
    local="udp://0.0.0.0:11010" remote="udp://192.168.2.110:37693"
    err="Timeout error: deadline has elapsed"
```

`easytier-relay` 实例（228 行）中同样有 20 次 `too many direct conns`。

### 疑问

1. `max: 3` 这个上限是如何确定的？是硬编码常量还是可配置？
2. `too many direct conns` 出现时，是**拒绝新连接**还是**断开旧连接**？
   从日志看是持续重试（每 5 秒一轮 "connection error" + "new connection accepted" 交替），
   形成**连接抖动循环**：
   ```
   15:52:38  connection error (Timeout)
   15:52:39  connection error (Timeout)
   15:52:40  new connection accepted   ← 重连成功
   15:52:40  new connection accepted
   15:52:40  new connection accepted   ← 同时建了 3 条
   15:52:45  connection error (Timeout)  ← 5 秒后全超时
   15:52:45  connection error (Timeout)
   ```
   这个 5 秒一轮的抖动是否是预期行为？
3. 同一 peer 对之间建立多条连接（`tcp://` + `ws://` + `udp://` 并存）是设计意图吗？
   观察到「new peer connection added」同时出现 `tunnel_type: "ws"`、`"ws"`、`"tcp"` 三条。
   这是否导致 `max: 3` 被瞬间打满？

---

## 问题 2：跨隧道 TCP 长连接每 ~120 秒被断开

### 现象

通过隧道访问对端（199）的 HTTP 服务：

- **纯 TCP 连接（无数据）**：**66 秒**被断开
- **WebSocket（每秒 ping）**：**110–132 秒**断开，`code=1006`（Abnormal Closure）

### 测试数据（4 次独立 WebSocket 长连测试）

| 序号 | MTU | 存活时间 |
|---|---|---|
| 1 | 1360 | 118 s |
| 2 | 1360 | 126 s |
| 3 | 1360 | 115 s |
| 4 | 1300 | 132 s |
| 5 | 1300 | 110 s |

**高度集中在 ~120 秒**，且**降 MTU 无改善** → 排除 MTU 因素，指向固定超时阈值。

### 疑问

1. EasyTier 是否有 **~120 秒的会话/NAT 表项老化**？如果有：
   - 该阈值是否可配置？（`easytier-core --help` 中未见 timeout 相关选项）
   - 是否会主动向已建立的 TCP 连接发 RST/FIN？
2. 长连接应用（SSH / VNC / RDP / WebSocket）在 EasyTier 上是否需要
   **保活（keepalive）配置**才能长期存活？文档未提及。
3. UDP 传输路径下，NAT 映射老化是否导致这条 TCP 会话被动中断？

---

## 问题 3：MTU 声明与实际不符

### 现象

`tun0` 接口声明 **MTU 1360**，但实测：

```bash
# 逐步探测（-M do 禁止分片）
ping -M do -s 1400 192.168.10.199   → 100% 丢包
ping -M do -s 1300 192.168.10.199   → 通
```

即实际可通行 payload 上限 **1300 字节**（MTU ≈ 1328），比声明的 1360 小 32 字节。

**注意：后续复测时该阈值还会漂移** —— 同一链路在数小时后，连 payload 1300 也不通，只有 1200 才通
（MTU 1300 已设置为接口值，仍无法通过 1300 payload）。说明路径 MTU **不是固定值**，
会随链路状况变化，这使静态配置 MTU 的方案不可靠。

### 影响

- MTU 1360 时：连续 10 次 HTTP 请求 **10% 失败**
- 手动降到 MTU 1300 后：连续 20 次请求 **0 失败**

说明 `1328 < packet < 1360` 区间的包被**静默丢弃**（黑洞），导致 TCP 重传、长尾延迟。

### 疑问

1. EasyTier 的 `tun0` MTU 是根据什么计算的？是否考虑了**底层承载链路**（UDP over 公网）的实际 MTU？
2. 是否应该做 **PMTUD（路径 MTU 发现）**或默认取更保守的值（如 1300）？
3. 是否有配置项可指定 tun MTU？

---

## 问题 4：跨网延迟异常高且抖动

### 现象

| 目标 | 平均延迟 | 丢包 | 抖动 (mdev) |
|---|---|---|---|
| VPN 网关 `10.126.126.1` | 0.8–14.8 ms | 0% | **27.7 ms** |
| 对端节点 `10.126.126.5` | **370–430 ms** | 3–5% | 25 ms |
| 本机回环 `192.168.2.110` | 0.036 ms | 0% | 0.008 ms |

60 秒持续 ping 对端（两次采样，间隔数小时）：

| 采样 | 丢包 | 平均 | 最大 | mdev |
|---|---|---|---|---|
| 第一次 | 2/60（**3.33%**） | 430.7 ms | 455.7 ms | 19.3 ms |
| 第二次 | 10/60（**16.67%**） | 431.3 ms | 493.8 ms | 20.7 ms |

**丢包率在 3% ~ 17% 之间大幅波动**，且延迟稳定在 430ms（说明不是拥塞，更像链路质量问题或丢包）。

### 疑问

1. 网关 1ms、对端 430ms，说明**中继链路**很慢。EasyTier 是否有内建的
   **路径质量检测 / 自动选择最优中继**机制？是否需要开启某个参数？
2. 观察到节点 A 上跑 signal + relay 两个实例，relay 的 peers 指向本机 signal。
   这种「自己连自己」的拓扑是否会让流量绕行中继而增加跳数？
3. `--latency-first`（帮助里提到 "latency first mode"）是否应该在我们场景开启？

---

## 问题 5：日志级别与可观测性

### 现象

上述所有 `connection error` 全部是 **INFO 级别**，在 72 小时内刷了 6.7 万条，
导致：

- 日志文件被淹没（157k 行）
- 真正的故障很难定位
- `docker logs --since 24h` 返回 0 条（日志被 json-file driver 轮转掉）

### 建议

1. 高频重连类错误（`too many direct conns` / `Timeout error`）是否适合降级为
   **WARN 且限流**（例如每分钟最多 1 条 + 累计计数）？
2. 是否有 `--log-level` 或 `--log-ratelimit` 配置？
3. 是否提供**连接状态指标**（peer 数、重连次数、RTT）供 Prometheus 采集？

---

## 复现步骤（针对问题 2）

```bash
# 1. 在节点 A 上，向后端发起一个长连接 WebSocket，每秒 ping 一次
#    （我们用的是 PM 项目自带的 web terminal，协议：
#      client → {"type":"ping","t":<ts>}，server → {"type":"pong","t":<ts>}）

# 2. 观察连接存活时间
#    结果：稳定在 110–132 秒后 code=1006 断开

# 3. 对比：同样的 WebSocket 直连（不经隧道）跑 150 秒无断连
```

### 排查过的假设（均已排除）

| 假设 | 验证方式 | 结论 |
|---|---|---|
| 应用层超时 | 检查后端代码，无 WS 空闲超时 | ❌ 排除 |
| 反代（Caddy）超时 | Caddyfile 无 `read_timeout`/`idle_timeout` | ❌ 排除 |
| Cloudflare 100s 空闲 | 有持续 ping，且 150s 测试未断 | ❌ 排除 |
| 本机 conntrack | `nf_conntrack_tcp_timeout_established = 432000`（5 天） | ❌ 排除 |
| **MTU 不匹配** | MTU 1300 vs 1360 对比测试 | ❌ **对 ~120s 断连无影响**（但独立造成 HTTP 10% 失败） |

---

## 期望

1. 明确 `max direct conns = 3` 的来源与配置方式，以及「连接数打满」时的正确预期行为
2. 明确是否存在 ~120 秒会话老化，以及长连接应用的推荐配置
3. 明确 tun MTU 的计算方式，并支持手动指定
4. 高频错误日志限流 / 降级
5. 建议在文档中补充 **长连接（SSH/VNC/WebSocket）场景**的调优章节

---

## 附 A：诊断脚本

本报告配套一个可直接运行的诊断脚本（需 Docker + sudo）：



它会自动采集：容器版本/参数、虚拟网卡状态、60 秒延迟丢包、MTU 逐级探测、
裸 TCP 长连接存活、连接错误分类统计、近期抖动样本。

## 附 B：完整错误统计原始数据

```
# easytier-signal（72 小时）
Total lines:        157,499
Time range:         2026-10-26T10:25:57Z ~ 2026-10-29T16:17:46Z
connection error:   67,632
  ├─ too many direct conns, cur: 3, max: 3   56,896  (84.1%)
  ├─ Timeout error: deadline has elapsed     10,604  (15.7%)
  └─ conn closed during wait handshake          132  (0.2%)

# easytier-relay
Total lines:        228
  ├─ too many direct conns, cur: 3, max: 3       20
  └─ conn closed during wait handshake            1
```

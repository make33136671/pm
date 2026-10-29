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

### 本环境已采用的规避方案（2.6.4 实机验证）

无需等待上游修改，以下两项即可根治：

**方案 A（推荐）：监听器绑定到具体地址，不绑 `0.0.0.0`**

```bash
-l tcp://192.168.2.110:11010 -l udp://192.168.2.110:11010 \
-l ws://192.168.2.110:11011  -l wss://192.168.2.110:11011
```

绑定到具体 IP 后，同机其它实例就无法再通过 `172.17/19/20.0.1` 等网桥地址发现它，
连接风暴从源头消失。已在启动日志 TOML 中确认 `listeners` 正确落为具体地址。

**方案 B（终极手段）：`--disable-p2p true`**

2.6.4 支持该开关：关闭自动 P2P 后，节点不再主动与其他节点建立 p2p 连接，
同机实例互连彻底杜绝。**代价是放弃直连打洞**（全部走中继，延迟会升高），
仅在同机多实例且方案 A 无法实施时使用。

> 两项均已集成进 `tools/install-easytier-node.sh`
> （`--listen-addr` / `--disable-p2p`），默认只改 MTU，监听地址需显式指定。

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
   → **是同一根因**。已确认 `172.17.0.1 / 172.19.0.1 / 172.20.0.1:8080` 与
   `192.168.2.110:8080` 返回完全相同的响应，即这些网桥地址指向**同一台宿主机**。
   a-side 反复连上同机的 relay/signal 实例，对端已达 `max: 3` 上限而拒绝，
   客户端随即重试，于是刷出 56,896 条 `too many direct conns`。
   **修掉问题 1 即同时消除问题 2 的绝大部分噪音。**

> 注：`max direct conns` 在 2.6.4 的 `--help` 中**没有对应配置项**，
> 应为上游硬编码（默认 3）。已作为上游需求提出。

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
   → **当前无法从本环境证实**。`easytier-core --help` 中未找到可调的
   idle/timeout/keepalive 与会话老化相关参数，若存在则为硬编码。
2. 长连接应用（SSH/VNC/RDP/WebSocket）是否需要专门的保活配置？文档未提及。

### 排查建议（尚未验证，下一步待做）

> ⚠️ 本问题**未解决**，与「网页终端断连」的 PM 代码问题（见
> `docs/webterm/网页终端断连问题与调试日志.md`）是**两件事**，不要混淆。

区分方法很直接：**看关闭码**。

- 关闭码 `1000` / `4001` → 应用层问题（PM）
- 关闭码 **`1006`** → 链路被异常中断，才轮到 EasyTier / 隧道

若确认为 1006，按以下顺序排查：

1. **分协议对比**：同一对端分别走 `tcp://` 与 `udp://` 承载，看是否只有一种断。
   TCP-over-TCP 在丢包链路下会因重传叠加而恶化，这是常见诱因。
2. **改走中继**：临时 `--disable-p2p true`（见问题 1 方案 B）后复测，
   若不再断，说明直连打洞路径有问题。
3. **抓两侧时间戳**：对端与本机同时 `tcpdump`，确认是 EasyTier 先发 RST，
   还是中间设备（云厂商 NAT / 安全组）先掐断。
4. **调 MTU 复测**：虽然 1300/1360 对比无差异，但建议再试 **1280**（IPv6 最小 MTU），
   排除分片相关的老化。

---

## 五、问题 4：MTU 默认值 1360 偏大，导致中间尺寸包被黑洞丢弃

### 数据

tun0 默认声明 MTU 1360，已手动改为 1300 并固定。实测：

```bash
# 第一次探测（接口 MTU 1360）
ping -M do -s 1400   -> 100% 丢包
ping -M do -s 1300   -> 通 (MTU≈1328)
```

> ⚠️ **勘误（2026-10-30）**：初版报告此处还记录过「数小时后复测，payload 1300~1472 全丢、
> 仅 1200 通（MTU≈1228）」，并据此得出「路径 MTU 会漂移」的结论。**该结论不成立**，原文数据是
> 在接口 MTU 已改为 1300 之后采集的。tun 接口会直接丢弃总长超过自身 MTU 的 IP 包，而
> ICMP 总长 = payload + 28，故 payload 1300（=1328 字节）在 MTU 1300 下必然不通，
> 能通的只到 payload ≤ 1272。也就是说，这组「1200 通、1300 不通」的数据恰恰**印证**了
> 1300 已生效，而不是证明 MTU 漂移。

结论修正为：**路径 MTU 未观察到漂移，实测可用 MTU ≈ 1328**；原先 MTU 1360 的默认值偏大，
导致 1328 < packet < 1360 的包被静默黑洞丢弃，引发 TCP 重传与长尾延迟。

### 影响

- MTU 1360 时：连续 10 次 HTTP 请求 1 次失败（10%）
- 手动降到 1300 并固定：连续 20 次请求 0 失败

说明 1328 < packet < 1360 的包被静默黑洞丢弃，引发 TCP 重传与长尾延迟。
当前生产取值：**tun MTU = 1300（已固定）**，实测无 HTTP 失败。

### 疑问（已在 2.6.4 实机验证，附答案）

1. tun MTU 的计算依据是什么？是否考虑了底层承载链路（UDP/WSS over 公网）实际 MTU？
   → **已查到**：`easytier-core --help` 明确写着 `--mtu <MTU>`，
   默认 **加密 1360 / 非加密 1380**。该默认值针对局域网/Happy-Eyeballs 场景，
   在公网 UDP/WSS 承载链路上偏大，正是本次黑洞丢包的来源。
2. 是否支持手动指定 tun MTU 的配置项？
   → **支持**：命令行 `--mtu <数字>`（环境变量 `ET_MTU`）。
   实机验证 `--mtu 1300` 在启动日志的 TOML 中确认为 `mtu = 1300`。
3. 是否会做 PMTUD？默认值是否应更保守（如 1280）？
   → 未观察到有效 PMTUD（否则不会出现 1328~1360 区间的静默黑洞）。
   建议默认值下调到 1280~1300，或在产生 tun 时按承载链路探测。

> 实践结论：**显式传 `--mtu 1300`**，不要依赖默认值。
> 已在 `tools/install-easytier-node.sh` 中设为默认（可用 `--mtu` 覆盖）。

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

## 十、报告后复核（2026-10-30，从 110 本机外网侧）

为确认「是不是 EasyTier 本身出故障」，对运行中的实例做了一轮外部健康检查：

| 检查项 | 方式 | 结果 |
|---|---|---|
| 监听器存活 | 11010/11011/11020/11021 各连 3 次 | 全部 3/3 成功，无抖动 |
| RPC 端口 | 15888 建连 | 正常（TCP 可连、不发 HTTP，符合 RPC 行为） |
| WSS 信令 | `https://et.saibo.club/etws` | 稳定返回 HTTP 400（空体），30 秒内 6 次延迟 0.047–0.056s |
| TLS 证书 | `openssl s_client` | `*.saibo.club`，有效期 2026-10-20 ~ 2027-01-18，正常 |
| 错误路径对照 | `/zzz` | 由 nginx 返回 404，而 `/etws` 空体 400 → 证明 `/etws` 已正确转发到 EasyTier |

**结论：EasyTier 进程与信令链路健康，未发现服务端宕机、端口丢挂或 TLS 异常。**
`/etws` 返回 400 是 EasyTier 对非 EasyTier 握手请求的正常拒绝（直接用 HTTP GET 打 11010 也无响应），
不是故障。报告中的 6 个问题均属**行为/配置层面**，而非 EasyTier 崩坏。

### 根因再确认：问题 1 的机制

从本机复核发现，`172.17.0.1 / 172.19.0.1 / 172.20.0.1:8080` 与 `192.168.2.110:8080`
返回**完全相同的 GitLab 302**，即这三个地址都是**同一台宿主机（110）自身的网桥地址**。
因 EasyTier 监听器绑定 `0.0.0.0`，同机的 3 个实例会通过本机每一个网桥地址互相发现，
于是 a-side 反复连上同机的 relay/signal 实例，再因对端拒绝（too many direct conns）而断开重试，
形成 3264 次 vs 16 次的连接风暴。**问题 1 与问题 2 属同一根因。**

缓解方向（无需改 EasyTier）：把 3 个实例的监听器改为只绑外网地址（如 `-l tcp://192.168.2.110:11010`），
不要再绑 `0.0.0.0`；或让 a-side 与 relay/signal 不在同一台机器。

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

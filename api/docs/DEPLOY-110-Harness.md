# PM 部署到 110（harness 容器 + pm.saibo.club）

> 部署完成：2026-10-29
> 访问地址：**https://pm.saibo.club**
> 登录：`admin` / `111111`

---

## 一、最终架构

```
┌── 内网（在家）───────────────────────────────────────────────┐
│ 浏览器 → DNS 问 192.168.2.110 → 192.168.2.110:443 → Caddy    │
└──────────────────────────────────────────────────────────────┘
┌── 外网（在外）───────────────────────────────────────────────┐
│ 浏览器 → Cloudflare 边缘 → cf-tunnel → proxy:443（同一台 Caddy）│
└──────────────────────────────────────────────────────────────┘
                              ↓
                    Caddy (local-proxy) 按路径分流
                    ├─ /admin/*   → 192.168.2.110:6200（后端 API）
                    ├─ /api/*     → 同上
                    ├─ /webterm/* → 同上（WebSocket）
                    ├─ /storage/* → 同上（上传文件）
                    └─ 其它       → /data/pm-admin 静态文件（前端）
                              ↓
                    宿主 6200 → docker -p → 容器 6200 → PM 后端

证书：Let's Encrypt 通配 *.saibo.club（DNS-01），自动续签
```

| 组件 | 位置 / 值 |
|---|---|
| 容器 | `dsh-harness`（重启策略 `unless-stopped`） |
| 端口映射 | `-p 3080:3080`（harness）、`-p 6200:6200`（PM） |
| **前端静态文件** | 宿主 `/root/docker/dns-proxy/data/pm-admin`，容器内 `/data/pm-admin` |
| Caddy 站点块 | `/root/docker/dns-proxy/Caddyfile` → `pm.saibo.club` |
| 内网解析 | `/root/docker/dns-proxy/local.hosts` → `192.168.2.110 pm.saibo.club` |
| PM 后端 | 容器内 `/opt/pm/api` |
| 启动脚本 | 容器内 `/opt/pm/start.sh` |
| 持久备份 | 容器内 `/root/pm-backup/`（挂在宿主 `/root/projects/pm-backup`） |

> **3080 为什么不能给 PM**：3080 是 harness 自己的反向代理
> （`/app/proxy/index.js` → 3079 dsh web）。PM 独占 6200。

---

## 二、前端

| 项 | 值 |
|---|---|
| 源码 | `admin/`（Vben Admin monorepo，派生自 sb-framework-admin） |
| 构建命令 | `pnpm run build:antd` |
| 产物 | `admin/apps/web-antd/dist/`（约 24MB） |
| API 地址 | `VITE_GLOB_API_URL=/admin`（见 `.env.production`） |
| 路由模式 | `VITE_ROUTER_HISTORY=hash` |
| 部署位置 | `/root/docker/dns-proxy/data/pm-admin/` |

### 重新构建并发布前端

```bash
cd /root/projects/saibo/apps/pm/admin
pnpm install            # 首次
pnpm run build:antd     # 约 2 分钟

rsync -a --delete apps/web-antd/dist/ /root/docker/dns-proxy/data/pm-admin/
```

> Caddy 无需重载——它直接读目录。

### 首次构建遇到的问题

- 容器内没有 pnpm：`corepack prepare pnpm@10.22.0 --activate`
  并建 `/usr/local/bin/pnpm` 包装脚本（`exec corepack pnpm "$@"`）
- npm 官方源慢：临时把 `.npmrc` 的 registry 改为
  `https://registry.npmmirror.com/`，构建完已还原
- `postinstall` 报 `pnpm: not found`：创建全局包装脚本后重跑
  `pnpm -r run stub --if-present`

---

## 三、数据库

| 项 | 值 |
|---|---|
| 类型 | PostgreSQL |
| 地址 | `192.168.2.110:5432` |
| 库名 | `sb_pm` |
| 用户 | `postgres` / `111111` |
| 缓存 | Redis `192.168.2.110:6379`（**无密码**） |

> 原始 `appsettings.json` 配的是 `192.168.10.62` / `192.168.10.63`，
> **从容器不可达**。已对齐 110 上其它项目的惯例。

---

## 四、自启动

容器入口 `/root/dsh-harness/entrypoint-supervised.sh`
（宿主机 `/root/projects/dsh-harness/`）已改造：

- 原有：监管 `dsh web`，死了自动拉起
- **新增**：启动时调用 `/opt/pm/start.sh` 拉起 PM
- **新增**：监管循环里检测 PM 存活，死了 5 秒后自动拉起
- 退出信号时一并停止 PM

配合容器 `--restart unless-stopped`，
**宿主重启 / 容器重启都会自动带起 PM**。

实测：`docker restart dsh-harness` 后 PM 自动恢复（pid 变化、`installed:true`）。

备份：`entrypoint-supervised.sh.bak-20261029-090116`

---

## 五、⚠️ 工具链在容器层，重建容器会丢

PM 运行需要的工具（.NET SDK、pnpm/node_modules、libicu）
**装在容器可写层**，`docker rm` 重建后会全部丢失
（`/opt` 不在挂载卷；前端静态文件在宿主，不受影响）。

### 容器重建后的一键恢复

```bash
docker exec -it dsh-harness sh
sh /root/pm-backup/restore.sh
```

`restore.sh` 依次：换 apt 源 → 装 curl/wget/libicu → 装 .NET 10 SDK →
重新 publish → 恢复 `appsettings.json` / `installed.lock` → 启动 PM。

前端静态文件在宿主 `/root/docker/dns-proxy/data/pm-admin`，**不受容器重建影响**。

### 持久化清单（`/root/pm-backup/`）

| 文件 | 作用 |
|---|---|
| `appsettings.json` | 数据库/Redis 配置 |
| `installed.lock` | 安装标记 |
| `start.sh` | PM 启动脚本 |
| `restore.sh` | 一键恢复脚本 |

> **根治方案**（建议后续做）：把 PM 拆成独立容器，
> 基于 `mcr.microsoft.com/dotnet/aspnet:10.0`（宿主已有），
> 挂载源码目录，与 harness 完全解耦。

---

## 六、日常操作

```bash
# 进容器
docker exec -it dsh-harness sh

# 启动 / 重启 PM
/opt/pm/start.sh 6200

# 停止
kill $(cat /opt/pm/pm.pid)

# 日志
tail -f /opt/pm/api/run.log
tail -f /opt/pm/api/logs/info-$(date +%Y%m%d).ndjson

# 重新发布后端
export DOTNET_ROOT=/usr/local/dotnet
cd /root/saibo/apps/pm/api
dotnet publish Sb.Apps.Pm.WebApi/Sb.Apps.Pm.WebApi.csproj -c Release -o /opt/pm/api

# 重载 Caddy（改 Caddyfile 后）
cd /root/docker/dns-proxy && docker compose up -d --force-recreate proxy
```

---

## 七、验收记录

| 项 | 结果 |
|---|---|
| 内网 `https://pm.saibo.club/`（前端 HTML） | **200 text/html** |
| 外网 `https://pm.saibo.club/`（走 CF 隧道） | **200 text/html** |
| 内网 `/admin/Setup/Status` | **200** `installed:true` |
| 外网 `/admin/Setup/Status` | **200** |
| 登录 `admin/111111`（走域名） | **code:0**，token 215 字符 |
| 静态资源 `/_app.config.js` | 200 `text/javascript` |
| `_app.config.js` 内容 | `VITE_GLOB_API_URL:"/admin"` |
| 内网 DNS 解析 | `192.168.2.110` |
| 证书 | Let's Encrypt `*.saibo.club` |
| 容器重启后自动恢复 | **已验证** |

---

## 八、踩坑记录

### 坑 1：3080 被 harness 占用

`3080` 是 `/app/proxy/index.js`（harness 反代 → 3079），PM 不能监听。
→ PM 用 6200，`docker -p 6200:6200` 映射。

### 坑 2：Caddy 在容器内，不能写容器 IP

Caddy（local-proxy）在 `dnp` 网络，访问不到 harness 的 `172.17.0.4`。
→ 必须走 docker 端口映射，Caddy 上游写宿主 `192.168.2.110:6200`。

### 坑 3：Caddy 静态文件路径

Caddy 容器只挂了 `/root/docker/dns-proxy/data` → `/data`。
静态文件必须放 `data/pm-admin`，Caddyfile 写 `/data/pm-admin`
（不是宿主路径）。

### 坑 4：安装向导覆盖 appsettings.json

`Setup/Install` 成功后会按请求参数重写 `appsettings.json`。
**先改配置再安装 = 被冲掉**。
→ 安装请求里直接传正确参数，或安装后改一次再重启。

### 坑 5：重新发布清空 /opt/pm/api

`dotnet publish -o /opt/pm/api` 会清空目录，`installed.lock` 随之丢失，
服务变成 `installed:false`。
→ 发布后恢复 `installed.lock`，或重新走安装。

### 坑 6：重建容器丢失工具链（本次踩到）

`docker inspect` 查到的镜像名 `smanx/deepseek-harness` 与旧容器实际环境不符
（旧容器装过 dotnet 等工具）。照镜像名重建后工具全丢。
→ 已用 `restore.sh` 兜底；根治建议拆独立容器。

### 坑 7：容器内无 ssh/curl/git 时如何操作宿主

新容器里没有任何 HTTP/SSH/git 客户端。
→ `npm install ssh2` + Node 写最小 SSH 客户端（`/root/.sshcli/h.js`），
   通过它到宿主机执行 docker/git 命令。

# 赛博 PM(sb-apps-pm)运维部署手册

涵盖 pm 三个子项目(api / admin / mcp)的部署、升级、备份、排错。webterm(网页终端)的专项配置见 [webterm-deploy.md](./webterm-deploy.md)。

## 1. 拓扑总览

```
┌──────────────────────────┐
│  浏览器 / Claude Code    │
└──────────┬───────────────┘
           │ HTTPS / stdio
┌──────────▼─────────┐         ┌──────────────────────────────┐
│      nginx         │  反代   │  pm-api(.NET 8)              │
│   pm.example.com   ├────────▶│  127.0.0.1:6020              │
│   静态: admin      │         │  WebSocket: /webterm/ws/{id} │
└────────────────────┘         └──────┬───────────────────────┘
                                      │
                                ┌─────▼─────┐
                                │  MySQL    │
                                │  SbPm 库  │
                                └───────────┘

   pm-mcp(Node)        ←   stdio   →   AI Agent / IDE
```

## 2. 系统要求

| 组件 | 要求 |
|------|------|
| OS | Ubuntu 20.04+ / Debian 11+(其它 Linux 发行版能跑 .NET 8 也可) |
| .NET runtime | 8.0+ |
| Node.js | 18+(只为 mcp,api 不需要) |
| MySQL | 5.7+ / 8.x |
| Redis | 6+(可选,用于缓存) |
| nginx | 1.18+ (要支持 WebSocket upgrade) |
| Tools | `unzip`, `sudo`, `tmux`(可选), `tcpdump`(排错用) |
| 端口 | 6020(api) + 80/443(nginx) |
| 时区 | 推荐 `Asia/Shanghai`,后端用 UTC 存,前端按用户时区显示 |

### 数据库准备

```sql
CREATE DATABASE SbPm CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
CREATE USER 'pm'@'%' IDENTIFIED BY '强密码';
GRANT ALL PRIVILEGES ON SbPm.* TO 'pm'@'%';
FLUSH PRIVILEGES;
```

> 库名固定 `SbPm`,表前缀 `sb_pm_*` / `sb_*`,**不要改库名**(代码硬编码)。

## 3. api 部署

### 3.0 部署产物规则（首次 vs 后续 — 必读）

api 端产物（`api.zip`）的打包范围**取决于是不是首次部署**：

| 场景 | 配置文件（`appsettings.json` / `appsettings.*.json`） | 静态资源（`wwwroot/`） | 说明 |
|---|---|---|---|
| **首次部署** | ✅ **全量包含** | ✅ **全量包含** | 目标机什么都没有，需要完整产物把配置模板、安装向导静态页一起铺下去 |
| **后续每次部署（升级）** | ❌ **过滤掉，不打进 zip** | ❌ **过滤掉，不打进 zip** | 配置含线上密钥、wwwroot 含安装向导生成的品牌资源（logo/favicon/`installed.lock` 等），都是目标机的运行时状态，**绝不能被部署产物覆盖** |

**为什么后续要过滤**：

- `appsettings.json` 里有 DB 密码 / `MasterKey` / `EncryptionKey` / JWT Key —— 覆盖 = 线上配置被冲掉，服务起不来或密钥错乱。
- `wwwroot/storage/` 是安装向导生成的品牌资源；`installed.lock` 是安装完成标记 —— 覆盖/丢失 = 触发 install guard，**全站 503 `setup_required`**。

**怎么过滤**（`build-publish.sh` 已内置后续部署的过滤逻辑）：

```bash
dotnet publish ... -o dist/api
cd dist/api
rm -f appsettings.json appsettings.*.json   # 过滤配置
rm -rf wwwroot                              # 过滤静态资源
cd .. && zip -qr api.zip api/               # 打 zip（内部直接是 dll，无外层文件夹）
```

> framework 的 API 部署端点（`POST /admin/App/UploadApi`，multipart `file` + `X-Master-Key`）也是自动解压→替换→重启，**同样依赖 zip 不含配置/wwwroot 来避免覆盖** —— 后续部署务必走过滤后的产物。

### 3.1 首次安装

```bash
# 1. 系统用户
useradd -r -m -d /opt/pm pm

# 2. 解压产物
mkdir -p /opt/pm/api
unzip api.zip -d /opt/pm/api
chown -R pm:pm /opt/pm

# 3. 配置文件(zip 里没有,首次需要手工创建)
cat > /opt/pm/api/appsettings.json <<'EOF'
{
  "Logging": { "LogLevel": { "Default": "Information" } },
  "Serilog": { /* 直接抄源仓库的 appsettings.json 模板 */ },

  "ProjectAssemblyName": "Sb.Apps.Pm.Services",
  "ProductCn": "赛博项目管理",
  "ProductEn": "sb-pm",
  "PrimaryDomain": "pm.example.com",
  "TimeZone": "Asia/Shanghai",

  "ConnectionString": "Server=127.0.0.1;Port=3306;Database=SbPm;Uid=pm;Pwd=强密码;Charset=utf8mb4;",
  "DbType": "MySql",

  "JwtAdmin": {
    "Issuer": "sb-pm",
    "Audience": "sb-pm-admin",
    "Key": "<32+ 字符随机串,openssl rand -hex 32>",
    "Expires": "10080"
  },
  "EncryptionKey": "<32 字符随机串,用于 SSH 私钥/SSH 密码等敏感字段加密>",
  "MasterKey": "<32 字符随机串,给 mcp 走 X-Master-Key 调用 api>",

  "CorsConfig": { "AllowAll": false, "Origins": ["https://pm.example.com"] },
  "CacheConfig": {
    "Enabled": true,
    "Provider": "Redis",
    "Redis": { "Connection": "127.0.0.1:6379", "InstanceName": "sbpm:" }
  },

  "WebTerminal": {
    "LogRoot": "logs/cli",
    "ScrollbackBytes": 4194304
  }
}
EOF

# 4. wwwroot(品牌资源,首次安装后由安装向导生成)
mkdir -p /opt/pm/api/wwwroot/storage

# 5. systemd 服务
cat > /etc/systemd/system/pm-api.service <<'EOF'
[Unit]
Description=PM API
After=network.target mysql.service redis.service

[Service]
Type=simple
WorkingDirectory=/opt/pm/api
ExecStart=/usr/bin/dotnet /opt/pm/api/Sb.Apps.Pm.WebApi.dll --urls=http://127.0.0.1:6020
Restart=always
RestartSec=5
User=pm
Group=pm
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=DOTNET_PRINT_TELEMETRY_MESSAGE=false

# 文件描述符上限 - WebSocket 多会话需要
LimitNOFILE=65536

[Install]
WantedBy=multi-user.target
EOF

systemctl daemon-reload
systemctl enable --now pm-api

# 6. 走安装向导(首次必须)
curl http://127.0.0.1:6020/api/Setup/Status
# 期望: {"installed":false}
# 然后浏览器开 https://pm.example.com 跟向导走一遍 → 完成后 /opt/pm/api/ 出现 installed.lock 文件
```

### 3.2 关键路径与目录

| 路径 | 用途 | 备份? |
|------|------|-------|
| `/opt/pm/api/appsettings.json` | 配置(含密钥) | ✅ |
| `/opt/pm/api/installed.lock` | 安装完成标记(空文件) | ✅ |
| `/opt/pm/api/wwwroot/storage/` | 品牌资源(logo/favicon),向导生成 | ✅ |
| `/opt/pm/api/logs/` | Serilog 日志(按天滚动) | 按需 |
| `/opt/pm/api/logs/cli/` | webterm 输出日志 | 按需 |
| MySQL `SbPm` 库 | 业务数据 | ✅ 必须每天 |

### 3.3 端口与防火墙

只对外开 nginx 的 80/443。**6020 不要直接对公网**(api 假定走反代,JwtAdmin 是它唯一鉴权)。

```bash
ufw allow 80
ufw allow 443
ufw deny 6020
```

## 4. admin 部署(nginx 静态)

```bash
mkdir -p /www/wwwroot/sbpm_admin
unzip admin.zip -d /www/wwwroot/sbpm_admin

# nginx 配置
cat > /etc/nginx/sites-available/pm.conf <<'EOF'
server {
    listen 80;
    server_name pm.example.com;
    return 301 https://$host$request_uri;
}

server {
    listen 443 ssl http2;
    server_name pm.example.com;

    ssl_certificate     /etc/letsencrypt/live/pm.example.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/pm.example.com/privkey.pem;

    root /www/wwwroot/sbpm_admin;
    index index.html;

    # 上传体积上限(.NET 那边也配了 500M,这里要 ≥)
    client_max_body_size 500M;

    # SPA 路由回退
    location / { try_files $uri $uri/ /index.html; }

    # API 反代
    location /admin/   { proxy_pass http://127.0.0.1:6020/admin/;   proxy_set_header Host $host; proxy_set_header X-Real-IP $remote_addr; }
    location /api/     { proxy_pass http://127.0.0.1:6020/api/;     proxy_set_header Host $host; proxy_set_header X-Real-IP $remote_addr; }
    location /storage/ { proxy_pass http://127.0.0.1:6020/storage/; proxy_set_header Host $host; }
    location /upload/  { proxy_pass http://127.0.0.1:6020/upload/;  client_max_body_size 500M; proxy_set_header Host $host; }

    # webterm WebSocket(必须,否则网页终端无法连接)
    location /webterm/ws/ {
        proxy_pass http://127.0.0.1:6020;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host $host;
        proxy_read_timeout 86400s;
        proxy_send_timeout 86400s;
    }

    # gzip
    gzip on;
    gzip_types text/css application/javascript application/json image/svg+xml;
    gzip_min_length 1024;
}
EOF

ln -sf /etc/nginx/sites-available/pm.conf /etc/nginx/sites-enabled/
nginx -t && nginx -s reload
```

浏览器访问 `https://pm.example.com`,默认账号 `admin / 111111`(**首次登录立即改密码**)。

## 5. mcp 部署(给 AI Agent 调用)

mcp 不是 web service,是 stdio 协议的 MCP server,由 Claude Code / Cursor 等直接 spawn。

```bash
mkdir -p /opt/pm/mcp
unzip mcp.zip -d /opt/pm/mcp
cd /opt/pm/mcp
npm install --omit=dev    # 只装 prod 依赖
node dist/index.js        # 测试启动,看到等待 stdio 输入即正常,Ctrl+C 退出
```

在 Claude Code 用户级配置(`~/.claude.json`)或项目级(`.mcp.json`)注册:

```json
{
  "mcpServers": {
    "sb-pm": {
      "command": "node",
      "args": ["/opt/pm/mcp/dist/index.js"],
      "env": {
        "PM_API_URL": "https://pm.example.com",
        "PM_MASTER_KEY": "<appsettings.json 里的 MasterKey>"
      }
    }
  }
}
```

`PM_MASTER_KEY` 走 `X-Master-Key` header 绕过 JWT,**仅供受信任的 AI 调用**,不要暴露到公网。

## 6. webterm 专项

新功能,部署上有额外要求(sudoers + nginx ws upgrade)。完整步骤见独立文档:

➡ **[webterm-deploy.md](./webterm-deploy.md)**

要点速记:
- 不用 `runAsUser` 切身份 → pm 服务用户能干啥 webterm 就能干啥,无需任何额外配置
- 用 `runAsUser` → ubuntu 上加一份 `/etc/sudoers.d/pm-webterm`(`pm ALL=(deploy) NOPASSWD: SETENV: ALL`)
- nginx 必须有 `/webterm/ws/` 的 WebSocket upgrade 配置(见 §4 已包含)
- pm 进程内 PTY,**pm 重启 → 所有 CLI 会话被 SIGHUP**(设计取舍,文档里列为"已知行为"不要当 bug)

## 7. 升级流程

```bash
# 1. 备份(必做!)
mysqldump -upm -p SbPm | gzip > /backup/sbpm-$(date +%F).sql.gz
tar czf /backup/pm-config-$(date +%F).tgz \
  /opt/pm/api/appsettings.json \
  /opt/pm/api/installed.lock \
  /opt/pm/api/wwwroot/storage/

# 2. 停服(短停 — webterm 进程内 PTY 会断)
systemctl stop pm-api

# 3. 替换 api 二进制(保留 appsettings.json + wwwroot + installed.lock)
cd /opt/pm/api
# 注意: 解压会覆盖同名 dll,但配置和 wwwroot 因 zip 不含 → 不会被覆盖
# 前提: api-new.zip 必须是【后续部署产物】= 已过滤掉配置 + wwwroot(见 §3.0)。
#       若误用首次部署的全量包,会覆盖线上配置和品牌资源 → 服务异常 / 全站 503。
unzip -o /tmp/api-new.zip

# 4. 替换 admin 静态(直接覆盖)
unzip -o /tmp/admin-new.zip -d /www/wwwroot/sbpm_admin

# 5. 启动 + 验证
systemctl start pm-api
curl http://127.0.0.1:6020/api/Setup/Status      # 应返回 {"installed":true}
curl -i http://127.0.0.1:6020/health 2>&1 | head # 应 200

# 6. 数据库迁移
# pm 用 FreeSql codeFirst,启动时自动同步表结构(加字段/加表自动,改字段类型/删字段不会自动)
# 涉及 schema 大改的版本看 changelog,可能要走"手动 SQL 迁移"
# pm 自带 DB 迁移脚本管理,在网页 → DB 迁移脚本 里跑

# 7. mcp 更新
unzip -o /tmp/mcp-new.zip -d /opt/pm/mcp
cd /opt/pm/mcp && npm install --omit=dev
# 客户端会自动重启 mcp(它每次按需 spawn)
```

回滚:把 §1 的备份反向恢复即可。

## 8. 备份策略

| 内容 | 频率 | 命令 | 保留 |
|------|------|------|------|
| MySQL `SbPm` 库 | 每天 02:00 | `mysqldump SbPm \| gzip > sbpm-$(date +%F).sql.gz` | 30 天本地 + 7 天异地 |
| `/opt/pm/api/appsettings.json` | 改了立刻 | `cp ... /backup/` | 永久(含密钥,加密保存) |
| `/opt/pm/api/wwwroot/storage/` | 每周 | `tar czf storage-$(date +%F).tgz` | 4 周 |

> pm 自身有"数据库自动备份"BackgroundTask(`backup_interval_hours` 系统设置控制,默认 12 小时),会落到 api 同目录或上传到云端;但**不要替代上面的外部备份**,本地盘故障就一起完了。

## 9. 日志位置

| 文件 | 内容 |
|------|------|
| `/opt/pm/api/logs/info-YYYY-MM-DD.ndjson` | INFO 级以下,CLEF 格式(JSON Lines) |
| `/opt/pm/api/logs/error-YYYY-MM-DD.ndjson` | ERROR 级以上 |
| `/opt/pm/api/logs/cli/YYYYMM/session-{id}.log` | webterm 每个会话的 PTY 输出原文 |
| `journalctl -u pm-api -f` | systemd 启动 / 停止 / OOM 等系统级 |
| `/var/log/nginx/{access,error}.log` | HTTP 层 |

ndjson 用 `jq` 看比较方便:`tail -f logs/info-*.ndjson | jq -r '.["@t"] + " " + .["@m"]'`

## 10. 排错速查

| 现象 | 第一步看 | 常见原因 |
|------|---------|---------|
| 网页打不开 | `systemctl status pm-api` + `nginx -t` | api 没起 / nginx 配错 / cert 过期 |
| 登录后菜单空 | 浏览器 Network: `/admin/Menu/*` 返回 | 用户没角色 / 角色没菜单授权 |
| `/api/Setup/Status` returns `{"installed":false}` | `installed.lock` 丢了? | 走一遍安装向导 / 手工 `touch installed.lock` |
| 接口 500 | `tail logs/error-*.ndjson \| jq` | DB 连不上 / 字段不存在 / 序列化异常 |
| webterm 创建会话立即"已退出" | `journalctl -u pm-api \| grep PTY` | sudoers 没配,见 webterm-deploy.md §2 |
| webterm 网页连上没输出 | 浏览器 console / nginx access.log | nginx 缺 WebSocket upgrade,见 §4 |
| 数据库连接超时 | `mysql -upm -p -e "SELECT 1"` | DB 挂 / 防火墙 / 密码改了 |
| 上传文件失败 | nginx error.log 找 `client intended to send too large body` | nginx `client_max_body_size` < 500M |
| 内存涨到 OOM | `journalctl -u pm-api \| grep -i killed` | 长查询无分页 / webterm 会话太多;调大 LimitMemory 或限并发 |

## 11. 启停 / 重启 / 升级 cheat-sheet

```bash
# 启动 / 停止 / 重启 / 状态
systemctl start   pm-api
systemctl stop    pm-api
systemctl restart pm-api
systemctl status  pm-api

# 看实时日志(systemd + serilog 双线)
journalctl -u pm-api -f
tail -f /opt/pm/api/logs/info-*.ndjson | jq -r '.["@t"] + " [" + .["@l"] + "] " + .["@m"]'

# 修改后端代码(开发态)后重启 api
systemctl restart pm-api      # ← webterm 会话会全断,提前打招呼

# nginx 改配
nginx -t && nginx -s reload   # -t 先校验,通过再 reload

# 强制清缓存(Redis)
redis-cli -n 0 KEYS 'sbpm:*' | xargs redis-cli DEL
```

## 12. 常见运维场景

### 12.1 改 admin 默认密码

第一次登录后 → 右上角头像 → 修改密码,或在网页 → 管理员 → admin → 重置密码。

### 12.2 加新管理员

网页 → 管理员 → 新建,选角色"超级管理员"或自定义角色。

### 12.3 给某角色开"网页终端"权限

网页 → 角色管理 → 选目标角色 → 菜单授权 → 勾选"网页终端"+ "终端配置" → 保存。

### 12.4 切换数据库到 PostgreSQL

不推荐(pm 写 entity 时偏向 MySQL 语义,见 [memory: PG 派生项目 entity 跨库写法](../../../../.claude/projects/-Users-tianlong-projects-saibo/memory/feedback_pg_freesql_cross_db.md))。如果一定要,改 `appsettings.json`:
```json
"DbType": "PostgreSQL",
"ConnectionString": "Host=...;Port=5432;Database=SbPm;Username=pm;Password=..."
```
然后**新建空库重新走安装向导**,不要直接迁移现有 MySQL 数据。

### 12.5 关闭授权管理面板

pm 用的是 `SB.Framework.*.NoLicense` 包,授权机制在编译期已剔除,**没有"授权管理"菜单也没有授权验证拦截**。无需做任何事。

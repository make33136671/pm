# PM 部署包说明

> 构建时间：2026-10-30
> 版本：**1.0.18**（基于 commit `3d886bf`）
> 包含：API（.NET 10）+ Admin（Vue/Vben）

---

## 包内容

```
dist/
├── api/                     # 后端发布产物（99 文件 / 46MB）
│   ├── Sb.Apps.Pm.WebApi.dll        ← 主程序
│   ├── Sb.Apps.Pm.Services.dll
│   ├── Sb.Apps.Pm.Models.dll
│   └── ...
├── admin/                   # 前端静态文件（24MB）
│   ├── index.html
│   ├── _app.config.js               ← VITE_GLOB_API_URL=/admin
│   ├── js/  css/  assets/ ...
└── appsettings.template.json  # 配置模板（已脱敏，需填真实值）
```

---

## 一、后端部署

### 1. 准备配置

复制 `appsettings.template.json` 为 `appsettings.json`，填写：

| 占位符 | 说明 |
|---|---|
| `__DB_HOST__` / `__DB_PORT__` | PostgreSQL 地址 |
| `__DB_NAME__` | 库名（如 `sb_pm`） |
| `__DB_USER__` / `__DB_PASSWORD__` | 数据库账号 |
| `__REDIS_HOST__` | Redis 地址（不用可改 `CacheConfig.Enabled=false`） |
| `__JWT_ADMIN_KEY__` / `__JWT_API_KEY__` | JWT 签名密钥（32 字节以上随机串） |
| `__ENCRYPTION_KEY__` | 加密密钥 |

> ⚠️ **首次部署必须设 `DbConfig.AutoSyncStructure = true`** 让它自动建表，
> 稳定后可改回 `false`。

### 2. 上传并启动

```bash
# 上传
scp -r dist/api/ root@<目标机>:/opt/pm/api/

# 首次部署：确保没有旧 installed.lock
rm -f /opt/pm/api/installed.lock

# 启动
cd /opt/pm/api
export DOTNET_ROOT=/usr/local/dotnet
./Sb.Apps.Pm.WebApi            # 或用 dotnet Sb.Apps.Pm.WebApi.dll
# 默认监听 5000，可指定：
dotnet Sb.Apps.Pm.WebApi.dll --urls=http://0.0.0.0:6200
```

### 3. 安装向导

首次访问会返回 503，需走安装接口：

```bash
curl -X POST http://127.0.0.1:6200/admin/Setup/Install \
  -H "Content-Type: application/json" -d '{
  "database":{"dbType":"PostgreSql","server":"<DB_HOST>","port":"5432",
              "database":"sb_pm","user":"postgres","password":"<PWD>"},
  "enableCache":true,"cacheProvider":"Redis",
  "redis":{"connection":"<REDIS_HOST>:6379","instanceName":"sbpm:"},
  "productCn":"PM 项目管理平台","productEn":"pm",
  "timeZone":"Asia/Shanghai","corsAllowAll":true,"corsOrigins":[],
  "adminUsername":"admin","adminPassword":"111111","adminDisplayName":"超级管理员"
}'
```

**然后重启进程**，验证：

```bash
curl http://127.0.0.1:6200/admin/Setup/Status
# 期望：{"code":0,"message":"ok","data":{"installed":true}}
```

> ⚠️ **坑**：安装向导会按请求参数**重写 appsettings.json**。
> 装完后要检查配置是否被冲掉，必要时改回来再重启。

---

## 二、前端部署

把 `dist/admin/` 整个目录放到 nginx / Caddy 的站点根目录。

### nginx 示例

```nginx
server {
    listen 8080;
    server_name _;

    root /usr/share/nginx/html;   # dist/admin 内容

    # 后端接口 —— 必须放在静态站之前
    location ~ ^/(admin|api|webterm|storage)/ {
        proxy_pass http://127.0.0.1:6200;
        proxy_http_version 1.1;

        # WebSocket（网页终端必需）
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";

        # 长连接超时（网页终端）
        proxy_read_timeout 86400s;
        proxy_send_timeout 86400s;

        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }

    # SPA 兜底
    location / {
        try_files $uri $uri/ /index.html;
    }
}
```

### Caddy 示例

```
pm.saibo.club {
  handle /admin/*   { reverse_proxy 127.0.0.1:6200 }
  handle /api/*     { reverse_proxy 127.0.0.1:6200 }
  handle /webterm/* { reverse_proxy 127.0.0.1:6200 }
  handle /storage/* { reverse_proxy 127.0.0.1:6200 }
  handle {
    root * /srv/pm-admin
    try_files {path} /index.html
    file_server
  }
}
```

> **关键**：`/webterm/*` 必须反代并且**透传 Upgrade 头**，否则网页终端连不上。

---

## 三、本版重点变更

### 1. 网页终端断连诊断（本次核心）

**后端**新增独立诊断日志 `logs/webterm-diag-YYYYMMDD.log`（绕开 Serilog）：

```
[CONNECT]  conn=xxx s=9 user=admin remote=1.2.3.4 ua=...
[CANCELED] conn=xxx s=9 alive=19s
[END]      conn=xxx s=9 alive=19s reason=request-aborted pings=19
[CLIENT-LOG] user=admin remote=1.2.3.4 body={...}
```

**新增 HTTP 上报端点**（不依赖 WebSocket）：

```
POST /webterm/clientlog
→ 返回 ok diag=<写入路径>
```

**前端**：断连日志本地缓冲 + `sendBeacon` 上报 + 下次连接补传，
并采集 `online / visibility / 存活时长 / pong 超时`。

### 2. 关闭码语义修复

- 顶替旧连接从 `1000` 改为 **`4001`**（原来被前端误判为「正常结束」而放弃重连）
- 前端新增 `wsGeneration` 忽略迟到的旧事件

### 3. 文件管理器

- 修复左侧目录树无法展开（antd `loadData` 签名用错）
- 修复属主显示成 `16877`（`struct stat` 偏移错：uid 应在 28 而非 24）
- 新增系统设置项「文件管理默认打开路径」

---

## 四、部署后验证

```bash
# 1. 服务存活
curl http://<host>:6200/admin/Setup/Status

# 2. 登录
curl -X POST http://<host>:6200/admin/Auth/Login \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "username=admin&password=111111"

# 3. 前端首页（应为 HTML）
curl -I http://<host>:8080/

# 4. 诊断日志端点
curl -X POST http://<host>:6200/webterm/clientlog \
  -H "Content-Type: application/json" -d '{"lines":["deploy-check"]}'
# 期望返回：ok diag=/opt/pm/api/logs/webterm-diag-XXXXXXXX.log

# 5. 查看诊断日志
tail -f logs/webterm-diag-*.log
```

---

## 五、注意事项

1. **不要覆盖线上的 `appsettings.json` / `installed.lock` / `client.key` / `logs/`**
2. 本包**不含 appsettings.json**（已按官方 `build-publish.sh` 惯例移除）
3. **前端有缓存问题**：升级后务必清浏览器缓存或强制刷新（Ctrl+Shift+R），
   否则可能加载到旧 chunk
4. 网页终端的 WebSocket 反代**必须透传 `Upgrade` 头**且设置长超时
5. 诊断日志日期按 **UTC** 计算，与本地日期可能差一天

---

## 六、无域名访问（IP 直连）

> 场景：临时排查、内网没有 DNS、或不想配域名时，用 IP 直接打开管理后台。

### 110 上的实现（已配置）

```
http://192.168.2.110:8088/          ← 前端（管理后台）
https://pm.saibo.club/              ← 同一份前端（域名访问，推荐）
```

两者共用同一套静态文件与后端，行为完全一致。

**配置位置**：

| 文件 | 改动 |
|---|---|
| `/root/docker/dns-proxy/docker-compose.yml` | proxy 服务加 `- "192.168.2.110:8088:8088"` |
| `/root/docker/dns-proxy/Caddyfile` | 末尾加 `http://192.168.2.110:8088 { ... }` 站点块 |

Caddy 站点块（与域名块分流规则一致）：

```
http://192.168.2.110:8088 {
  handle /admin/*   { reverse_proxy 192.168.2.110:6200 }
  handle /api/*     { reverse_proxy 192.168.2.110:6200 }
  handle /webterm/* { reverse_proxy 192.168.2.110:6200 }
  handle /storage/* { reverse_proxy 192.168.2.110:6200 }
  handle {
    root * /data/pm-admin
    try_files {path} /index.html
    file_server
  }
}
```

**为什么不复用 443**：

1. 443 按 `Host` 头分流，IP 访问时 Host 是 `192.168.2.110`，会命中 `*.saibo.club` 兜底块返回 404
2. 443 的证书只覆盖 `*.saibo.club`，IP 访问必然证书告警

所以用**独立明文 HTTP 端口**最省事，且不影响现有域名链路。

### 换成 nginx 部署时

```nginx
server {
    listen 8088;
    server_name _;
    root /usr/share/nginx/html;   # dist/admin 内容

    location ~ ^/(admin|api|webterm|storage)/ {
        proxy_pass http://127.0.0.1:6200;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_read_timeout 86400s;
        proxy_send_timeout 86400s;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
    }

    location / {
        try_files $uri $uri/ /index.html;
    }
}
```

### 验证

```bash
# 前端
curl -I http://192.168.2.110:8088/
# 期望：200 text/html

# 后端接口
curl http://192.168.2.110:8088/admin/Setup/Status
# 期望：{"code":0,"message":"ok","data":{"installed":true}}

# 登录
curl -X POST http://192.168.2.110:8088/admin/Auth/Login \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "username=admin&password=111111"

# WebSocket（网页终端）—— 返回 400 是正常的（非 WS 请求）
curl -o /dev/null -w "%{http_code}" http://192.168.2.110:8088/webterm/ws/1
```

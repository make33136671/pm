# PM 部署到 110 harness 容器

> 部署日期：2026-10-29
> 容器：`df49e06f5900`（DSH Harness，对外映射 3080）
> 访问地址：容器内 `http://127.0.0.1:6200`

---

## 一、端口规划

容器内已有服务：

| 端口 | 服务 | 说明 |
|---|---|---|
| 3079 | `dsh web` | DSH Harness 本体 |
| **3080** | **`/app/proxy/index.js`** | **harness 自己的反向代理，转发到 3079** |
| **6200** | **PM** | 本次新增 |

> ⚠️ **3080 已被 harness 的代理占用**，PM 不能直接监听 3080。
> 当前方案：PM 独立监听 6200，宿主机侧再把 6200 映射出来（或让 Caddy 反代）。
>
> 若希望 `3080/pm/*` 访问 PM，需改 `/app/proxy/index.js` 做路径分流，
> 这会影响 harness 本身，未在本次改动范围内。

---

## 二、数据库

| 项 | 值 |
|---|---|
| 类型 | PostgreSQL |
| 地址 | `192.168.2.110:5432` |
| 库名 | `sb_pm`（本次新建） |
| 用户 | `postgres` / `111111` |
| 缓存 | Redis `192.168.2.110:6379`（**无密码**） |

> 原 `appsettings.json` 配的是 `192.168.10.62:5432` 与 `192.168.10.63:6379`，
> **从本容器不可达**。110 上其它项目（SwiftDesk / dramafloow）都用
> `192.168.2.110`，本次一并对齐。

---

## 三、目录结构

```
/opt/pm/
├── start.sh          # 启动脚本（带 PID 文件与就绪等待）
├── pm.pid            # 当前进程 PID
├── docs/             # 文档（本文件）
└── api/              # 发布产物（dotnet publish 输出）
    ├── appsettings.json   # 已改为 110 的库与 Redis
    ├── run.log            # 标准输出
    ├── logs/              # Serilog 结构化日志（ndjson）
    └── files-recycle/     # 回收站存储目录
```

---

## 四、部署步骤（可复现）

### 1. 建库

```bash
# 无 psql 客户端时用 Npgsql 建库，或直接在 PG 上执行：
#   CREATE DATABASE sb_pm ENCODING 'UTF8';
```

### 2. 发布

```bash
cd /root/saibo/apps/pm/api
dotnet publish Sb.Apps.Pm.WebApi/Sb.Apps.Pm.WebApi.csproj \
  -c Release -o /tmp/pm-publish -p:Version=x.y.z
```

### 3. 部署

```bash
cp -a /tmp/pm-publish/* /opt/pm/api/
```

### 4. 改配置（**关键，见下方坑 1**）

```bash
cd /opt/pm/api
node -e "
const fs=require('fs');
const j=JSON.parse(fs.readFileSync('appsettings.json','utf8'));
j.DbConfig={DbType:'PostgreSql',Server:'192.168.2.110',Port:'5432',Database:'sb_pm',
  User:'postgres',Password:'111111',AutoSyncStructure:true,
  MonitorSqlCommandForLog:false,SlowQueryThresholdMs:200};
j.CacheConfig={Enabled:true,Provider:'Redis',
  Redis:{Connection:'192.168.2.110:6379',InstanceName:'sbpm:'}};
fs.writeFileSync('appsettings.json', JSON.stringify(j,null,2));
"
```

### 5. 首次安装（仅第一次）

```bash
sh /opt/pm/start.sh 6200

curl -s -X POST "http://127.0.0.1:6200/admin/Setup/Install" \
  -H "Content-Type: application/json" -d '{
  "database":{"dbType":"PostgreSql","server":"192.168.2.110","port":"5432",
              "database":"sb_pm","user":"postgres","password":"111111"},
  "enableCache":true,"cacheProvider":"Redis",
  "redis":{"connection":"192.168.2.110:6379","instanceName":"sbpm:"},
  "productCn":"PM 项目管理平台","productEn":"pm",
  "timeZone":"Asia/Shanghai","corsAllowAll":true,"corsOrigins":[],
  "adminUsername":"admin","adminPassword":"111111","adminDisplayName":"超级管理员"
}'
```

安装成功后会生成 `installed.lock`，并**覆盖 appsettings.json**（见坑 1），
所以第 4 步的配置改完后要**再确认一次**。

### 6. 重启

```bash
sh /opt/pm/start.sh 6200
```

---

## 五、启动与停止

```bash
sh /opt/pm/start.sh 6200      # 启动（自动停旧进程、等就绪）
kill $(cat /opt/pm/pm.pid)    # 停止
```

`start.sh` 特点：

- PID 文件 `/opt/pm/pm.pid`，避免手杀误伤
- 启动后轮询 `/admin/Setup/Status`，就绪才返回
- 进程提前退出会打印 `run.log` 尾部

---

## 六、验证清单（本次已全部实测通过）

| 项 | 结果 |
|---|---|
| `GET /admin/Setup/Status` | `installed: true` |
| 登录 `admin/111111` | 拿到 accessToken |
| `GET /admin/FileManager/List?path=/opt` | 4 目录 / 1 文件 |
| 新建目录 / 新建文件 / 写入 / 读取 | 全部成功 |
| 删除 → 进回收站 → 列表 → 恢复 | 全链路成功，内容一致 |
| 压缩 zip → 预览 → 解压 | 成功，内容一致 |
| 搜索 `*.dll` | 命中 94，扫描 217，2ms |
| 路径大小统计 | 48024926 字节 / 187 文件 / 30 目录 |
| 批量操作 | 返回逐项结果 |

---

## 七、踩过的坑（重要）

### 坑 1：安装向导会覆盖 appsettings.json

`Setup/Install` 成功后会把请求里的数据库参数写回 `appsettings.json`。
**先改配置再安装 = 被冲掉**。

现象：服务能起来，`/admin/Setup/Status` 正常，但**登录 5 秒超时、返回 500**，
日志里全是：

```
Failed to connect to 192.168.10.62:5432
RedisConnectionException: It was not possible to connect to the redis server(s)
```

正确顺序：**安装 → 检查/修正 appsettings.json → 重启**。

### 坑 2：回收站表没建 —— GetEntityTypes 返回空

`FileManagerService` 实现 `IModuleInitializer` 时 `GetEntityTypes() => []`，
导致 `sb_pm_file_recycle` / `sb_pm_file_note` 两张表从未创建。

现象：`RecycleList` 报 `42P01: relation "sb_pm_file_recycle" does not exist`。

修复：改为返回实体类型数组。

### 坑 3：FreeSql `ExecuteAffrows()` 不回填自增主键

`_fsql.Insert(item).ExecuteAffrows()` 之后 `item.Id` 仍是 0，存储名变成 `0_xxx`，
列表返回的 `id` 为空字符串，恢复时按空 `StorageName` 查不到行。

现象：删除能成功、列表能看到条目，但**恢复报「回收站条目不存在」**。

修复：改用 `ExecuteIdentity()` 拿回主键。另对历史脏数据（`StorageName == ''`）
在列表查询时过滤掉。

### 坑 4：端口占用

`pkill -f "Sb.Apps.Pm.WebApi.dll"` 在容器里不可靠（可能残留多个实例），
且命令行匹配会误伤自己的 shell。

现象：`Failed to bind to address http://0.0.0.0:6200: address already in use`。

修复：用 PID 文件管理（见 `start.sh`），或按 PID 精确 kill。

### 坑 5：harness 的 supervisor 只管 dsh

容器入口 `/root/dsh-harness/entrypoint-supervised.sh` 只监管 `dsh web` 与
`/app/proxy`，**不会拉起 PM**。容器重启后 PM 需要手动 `start.sh`。

（本次按需求选择「先不常驻」；要常驻需另加 watchdog 或改进程入口。）

---

## 八、待办

- [ ] 宿主机侧映射 6200 端口，或 Caddy 加 `pm.saibo.club` 站点块
- [ ] 按需做常驻（watchdog / supervisor / 入口脚本）
- [ ] Chat 模块迁移（见 `api/docs/Chat-Module-Disabled.md`）
- [ ] 前端 `sb-framework-admin` 构建并部署到 `/opt/pm/admin`

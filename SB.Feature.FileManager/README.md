# SB.Feature.FileManager

> 服务器文件管理器插件 —— 在 admin 后台浏览、编辑、管理服务器上的真实文件。

项目已有 web shell 能力（完全 root 权限的命令行），本插件提供与之对等的**图形化文件视图**：
浏览目录、在线改配置文件、上传下载、重命名、删除、改权限。

---

## 一、能力清单

| 能力 | 接口 | 说明 |
|---|---|---|
| 目录浏览 | `GET /admin/FileManager/List?path=` | 目录优先排序，返回大小/权限/属主/mtime；超过 `MaxListEntries` 截断并提示 |
| 目录树 | `GET /admin/FileManager/Directories?path=` | 左侧树懒加载，只返回子目录 |
| 根目录列表 | `GET /admin/FileManager/Roots` | 白名单根（默认 `/`） |
| 磁盘用量 | `GET /admin/FileManager/DiskUsage?path=` | 当前分区总容量 / 可用 / 已用百分比 |
| 读取文本 | `GET /admin/FileManager/Read?path=` | 自动识别二进制、大小上限、UTF-8/GB18030 解码、换行符与行数、内容 SHA256 |
| 下载 | `GET /admin/FileManager/Download?path=` | 支持 Range 断点续传；中文文件名走 RFC 5987 |
| 保存 | `POST /admin/FileManager/Save` | 原子写（临时文件 + rename）；可自动 `.bak`；带 `expectedHash` 时做并发保护 |
| 新建 | `POST /admin/FileManager/Create` | 文件或目录，权限归一为 644 / 755 |
| 重命名 | `POST /admin/FileManager/Rename` | 同目录改名，不允许跨目录移动 |
| 删除 | `POST /admin/FileManager/Delete` | 目录需 `recursive=true` 才递归删 |
| chmod | `POST /admin/FileManager/Chmod` | 八进制权限位，`AllowChmod=false` 可整体关闭 |
| 上传 | `POST /admin/FileManager/Upload` | 单文件上限 `MaxUploadSizeMB`，同名可选覆盖 |

前端页面：`admin/views/file-manager/List.vue`（Vben Admin，完整文件管理器 UI）。

---

## 二、安全模型

### 2.1 权限边界：与 web shell 对等

管理员账号 = 运维权限。本插件**不做**额外的业务权限分级，所有接口都要求
`JwtAdmin` 鉴权（与 Sql 执行、日志查看同级别）。

### 2.2 路径边界（`PathGuard`）

所有「用户传入的路径」都必须经 `PathGuard.Resolve()`，五层校验：

1. **拒绝 NUL 字符**；
2. **相对路径**统一挂到默认根（默认根 = 白名单第一个根，全盘时为 `$HOME`），不依赖进程 CWD；
3. **黑名单** `DeniedPaths`（默认 `/proc` `/sys` `/dev`）优先级最高；
4. **白名单** `AllowedRoots`：为空 = 全盘；非空 = 必须落在某个根之下（按路径段边界判断，`/opt/ap` 不会命中 `/opt/app`）；
5. **符号链接逃逸校验**：解析链接真实目标，指向黑名单或白名单外则拒绝（防 `ln -s /etc/shadow /opt/app/x`）。

> 生产收敛示例（`appsettings.json`）：
> ```json
> { "Features": { "FileManager": {
>     "AllowedRoots": ["/opt/app", "/var/log", "/root/deploy"],
>     "DeniedPaths": ["/proc", "/sys", "/dev", "/root/.ssh"]
> } } }
> ```

### 2.3 其它防护

- **并发保护**：`Read` 返回内容 `hash`，保存时回传；磁盘文件在此期间被改过则拒绝覆盖，避免静默丢改动。
- **原子写**：写 `*.sbwrite.tmp` 再 `File.Move(overwrite)`，进程被杀不会留下半个文件。
- **审计日志**：所有写操作（write / create / mkdir / rename / rm / rmdir / chmod / upload）
  追加一行到 `logs/file-manager-audit.log`（时间、操作人、动作、路径、附加信息）。
- **只读保护**：`IsReadOnly` 的文件拒绝保存；`Editable=false` 时前端也禁止编辑。
- **根目录保护**：不允许删除白名单根目录本身。

---

## 三、安装

### 3.1 后端

```xml
<PackageReference Include="SB.Feature.FileManager" Version="0.1.0" />
```

框架按 `SB.Feature.` 前缀自动发现并装配，**无需任何手写注册代码**。

`appsettings.json` 加配置节（全部可选，不写就是默认值）：

```json
{
  "Features": {
    "FileManager": {
      "AllowedRoots": [],
      "DeniedPaths": ["/proc", "/sys", "/dev"],
      "MaxEditFileSizeMB": 5,
      "MaxUploadSizeMB": 512,
      "MaxListEntries": 5000,
      "AllowChmod": true,
      "AuditLogEnabled": true,
      "HideDotFiles": false,
      "NormalizePermissions": true
    }
  }
}
```

临时关闭整个插件（不改代码、不重编）：

```json
{ "Features": { "Disabled": ["FileManager"] } }
```

### 3.2 前端

```bash
bash SB.Feature.FileManager/admin/install.sh ~/saibo/sb/sb-framework-admin
```

然后按脚本末尾提示在「菜单管理」加一条指向 `file-manager/List` 的菜单。

---

## 四、配置项说明

| 配置 | 默认 | 说明 |
|---|---|---|
| `AllowedRoots` | `[]` | `[]` = 全盘（与 web shell 对等）；填了则只开放这些目录 |
| `DeniedPaths` | `/proc /sys /dev` | 一律拒绝的路径前缀，优先级高于白名单 |
| `MaxEditFileSizeMB` | 5 | 超过该大小的文件只能下载，不能在线编辑 |
| `MaxUploadSizeMB` | 512 | 单文件上传上限 |
| `MaxListEntries` | 5000 | 单目录返回条目上限（防大目录卡死浏览器） |
| `BinarySniffBytes` | 8192 | 二进制嗅探字节数（出现 NUL 即判为二进制） |
| `AllowChmod` | `true` | 是否允许改权限 |
| `AuditLogEnabled` | `true` | 写操作审计日志开关 |
| `AuditLogPath` | `logs/file-manager-audit.log` | 审计日志路径（相对 ContentRootPath） |
| `HideDotFiles` | `false` | 默认隐藏 `.` 开头文件 |
| `NormalizePermissions` | `true` | 新建文件 644 / 目录 755，避免 umask 产生不可读文件 |

---

## 五、已知限制

1. **不打包下载目录**：目录下载需自行 `tar`；当前只提供单文件下载。
2. **符号链接只读**：删除软链本身可以，但不会递归删除链接目标。
3. **属主显示为数字 uid/gid**：未做 `/etc/passwd` 反查（同名用户每次请求都要读文件，不值当）。
4. **chown 仅 Linux**：`TryChown` 走 libc，其它平台静默跳过。
5. **大文件编辑**：编辑器是纯 `<textarea>`，超过 `MaxEditFileSizeMB` 直接拒绝，避免浏览器卡死。
6. **并发保护可绕过**：`expectedHash` 不传就不校验，前端默认会传；脚本调用需自行保证。

---

## 六、与 web shell 的关系

web shell 和本插件是**互补**关系，不是替代：

- web shell：一条命令能干所有事，但改配置要 `vim`/`sed`，看不到文件树，容易手滑。
- 文件管理器：可视化浏览 + 直接改文本 + 上传下载，适合日常运维；但**不执行命令**。

两者共用同一份权限模型（JwtAdmin），不引入第二个后门。

# 文件管理模块（FileManager）

> PM 后台的服务器文件浏览与编辑能力。**与网页终端（webterm）权限对等**——终端能执行任意命令，
> 本模块提供同一权限下的图形化文件视图。

---

## 一、与 webterm 的关系

PM 的网页终端是**本地 root PTY**（`NativePty` → `posix_spawn` → `/bin/bash -i`），
不是 SSH 到远端设备。所以文件管理器和它是同一权限层级的两个视图：

| | 网页终端 webterm | 文件管理 FileManager |
|---|---|---|
| 做什么 | 执行任意命令 | 浏览 / 编辑 / 上传下载 / 改权限 |
| 权限 | `/bin/bash -i`（可 `sudo -u <RunAsUser>`） | 与 PM 进程同身份 |
| 当前目录 | 绑定项目/子项目的 `workspace_root` | 全盘自由浏览 |
| 不做什么 | 看不到文件树，改配置要 vim | 不执行命令 |

两者**互补不替代**：终端适合跑命令，文件管理器适合改配置、传文件。

---

## 二、能力清单

| 能力 | 接口 | 说明 |
|---|---|---|
| 列目录 | `GET /admin/FileManager/List?path=` | 目录优先排序，含大小/权限/属主/mtime；超限截断并提示 |
| 目录树 | `GET /admin/FileManager/Directories?path=` | 左侧树懒加载 |
| 根目录 | `GET /admin/FileManager/Roots` | 白名单根（默认 `/`） |
| 磁盘用量 | `GET /admin/FileManager/DiskUsage?path=` | 总容量 / 可用 / 已用百分比 |
| 读取文本 | `GET /admin/FileManager/Read?path=` | 二进制识别、UTF-8/GB18030 解码、换行符、行数、内容 SHA256 |
| 下载 | `GET /admin/FileManager/Download?path=` | 支持 Range 续传；中文名走 RFC 5987 |
| 保存 | `POST /admin/FileManager/Save` | 原子写（临时文件 + rename）、可选 `.bak`、`expectedHash` 并发保护 |
| 新建 | `POST /admin/FileManager/Create` | 文件或目录，权限归一 644 / 755 |
| 重命名 | `POST /admin/FileManager/Rename` | 同目录改名 |
| 删除 | `POST /admin/FileManager/Delete` | 目录需 `recursive=true` |
| chmod | `POST /admin/FileManager/Chmod` | 八进制权限位 |
| 上传 | `POST /admin/FileManager/Upload` | 单文件上限 `MaxUploadSizeMB` |

前端页面：`admin/apps/web-antd/src/views/file-manager/`（`index.vue` + `api.ts`）。

---

## 三、代码位置

```
api/
  Sb.Apps.Pm.Models/FileManager/FileManagerDtos.cs       # DTO
  Sb.Apps.Pm.Services/FileManager/
    FileManagerOptions.cs                                # 配置（appsettings "FileManager" 节）
    FileManagerModule.cs                                  # DI 装配（AddPmFileManager）
    PathGuard.cs                                          # 路径安全核心
    FileManagerService.cs                                 # 业务实现（IModuleInitializer 注册菜单）
  Sb.Apps.Pm.WebApi/Controllers/FileManager/
    FileManagerController.cs                              # REST 接口
admin/apps/web-antd/src/views/file-manager/
  index.vue                                              # 文件管理器界面
  api.ts                                                 # 接口封装
tests/Sb.Apps.Pm.FileManager.Tests/Program.cs            # 52 项冒烟测试
```

装配入口：`PmCoreFeature.ConfigureServices` 里调用 `services.AddPmFileManager(ctx.Configuration)`。

---

## 四、安全模型

### 4.1 路径边界（`PathGuard`）

所有用户传入的路径都必须经 `PathGuard.Resolve()`，五层校验：

1. **拒绝 NUL 字符**；
2. **相对路径**挂到默认根（白名单第一个根，全盘时为 `$HOME`），不依赖进程 CWD
   （容器里 CWD 可能是 `/app`，直接 `Path.GetFullPath(".")` 会跑偏）；
3. **黑名单** `DeniedPaths`（默认 `/proc` `/sys` `/dev` `/run`）优先级最高；
4. **白名单** `AllowedRoots`：为空 = 全盘；非空则必须落在某个根下
   （按路径段边界判断，`/opt/ap` 不会命中 `/opt/app`）；
5. **符号链接逃逸校验**：解析链接真实目标，指向黑名单或白名单外即拒绝。

### 4.2 其它防护

| 机制 | 做法 |
|---|---|
| 并发保护 | `Read` 返回内容 `hash`，保存时回传；磁盘被改过则拒绝覆盖 |
| 原子写 | 写 `*.sbwrite.tmp` 再 `File.Move(overwrite)`，进程被杀不留半个文件 |
| 审计 | 写操作追加到 `logs/file-manager/audit.log`（时间 / 操作人 / 动作 / 路径） |
| 只读保护 | 无写权限位（0444）的文件拒绝保存 |
| 根目录保护 | 不允许删除白名单根目录本身 |
| 大文件 | 超过 `MaxEditFileSizeMB` 只允许下载，不加载进编辑器 |
| 大目录 | 超过 `MaxListEntries` 截断，避免浏览器卡死 |

### 4.3 权限模型说明

与 webterm 对等：能打开网页终端的管理员，必然已经能 `cat /etc/shadow`，
所以文件管理器**不做额外业务权限细分**，避免出现「终端能看、文件管理看不了」的割裂。

需要收敛时改 `appsettings` 的 `FileManager:AllowedRoots`。

---

## 五、配置（appsettings.json）

```json
{
  "FileManager": {
    "AllowedRoots": [],
    "DeniedPaths": [ "/proc", "/sys", "/dev", "/run" ],
    "MaxEditFileSizeMB": 5,
    "MaxUploadSizeMB": 512,
    "MaxListEntries": 5000,
    "BinarySniffBytes": 8192,
    "AllowChmod": true,
    "AuditLogEnabled": true,
    "AuditLogPath": "logs/file-manager/audit.log",
    "HideDotFiles": false,
    "NormalizePermissions": true
  }
}
```

| 配置 | 默认 | 说明 |
|---|---|---|
| `AllowedRoots` | `[]` | `[]` = 全盘（与 webterm 对等）；填了则只开放这些目录 |
| `DeniedPaths` | `/proc /sys /dev /run` | 一律拒绝的路径前缀，优先级高于白名单 |
| `MaxEditFileSizeMB` | 5 | 超过只能下载 |
| `MaxUploadSizeMB` | 512 | 单文件上传上限 |
| `MaxListEntries` | 5000 | 单目录返回条目上限 |
| `BinarySniffBytes` | 8192 | 二进制嗅探字节数（出现 NUL 即判为二进制） |
| `AllowChmod` | `true` | 是否允许改权限 |
| `AuditLogEnabled` | `true` | 审计日志开关 |
| `AuditLogPath` | `logs/file-manager/audit.log` | 相对 ContentRootPath |
| `HideDotFiles` | `false` | 默认隐藏 `.` 开头文件 |
| `NormalizePermissions` | `true` | 新建文件 644 / 目录 755 |

---

## 六、菜单

由 `FileManagerService` 实现 `IModuleInitializer` 自动注册（`InitOrder=255`，紧跟网页终端 250）：

| Name | Path | Component | Title | 父级 |
|---|---|---|---|---|
| `FileManager` | `/file-manager` | `/file-manager/index` | 文件管理 | `/system` |

首次启动后框架会自动建菜单并写入 `sb_pm_menu`，无需手工加。

---

## 七、测试

```bash
cd tests/Sb.Apps.Pm.FileManager.Tests
dotnet run
```

52 项断言，覆盖：

1. 全盘模式 + 黑名单（`/proc` 拒绝、路径规范化）
2. 白名单模式（越界、`../` 穿越、NUL、新建父目录越界）
3. 写入 / 读取 / 并发 hash（他人改动后旧 hash 保存被拒）
4. 备份与原子写（`.bak` 生成、无 `.tmp` 残留）
5. 二进制识别（NUL 判定、UTF-8 中文、CRLF 检测）
6. 目录操作（权限 755、非空目录拒删、递归删、重命名、分隔符拒绝）
7. chmod（600 生效、非法格式拒绝、无写位后保存拒绝）
8. 软链接逃逸（目录软链 / 文件软链指向白名单外均拒绝）
9. 上传（成功、内容一致、无 `.tmp` 残留）
10. 磁盘用量 / 根列表
11. 审计日志（write / chmod 记录、操作人）
12. 大文件 TooLarge 标记
13. Content-Disposition 中文名编码

---

## 八、已知限制

1. **不打包下载目录**：目录需自行 `tar`，当前只支持单文件下载。
2. **符号链接只读**：删除软链本身可以，但不递归删链接目标。
3. **属主显示为数字 uid/gid**：未做 `/etc/passwd` 反查。
4. **chown 仅 Linux**：走 libc，其它平台静默跳过。
5. **编辑器是纯 `<textarea>`**：无语法高亮（可后续接 CodeMirror）。
6. **并发保护可绕过**：不传 `expectedHash` 就不校验，脚本调用需自行保证。

---

## 九、踩过的坑（实现时要小心）

| 坑 | 现象 | 正解 |
|---|---|---|
| libc `struct stat` 结构体 | `AccessViolationException` 崩溃 | 各架构 `st_nlink` 宽度不同（x86_64 是 u64、arm64 是 u32），**不能**逐字段声明；改用 144 字节不透明缓冲区，按固定偏移读 uid/gid |
| `FileInfo.IsReadOnly` | 0644 文件也返回 `true`，误拒保存 | 只认 POSIX 写位（`File.GetUnixFileMode` 的三个 write 位） |
| `FileInfo` 属性缓存 | 下载长度是旧值 | 以首次访问的属性为基准刷新，先读 `Length` 再取其它 |
| chmod 八进制校验 | 输入 `999` 通过格式检查，`Convert.ToInt32(,8)` 抛异常 | 先按位限制 `0-7` 再转换 |
| C# 无八进制字面量 | `0o644` 编译失败 | 用十进制常量 + 注释（`420 = 0644`） |

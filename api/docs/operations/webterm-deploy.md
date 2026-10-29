# 网页终端 (WebTerminal) 部署清单

> pm 的"网页 CLI"功能依赖 PTY 子进程 + sudo 切换运行身份。pm 部署到 ubuntu 后,需要按本清单做一次性配置,否则功能起不来。

## 1. 系统依赖

| 项 | 要求 | 检查命令 |
|----|------|---------|
| OS | Linux (ubuntu/debian/centos) 或 macOS;**不支持 Windows** | `uname -s` |
| libutil | 提供 `forkpty`,系统默认包含 | `ldconfig -p \| grep libutil` |
| sudo | 用于 runAsUser 切换 | `which sudo` |
| /dev/shm | tmpfs 用于存 SSH key 临时文件;没有也能跑(降级到 /tmp) | `mountpoint /dev/shm` |

## 2. sudoers 配置(关键)

如果**没有**设置 `runAsUser`(空值),pm 直接以服务用户身份跑 CLI,**不需要任何 sudoers 配置**,本节可跳过。

如果设了 `runAsUser`(如 `deploy`),需要给 pm 服务用户配 NOPASSWD 切换到目标用户的权限:

### 步骤

```bash
sudo visudo -f /etc/sudoers.d/pm-webterm
```

写入(假设 pm 服务以 `pm` 用户运行,要切换到 `deploy`):

```
# pm 网页终端 - 切换到目标用户跑 PTY,需要 NOPASSWD 且允许 SETENV(给 -E 参数用)
pm ALL=(deploy) NOPASSWD: SETENV: ALL

# 终端常用环境变量保留(给 sudo -E 用)
Defaults>deploy env_keep += "TERM LANG LC_ALL COLUMNS LINES GIT_SSH_COMMAND"
Defaults>deploy env_keep += "GIT_AUTHOR_NAME GIT_AUTHOR_EMAIL GIT_COMMITTER_NAME GIT_COMMITTER_EMAIL"
```

保存后 `visudo` 会自动校验语法。

### 验证

以 pm 服务用户切到 deploy 试一次,要立即返回不提示密码:

```bash
sudo -u pm sudo -H -E -u deploy -- bash -c 'whoami'
# 期望输出:deploy
```

如果提示输入密码,说明 sudoers 没生效,检查文件权限 (`chmod 440 /etc/sudoers.d/pm-webterm`) 和 visudo 报错。

### 多个 runAsUser

如果你预期有多个目标身份(开发 / 运维分开),把 `(deploy)` 换成 `(deploy, opsuser)`:

```
pm ALL=(deploy, opsuser) NOPASSWD: SETENV: ALL
```

但目前 pm webterm 的 CliConfig **只支持配置一个全局 runAsUser**(Q3 b),设多个 user 是为后续扩展预留。

## 3. systemd 服务用户配置

pm 服务的 `User=` 决定了 webterm 默认身份(没设 runAsUser 时 = 该用户)。常见 systemd 单元:

```ini
[Service]
User=pm
Group=pm
WorkingDirectory=/opt/pm
ExecStart=/usr/bin/dotnet Sb.Apps.Pm.WebApi.dll
Restart=always
```

如果之前 pm 一直以 `root` 运行,**强烈建议**改为专用低权限用户(`useradd -r -m pm`),配合上面的 sudoers 实现"默认低权限 + 按需切换"。

## 4. 应用配置

`appsettings.json` 默认已有 `WebTerminal` 节点:

```json
"WebTerminal": {
  "LogRoot": "logs/cli",
  "ScrollbackBytes": 4194304
}
```

| 配置项 | 默认值 | 说明 |
|--------|--------|------|
| `LogRoot` | `logs/cli` | PTY 输出落盘根目录(相对 ContentRoot 或绝对路径) |
| `ScrollbackBytes` | `4194304` (4 MiB) | 内存环形缓冲大小,网页重连 replay 上限 |

业务参数(运行身份/SSH key/清理策略)在 **网页 → 终端配置** 页面里改,会写到 `sb_pm_webterm_config` 表。

## 5. 数据库表

首次启动会通过 FreeSql CodeFirst 自动建表:

- `sb_pm_webterm_config` — 单行全局配置(runAsUser/SSH key 等)
- `sb_pm_webterm_session` — 每个会话的元数据 + PID + 退出码 + 日志路径

## 6. WebSocket 反向代理(nginx)

如果 pm 走 nginx 反代,需要给 `/webterm/ws/` 加 WebSocket upgrade 配置:

```nginx
location /webterm/ws/ {
    proxy_pass http://127.0.0.1:6020;
    proxy_http_version 1.1;
    proxy_set_header Upgrade $http_upgrade;
    proxy_set_header Connection "upgrade";
    proxy_set_header Host $host;
    proxy_read_timeout 86400s;
    proxy_send_timeout 86400s;
}
```

`proxy_read_timeout 86400s` 保证 idle 连接不被 nginx 主动 reset(默认 60s 会断)。

## 7. 排错

| 现象 | 原因 | 解决 |
|------|------|------|
| 创建会话立即退出,日志为空 | sudoers 没配,`sudo` 卡在密码提示 → 进程被 SIGTERM | 按 §2 配 sudoers + visudo 验证 |
| `forkpty failed: errno=2` | libutil 缺失或 shell 路径错 | 确认 `/bin/bash` 存在 |
| `forkpty failed: errno=12` | OOM | 加 swap 或排查内存泄漏 |
| 网页连上但没输出 | WebSocket 没 upgrade(nginx 配置缺) | §6 配置 |
| `git push` 提示无 SSH key | 没在终端配置里贴 SSH 私钥 | 配置页填好私钥保存 |
| pm 重启后所有会话变"已终止 pm_restart" | 设计如此 — pm 进程内 PTY 模式,Q1 a 决定的取舍 | 不修复 |

## 8. 安全要点

- **sudoers `NOPASSWD: ALL`** 等于把目标用户的全部 root 内权限暴露给 pm 服务用户 — 限制 `runAsUser` 到一个本身就是低权限的用户,不要直接切到 root
- 网页终端是**通用 shell 暴露**,角色权限走 pm 菜单授权 — 默认仅超管能进 `/webterm` 菜单,其他角色想用要超管显式授权
- SSH 私钥**加密**存数据库(AES,key 在 `appsettings.json` 的 `EncryptionKey`),启动会话时解密落盘到 tmpfs(600 权限),会话退出立即擦除
- 操作审计:每个会话的全部 stdout/stderr 落盘到 `LogRoot/{yyyymm}/session-{id}.log`,带元数据(owner / 启动时间 / 退出码)入库,30 天自动清理(可在配置页改)

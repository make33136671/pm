# pm

PM 项目仓库。

## 远程仓库（双远程）

| 远程名 | 地址 | 用途 |
|---|---|---|
| `origin` | 内网 GitLab `saibo/apps/pm` | **日常开发主用**，push / pull 都走这里 |
| `github` | https://github.com/make33136671/pm.git | **公网备份**，定期同步 |

`origin` 配置为「本地源码 + 内网 GitLab」，`github` 始终保留，不会被删除或改名。

## 日常流程

```bash
# 1. 日常提交（走 GitLab）
git add -A && git commit -m "feat: xxx"
git push origin main

# 2. 定期同步到 GitHub（见 scripts/sync-github.sh）
bash scripts/sync-github.sh
```

## 同步约定

- **更新代码优先 GitLab**：日常 `push` / `pull` 一律 `origin`。
- **定期同步 GitHub**：用 `scripts/sync-github.sh`，把 GitLab 的 `main` 推到 `github`。
- **永远保留 GitHub 远程**：任何清理操作都不得 `git remote remove github`。
- 首次从 GitHub 拉取时若本地为空，可先 `git pull github main --rebase` 再推 origin。

## 分支

主分支 `main`，与两个远程保持一致。

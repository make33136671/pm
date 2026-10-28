# pm

PM 项目仓库。

## 远程仓库（双远程）

| 远程名 | 地址 | 用途 |
|---|---|---|
| `origin` | 内网 GitLab `saibo/apps/pm` | **日常开发主用**，push / pull 都走这里 |
| `github` | https://github.com/make33136671/pm.git | **公网备份**，定期同步 |

`origin` 走内网 GitLab（`http://192.168.2.110:8080/saibo/apps/pm.git`，凭据已存在 `~/.git-credentials`），
`github` 始终保留，不会被删除或改名。

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
- **定期同步 GitHub**：用 `scripts/sync-github.sh`，先把 GitLab 拉到最新，再推到 `github`，带 3 次重试。
- **永远保留 GitHub 远程**：任何清理操作都不得 `git remote remove github`。
- 首次从 GitHub 拉取时若本地为空，可先 `git pull github main --rebase` 再推 origin。

## 分支

主分支 `main`，与两个远程保持一致。

---

## 内容

### `SB.Feature.FileManager` —— 服务器文件管理器

给 PM 后台加的**图形化文件管理**能力，与已有 web shell 互补：

- 目录浏览（面包屑 + 懒加载目录树 + 磁盘用量）
- 在线编辑文本文件（二进制识别、编码兼容、并发 hash 保护、原子写、自动 `.bak`）
- 新建 / 重命名 / 删除 / chmod / 上传 / 下载

完整说明见 [SB.Feature.FileManager/README.md](SB.Feature.FileManager/README.md)。

### 快速冒烟测试

```bash
cd tests/SB.Feature.FileManager.Smoke
dotnet run     # 52 项断言，覆盖路径穿越 / 白名单 / 软链逃逸 / 并发写 / 权限位
```

---

## 开发环境

- .NET 10（`dotnet --version` 应为 10.x）
- 后端插件依赖 `sb-framework` 源码（`SB.Framework.Core` / `SB.Framework.Models` / `SB.Framework.Analyzers`），
  以 ProjectReference 方式引用同级目录 `../../sb/sb-framework/`；发布时改为 PackageReference
  `SB.Framework.Core` / `SB.Framework.Models`。
- 前端页面按 Vben Admin（`sb-framework-admin`）的 views 约定书写，用 `admin/install.sh` 拷进去。

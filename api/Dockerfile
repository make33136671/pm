# syntax=docker/dockerfile:1.7
#
# Sb.Apps.Pm —— 单镜像同时承担两个角色：
#   1. 运行 PM 后端（.NET 8 WebApi）
#   2. 作为「代码管理 / 远程开发 / 编译机」—— webterm 里能直接跑 git / dotnet / node / pnpm
#      所以 runtime 阶段不是 aspnet runtime-only，而是装了全套工具链的 Ubuntu。
#
# 基板：Ubuntu 24.04（用户习惯）
# 工具链：.NET SDK 10 + Node 22 + pnpm + git
#
# 私有制品源（均为内网匿名源，无需凭据，已写进镜像环境）：
#   NuGet : https://nuget.saibo.club/v3/index.json   （来自仓库 NuGet.config）
#   npm   : https://npm.saibo.club                    （镜像内 /etc/npmrc 全局配置）
#
# 构建：
#   docker build -t sb-apps-pm:latest .
#
# 注意：DNS（首选 192.168.10.56 / 备选 8.8.8.8）是“运行期”属性，
#   写在 docker-compose.yml 的 dns: 字段或 docker run --dns，不在镜像里。
#   构建期若内网域名解析不到，给 build 加 --dns=192.168.10.56（或配 daemon.json）。

ARG UBUNTU_VERSION=24.04
ARG DOTNET_CHANNEL=10.0
ARG NODE_MAJOR=22

# ============================================================
# Stage 0: 公共基板 —— Ubuntu + .NET SDK 10 + Node 22 + pnpm + git
#   builder 和 runtime 都从它派生，保证编译环境与运行环境完全一致
# ============================================================
FROM ubuntu:${UBUNTU_VERSION} AS base
ARG DOTNET_CHANNEL
ARG NODE_MAJOR

ENV DEBIAN_FRONTEND=noninteractive \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
    DOTNET_ROOT=/usr/share/dotnet \
    PATH=/usr/share/dotnet:/usr/share/dotnet/tools:$PATH \
    NUGET_PACKAGES=/root/.nuget/packages

# --- 系统基础包 + git + webterm 依赖（bash/sudo/setsid 等）---
RUN set -eux; \
    apt-get update; \
    apt-get install -y --no-install-recommends \
        ca-certificates curl wget gnupg \
        git openssh-client \
        bash coreutils util-linux \
        sudo procps \
        unzip zip tzdata \
        # 部署看门狗(pm-deploy-guard)快照/回滚依赖
        rsync \
        # admin 静态站 + 反代 + 进程管理
        nginx supervisor \
        # .NET 在精简 Ubuntu 上的运行期依赖
        libicu74 libssl3 \
    ; \
    rm -rf /var/lib/apt/lists/*

# --- .NET SDK 10（官方 dotnet-install 脚本，装到 /usr/share/dotnet）---
RUN set -eux; \
    curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh; \
    chmod +x /tmp/dotnet-install.sh; \
    /tmp/dotnet-install.sh --channel "${DOTNET_CHANNEL}" --install-dir /usr/share/dotnet; \
    rm -f /tmp/dotnet-install.sh; \
    dotnet --info

# --- Node 22 (NodeSource) + pnpm (via corepack) ---
RUN set -eux; \
    curl -fsSL "https://deb.nodesource.com/setup_${NODE_MAJOR}.x" | bash -; \
    apt-get install -y --no-install-recommends nodejs; \
    rm -rf /var/lib/apt/lists/*; \
    corepack enable; \
    corepack prepare pnpm@latest --activate; \
    node --version; npm --version; pnpm --version

# --- 私有制品源（内网匿名，写进镜像全局配置）---
#   npm/pnpm → /etc/npmrc（全局，所有用户与 webterm 会话生效）
#   NuGet    → 仓库自带 NuGet.config 已声明 nuget.saibo.club（匿名），无需额外配置
ENV NPM_CONFIG_REGISTRY=https://npm.saibo.club/
RUN set -eux; \
    { \
      echo "registry=https://npm.saibo.club/"; \
      echo "fetch-retries=3"; \
    } > /etc/npmrc; \
    npm config --global set registry https://npm.saibo.club/; \
    npm config --global get registry

# ============================================================
# Stage 1: builder —— 还原 + 发布 PM 自身
# ============================================================
FROM base AS builder
WORKDIR /src

# NuGet 私有源：仓库 NuGet.config 已声明 nuget.saibo.club（匿名 v3 源），直接复用
COPY NuGet.config ./

# 先只 copy 工程清单，最大化利用 restore 层缓存
COPY sb-apps-pm.sln ./
COPY Sb.Apps.Pm.Models/*.csproj      Sb.Apps.Pm.Models/
COPY Sb.Apps.Pm.Services/*.csproj    Sb.Apps.Pm.Services/
COPY Sb.Apps.Pm.WebApi/*.csproj      Sb.Apps.Pm.WebApi/
COPY Sb.Apps.Pm.Services.Tests/*.csproj Sb.Apps.Pm.Services.Tests/

# restore：NuGet.config 里有一个 ../../../framework/core/nupkgs 本地源（构建上下文外，
#   容器内不存在），restore 单个 WebApi 工程不依赖它；--ignore-failed-sources 防其缺失报错。
RUN set -eux; \
    dotnet restore Sb.Apps.Pm.WebApi/Sb.Apps.Pm.WebApi.csproj \
      --ignore-failed-sources

# copy 全部源码并 publish（只发 WebApi，Tests 不参与）
COPY . .
RUN set -eux; \
    dotnet publish Sb.Apps.Pm.WebApi/Sb.Apps.Pm.WebApi.csproj \
      -c Release \
      -o /app/publish \
      --no-restore \
      -p:IncludeSourceRevisionInInformationalVersion=false \
      --nologo; \
    # appsettings 与 wwwroot 在运行时挂载，不进镜像
    rm -f /app/publish/appsettings.json /app/publish/appsettings.*.json; \
    rm -rf /app/publish/wwwroot

# ============================================================
# Stage 2: runtime —— 带全套工具链的 Ubuntu，跑 PM
# ============================================================
FROM base AS runtime
WORKDIR /app

# 时区（PM 后端 UTC 存储，容器时区给 webterm / 日志用）
ENV TZ=Asia/Shanghai \
    ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://0.0.0.0:6020

COPY --from=builder /app/publish ./

# admin 前端静态站（本地预构建产物，放到 deploy/admin-dist/）
COPY deploy/admin-dist/ /app/admin/

# nginx + supervisor 配置
COPY deploy/nginx.conf /etc/nginx/nginx.conf
COPY deploy/supervisord.conf /etc/supervisor/conf.d/pm.conf

# 部署看门狗:烤进镜像 /usr/local/bin(独立于 /app,坏部署也动不到它)
COPY deploy/pm-deploy-guard.sh /usr/local/bin/pm-deploy-guard.sh
RUN chmod +x /usr/local/bin/pm-deploy-guard.sh

# 运行期目录：均建议在 compose 里挂 volume 持久化
#   /app/appsettings.json   —— 配置（含密钥），运行时挂载
#   /app/wwwroot/storage    —— 品牌资源（安装向导生成）
#   /app/logs               —— Serilog + webterm 会话日志 + nginx 日志
#   /data/pm-workspace      —— git clone 工作区（workspace_root 指向这里）
RUN mkdir -p /app/wwwroot/storage /app/logs /data/pm-workspace /run

# 80 = nginx(admin + 反代)；6020 仅容器内部，不对外
EXPOSE 80

# 健康检查走 nginx → 后端
HEALTHCHECK --interval=30s --timeout=5s --start-period=60s --retries=3 \
    CMD curl -fsS http://127.0.0.1/api/Setup/Status || exit 1

# 注意：以 root 运行。本镜像定位为「单租户的代码管理 / 编译机」。
# supervisor 同时管 dotnet(API :6020 内部) + nginx(:80 admin+反代)。
ENTRYPOINT ["supervisord", "-c", "/etc/supervisor/conf.d/pm.conf"]

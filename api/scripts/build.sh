#!/bin/bash
# ============================================================================
# PM 项目专用构建脚本 (build.sh)
#
# 基于框架通用 sb-build.sh 派生，封装 PM 项目的特有逻辑：
#   - API：.NET 后端（调 sb-build.sh）
#   - Admin：Vue 3 前端（pnpm build → zip）
#   - 产物统一输出到 apps/pm/dist/
#
# 依赖：framework/sb-framework/scripts/sb-build.sh
# 用法：bash build.sh [api|admin|all|deploy] [options]
# ============================================================================
set -eo pipefail

# ============================================================================
# 1. 路径定位（不依赖调用位置）
# ============================================================================

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"          # scripts/
API_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"             # apps/pm/api
PM_ROOT="$(cd "$API_ROOT/.." && pwd)"                # apps/pm
DIST_DIR="$PM_ROOT/dist"
SB_BUILD="$(cd "$API_ROOT/../../framework/sb-framework/scripts/sb-build.sh" 2>/dev/null && echo "$PWD/sb-build.sh" || echo "")"

# ============================================================================
# 2. PM 项目默认配置
# ============================================================================

# API 的 .csproj 路径（相对于 API_ROOT）
API_CSPROJ="Sb.Apps.Pm.WebApi"

# Admin 目录（相对于 PM_ROOT）
ADMIN_DIR="admin"

# Archive 目录（部署前的备份）
ARCHIVE_DIR="$DIST_DIR/.archive"

# ============================================================================
# 3. 辅助函数
# ============================================================================

info()  { echo "[pm-build] INFO  $*"; }
warn()  { echo "[pm-build] WARN  $*" >&2; }
error() { echo "[pm-build] ERROR $*" >&2; exit 1; }

# ============================================================================
# 4. 构建函数
# ============================================================================

# ---- 4.1 API 构建 ----
# 委托给框架 sb-build.sh，传 PM 专属参数
build_api() {
    info "===== API (.NET 10) ====="

    if [ ! -f "$SB_BUILD" ]; then
        # 框架脚本不存在时用内联构建（容错）
        info "sb-build.sh 未安装，使用内联构建..."
        cd "$API_ROOT"
        dotnet publish "$API_CSPROJ/$API_CSPROJ.csproj" \
            -c Release -o "$DIST_DIR/api" \
            -p:DisableLicense=true \
            -p:IncludeSourceRevisionInInformationalVersion=false \
            --nologo -v minimal
        rm -f "$DIST_DIR"/api/appsettings*.json
        rm -rf "$DIST_DIR"/api/wwwroot
        info "API 构建完成: $DIST_DIR/api"
        return
    fi

    # 使用框架通用脚本
    bash "$SB_BUILD" \
        --project "$API_ROOT/$API_CSPROJ" \
        --name api \
        --output "$DIST_DIR/api" \
        --no-license \
        --no-zip \
        --keep-dist
}

# ---- 4.2 Admin 构建 ----
# PM 特有：Vben Admin monorepo，每次构建前需重生成 vite-config stub
build_admin() {
    info "===== Admin (Vue 3 + Vite) ====="

    local admin_root="$PM_ROOT/$ADMIN_DIR"

    if [ ! -d "$admin_root/apps/web-antd" ]; then
        error "Admin 目录不存在或结构异常: $admin_root"
    fi

    cd "$admin_root"

    # ════════════════════════════════════════════════════════════
    # 坑：vite-config dist 缓存了旧绝对路径，迁移项目目录后
    # jiti 模块解析报 ERR_MODULE_NOT_FOUND。
    # 每次构建前 clean + stub 是可靠修复。
    # ════════════════════════════════════════════════════════════
    info "修复 vite-config 缓存..."
    if [ -d "internal/vite-config" ]; then
        rm -rf internal/vite-config/dist
        (cd internal/vite-config && pnpm stub) 2>&1 | tail -1 || warn "vite-config stub 失败，继续..."
    else
        warn "internal/vite-config 不存在，跳过 stub"
    fi

    info "执行 pnpm build:antd..."
    pnpm build:antd 2>&1 | tail -3

    # 复制产物到统一 dist 目录
    if [ -f "apps/web-antd/dist.zip" ]; then
        cp apps/web-antd/dist.zip "$DIST_DIR/admin.zip"
        local size
        size=$(du -sh "$DIST_DIR/admin.zip" | cut -f1)
        info "Admin 构建完成: $DIST_DIR/admin.zip ($size)"
    else
        # 未打 zip 时直接复制 dist 目录
        if [ -d "apps/web-antd/dist" ]; then
            rm -rf "$DIST_DIR/admin"
            cp -r apps/web-antd/dist "$DIST_DIR/admin"
            info "Admin 构建完成: $DIST_DIR/admin/ (未打包)"
        else
            warn "Admin 构建产物未找到"
        fi
    fi

    cd "$API_ROOT"
}

# ---- 4.3 直接部署（服务器上用） ----
# 1. 备份当前运行目录
# 2. 构建
# 3. 替换运行目录
# 4. 重启
deploy_api() {
    local target_dir="${1:-/opt/pm/api}"
    info "===== 直接部署 API → $target_dir ====="

    # 备份
    if [ -d "$target_dir" ]; then
        mkdir -p "$ARCHIVE_DIR"
        local backup_name="api-$(date +%Y%m%d_%H%M%S)"
        cp -r "$target_dir" "$ARCHIVE_DIR/$backup_name"
        info "备份: $ARCHIVE_DIR/$backup_name"
    fi

    # 构建
    build_api

    # 部署（保留线上配置）
    if [ -d "$target_dir" ]; then
        # 备份线上配置
        if [ -f "$target_dir/appsettings.json" ]; then
            cp "$target_dir/appsettings.json" /tmp/_pm_appsettings.json
        fi
        if [ -d "$target_dir/wwwroot" ]; then
            cp -r "$target_dir/wwwroot" /tmp/_pm_wwwroot
        fi
    fi

    # 替换
    rm -rf "$target_dir"
    mkdir -p "$(dirname "$target_dir")"
    cp -r "$DIST_DIR/api" "$target_dir"

    # 恢复线上配置
    if [ -f /tmp/_pm_appsettings.json ]; then
        cp /tmp/_pm_appsettings.json "$target_dir/appsettings.json"
        rm /tmp/_pm_appsettings.json
        info "已恢复 appsettings.json"
    fi
    if [ -d /tmp/_pm_wwwroot ]; then
        cp -r /tmp/_pm_wwwroot "$target_dir/wwwroot"
        rm -rf /tmp/_pm_wwwroot
        info "已恢复 wwwroot"
    fi

    info "API 已部署到: $target_dir"

    # 重启
    local svc="${2:-pm-api}"
    info "重启服务: $svc"
    systemctl restart "$svc" 2>/dev/null || \
        supervisorctl restart "$svc" 2>/dev/null || \
        warn "请手动重启 $svc"
}

deploy_admin() {
    local target_dir="${1:-/opt/pm/admin}"
    info "===== 直接部署 Admin → $target_dir ====="

    build_admin

    if [ ! -f "$DIST_DIR/admin.zip" ]; then
        error "Admin 构建产物不存在: $DIST_DIR/admin.zip"
    fi

    # 备份
    if [ -d "$target_dir" ]; then
        mkdir -p "$ARCHIVE_DIR"
        local backup_name="admin-$(date +%Y%m%d_%H%M%S)"
        cp -r "$target_dir" "$ARCHIVE_DIR/$backup_name"
        info "备份: $ARCHIVE_DIR/$backup_name"
    fi

    # 部署
    rm -rf "$target_dir"
    mkdir -p "$target_dir"
    cd "$target_dir"
    unzip -q "$DIST_DIR/admin.zip"
    info "Admin 已部署到: $target_dir"

    local nginx_svc="${2:-nginx}"
    info "重启 nginx: $nginx_svc"
    systemctl restart "$nginx_svc" 2>/dev/null || \
        nginx -s reload 2>/dev/null || \
        warn "请手动重启 nginx"
}

# ---- 4.4 清理 ----
clean() {
    info "清理构建产物..."
    rm -rf "$DIST_DIR"/api "$DIST_DIR"/api.zip "$DIST_DIR"/admin "$DIST_DIR"/admin.zip
    info "已清理: $DIST_DIR"
}

# ============================================================================
# 5. 报告输出
# ============================================================================
report() {
    echo ""
    echo "========================================="
    echo "  PM 构建报告"
    echo "========================================="
    echo "  产物目录: $DIST_DIR"
    [ -d "$DIST_DIR/api" ]     && echo "  API:     $DIST_DIR/api/       ($(du -sh "$DIST_DIR/api" | cut -f1))"
    [ -f "$DIST_DIR/api.zip" ] && echo "  API zip: $DIST_DIR/api.zip"
    [ -d "$DIST_DIR/admin" ]   && echo "  Admin:   $DIST_DIR/admin/     ($(du -sh "$DIST_DIR/admin" | cut -f1))"
    [ -f "$DIST_DIR/admin.zip" ] && echo "  Admin zip: $DIST_DIR/admin.zip"
    echo "========================================="
}

# ============================================================================
# 6. 参数解析与派发
# ============================================================================

show_help() {
    cat << 'HELP'
PM 项目构建脚本

用法:
  bash build.sh [command] [options]

命令:
  api               构建 API（调框架 sb-build.sh）
  admin             构建 Admin（pnpm build:antd）
  all (默认)        全量构建 API + Admin
  deploy-api [dir]  构建并部署 API 到运行目录（默认 /opt/pm/api）
  deploy-admin [dir] 构建并部署 Admin 到运行目录（默认 /opt/pm/admin）
  clean             清理构建产物
  --help            显示帮助

选项（传递给 sb-build.sh）:
  --no-license      跳过 License（默认）
  --with-license    注入 License 公钥
  --version <ver>   指定版本号
  --keep-config     保留 appsettings.json
  --keep-dist       不清空输出目录
  --rid <id>        目标运行时

示例:
  bash build.sh                  # 全量构建
  bash build.sh api --version 2.0.0  # 指定版本构建 API
  bash build.sh deploy-api /opt/pm/api  # 构建并部署 API
  bash build.sh clean          # 清理
HELP
    exit 0
}

COMMAND="${1:-all}"

# 检查第一个参数如果是 --help 则显示帮助
case "$1" in
    -h|--help) show_help ;;
esac

shift 2>/dev/null || true

# 收集 sb-build.sh 的额外参数
EXTRA_ARGS=()
while [ $# -gt 0 ]; do
    EXTRA_ARGS+=("$1")
    shift
done

case "$COMMAND" in
    api|--api)
        build_api
        report
        ;;
    admin|--admin)
        build_admin
        report
        ;;
    all|--all|"")
        build_api
        echo ""
        build_admin
        report
        ;;
    deploy-api|deploy)
        deploy_api "${EXTRA_ARGS[0]:-/opt/pm/api}" "${EXTRA_ARGS[1]:-pm-api}"
        ;;
    deploy-admin)
        deploy_admin "${EXTRA_ARGS[0]:-/opt/pm/admin}" "${EXTRA_ARGS[1]:-nginx}"
        ;;
    clean)
        clean
        ;;
    *)
        echo "未知命令: $COMMAND"
        show_help
        ;;
esac

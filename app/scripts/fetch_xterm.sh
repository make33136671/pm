#!/usr/bin/env bash
# 下载 xterm.js 并内联进 terminal.html
#
# 为什么自己下载而不走 pubspec 依赖：
#   xterm.js 是 JS 库，Flutter 无法直接使用；且需要内联进 HTML 才能在 WebView 里跑。
#
# 用法：
#   bash scripts/fetch_xterm.sh
#
# 产物：
#   js/xterm.js / js/xterm.css / js/addon-fit.js
#   js/terminal.html（已被 inline_terminal.py 改成自包含版本）
set -euo pipefail

cd "$(dirname "$0")/.."

XTERM_VER="${XTERM_VER:-5.5.0}"
FIT_VER="${FIT_VER:-0.10.0}"

mkdir -p /tmp/xterm-dl js
cd /tmp/xterm-dl

echo "=== 下载 @xterm/xterm@${XTERM_VER} ==="
npm pack "@xterm/xterm@${XTERM_VER}" 2>/dev/null || \
  curl -sSL -o "xterm-xterm-${XTERM_VER}.tgz" \
    "https://registry.npmmirror.com/@xterm/xterm/-/xterm-${XTERM_VER}.tgz"
tar xzf "xterm-xterm-${XTERM_VER}.tgz"
cp package/lib/xterm.js  "$OLDPWD/js/xterm.js"
cp package/css/xterm.css "$OLDPWD/js/xterm.css"
cd "$OLDPWD"

echo "=== 下载 @xterm/addon-fit@${FIT_VER} ==="
cd /tmp/xterm-dl
npm pack "@xterm/addon-fit@${FIT_VER}" 2>/dev/null || \
  curl -sSL -o "xterm-addon-fit-${FIT_VER}.tgz" \
    "https://registry.npmmirror.com/@xterm/addon-fit/-/addon-fit-${FIT_VER}.tgz"
tar xzf "xterm-addon-fit-${FIT_VER}.tgz"
cp package/lib/addon-fit.js "$OLDPWD/js/addon-fit.js"
cd "$OLDPWD"

ls -lh js/*.js js/*.css

echo "=== 内联进 terminal.html ==="
node scripts/inline_terminal.js

echo ""
echo "完成。接下来："
echo "  flutter pub get"
echo "  flutter build apk --release"

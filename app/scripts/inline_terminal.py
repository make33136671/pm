#!/usr/bin/env python3
"""
把 xterm.js / CSS / fit-addon 内联进 terminal.html。

为什么需要它：
  WebView 用 loadHtmlString 加载页面时，页面的 origin 是 about:blank，
  无法通过相对路径访问 Flutter assets 里的 js/css 文件。
  所以构建前把所有 JS/CSS 直接内联进 HTML，成为一个自包含文件。

用法：
  python3 scripts/inline_terminal.py

输入：
  js/xterm.js          官方 xterm.js（下载：npm pack @xterm/xterm）
  js/xterm.css         xterm 样式
  js/addon-fit.js      @xterm/addon-fit
  js/terminal.html     模板（含 PLACEHOLDER 标记）

输出：
  js/terminal.html     就地覆盖为内联版
"""
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
JS_DIR = ROOT / "js"
TPL = JS_DIR / "terminal.html"

PLACEHOLDERS = {
    "/* ==== XTERM_CSS_PLACEHOLDER ==== */": "xterm.css",
    "// ==== XTERM_JS_PLACEHOLDER ====": "xterm.js",
    "// ==== FITADDON_JS_PLACEHOLDER ====": "addon-fit.js",
}


def main() -> int:
    if not TPL.exists():
        print(f"错误：找不到模板 {TPL}")
        return 1

    html = TPL.read_text(encoding="utf-8")

    # 已经内联过（模板标记不存在）就跳过，避免重复膨胀
    if not any(p in html for p in PLACEHOLDERS):
        print("提示：terminal.html 已内联过，跳过（如需重新生成请用 git 还原模板）")
        return 0

    for marker, filename in PLACEHOLDERS.items():
        src = JS_DIR / filename
        if not src.exists():
            print(f"警告：缺少 {src}，跳过该处内联")
            continue
        content = src.read_text(encoding="utf-8", errors="replace")
        # 防止内容里的 </script> 提前闭合标签
        content = content.replace("</script>", "<\\/script>")
        html = html.replace(marker, content)
        print(f"  内联 {filename} ({len(content) // 1024} KB)")

    TPL.write_text(html, encoding="utf-8")
    print(f"完成：{TPL} ({len(html) // 1024} KB)")
    return 0


if __name__ == "__main__":
    sys.exit(main())

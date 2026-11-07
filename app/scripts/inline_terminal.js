/**
 * 把 xterm.js / CSS / fit-addon 内联进 terminal.html（Node 版，无需 python）。
 *
 * 为什么需要：WebView 用 loadHtmlString 加载时 origin 是 about:blank，
 * 不能用相对路径访问 Flutter assets 里的 js/css，必须内联成自包含文件。
 *
 * 用法：node scripts/inline_terminal.js
 */
const fs = require('fs');
const path = require('path');

const ROOT = path.resolve(__dirname, '..');
const JS_DIR = path.join(ROOT, 'js');
const TEMPLATE = path.join(JS_DIR, 'terminal.template.html');
const OUTPUT = path.join(JS_DIR, 'terminal.html');

const PLACEHOLDERS = [
  ['/* ==== XTERM_CSS_PLACEHOLDER ==== */', 'xterm.css'],
  ['// ==== XTERM_JS_PLACEHOLDER ====', 'xterm.js'],
  ['// ==== FITADDON_JS_PLACEHOLDER ====', 'addon-fit.js'],
];

let html = fs.readFileSync(TEMPLATE, 'utf8');
console.log('模板: ' + TEMPLATE);

for (const [marker, file] of PLACEHOLDERS) {
  const src = path.join(JS_DIR, file);
  if (!fs.existsSync(src)) {
    console.warn(`警告：缺少 ${src}，跳过`);
    continue;
  }
  let content = fs.readFileSync(src, 'utf8');
  // 防止内容里的 </script> 提前闭合标签
  content = content.split('</script>').join('<\\/script>');
  html = html.split(marker).join(content);
  console.log(`  内联 ${file} (${Math.round(content.length / 1024)} KB)`);
}

fs.writeFileSync(OUTPUT, html, 'utf8');
console.log(`完成：${OUTPUT} (${Math.round(html.length / 1024)} KB)`);

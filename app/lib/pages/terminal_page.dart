import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:webview_flutter/webview_flutter.dart';

import '../session.dart';

/// 终端页：WebView 内嵌 xterm.js，通过 WebSocket 连后端 PTY。
///
/// 为什么用 WebView + xterm.js 而不是原生终端模拟器：
///   · xterm.js 已完整支持 VT100/xterm 序列（颜色、光标、鼠标、宽字符）
///   · 与网页端共用同一套后端协议（/webterm/ws/{id}），零后端改动
///   · 原生自己实现 VT100 解析器工作量巨大且难保兼容
///
/// 页面内置 html 由 assets/js/terminal.html 提供，
/// xterm.js 等资源本地打包，不依赖外网 CDN（内网环境必须离线可用）。
class TerminalPage extends StatefulWidget {
  final Session session;
  final int sessionId;

  const TerminalPage({
    super.key,
    required this.session,
    required this.sessionId,
  });

  @override
  State<TerminalPage> createState() => _TerminalPageState();
}

class _TerminalPageState extends State<TerminalPage> {
  late final WebViewController _controller;
  bool _ready = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    _initWebView();
  }

  Future<void> _initWebView() async {
    final wsUrl = widget.session.api.webtermSocketUrl(widget.sessionId);

    _controller = WebViewController()
      // 允许页面用 WebSocket 连后端（http 页面连 ws:// 是允许的，
      // 但为了兼容 https 页面 → wss://，这里放开混合内容限制）
      ..setJavaScriptMode(JavaScriptMode.unrestricted)
      ..setBackgroundColor(const Color(0xFF1E1E1E))
      ..addJavaScriptChannel(
        'FlutterBridge',
        onMessageReceived: (msg) {
          final data = msg.message;
          if (data == 'ready') {
            setState(() => _ready = true);
          } else if (data.startsWith('err:')) {
            setState(() => _error = data.substring(4));
          }
        },
      )
      ..setNavigationDelegate(
        NavigationDelegate(
          onPageFinished: (_) {
            // 页面加载完毕后，把 WebSocket 地址注入进去开始连接
            _controller.runJavaScript(
              'window.__PM_WS_URL__ = ${jsonEncode(wsUrl)};'
              'if (window.pmConnect) window.pmConnect();',
            );
          },
          onWebResourceError: (e) {
            setState(() => _error = '页面加载失败：${e.description}');
          },
        ),
      );

    // 加载本地打包的 terminal.html
    final html = await rootBundle.loadString('js/terminal.html');
    // 注意：xterm.js 走 flutter assets 无法被 webview 相对路径加载，
    // 所以把 CSS/JS 内容内联进 HTML（build 前由 scripts/inline_terminal.py 生成）
    await _controller.loadHtmlString(html);
  }

  void _reconnect() {
    setState(() {
      _error = null;
      _ready = false;
    });
    _controller.runJavaScript('if (window.pmConnect) window.pmConnect();');
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: const Color(0xFF1E1E1E),
      appBar: AppBar(
        title: Text('终端 #${widget.sessionId}'),
        actions: [
          IconButton(
            tooltip: '重新连接',
            icon: const Icon(Icons.refresh),
            onPressed: _reconnect,
          ),
          IconButton(
            tooltip: '粘贴',
            icon: const Icon(Icons.content_paste),
            onPressed: () async {
              final data = await Clipboard.getData(Clipboard.kTextPlain);
              if (data?.text != null) {
                _controller.runJavaScript(
                  'if (window.pmPaste) window.pmPaste(${jsonEncode(data!.text)});',
                );
              }
            },
          ),
        ],
      ),
      body: Column(
        children: [
          if (_error != null)
            Container(
              width: double.infinity,
              color: Theme.of(context).colorScheme.errorContainer,
              padding: const EdgeInsets.all(10),
              child: Row(
                children: [
                  const Icon(Icons.error_outline, size: 18),
                  const SizedBox(width: 8),
                  Expanded(
                    child: Text(
                      _error!,
                      style: const TextStyle(fontSize: 12),
                    ),
                  ),
                  TextButton(onPressed: _reconnect, child: const Text('重连')),
                ],
              ),
            ),
          if (!_ready && _error == null)
            const LinearProgressIndicator(minHeight: 2),
          Expanded(
            child: WebViewWidget(controller: _controller),
          ),
        ],
      ),
    );
  }
}

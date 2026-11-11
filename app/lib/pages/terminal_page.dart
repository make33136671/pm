import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:xterm/xterm.dart';

import '../session.dart';

/// 终端页：原生 xterm 渲染 + WebSocket 直连后端 PTY。
///
/// 为什么不再用 WebView 内嵌 xterm.js：
///   · WebView 首帧慢、内存占用高，软键盘与滚动手感与原生不一致
///   · 主题、字体、选中复制无法与 App 其余界面统一
///   · xterm 包为纯 Dart 实现，已完整支持 VT100/xterm 转义序列
///
/// 协议与网页端、旧版 WebView 完全一致（`/webterm/ws/{id}?token=`）：
///   client → server: Binary 帧 = PTY 输入
///                    Text 帧 {"type":"resize","cols":N,"rows":N}
///                    Text 帧 {"type":"ping","t":<ts>}
///   server → client: Binary 帧 = PTY 输出
///                    Text 帧 {"type":"pong","t":<ts>} / {"type":"exit","code":N}
///
/// 关闭码语义：
///   1000/1001  正常关闭 → 不重连
///   4001       被同账号其他设备顶替 → 提示，不重连（避免两端互顶）
///   其他       异常断开 → 指数退避重连（1s→2s→…上限 10s，最多 8 次）
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
  static const _maxRetries = 8;
  static const _maxBackoff = Duration(seconds: 10);
  static const _codeSuperseded = 4001;

  /// 10000 行滚动缓冲：够翻历史，又不至于吃太多内存
  final Terminal _terminal = Terminal(maxLines: 10000);
  final TerminalController _controller = TerminalController();
  final FocusNode _focusNode = FocusNode();

  WebSocket? _ws;
  StreamSubscription<dynamic>? _sub;

  bool _ready = false;
  String? _error;
  int _retries = 0;
  Timer? _reconnectTimer;
  Timer? _pingTimer;
  bool _disposed = false;

  /// 被别处顶替后不再重连，避免两个设备互相踢
  bool _superseded = false;

  @override
  void initState() {
    super.initState();

    // 键盘输入 → 发往后端（终端把用户输入通过 onOutput 抛出来）
    _terminal.onOutput = (data) {
      _send(Uint8List.fromList(utf8.encode(data)));
    };

    // 终端尺寸变化（软键盘弹出 / 旋转）→ 通知后端 PTY resize
    _terminal.onResize = (width, height, _, __) {
      _sendText('{"type":"resize","cols":$width,"rows":$height}');
    };

    _connect();

    // 首帧后请求焦点，让软键盘可直接使用
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (mounted) _focusNode.requestFocus();
    });
  }

  @override
  void dispose() {
    _disposed = true;
    _reconnectTimer?.cancel();
    _pingTimer?.cancel();
    _sub?.cancel();
    _ws?.close();
    _focusNode.dispose();
    _controller.dispose();
    super.dispose();
  }

  // ======================= 连接管理 =======================

  Future<void> _connect() async {
    if (_disposed || _superseded) return;
    _reconnectTimer?.cancel();
    _pingTimer?.cancel();
    await _sub?.cancel();
    try {
      await _ws?.close();
    } catch (_) {}

    final url = widget.session.api.webtermSocketUrl(widget.sessionId);
    if (mounted) {
      setState(() {
        _error = null;
        _ready = false;
      });
    }

    try {
      final ws = await WebSocket.connect(url);
      if (_disposed) {
        await ws.close();
        return;
      }
      ws.pingInterval = const Duration(seconds: 20);
      _ws = ws;
      _retries = 0;

      _sub = ws.listen(
        _onData,
        onError: (Object e) => _onDisconnected('连接异常：$e'),
        onDone: _onDone,
        cancelOnError: true,
      );

      // 建连后立刻把当前尺寸告诉后端，避免 PTY 用默认尺寸渲染错位
      _sendText(
        '{"type":"resize","cols":${_terminal.viewWidth},"rows":${_terminal.viewHeight}}',
      );

      if (mounted) setState(() => _ready = true);

      // 应用层心跳：部分反向代理会掐掉长时间无数据的连接
      _pingTimer = Timer.periodic(const Duration(seconds: 30), (_) {
        _sendText(
          '{"type":"ping","t":${DateTime.now().millisecondsSinceEpoch}}',
        );
      });
    } catch (e) {
      _onDisconnected('无法连接终端：$e');
    }
  }

  void _onData(dynamic data) {
    if (_disposed) return;

    if (data is String) {
      // Text 帧：pong / exit 等控制消息
      try {
        final m = jsonDecode(data);
        if (m is Map && m['type'] == 'exit') {
          _terminal.write('\r\n\x1b[33m[进程已退出，code=${m['code']}]\x1b[0m\r\n');
        }
      } catch (_) {
        // 不是 JSON，当普通文本输出
        _terminal.write(data);
      }
      return;
    }

    // Binary 帧：PTY 原始字节。
    // 用 allowMalformed 兜底——多字节 UTF-8 可能被切在帧边界上。
    if (data is List<int>) {
      _terminal.write(utf8.decode(data, allowMalformed: true));
    }
  }

  void _onDone() {
    if (_disposed) return;
    final code = _ws?.closeCode;
    final reason = _ws?.closeReason ?? '';

    if (code == _codeSuperseded) {
      _superseded = true;
      setState(() {
        _ready = false;
        _error = '该会话已在其他设备打开（$reason），本机连接已断开';
      });
      return;
    }
    if (code == 1000 || code == 1001) {
      setState(() {
        _ready = false;
        _error = '连接已关闭（code=$code）';
      });
      return;
    }
    _onDisconnected('连接断开（code=${code ?? '无'}）');
  }

  void _onDisconnected(String message) {
    if (_disposed || _superseded) return;
    _pingTimer?.cancel();

    if (_retries >= _maxRetries) {
      setState(() {
        _ready = false;
        _error = '$message，已重试 $_maxRetries 次，可手动重连';
      });
      return;
    }

    _retries++;
    // 指数退避：1s, 2s, 4s, 8s, 10s, 10s...
    final delay = Duration(seconds: 1 << (_retries - 1));
    final capped = delay > _maxBackoff ? _maxBackoff : delay;
    setState(() {
      _ready = false;
      _error = '$message，${capped.inSeconds}s 后重连（第 $_retries/$_maxRetries 次）';
    });
    _reconnectTimer = Timer(capped, _connect);
  }

  void _send(Uint8List bytes) {
    final ws = _ws;
    if (ws == null || ws.readyState != WebSocket.open) return;
    try {
      ws.add(bytes);
    } catch (_) {}
  }

  void _sendText(String text) {
    final ws = _ws;
    if (ws == null || ws.readyState != WebSocket.open) return;
    try {
      ws.add(text);
    } catch (_) {}
  }

  // ======================= 交互动作 =======================

  void _reconnect() {
    setState(() {
      _error = null;
      _retries = 0;
      _superseded = false;
    });
    _connect();
  }

  Future<void> _paste() async {
    final data = await Clipboard.getData(Clipboard.kTextPlain);
    final text = data?.text;
    if (text == null || text.isEmpty) return;
    _terminal.paste(text);
    if (mounted) _focusNode.requestFocus();
  }

  Future<void> _copyAll() async {
    final lines = _terminal.buffer.lines.toList();
    final sb = StringBuffer();
    for (final line in lines) {
      sb.writeln(line.toString().replaceAll(RegExp(r'\s+$'), ''));
    }
    await Clipboard.setData(ClipboardData(text: sb.toString()));
    if (!mounted) return;
    ScaffoldMessenger.of(context).showSnackBar(
      const SnackBar(content: Text('已复制全部输出')),
    );
  }

  @override
  Widget build(BuildContext context) {
    final cs = Theme.of(context).colorScheme;
    return Scaffold(
      backgroundColor: const Color(0xFF1E1E1E),
      appBar: AppBar(
        title: Text('终端 #${widget.sessionId}'),
        actions: [
          IconButton(
            tooltip: '重连',
            icon: const Icon(Icons.refresh),
            onPressed: _reconnect,
          ),
          IconButton(
            tooltip: '粘贴',
            icon: const Icon(Icons.content_paste),
            onPressed: _paste,
          ),
          IconButton(
            tooltip: '复制全部输出',
            icon: const Icon(Icons.copy_all),
            onPressed: _copyAll,
          ),
        ],
      ),
      body: Column(
        children: [
          if (_error != null)
            Container(
              width: double.infinity,
              color: cs.errorContainer,
              padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 8),
              child: Row(
                children: [
                  Icon(Icons.error_outline,
                      size: 18, color: cs.onErrorContainer),
                  const SizedBox(width: 8),
                  Expanded(
                    child: Text(
                      _error!,
                      style: TextStyle(fontSize: 12, color: cs.onErrorContainer),
                    ),
                  ),
                  TextButton(
                    onPressed: _reconnect,
                    child: const Text('重连'),
                  ),
                ],
              ),
            ),
          if (!_ready && _error == null)
            const LinearProgressIndicator(minHeight: 2),
          Expanded(
            // 点终端任意位置都唤起软键盘
            child: GestureDetector(
              onTap: () => _focusNode.requestFocus(),
              child: TerminalView(
                _terminal,
                controller: _controller,
                focusNode: _focusNode,
                autofocus: true,
                // 手机屏窄，字号固定，保证列数合理
                textStyle: const TerminalStyle(fontSize: 13, height: 1.25),
                backgroundOpacity: 1,
              ),
            ),
          ),
        ],
      ),
    );
  }
}

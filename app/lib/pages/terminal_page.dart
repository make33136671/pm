import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:file_picker/file_picker.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:xterm/xterm.dart';

import '../api/pm_api.dart';
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

  /// 底部命令输入框（与网页端一致：快捷操作栏与终端原生输入并存）
  final TextEditingController _inputCtrl = TextEditingController();
  bool _uploading = false;

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
    _inputCtrl.dispose();
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

  /// 发送一行命令：文本和回车**必须分两帧**发，中间留间隔。
  ///
  /// 合并成 `text\r` 单帧发出时，PTY 端的 TUI 程序（如 Claude Code、vim）
  /// 会把整块识别成「粘贴」，`\r` 被当成粘贴内容里的换行而不是「提交」。
  /// 分段发送后接收端才会把它当成独立的回车键。
  void _sendLine(String text) {
    _send(Uint8List.fromList(utf8.encode(text)));
    Timer(const Duration(milliseconds: 100), () {
      _send(Uint8List.fromList(utf8.encode('\r')));
    });
  }

  void _sendEnter() {
    _send(Uint8List.fromList(utf8.encode('\r')));
  }

  /// 方向键：标准 ANSI 转义序列
  void _sendArrow(String dir) {
    const seq = {
      'up': '\x1b[A',
      'down': '\x1b[B',
      'right': '\x1b[C',
      'left': '\x1b[D',
    };
    final s = seq[dir];
    if (s != null) _send(Uint8List.fromList(utf8.encode(s)));
  }

  /// 底部命令输入框发送
  void _sendFromInput() {
    final text = _inputCtrl.text;
    if (text.isEmpty) return;
    _sendLine(text);
    _inputCtrl.clear();
  }

  /// 上传文件：上传到 PM 通用存储后，把 curl 下载命令填进输入框。
  ///
  /// shell 与 pm 进程权限域隔离，不能直接给文件系统路径，
  /// 所以走「上传拿 URL → 终端内 curl 拉取」这条与网页端一致的路径。
  Future<void> _pickAndUpload() async {
    if (_uploading) return;
    FilePickerResult? picked;
    try {
      picked = await FilePicker.platform.pickFiles(withData: false);
    } catch (e) {
      _toast('无法打开文件选择器：$e');
      return;
    }
    final path = picked?.files.singleOrNull?.path;
    final name = picked?.files.singleOrNull?.name;
    if (path == null || name == null) return;

    setState(() => _uploading = true);
    try {
      final detail = await widget.session.api.uploadDirect(path, name);
      final url = detail['absfileName'] as String? ?? '';
      final realName = detail['name'] as String? ?? name;
      if (url.isEmpty) throw ApiException('上传成功但未返回 URL');
      // 文件名里的单引号要转义，避免拼出错误的 shell 命令
      final safe = realName.replaceAll("'", "'\\''");
      final cmd = "curl -fsSL '$url' -o '$safe'";
      setState(() {
        _inputCtrl.text = _inputCtrl.text.isEmpty
            ? cmd
            : '${_inputCtrl.text} $cmd';
      });
      _toast('已上传，curl 命令已填入输入框');
    } on ApiException catch (e) {
      _toast('上传失败：${e.message}');
    } catch (e) {
      _toast('上传失败：$e');
    } finally {
      if (mounted) setState(() => _uploading = false);
    }
  }

  void _toast(String msg) {
    if (!mounted) return;
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(content: Text(msg), duration: const Duration(seconds: 2)),
    );
  }

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
          _buildShortcutBar(context),
        ],
      ),
    );
  }

  /// 底部快捷操作栏（对齐网页端终端的两行设计）
  ///
  /// 第一行：📎上传 + 命令输入框 + 发送
  /// 第二行：方向键 / Tab / ok / 继续 / 换行 / 空格 / 回车
  ///
  /// 说明：这些按钮**不抢终端焦点**，点击后焦点仍留在输入框或终端，
  /// 方便连续操作（与网页端 `@mousedown.prevent` 的意图一致）。
  Widget _buildShortcutBar(BuildContext context) {
    final cs = Theme.of(context).colorScheme;
    return Container(
      decoration: BoxDecoration(
        color: const Color(0xFF252526),
        border: const Border(top: BorderSide(color: Color(0xFF3C3C3C))),
      ),
      padding: EdgeInsets.only(
        left: 8,
        right: 8,
        top: 6,
        // 避开系统手势条
        bottom: 6 + MediaQuery.of(context).padding.bottom,
      ),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          // ---- 第一行：上传 + 命令输入 ----
          Row(
            children: [
              IconButton(
                tooltip: '上传文件到终端',
                onPressed: _uploading ? null : _pickAndUpload,
                icon: _uploading
                    ? const SizedBox(
                        width: 18,
                        height: 18,
                        child: CircularProgressIndicator(strokeWidth: 2),
                      )
                    : const Icon(Icons.attach_file, size: 20),
                visualDensity: VisualDensity.compact,
                color: cs.onSurfaceVariant,
              ),
              Expanded(
                child: SizedBox(
                  height: 38,
                  child: TextField(
                    controller: _inputCtrl,
                    style: const TextStyle(fontSize: 13),
                    decoration: InputDecoration(
                      isDense: true,
                      hintText: '输入命令，回车发送到终端',
                      hintStyle: TextStyle(
                        fontSize: 12,
                        color: cs.onSurfaceVariant.withValues(alpha: 0.6),
                      ),
                      filled: true,
                      fillColor: const Color(0xFF1E1E1E),
                      contentPadding:
                          const EdgeInsets.symmetric(horizontal: 10, vertical: 8),
                      border: OutlineInputBorder(
                        borderRadius: BorderRadius.circular(6),
                        borderSide: BorderSide.none,
                      ),
                      suffixIcon: ListenableBuilder(
                        listenable: _inputCtrl,
                        builder: (_, __) => _inputCtrl.text.isEmpty
                            ? const SizedBox.shrink()
                            : IconButton(
                                tooltip: '清空',
                                icon: const Icon(Icons.clear, size: 16),
                                visualDensity: VisualDensity.compact,
                                onPressed: () => _inputCtrl.clear(),
                              ),
                      ),
                    ),
                    onSubmitted: (_) => _sendFromInput(),
                  ),
                ),
              ),
              const SizedBox(width: 6),
              ListenableBuilder(
                listenable: _inputCtrl,
                builder: (_, __) => FilledButton(
                  onPressed: _inputCtrl.text.isEmpty ? null : _sendFromInput,
                  style: FilledButton.styleFrom(
                    minimumSize: const Size(56, 38),
                    padding: const EdgeInsets.symmetric(horizontal: 12),
                    visualDensity: VisualDensity.compact,
                  ),
                  child: const Text('发送', style: TextStyle(fontSize: 13)),
                ),
              ),
            ],
          ),
          const SizedBox(height: 6),
          // ---- 第二行：快捷按键 ----
          SingleChildScrollView(
            scrollDirection: Axis.horizontal,
            child: Row(
              children: [
                _key(context, '↑', () => _sendArrow('up')),
                _key(context, '↓', () => _sendArrow('down')),
                _key(context, '←', () => _sendArrow('left')),
                _key(context, '→', () => _sendArrow('right')),
                _key(context, 'Tab', () => _sendTextRaw('\t')),
                _key(context, 'ok', () => _sendLine('ok')),
                _key(context, '继续', () => _sendLine('继续')),
                _key(context, '换行', () => _sendTextRaw('\n')),
                _key(context, '空格', () => _sendTextRaw(' ')),
                _key(
                  context,
                  '⏎ 回车',
                  _sendEnter,
                  primary: true,
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }

  /// 发送原始字符串（不追加回车）
  void _sendTextRaw(String s) {
    _send(Uint8List.fromList(utf8.encode(s)));
  }

  /// 快捷按键：紧凑样式，横向排列
  Widget _key(
    BuildContext context,
    String label,
    VoidCallback onTap, {
    bool primary = false,
  }) {
    final cs = Theme.of(context).colorScheme;
    return Padding(
      padding: const EdgeInsets.only(right: 6),
      child: SizedBox(
        height: 34,
        child: primary
            ? FilledButton(
                onPressed: onTap,
                style: FilledButton.styleFrom(
                  padding: const EdgeInsets.symmetric(horizontal: 14),
                  visualDensity: VisualDensity.compact,
                  textStyle: const TextStyle(fontSize: 13),
                ),
                child: Text(label),
              )
            : OutlinedButton(
                onPressed: onTap,
                style: OutlinedButton.styleFrom(
                  padding: const EdgeInsets.symmetric(horizontal: 12),
                  visualDensity: VisualDensity.compact,
                  textStyle: const TextStyle(fontSize: 13),
                  side: BorderSide(color: cs.outlineVariant),
                  foregroundColor: cs.onSurface,
                ),
                child: Text(label),
              ),
      ),
    );
  }
}

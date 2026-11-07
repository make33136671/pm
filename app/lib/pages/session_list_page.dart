import 'package:flutter/material.dart';

import '../api/pm_api.dart';
import '../models/webterm.dart';
import '../session.dart';
import 'terminal_page.dart';

/// 终端会话列表：新建 / 打开 / 结束 / 删除
class SessionListPage extends StatefulWidget {
  final Session session;
  const SessionListPage({super.key, required this.session});

  @override
  State<SessionListPage> createState() => _SessionListPageState();
}

class _SessionListPageState extends State<SessionListPage> {
  List<CliSession> _sessions = [];
  bool _loading = true;
  String? _error;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final list = await widget.session.api.listSessions();
      setState(() => _sessions = list);
    } on ApiException catch (e) {
      setState(() => _error = e.message);
    } catch (e) {
      setState(() => _error = '加载失败：$e');
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _createSession() async {
    final ctrl = TextEditingController();
    final name = await showDialog<String>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Text('新建终端会话'),
        content: TextField(
          controller: ctrl,
          autofocus: true,
          decoration: const InputDecoration(
            labelText: '会话名称（可留空自动命名）',
            hintText: '例如：部署调试',
          ),
          onSubmitted: (v) => Navigator.pop(ctx, v),
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(ctx),
            child: const Text('取消'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(ctx, ctrl.text),
            child: const Text('创建'),
          ),
        ],
      ),
    );
    if (name == null) return;

    try {
      // 用手机屏幕大致估算终端尺寸（后续 WebView 建连后会按实际尺寸 resize）
      final id = await widget.session.api.createSession(
        name: name.trim().isEmpty ? null : name.trim(),
        cols: 100,
        rows: 30,
      );
      if (!mounted) return;
      if (id <= 0) {
        _toast('创建失败：未返回会话 ID');
        return;
      }
      _openTerminal(id);
    } on ApiException catch (e) {
      _toast(e.message);
    }
  }

  void _openTerminal(int id) {
    Navigator.of(context).push(
      MaterialPageRoute(
        builder: (_) => TerminalPage(session: widget.session, sessionId: id),
      ),
    );
  }

  Future<void> _destroy(CliSession s) async {
    final ok = await _confirm('结束会话', '将终止「${s.name}」里的所有进程，确定？');
    if (!ok) return;
    try {
      await widget.session.api.destroySession(s.id);
      await _load();
    } on ApiException catch (e) {
      _toast(e.message);
    }
  }

  Future<void> _delete(CliSession s) async {
    final ok = await _confirm('删除记录', '只会删除这条会话记录，不影响已结束的进程。继续？');
    if (!ok) return;
    try {
      await widget.session.api.deleteSession(s.id);
      await _load();
    } on ApiException catch (e) {
      _toast(e.message);
    }
  }

  Future<bool> _confirm(String title, String content) async {
    final r = await showDialog<bool>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: Text(title),
        content: Text(content),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(ctx, false),
            child: const Text('取消'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(ctx, true),
            child: const Text('确定'),
          ),
        ],
      ),
    );
    return r ?? false;
  }

  void _toast(String msg) {
    if (!mounted) return;
    ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(msg)));
  }

  @override
  Widget build(BuildContext context) {
    if (_loading) {
      return const Center(child: CircularProgressIndicator());
    }
    if (_error != null) {
      return Center(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(Icons.cloud_off,
                size: 48, color: Theme.of(context).colorScheme.error),
            const SizedBox(height: 12),
            Padding(
              padding: const EdgeInsets.symmetric(horizontal: 32),
              child: Text(_error!, textAlign: TextAlign.center),
            ),
            const SizedBox(height: 16),
            FilledButton.tonal(onPressed: _load, child: const Text('重试')),
          ],
        ),
      );
    }

    return RefreshIndicator(
      onRefresh: _load,
      child: _sessions.isEmpty
          ? ListView(
              children: [
                const SizedBox(height: 120),
                Icon(Icons.terminal_outlined,
                    size: 64,
                    color: Theme.of(context).colorScheme.outlineVariant),
                const SizedBox(height: 12),
                const Center(child: Text('还没有终端会话')),
                const SizedBox(height: 8),
                Center(
                  child: Text(
                    '点右下角 + 新建一个',
                    style: TextStyle(
                        color: Theme.of(context).colorScheme.onSurfaceVariant),
                  ),
                ),
              ],
            )
          : ListView.separated(
              itemCount: _sessions.length,
              separatorBuilder: (_, __) => const Divider(height: 1),
              itemBuilder: (ctx, i) {
                final s = _sessions[i];
                return ListTile(
                  leading: CircleAvatar(
                    backgroundColor: s.running
                        ? Colors.green.withValues(alpha: 0.15)
                        : Theme.of(ctx).colorScheme.surfaceContainerHighest,
                    child: Icon(
                      Icons.terminal,
                      size: 20,
                      color: s.running
                          ? Colors.green
                          : Theme.of(ctx).colorScheme.outline,
                    ),
                  ),
                  title: Text(
                    s.name.isEmpty ? '会话 #${s.id}' : s.name,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                  ),
                  subtitle: Text(
                    '${s.running ? "运行中" : "已结束"} · ${s.cwd ?? "-"}',
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: const TextStyle(fontSize: 12),
                  ),
                  onTap: s.running
                      ? () => _openTerminal(s.id)
                      : () => _toast('会话已结束，请新建一个'),
                  trailing: PopupMenuButton<String>(
                    onSelected: (v) {
                      if (v == 'destroy') _destroy(s);
                      if (v == 'delete') _delete(s);
                    },
                    itemBuilder: (_) => [
                      if (s.running)
                        const PopupMenuItem(
                          value: 'destroy',
                          child: Row(children: [
                            Icon(Icons.stop_circle_outlined, size: 18),
                            SizedBox(width: 8),
                            Text('结束会话'),
                          ]),
                        ),
                      const PopupMenuItem(
                        value: 'delete',
                        child: Row(children: [
                          Icon(Icons.delete_outline, size: 18),
                          SizedBox(width: 8),
                          Text('删除记录'),
                        ]),
                      ),
                    ],
                  ),
                );
              },
            ),
    );
  }
}

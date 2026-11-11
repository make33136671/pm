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

  /// 彻底删除单条（物理删除，不可恢复）
  Future<void> _purgeOne(CliSession s) async {
    final ok = await _confirmDanger(
      '彻底删除会话',
      '将永久删除「${s.name}」的记录和日志文件。\n\n'
          '此操作不可恢复！',
    );
    if (!ok) return;
    try {
      await widget.session.api.purgeSession(s.id);
      _toast('已彻底删除');
      await _load();
    } on ApiException catch (e) {
      _toast(e.message);
    }
  }

  /// 清空所有已结束会话的历史（运行中的不受影响）
  Future<void> _purgeAll() async {
    final finished = _sessions.where((s) => !s.running).length;
    if (finished == 0) {
      _toast('没有可清除的历史会话');
      return;
    }
    final ok = await _confirmDanger(
      '清空历史',
      '将永久删除所有已结束的 $finished 个会话及其日志文件。\n'
          '运行中的会话不受影响。\n\n'
          '此操作不可恢复！',
    );
    if (!ok) return;
    try {
      await widget.session.api.purgeAllSessions();
      _toast('历史已清空');
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

  /// 危险操作确认：红色按钮 + 警示图标，默认焦点在「取消」
  Future<bool> _confirmDanger(String title, String content) async {
    final r = await showDialog<bool>(
      context: context,
      builder: (ctx) {
        final cs = Theme.of(ctx).colorScheme;
        return AlertDialog(
          icon: Icon(Icons.warning_amber_rounded, color: cs.error, size: 28),
          title: Text(title),
          content: Text(content),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(ctx, false),
              child: const Text('取消'),
            ),
            FilledButton(
              style: FilledButton.styleFrom(backgroundColor: cs.error),
              onPressed: () => Navigator.pop(ctx, true),
              child: const Text('确认删除'),
            ),
          ],
        );
      },
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
      child: ListView(
        padding: EdgeInsets.zero,
        children: [
          // 列表最顶部固定入口：新建会话（替代原右下角悬浮按钮）
          _buildCreateTile(context),
          const Divider(height: 1),
          _buildClearHistoryBar(context),
          if (_sessions.isEmpty)
            ..._buildEmptyState(context)
          else
            ..._buildSessionTiles(context),
        ],
      ),
    );
  }

  /// 顶部「新建终端会话」入口
  Widget _buildCreateTile(BuildContext context) {
    final cs = Theme.of(context).colorScheme;
    return InkWell(
      onTap: _createSession,
      child: Padding(
        padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 14),
        child: Row(
          children: [
            Container(
              width: 40,
              height: 40,
              decoration: BoxDecoration(
                color: cs.primaryContainer,
                borderRadius: BorderRadius.circular(3),
              ),
              child: Icon(Icons.add, color: cs.onPrimaryContainer, size: 22),
            ),
            const SizedBox(width: 16),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                mainAxisSize: MainAxisSize.min,
                children: [
                  Text(
                    '新建终端会话',
                    style: TextStyle(
                      fontSize: 16,
                      fontWeight: FontWeight.w600,
                      color: cs.primary,
                    ),
                  ),
                  const SizedBox(height: 2),
                  Text(
                    '在服务器上开一个 shell',
                    style: TextStyle(fontSize: 12, color: cs.onSurfaceVariant),
                  ),
                ],
              ),
            ),
            Icon(Icons.chevron_right, size: 20, color: cs.outline),
          ],
        ),
      ),
    );
  }

  /// 顶部「清空历史」入口（位于新建行下方，仅在存在已结束会话时显示）
  Widget _buildClearHistoryBar(BuildContext context) {
    final cs = Theme.of(context).colorScheme;
    final finished = _sessions.where((s) => !s.running).length;
    if (finished == 0) return const SizedBox.shrink();
    return InkWell(
      onTap: _purgeAll,
      child: Padding(
        padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 10),
        child: Row(
          children: [
            Icon(Icons.delete_sweep_outlined, size: 18, color: cs.error),
            const SizedBox(width: 8),
            Text(
              '清空历史（$finished）',
              style: TextStyle(fontSize: 13, color: cs.error),
            ),
            const Spacer(),
            Text(
              '不含运行中',
              style: TextStyle(fontSize: 11, color: cs.onSurfaceVariant),
            ),
          ],
        ),
      ),
    );
  }

  /// 空状态
  List<Widget> _buildEmptyState(BuildContext context) {
    final cs = Theme.of(context).colorScheme;
    return [
      const SizedBox(height: 100),
      Icon(Icons.terminal_outlined, size: 64, color: cs.outlineVariant),
      const SizedBox(height: 12),
      const Center(child: Text('还没有终端会话')),
      const SizedBox(height: 8),
      Center(
        child: Text(
          '点上方「新建终端会话」开一个',
          style: TextStyle(color: cs.onSurfaceVariant),
        ),
      ),
      const SizedBox(height: 100),
    ];
  }

  /// 会话列表项
  List<Widget> _buildSessionTiles(BuildContext context) {
    final cs = Theme.of(context).colorScheme;
    final tiles = <Widget>[];
    for (var i = 0; i < _sessions.length; i++) {
      final s = _sessions[i];
      if (i > 0) tiles.add(const Divider(height: 1));
      tiles.add(
        ListTile(
          leading: CircleAvatar(
            backgroundColor: s.running
                ? Colors.green.withValues(alpha: 0.15)
                : cs.surfaceContainerHighest,
            child: Icon(
              Icons.terminal,
              size: 20,
              color: s.running ? Colors.green : cs.outline,
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
              if (v == 'purge') _purgeOne(s);
            },
            itemBuilder: (ctx) => [
              if (s.running)
                const PopupMenuItem(
                  value: 'destroy',
                  child: Row(children: [
                    Icon(Icons.stop_circle_outlined, size: 18),
                    SizedBox(width: 8),
                    Text('结束会话'),
                  ]),
                ),
              PopupMenuItem(
                value: 'purge',
                child: Row(children: [
                  Icon(Icons.delete_forever,
                      size: 18, color: Theme.of(ctx).colorScheme.error),
                  const SizedBox(width: 8),
                  Text(
                    '彻底删除',
                    style: TextStyle(color: Theme.of(ctx).colorScheme.error),
                  ),
                ]),
              ),
            ],
          ),
        ),
      );
    }
    return tiles;
  }
}

import 'package:flutter/material.dart';

import '../api/pm_api.dart';
import '../models/webterm.dart';
import '../session.dart';

/// 文件编辑页：读取文本 → 编辑 → 保存（带并发 hash 保护）
class FileEditPage extends StatefulWidget {
  final Session session;
  final String path;

  const FileEditPage({
    super.key,
    required this.session,
    required this.path,
  });

  @override
  State<FileEditPage> createState() => _FileEditPageState();
}

class _FileEditPageState extends State<FileEditPage> {
  final _ctrl = TextEditingController();
  final _scrollCtrl = ScrollController();

  FileContent? _file;
  bool _loading = true;
  bool _saving = false;
  bool _dirty = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    _load();
  }

  @override
  void dispose() {
    _ctrl.dispose();
    _scrollCtrl.dispose();
    super.dispose();
  }

  Future<void> _load() async {
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final f = await widget.session.api.readFile(widget.path);
      _ctrl.text = f.content;
      _ctrl.addListener(_onChanged);
      setState(() {
        _file = f;
        _dirty = false;
      });
    } on ApiException catch (e) {
      setState(() => _error = e.message);
    } catch (e) {
      setState(() => _error = '读取失败：$e');
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  void _onChanged() {
    if (_file == null) return;
    final changed = _ctrl.text != _file!.content;
    if (changed != _dirty) setState(() => _dirty = changed);
  }

  Future<void> _save() async {
    final f = _file;
    if (f == null) return;
    setState(() => _saving = true);
    try {
      await widget.session.api.saveFile(
        f.path,
        _ctrl.text,
        expectedHash: f.hash,
        createBackup: true,
      );
      if (!mounted) return;
      setState(() => _dirty = false);
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('保存成功（已生成 .bak 备份）')),
      );
      // 重新读一次，刷新 hash，避免下次保存被并发保护拦下
      final fresh = await widget.session.api.readFile(f.path);
      _file = fresh;
    } on ApiException catch (e) {
      if (!mounted) return;
      // 并发保护会被后端拒绝（文件已被他人修改）
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text(e.message)),
      );
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  Future<bool> _onWillPop() async {
    if (!_dirty) return true;
    final r = await showDialog<bool>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Text('放弃修改？'),
        content: const Text('当前文件有未保存的改动，返回将丢失。'),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(ctx, false),
            child: const Text('继续编辑'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(ctx, true),
            child: const Text('放弃'),
          ),
        ],
      ),
    );
    return r ?? false;
  }

  @override
  Widget build(BuildContext context) {
    final f = _file;

    return PopScope(
      canPop: !_dirty,
      onPopInvokedWithResult: (didPop, _) async {
        if (didPop) return;
        final ok = await _onWillPop();
        if (ok && context.mounted) Navigator.pop(context, _dirty);
      },
      child: Scaffold(
        appBar: AppBar(
          title: Text(
            f?.name ?? widget.path.split('/').last,
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
          ),
          actions: [
            if (_dirty)
              Padding(
                padding: const EdgeInsets.symmetric(horizontal: 8),
                child: Center(
                  child: Text(
                    '未保存',
                    style: TextStyle(
                      color: Theme.of(context).colorScheme.error,
                      fontSize: 12,
                    ),
                  ),
                ),
              ),
            IconButton(
              tooltip: '保存',
              icon: _saving
                  ? const SizedBox(
                      width: 20,
                      height: 20,
                      child: CircularProgressIndicator(strokeWidth: 2),
                    )
                  : const Icon(Icons.save_outlined),
              onPressed: (_saving || f == null || !f.editable) ? null : _save,
            ),
          ],
        ),
        body: _loading
            ? const Center(child: CircularProgressIndicator())
            : _error != null
                ? Center(
                    child: Column(
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        Icon(Icons.error_outline,
                            size: 48,
                            color: Theme.of(context).colorScheme.error),
                        const SizedBox(height: 12),
                        Text(_error!, textAlign: TextAlign.center),
                        const SizedBox(height: 16),
                        FilledButton.tonal(
                            onPressed: _load, child: const Text('重试')),
                      ],
                    ),
                  )
                : Column(
                    children: [
                      Container(
                        width: double.infinity,
                        color: Theme.of(context)
                            .colorScheme
                            .surfaceContainerHighest,
                        padding: const EdgeInsets.symmetric(
                            horizontal: 12, vertical: 6),
                        child: Text(
                          '${f!.lineCount} 行 · ${f.lineEnding} · ${f.mode} · '
                          '${(f.size / 1024).toStringAsFixed(1)} KB',
                          style: const TextStyle(fontSize: 11),
                        ),
                      ),
                      if (!f.editable)
                        Container(
                          width: double.infinity,
                          color: Theme.of(context).colorScheme.errorContainer,
                          padding: const EdgeInsets.all(10),
                          child: Text(
                            f.isBinary
                                ? '这是二进制文件，不支持在线编辑'
                                : f.tooLarge
                                    ? '文件过大，不支持在线编辑'
                                    : '文件没有写权限',
                            style: const TextStyle(fontSize: 12),
                          ),
                        ),
                      Expanded(
                        child: Padding(
                          padding: const EdgeInsets.all(8),
                          child: TextField(
                            controller: _ctrl,
                            scrollController: _scrollCtrl,
                            readOnly: !f.editable,
                            maxLines: null,
                            expands: true,
                            textAlignVertical: TextAlignVertical.top,
                            keyboardType: TextInputType.multiline,
                            style: const TextStyle(
                              fontFamily: 'monospace',
                              fontSize: 13,
                              height: 1.45,
                            ),
                            decoration: const InputDecoration(
                              border: InputBorder.none,
                              isDense: true,
                              contentPadding: EdgeInsets.zero,
                            ),
                          ),
                        ),
                      ),
                    ],
                  ),
      ),
    );
  }
}

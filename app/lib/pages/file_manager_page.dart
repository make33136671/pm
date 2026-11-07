import 'package:file_picker/file_picker.dart';
import 'package:flutter/material.dart';
import 'package:path_provider/path_provider.dart';
import 'package:share_plus/share_plus.dart';

import '../api/pm_api.dart';
import '../models/webterm.dart';
import '../session.dart';
import 'file_edit_page.dart';

/// 文件管理：浏览 / 编辑 / 上传 / 下载 / 新建 / 重命名 / 删除
class FileManagerPage extends StatefulWidget {
  final Session session;
  const FileManagerPage({super.key, required this.session});

  @override
  State<FileManagerPage> createState() => _FileManagerPageState();
}

class _FileManagerPageState extends State<FileManagerPage> {
  String _path = '';
  DirectoryListing? _listing;
  bool _loading = true;
  String? _error;
  bool _initialized = false;

  @override
  void initState() {
    super.initState();
    _init();
  }

  /// 首次进入：先读系统设置里的默认路径，失败则退回 /
  Future<void> _init() async {
    if (_initialized) return;
    _initialized = true;
    String start = '/';
    try {
      final d = await widget.session.api.defaultPath();
      if (d.path.isNotEmpty) start = d.path;
    } catch (_) {
      // 读不到默认路径就用根目录
    }
    await _load(start);
  }

  Future<void> _load(String path) async {
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final l = await widget.session.api.listDir(path);
      setState(() {
        _listing = l;
        _path = l.path;
      });
    } on ApiException catch (e) {
      setState(() => _error = e.message);
    } catch (e) {
      setState(() => _error = '加载失败：$e');
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  void _toast(String msg) {
    if (!mounted) return;
    ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(msg)));
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

  // ==================== 操作 ====================

  Future<void> _open(FileEntry e) async {
    if (e.isDirectory) {
      await _load(e.path);
      return;
    }
    if (!e.editable) {
      _toast('该文件不支持在线编辑（二进制或过大）');
      return;
    }
    final changed = await Navigator.of(context).push<bool>(
      MaterialPageRoute(
        builder: (_) => FileEditPage(session: widget.session, path: e.path),
      ),
    );
    if (changed == true) await _load(_path);
  }

  Future<void> _newEntry({required bool isDirectory}) async {
    final ctrl = TextEditingController();
    final name = await showDialog<String>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: Text(isDirectory ? '新建目录' : '新建文件'),
        content: TextField(
          controller: ctrl,
          autofocus: true,
          decoration: const InputDecoration(
            labelText: '名称',
            hintText: '例如 app.conf',
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
    if (name == null || name.trim().isEmpty) return;
    final base = _path == '/' ? '' : _path;
    try {
      await widget.session.api
          .createEntry('$base/${name.trim()}', isDirectory: isDirectory);
      await _load(_path);
      _toast(isDirectory ? '目录已创建' : '文件已创建');
    } on ApiException catch (e) {
      _toast(e.message);
    }
  }

  Future<void> _rename(FileEntry e) async {
    final ctrl = TextEditingController(text: e.name);
    final name = await showDialog<String>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Text('重命名'),
        content: TextField(
          controller: ctrl,
          autofocus: true,
          decoration: const InputDecoration(labelText: '新名称'),
          onSubmitted: (v) => Navigator.pop(ctx, v),
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(ctx),
            child: const Text('取消'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(ctx, ctrl.text),
            child: const Text('确定'),
          ),
        ],
      ),
    );
    if (name == null || name.trim().isEmpty || name == e.name) return;
    try {
      await widget.session.api.renameEntry(e.path, name.trim());
      await _load(_path);
    } on ApiException catch (ex) {
      _toast(ex.message);
    }
  }

  Future<void> _delete(FileEntry e) async {
    final ok = await _confirm(
      '删除「${e.name}」',
      e.isDirectory ? '目录及其中所有内容将被移入回收站。' : '文件将被移入回收站，可在 PC 后台恢复。',
    );
    if (!ok) return;
    try {
      final msg = await widget.session.api
          .deleteEntry(e.path, recursive: e.isDirectory);
      await _load(_path);
      _toast(msg);
    } on ApiException catch (ex) {
      _toast(ex.message);
    }
  }

  Future<void> _upload() async {
    try {
      final res = await FilePicker.platform.pickFiles(withData: false);
      if (res == null || res.files.isEmpty) return;
      final f = res.files.first;
      if (f.path == null) {
        _toast('无法读取所选文件');
        return;
      }
      _toast('正在上传 ${f.name}…');
      await widget.session.api.uploadFile(_path, f.path!, f.name);
      await _load(_path);
      _toast('上传完成');
    } on ApiException catch (e) {
      _toast(e.message);
    } catch (e) {
      _toast('上传失败：$e');
    }
  }

  Future<void> _download(FileEntry e) async {
    try {
      final dir = await getTemporaryDirectory();
      final savePath = '${dir.path}/${e.name}';
      _toast('正在下载 ${e.name}…');
      final file = await widget.session.api.downloadFile(e.path, savePath);
      if (!mounted) return;
      // 用系统分享面板让用户保存到本地/发送到其他 App
      await Share.shareXFiles([XFile(file.path)], text: e.name);
      _toast('已下载到 ${file.path}');
    } on ApiException catch (ex) {
      _toast(ex.message);
    } catch (ex) {
      _toast('下载失败：$ex');
    }
  }

  Future<void> _showActions(FileEntry e) async {
    final action = await showModalBottomSheet<String>(
      context: context,
      builder: (ctx) => SafeArea(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            ListTile(
              leading: const Icon(Icons.info_outline),
              title: Text(e.name),
              subtitle: Text('${e.mode}  ${e.owner}:${e.group}  ${e.sizeText}'),
            ),
            const Divider(height: 1),
            if (!e.isDirectory)
              ListTile(
                leading: const Icon(Icons.download_outlined),
                title: const Text('下载'),
                onTap: () => Navigator.pop(ctx, 'download'),
              ),
            ListTile(
              leading: const Icon(Icons.drive_file_rename_outline),
              title: const Text('重命名'),
              onTap: () => Navigator.pop(ctx, 'rename'),
            ),
            ListTile(
              leading: const Icon(Icons.delete_outline),
              title: const Text('删除'),
              onTap: () => Navigator.pop(ctx, 'delete'),
            ),
          ],
        ),
      ),
    );
    if (!mounted) return;
    switch (action) {
      case 'download':
        await _download(e);
        break;
      case 'rename':
        await _rename(e);
        break;
      case 'delete':
        await _delete(e);
        break;
    }
  }

  // ==================== UI ====================

  Widget _buildBreadcrumb() {
    final crumbs = _listing?.breadcrumbs ?? [];
    if (crumbs.isEmpty) return const SizedBox.shrink();
    return SizedBox(
      height: 40,
      child: ListView.builder(
        scrollDirection: Axis.horizontal,
        padding: const EdgeInsets.symmetric(horizontal: 12),
        itemCount: crumbs.length,
        itemBuilder: (ctx, i) {
          final c = crumbs[i];
          final last = i == crumbs.length - 1;
          return Center(
            child: Row(
              children: [
                InkWell(
                  onTap: last ? null : () => _load(c.path),
                  child: Padding(
                    padding: const EdgeInsets.symmetric(horizontal: 4, vertical: 4),
                    child: Text(
                      c.name,
                      style: TextStyle(
                        color: last
                            ? Theme.of(ctx).colorScheme.onSurface
                            : Theme.of(ctx).colorScheme.primary,
                        fontWeight: last ? FontWeight.w600 : FontWeight.normal,
                      ),
                    ),
                  ),
                ),
                if (!last)
                  Icon(Icons.chevron_right,
                      size: 16, color: Theme.of(ctx).colorScheme.outline),
              ],
            ),
          );
        },
      ),
    );
  }

  Widget _buildList() {
    final l = _listing;
    if (l == null) return const SizedBox.shrink();
    final items = l.all;

    if (items.isEmpty) {
      return const Center(child: Text('空目录'));
    }

    return RefreshIndicator(
      onRefresh: () => _load(_path),
      child: ListView.separated(
        itemCount: items.length,
        separatorBuilder: (_, __) => const Divider(height: 1),
        itemBuilder: (ctx, i) {
          final e = items[i];
          return ListTile(
            leading: Icon(
              e.isDirectory
                  ? Icons.folder
                  : _iconForExt(e.extension),
              color: e.isDirectory
                  ? Colors.amber
                  : Theme.of(ctx).colorScheme.primary,
            ),
            title: Text(
              e.name,
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
            ),
            subtitle: Text(
              e.isDirectory ? '${e.mode}  ${e.owner}' : '${e.sizeText}  ${e.mode}',
              style: const TextStyle(fontSize: 11),
            ),
            onTap: () => _open(e),
            onLongPress: () => _showActions(e),
            trailing: const Icon(Icons.more_vert, size: 18),
          );
        },
      ),
    );
  }

  IconData _iconForExt(String ext) {
    switch (ext) {
      case 'json':
      case 'yaml':
      case 'yml':
      case 'toml':
      case 'ini':
      case 'conf':
        return Icons.settings_suggest_outlined;
      case 'md':
      case 'txt':
        return Icons.description_outlined;
      case 'log':
        return Icons.receipt_long_outlined;
      case 'sh':
      case 'bash':
        return Icons.terminal;
      case 'zip':
      case 'gz':
      case 'tar':
      case '7z':
        return Icons.archive_outlined;
      case 'png':
      case 'jpg':
      case 'jpeg':
      case 'gif':
      case 'webp':
        return Icons.image_outlined;
      default:
        return Icons.insert_drive_file_outlined;
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      body: Column(
        children: [
          _buildBreadcrumb(),
          const Divider(height: 1),
          Expanded(
            child: _loading
                ? const Center(child: CircularProgressIndicator())
                : _error != null
                    ? Center(
                        child: Column(
                          mainAxisSize: MainAxisSize.min,
                          children: [
                            Icon(Icons.cloud_off,
                                size: 48,
                                color: Theme.of(context).colorScheme.error),
                            const SizedBox(height: 12),
                            Padding(
                              padding:
                                  const EdgeInsets.symmetric(horizontal: 32),
                              child: Text(_error!, textAlign: TextAlign.center),
                            ),
                            const SizedBox(height: 16),
                            FilledButton.tonal(
                              onPressed: () => _load(_path.isEmpty ? '/' : _path),
                              child: const Text('重试'),
                            ),
                          ],
                        ),
                      )
                    : _buildList(),
          ),
        ],
      ),
      floatingActionButton: PopupMenuButton<String>(
        icon: const Icon(Icons.add),
        onSelected: (v) {
          switch (v) {
            case 'file':
              _newEntry(isDirectory: false);
              break;
            case 'dir':
              _newEntry(isDirectory: true);
              break;
            case 'upload':
              _upload();
              break;
            case 'refresh':
              _load(_path.isEmpty ? '/' : _path);
              break;
          }
        },
        itemBuilder: (_) => const [
          PopupMenuItem(
            value: 'file',
            child: Row(children: [
              Icon(Icons.note_add_outlined, size: 20),
              SizedBox(width: 12),
              Text('新建文件'),
            ]),
          ),
          PopupMenuItem(
            value: 'dir',
            child: Row(children: [
              Icon(Icons.create_new_folder_outlined, size: 20),
              SizedBox(width: 12),
              Text('新建目录'),
            ]),
          ),
          PopupMenuItem(
            value: 'upload',
            child: Row(children: [
              Icon(Icons.upload_file_outlined, size: 20),
              SizedBox(width: 12),
              Text('上传文件'),
            ]),
          ),
          PopupMenuDivider(),
          PopupMenuItem(
            value: 'refresh',
            child: Row(children: [
              Icon(Icons.refresh, size: 20),
              SizedBox(width: 12),
              Text('刷新'),
            ]),
          ),
        ],
      ),
    );
  }
}

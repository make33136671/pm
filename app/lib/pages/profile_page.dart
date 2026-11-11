import 'package:file_picker/file_picker.dart';
import 'package:flutter/material.dart';

import '../api/pm_api.dart';
import '../session.dart';
import 'login_page.dart';

/// 个人中心：展示账号信息、修改头像、修改密码
///
/// 接口与网页端 profile 页一致：
///   POST /admin/Auth/ChangeAvatar   { avatar }
///   POST /admin/Auth/ChangePassword { oldPassword, newPassword }
class ProfilePage extends StatefulWidget {
  final Session session;
  const ProfilePage({super.key, required this.session});

  @override
  State<ProfilePage> createState() => _ProfilePageState();
}

class _ProfilePageState extends State<ProfilePage> {
  bool _uploadingAvatar = false;

  @override
  void initState() {
    super.initState();
    // 页面打开时刷新一次，拿到最新头像/名称
    widget.session.addListener(_onSessionChanged);
  }

  @override
  void dispose() {
    widget.session.removeListener(_onSessionChanged);
    super.dispose();
  }

  void _onSessionChanged() {
    if (mounted) setState(() {});
  }

  void _toast(String msg) {
    if (!mounted) return;
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(content: Text(msg), duration: const Duration(seconds: 2)),
    );
  }

  // ==================== 头像 ====================

  Future<void> _changeAvatar() async {
    if (_uploadingAvatar) return;
    FilePickerResult? picked;
    try {
      picked = await FilePicker.platform.pickFiles(
        type: FileType.image,
        withData: false,
      );
    } catch (e) {
      _toast('无法打开相册：$e');
      return;
    }
    final file = picked?.files.singleOrNull;
    if (file?.path == null) return;

    setState(() => _uploadingAvatar = true);
    try {
      // 与网页端一致：先传到通用存储拿 URL，再提交给 ChangeAvatar
      final detail = await widget.session.api
          .uploadDirect(file!.path!, file.name);
      final url = detail['absfileName'] as String? ?? '';
      if (url.isEmpty) throw ApiException('上传成功但未返回 URL');

      final info = await widget.session.api.changeAvatar(url);
      await widget.session.updateUserInfo(info);
      _toast('头像已更新');
    } on ApiException catch (e) {
      _toast('修改头像失败：${e.message}');
    } catch (e) {
      _toast('修改头像失败：$e');
    } finally {
      if (mounted) setState(() => _uploadingAvatar = false);
    }
  }

  // ==================== 密码 ====================

  Future<void> _changePassword() async {
    final result = await showDialog<bool>(
      context: context,
      builder: (_) => _ChangePasswordDialog(session: widget.session),
    );
    if (result == true) {
      // 修改成功后强制重新登录，避免旧 token 语义不清
      setState(() {});
    }
  }

  // ==================== 退出 ====================

  Future<void> _logout() async {
    final ok = await showDialog<bool>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Text('退出登录'),
        content: const Text('确定要退出当前账号吗？'),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(ctx, false),
            child: const Text('取消'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(ctx, true),
            child: const Text('退出'),
          ),
        ],
      ),
    );
    if (ok != true) return;

    await widget.session.api.logout();
    await widget.session.clear();
    if (!mounted) return;
    Navigator.of(context).pushAndRemoveUntil(
      MaterialPageRoute(builder: (_) => LoginPage(session: widget.session)),
      (_) => false,
    );
  }

  @override
  Widget build(BuildContext context) {
    final s = widget.session;
    final cs = Theme.of(context).colorScheme;
    return Scaffold(
      appBar: AppBar(title: const Text('个人中心')),
      body: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          // ---- 头像与基本信息 ----
          Center(
            child: Column(
              children: [
                GestureDetector(
                  onTap: _uploadingAvatar ? null : _changeAvatar,
                  child: Stack(
                    children: [
                      _Avatar(session: s, size: 88, uploading: _uploadingAvatar),
                      Positioned(
                        right: 0,
                        bottom: 0,
                        child: Container(
                          padding: const EdgeInsets.all(5),
                          decoration: BoxDecoration(
                            color: cs.primary,
                            shape: BoxShape.circle,
                            border: Border.all(
                              color: Theme.of(context).scaffoldBackgroundColor,
                              width: 2,
                            ),
                          ),
                          child: Icon(
                            Icons.photo_camera,
                            size: 14,
                            color: cs.onPrimary,
                          ),
                        ),
                      ),
                    ],
                  ),
                ),
                const SizedBox(height: 12),
                Text(
                  s.displayName.isEmpty ? s.username : s.displayName,
                  style: Theme.of(context)
                      .textTheme
                      .titleLarge
                      ?.copyWith(fontWeight: FontWeight.bold),
                ),
                const SizedBox(height: 4),
                Text(
                  s.roleName.isEmpty ? s.username : '${s.username} · ${s.roleName}',
                  style: TextStyle(color: cs.onSurfaceVariant, fontSize: 13),
                ),
                const SizedBox(height: 12),
                TextButton.icon(
                  onPressed: _uploadingAvatar ? null : _changeAvatar,
                  icon: _uploadingAvatar
                      ? const SizedBox(
                          width: 14,
                          height: 14,
                          child: CircularProgressIndicator(strokeWidth: 2),
                        )
                      : const Icon(Icons.edit_outlined, size: 16),
                  label: Text(_uploadingAvatar ? '上传中…' : '更换头像'),
                ),
              ],
            ),
          ),
          const SizedBox(height: 8),
          const Divider(),
          const SizedBox(height: 8),

          // ---- 服务器 ----
          _InfoTile(
            icon: Icons.dns_outlined,
            title: '当前服务器',
            subtitle: s.baseUrl,
          ),
          const SizedBox(height: 4),

          // ---- 功能入口 ----
          Card(
            elevation: 0,
            color: cs.surfaceContainerLowest,
            shape: RoundedRectangleBorder(
              borderRadius: BorderRadius.circular(3),
              side: BorderSide(color: cs.outlineVariant),
            ),
            child: Column(
              children: [
                ListTile(
                  leading: const Icon(Icons.lock_reset_outlined),
                  title: const Text('修改密码'),
                  trailing: const Icon(Icons.chevron_right, size: 20),
                  onTap: _changePassword,
                ),
                Divider(height: 1, color: cs.outlineVariant),
                ListTile(
                  leading: const Icon(Icons.image_outlined),
                  title: const Text('修改头像'),
                  trailing: const Icon(Icons.chevron_right, size: 20),
                  onTap: _uploadingAvatar ? null : _changeAvatar,
                ),
              ],
            ),
          ),

          const SizedBox(height: 24),
          OutlinedButton.icon(
            onPressed: _logout,
            icon: const Icon(Icons.logout, size: 18),
            style: OutlinedButton.styleFrom(
              foregroundColor: cs.error,
              side: BorderSide(color: cs.error.withValues(alpha: 0.5)),
              padding: const EdgeInsets.symmetric(vertical: 12),
              shape: RoundedRectangleBorder(
                borderRadius: BorderRadius.circular(3),
              ),
            ),
            label: const Text('退出登录'),
          ),
        ],
      ),
    );
  }
}

/// 头像：有图显示网络图，无图显示名字首字
class _Avatar extends StatelessWidget {
  final Session session;
  final double size;
  final bool uploading;

  const _Avatar({
    required this.session,
    required this.size,
    this.uploading = false,
  });

  @override
  Widget build(BuildContext context) {
    final cs = Theme.of(context).colorScheme;
    final url = session.avatarUrl;
    return Container(
      width: size,
      height: size,
      decoration: BoxDecoration(
        color: cs.primaryContainer,
        shape: BoxShape.circle,
        border: Border.all(color: cs.outlineVariant),
      ),
      clipBehavior: Clip.antiAlias,
      child: uploading
          ? Center(
              child: SizedBox(
                width: size * 0.35,
                height: size * 0.35,
                child: const CircularProgressIndicator(strokeWidth: 2),
              ),
            )
          : url.isEmpty
              ? Center(
                  child: Text(
                    session.avatarInitial,
                    style: TextStyle(
                      fontSize: size * 0.38,
                      fontWeight: FontWeight.bold,
                      color: cs.onPrimaryContainer,
                    ),
                  ),
                )
              : Image.network(
                  url,
                  width: size,
                  height: size,
                  fit: BoxFit.cover,
                  errorBuilder: (_, __, ___) => Center(
                    child: Text(
                      session.avatarInitial,
                      style: TextStyle(
                        fontSize: size * 0.38,
                        fontWeight: FontWeight.bold,
                        color: cs.onPrimaryContainer,
                      ),
                    ),
                  ),
                ),
    );
  }
}

class _InfoTile extends StatelessWidget {
  final IconData icon;
  final String title;
  final String subtitle;

  const _InfoTile({
    required this.icon,
    required this.title,
    required this.subtitle,
  });

  @override
  Widget build(BuildContext context) {
    return ListTile(
      contentPadding: EdgeInsets.zero,
      leading: Icon(icon, size: 20),
      title: Text(title, style: const TextStyle(fontSize: 13)),
      subtitle: Text(
        subtitle,
        style: const TextStyle(fontSize: 12),
        maxLines: 1,
        overflow: TextOverflow.ellipsis,
      ),
    );
  }
}

/// 修改密码对话框
class _ChangePasswordDialog extends StatefulWidget {
  final Session session;
  const _ChangePasswordDialog({required this.session});

  @override
  State<_ChangePasswordDialog> createState() => _ChangePasswordDialogState();
}

class _ChangePasswordDialogState extends State<_ChangePasswordDialog> {
  final _formKey = GlobalKey<FormState>();
  final _oldCtrl = TextEditingController();
  final _newCtrl = TextEditingController();
  final _confirmCtrl = TextEditingController();
  bool _saving = false;
  bool _obscure = true;
  String? _error;

  @override
  void dispose() {
    _oldCtrl.dispose();
    _newCtrl.dispose();
    _confirmCtrl.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    if (!_formKey.currentState!.validate()) return;
    setState(() {
      _saving = true;
      _error = null;
    });
    try {
      await widget.session.api
          .changePassword(_oldCtrl.text, _newCtrl.text);
      if (!mounted) return;
      Navigator.pop(context, true);
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('密码修改成功')),
      );
    } on ApiException catch (e) {
      setState(() => _error = e.message);
    } catch (e) {
      setState(() => _error = '修改失败：$e');
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final cs = Theme.of(context).colorScheme;
    return AlertDialog(
      title: const Text('修改密码'),
      content: Form(
        key: _formKey,
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            TextFormField(
              controller: _oldCtrl,
              obscureText: _obscure,
              decoration: const InputDecoration(
                labelText: '旧密码',
                prefixIcon: Icon(Icons.lock_outline),
              ),
              validator: (v) =>
                  (v == null || v.isEmpty) ? '请输入旧密码' : null,
            ),
            const SizedBox(height: 12),
            TextFormField(
              controller: _newCtrl,
              obscureText: _obscure,
              decoration: InputDecoration(
                labelText: '新密码',
                prefixIcon: const Icon(Icons.lock_reset_outlined),
                suffixIcon: IconButton(
                  icon: Icon(_obscure
                      ? Icons.visibility_off_outlined
                      : Icons.visibility_outlined),
                  onPressed: () => setState(() => _obscure = !_obscure),
                ),
              ),
              validator: (v) =>
                  (v == null || v.length < 6) ? '密码长度不能少于6位' : null,
            ),
            const SizedBox(height: 12),
            TextFormField(
              controller: _confirmCtrl,
              obscureText: _obscure,
              decoration: const InputDecoration(
                labelText: '确认新密码',
                prefixIcon: Icon(Icons.check_circle_outline),
              ),
              validator: (v) =>
                  (v != _newCtrl.text) ? '两次输入的密码不一致' : null,
            ),
            if (_error != null) ...[
              const SizedBox(height: 12),
              Container(
                width: double.infinity,
                padding: const EdgeInsets.all(10),
                decoration: BoxDecoration(
                  color: cs.errorContainer,
                  borderRadius: BorderRadius.circular(3),
                ),
                child: Text(
                  _error!,
                  style: TextStyle(fontSize: 12, color: cs.onErrorContainer),
                ),
              ),
            ],
          ],
        ),
      ),
      actions: [
        TextButton(
          onPressed: _saving ? null : () => Navigator.pop(context, false),
          child: const Text('取消'),
        ),
        FilledButton(
          onPressed: _saving ? null : _submit,
          child: _saving
              ? const SizedBox(
                  width: 16,
                  height: 16,
                  child: CircularProgressIndicator(strokeWidth: 2),
                )
              : const Text('确定'),
        ),
      ],
    );
  }
}

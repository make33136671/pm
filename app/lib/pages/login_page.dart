import 'package:flutter/material.dart';

import '../api/pm_api.dart';
import '../session.dart';
import 'home_page.dart';

/// 登录页：服务器地址 + 账号 + 密码
///
/// 首次使用要填服务器地址（例如 192.168.2.110:8080），之后会记住；
/// 退出登录后地址仍保留，改一下输入框即可连到别的服务器。
class LoginPage extends StatefulWidget {
  final Session session;
  const LoginPage({super.key, required this.session});

  @override
  State<LoginPage> createState() => _LoginPageState();
}

class _LoginPageState extends State<LoginPage> {
  final _formKey = GlobalKey<FormState>();
  late final TextEditingController _urlCtrl;
  final _userCtrl = TextEditingController(text: 'admin');
  final _pwdCtrl = TextEditingController();
  bool _loading = false;
  bool _probing = false;
  bool _obscure = true;
  String? _error;
  String? _probeOk;

  @override
  void initState() {
    super.initState();
    // 预填上次用的地址
    _urlCtrl = TextEditingController(text: widget.session.baseUrl);
    _userCtrl.text = widget.session.username.isEmpty ? 'admin' : widget.session.username;
  }

  @override
  void dispose() {
    _urlCtrl.dispose();
    _userCtrl.dispose();
    _pwdCtrl.dispose();
    super.dispose();
  }

  /// 地址格式校验：非空、能解析出 host
  String? _validateUrl(String? v) {
    final s = (v ?? '').trim();
    if (s.isEmpty) return '请输入服务器地址';
    final uri = Uri.tryParse(Session.normalizeUrl(s));
    if (uri == null || uri.host.isEmpty) {
      return '地址格式不正确，示例：192.168.2.110:8080';
    }
    return null;
  }

  /// 测试连接：不需要账号密码
  Future<void> _testConnection() async {
    final err = _validateUrl(_urlCtrl.text);
    if (err != null) {
      setState(() {
        _error = err;
        _probeOk = null;
      });
      return;
    }
    FocusScope.of(context).unfocus();
    setState(() {
      _probing = true;
      _error = null;
      _probeOk = null;
    });
    final url = Session.normalizeUrl(_urlCtrl.text);
    try {
      await PmApi(baseUrl: url).probe();
      if (!mounted) return;
      setState(() => _probeOk = '服务器可达：$url');
    } on ApiException catch (e) {
      if (!mounted) return;
      setState(() => _error = e.message);
    } catch (e) {
      if (!mounted) return;
      setState(() => _error = '连接失败：$e');
    } finally {
      if (mounted) setState(() => _probing = false);
    }
  }

  Future<void> _login() async {
    if (!_formKey.currentState!.validate()) return;
    FocusScope.of(context).unfocus();
    setState(() {
      _loading = true;
      _error = null;
      _probeOk = null;
    });

    final url = Session.normalizeUrl(_urlCtrl.text);
    try {
      final api = PmApi(baseUrl: url);
      final result = await api.login(_userCtrl.text.trim(), _pwdCtrl.text);
      await widget.session.saveLogin(url, result);
      if (!mounted) return;
      Navigator.of(context).pushReplacement(
        MaterialPageRoute(builder: (_) => HomePage(session: widget.session)),
      );
    } on ApiException catch (e) {
      setState(() => _error = e.message);
    } catch (e) {
      setState(() => _error = '登录失败：$e');
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final cs = Theme.of(context).colorScheme;
    final busy = _loading || _probing;
    return Scaffold(
      body: SafeArea(
        child: Center(
          child: SingleChildScrollView(
            padding: const EdgeInsets.all(24),
            child: ConstrainedBox(
              constraints: const BoxConstraints(maxWidth: 420),
              child: Form(
                key: _formKey,
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    Icon(Icons.terminal, size: 64, color: cs.primary),
                    const SizedBox(height: 12),
                    Text(
                      'PM',
                      textAlign: TextAlign.center,
                      style: Theme.of(context).textTheme.headlineMedium?.copyWith(
                            fontWeight: FontWeight.bold,
                          ),
                    ),
                    Text(
                      '项目管理平台 · 移动端',
                      textAlign: TextAlign.center,
                      style: TextStyle(color: cs.onSurfaceVariant),
                    ),
                    const SizedBox(height: 32),

                    // ===== 服务器地址 =====
                    TextFormField(
                      controller: _urlCtrl,
                      keyboardType: TextInputType.url,
                      autocorrect: false,
                      enableSuggestions: false,
                      textInputAction: TextInputAction.next,
                      onChanged: (_) {
                        if (_error != null || _probeOk != null) {
                          setState(() {
                            _error = null;
                            _probeOk = null;
                          });
                        }
                      },
                      decoration: InputDecoration(
                        labelText: '服务器地址',
                        hintText: '192.168.2.110:8080',
                        prefixIcon: const Icon(Icons.dns_outlined),
                        helperText: '不填协议默认 http，可带端口',
                        suffixIcon: Row(
                          mainAxisSize: MainAxisSize.min,
                          children: [
                            if (_urlCtrl.text.isNotEmpty)
                              IconButton(
                                tooltip: '清空',
                                icon: const Icon(Icons.clear, size: 20),
                                onPressed: busy
                                    ? null
                                    : () => setState(() {
                                          _urlCtrl.clear();
                                          _error = null;
                                          _probeOk = null;
                                        }),
                              ),
                            IconButton(
                              tooltip: '测试连接',
                              icon: _probing
                                  ? const SizedBox(
                                      width: 18,
                                      height: 18,
                                      child: CircularProgressIndicator(
                                          strokeWidth: 2),
                                    )
                                  : const Icon(Icons.wifi_tethering, size: 20),
                              onPressed: busy ? null : _testConnection,
                            ),
                          ],
                        ),
                      ),
                      validator: _validateUrl,
                    ),

                    // 常用地址快捷填入
                    const SizedBox(height: 8),
                    Wrap(
                      spacing: 8,
                      runSpacing: 4,
                      children: _kQuickHosts
                          .map(
                            (h) => ActionChip(
                              avatar: const Icon(Icons.bolt_outlined, size: 16),
                              label: Text(h, style: const TextStyle(fontSize: 12)),
                              onPressed: busy
                                  ? null
                                  : () => setState(() {
                                        _urlCtrl.text = h;
                                        _error = null;
                                        _probeOk = null;
                                      }),
                            ),
                          )
                          .toList(),
                    ),
                    const SizedBox(height: 16),

                    // ===== 账号 =====
                    TextFormField(
                      controller: _userCtrl,
                      keyboardType: TextInputType.text,
                      autocorrect: false,
                      textInputAction: TextInputAction.next,
                      decoration: const InputDecoration(
                        labelText: '账号',
                        prefixIcon: Icon(Icons.person_outline),
                      ),
                      validator: (v) =>
                          (v == null || v.trim().isEmpty) ? '请输入账号' : null,
                    ),
                    const SizedBox(height: 16),

                    // ===== 密码 =====
                    TextFormField(
                      controller: _pwdCtrl,
                      obscureText: _obscure,
                      decoration: InputDecoration(
                        labelText: '密码',
                        prefixIcon: const Icon(Icons.lock_outline),
                        suffixIcon: IconButton(
                          icon: Icon(_obscure
                              ? Icons.visibility_off_outlined
                              : Icons.visibility_outlined),
                          onPressed: () => setState(() => _obscure = !_obscure),
                        ),
                      ),
                      validator: (v) =>
                          (v == null || v.isEmpty) ? '请输入密码' : null,
                      onFieldSubmitted: (_) => busy ? null : _login(),
                    ),

                    // ===== 测试成功提示 =====
                    if (_probeOk != null) ...[
                      const SizedBox(height: 16),
                      Container(
                        padding: const EdgeInsets.all(12),
                        decoration: BoxDecoration(
                          color: cs.primaryContainer,
                          borderRadius: BorderRadius.circular(8),
                        ),
                        child: Row(
                          children: [
                            Icon(Icons.check_circle_outline,
                                size: 20, color: cs.onPrimaryContainer),
                            const SizedBox(width: 8),
                            Expanded(
                              child: Text(
                                _probeOk!,
                                style: TextStyle(color: cs.onPrimaryContainer),
                              ),
                            ),
                          ],
                        ),
                      ),
                    ],

                    // ===== 错误提示 =====
                    if (_error != null) ...[
                      const SizedBox(height: 16),
                      Container(
                        padding: const EdgeInsets.all(12),
                        decoration: BoxDecoration(
                          color: cs.errorContainer,
                          borderRadius: BorderRadius.circular(8),
                        ),
                        child: Row(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Icon(Icons.error_outline,
                                size: 20, color: cs.onErrorContainer),
                            const SizedBox(width: 8),
                            Expanded(
                              child: Text(
                                _error!,
                                style: TextStyle(color: cs.onErrorContainer),
                              ),
                            ),
                          ],
                        ),
                      ),
                    ],

                    const SizedBox(height: 24),
                    FilledButton(
                      onPressed: busy ? null : _login,
                      style: FilledButton.styleFrom(
                        padding: const EdgeInsets.symmetric(vertical: 14),
                      ),
                      child: _loading
                          ? const SizedBox(
                              width: 20,
                              height: 20,
                              child: CircularProgressIndicator(strokeWidth: 2),
                            )
                          : const Text('登 录'),
                    ),
                  ],
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}

/// 常用服务器地址（内网为主），点一下即可填入输入框
const _kQuickHosts = <String>[
  '192.168.2.110:8080',
  '192.168.10.199:8080',
  'pm.saibo.club',
];

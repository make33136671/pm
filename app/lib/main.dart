import 'package:flutter/material.dart';

import 'pages/home_page.dart';
import 'pages/login_page.dart';
import 'session.dart';
import 'theme/app_theme.dart';

void main() {
  WidgetsFlutterBinding.ensureInitialized();
  runApp(const PmApp());
}

class PmApp extends StatefulWidget {
  const PmApp({super.key});

  @override
  State<PmApp> createState() => _PmAppState();
}

class _PmAppState extends State<PmApp> {
  final _session = Session();
  bool _booting = true;

  @override
  void initState() {
    super.initState();
    _boot();
  }

  /// 启动流程：恢复上次登录态 → 校验 token 是否仍有效 → 决定进哪个页
  Future<void> _boot() async {
    await _session.restore();
    if (_session.loggedIn) {
      final ok = await _session.api.verifyToken();
      if (!ok) await _session.clear();
    }
    if (mounted) setState(() => _booting = false);
  }

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      title: 'PM',
      debugShowCheckedModeBanner: false,
      theme: PmTheme.light(),
      darkTheme: PmTheme.dark(),
      themeMode: ThemeMode.system,
      home: _booting
          ? const Scaffold(body: Center(child: CircularProgressIndicator()))
          : _session.loggedIn
              ? HomePage(session: _session)
              : LoginPage(session: _session),
    );
  }
}

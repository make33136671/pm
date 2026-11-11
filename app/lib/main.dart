import 'package:flutter/material.dart';

import 'pages/home_page.dart';
import 'pages/login_page.dart';
import 'session.dart';
import 'theme/app_theme.dart';
import 'theme/theme_controller.dart';

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
    // 主题变化时重建 MaterialApp
    themeController.addListener(_onThemeChanged);
  }

  @override
  void dispose() {
    themeController.removeListener(_onThemeChanged);
    super.dispose();
  }

  void _onThemeChanged() {
    if (mounted) setState(() {});
  }

  /// 启动流程：恢复主题 → 恢复上次登录态 → 校验 token → 决定进哪个页
  Future<void> _boot() async {
    await themeController.restore();
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
      // 默认暗黑；用户可在个人中心切换
      themeMode: themeController.mode,
      home: _booting
          ? const Scaffold(body: Center(child: CircularProgressIndicator()))
          : _session.loggedIn
              ? HomePage(session: _session)
              : LoginPage(session: _session),
    );
  }
}

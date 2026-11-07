import 'package:flutter/foundation.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'api/pm_api.dart';
import 'models/auth.dart';

/// 全局会话：保存服务器地址与 token，供各页面共享。
///
/// 用 ChangeNotifier 而非 Provider/Riverpod，是为了让 App 保持零额外依赖，
/// 后续要接状态管理也容易迁移。
class Session extends ChangeNotifier {
  static const _kBaseUrl = 'pm.baseUrl';
  static const _kToken = 'pm.token';
  static const _kUsername = 'pm.username';
  static const _kName = 'pm.name';

  String baseUrl = '';
  String? token;
  String username = '';
  String displayName = '';

  PmApi? _api;
  PmApi get api {
    final a = _api ??= PmApi(baseUrl: baseUrl, token: token);
    a.baseUrl = baseUrl;
    a.token = token;
    return a;
  }

  bool get loggedIn => (token ?? '').isNotEmpty && baseUrl.isNotEmpty;

  /// 启动时恢复上次的登录状态
  Future<void> restore() async {
    final sp = await SharedPreferences.getInstance();
    baseUrl = sp.getString(_kBaseUrl) ?? '';
    token = sp.getString(_kToken);
    username = sp.getString(_kUsername) ?? '';
    displayName = sp.getString(_kName) ?? '';
    notifyListeners();
  }

  Future<void> saveLogin(String url, LoginResult r) async {
    baseUrl = url;
    token = r.accessToken;
    username = r.userInfo.username;
    displayName = r.userInfo.name;
    final sp = await SharedPreferences.getInstance();
    await sp.setString(_kBaseUrl, baseUrl);
    await sp.setString(_kToken, token!);
    await sp.setString(_kUsername, username);
    await sp.setString(_kName, displayName);
    notifyListeners();
  }

  Future<void> clear() async {
    token = null;
    username = '';
    displayName = '';
    final sp = await SharedPreferences.getInstance();
    await sp.remove(_kToken);
    await sp.remove(_kUsername);
    await sp.remove(_kName);
    notifyListeners();
  }

  /// 规范化用户输入的服务器地址：
  ///   192.168.10.199        → http://192.168.10.199
  ///   192.168.10.199:8080   → http://192.168.10.199:8080
  ///   http://x.com/         → http://x.com   （去尾部斜杠）
  static String normalizeUrl(String input) {
    var s = input.trim();
    if (s.isEmpty) return s;
    if (!s.startsWith('http://') && !s.startsWith('https://')) {
      s = 'http://$s';
    }
    while (s.endsWith('/')) {
      s = s.substring(0, s.length - 1);
    }
    return s;
  }
}

import 'package:characters/characters.dart';
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
  static const _kRoleName = 'pm.roleName';
  static const _kAvatar = 'pm.avatar';

  String baseUrl = '';
  String? token;
  String username = '';
  String displayName = '';
  String roleName = '';
  String avatar = '';

  PmApi? _api;
  PmApi get api {
    final a = _api ??= PmApi(baseUrl: baseUrl, token: token);
    a.baseUrl = baseUrl;
    a.token = token;
    return a;
  }

  bool get loggedIn => (token ?? '').isNotEmpty && baseUrl.isNotEmpty;

  /// 头像完整可访问 URL。
  ///
  /// 后端返回的 avatar 可能是绝对 URL，也可能是 `/storage/images/xxx` 这类
  /// 相对路径，需要按当前服务器地址补全。
  String get avatarUrl {
    if (avatar.isEmpty) return '';
    if (avatar.startsWith('http://') || avatar.startsWith('https://')) {
      return avatar;
    }
    final base = baseUrl.endsWith('/')
        ? baseUrl.substring(0, baseUrl.length - 1)
        : baseUrl;
    return avatar.startsWith('/') ? '$base$avatar' : '$base/$avatar';
  }

  /// 头像为空时用的占位文字（取名字首字）
  String get avatarInitial {
    final s = displayName.isNotEmpty ? displayName : username;
    return s.isEmpty ? '?' : s.characters.first;
  }

  /// 启动时恢复上次的登录状态
  Future<void> restore() async {
    final sp = await SharedPreferences.getInstance();
    baseUrl = sp.getString(_kBaseUrl) ?? '';
    token = sp.getString(_kToken);
    username = sp.getString(_kUsername) ?? '';
    displayName = sp.getString(_kName) ?? '';
    roleName = sp.getString(_kRoleName) ?? '';
    avatar = sp.getString(_kAvatar) ?? '';
    notifyListeners();
  }

  Future<void> saveLogin(String url, LoginResult r) async {
    baseUrl = url;
    token = r.accessToken;
    username = r.userInfo.username;
    displayName = r.userInfo.name;
    roleName = r.userInfo.roleName;
    avatar = r.userInfo.avatar ?? '';
    final sp = await SharedPreferences.getInstance();
    await sp.setString(_kBaseUrl, baseUrl);
    await sp.setString(_kToken, token!);
    await sp.setString(_kUsername, username);
    await sp.setString(_kName, displayName);
    await sp.setString(_kRoleName, roleName);
    await sp.setString(_kAvatar, avatar);
    notifyListeners();
  }

  /// 用后端返回的最新用户信息刷新本地缓存（改头像/改昵称后调用）
  Future<void> updateUserInfo(UserInfo info) async {
    displayName = info.name;
    roleName = info.roleName;
    avatar = info.avatar ?? '';
    final sp = await SharedPreferences.getInstance();
    await sp.setString(_kName, displayName);
    await sp.setString(_kRoleName, roleName);
    await sp.setString(_kAvatar, avatar);
    notifyListeners();
  }

  /// 退出登录。
  ///
  /// 保留 [baseUrl]，下次登录页会自动带出上次的服务器地址；
  /// 若要连别的服务器，在登录页直接改输入框即可。
  Future<void> clear() async {
    token = null;
    username = '';
    displayName = '';
    roleName = '';
    avatar = '';
    _api = null;
    final sp = await SharedPreferences.getInstance();
    await sp.remove(_kToken);
    await sp.remove(_kUsername);
    await sp.remove(_kName);
    await sp.remove(_kRoleName);
    await sp.remove(_kAvatar);
    notifyListeners();
  }

  /// 规范化用户输入的服务器地址：
  ///   192.168.10.199        → http://192.168.10.199
  ///   192.168.10.199:8088   → http://192.168.10.199:8088
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

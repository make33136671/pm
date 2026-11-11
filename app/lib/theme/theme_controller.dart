import 'package:flutter/material.dart';
import 'package:shared_preferences/shared_preferences.dart';

/// 全局主题设置：暗黑 / 明亮 / 跟随系统。
///
/// **默认暗黑模式**（服务端管理工具夜用居多，且终端本就是深色）。
/// 用户可在「个人中心 → 外观」切换，选择持久化到 SharedPreferences。
class ThemeController extends ChangeNotifier {
  static const _kMode = 'pm.themeMode';

  /// 默认：暗黑
  ThemeMode _mode = ThemeMode.dark;
  ThemeMode get mode => _mode;

  /// 启动时恢复上次选择（若从未选过则保持默认暗黑）
  Future<void> restore() async {
    final sp = await SharedPreferences.getInstance();
    final v = sp.getString(_kMode);
    switch (v) {
      case 'light':
        _mode = ThemeMode.light;
      case 'system':
        _mode = ThemeMode.system;
      case 'dark':
      default:
        _mode = ThemeMode.dark;
    }
    notifyListeners();
  }

  Future<void> setMode(ThemeMode m) async {
    if (_mode == m) return;
    _mode = m;
    notifyListeners();
    final sp = await SharedPreferences.getInstance();
    await sp.setString(
      _kMode,
      switch (m) {
        ThemeMode.light => 'light',
        ThemeMode.system => 'system',
        ThemeMode.dark => 'dark',
      },
    );
  }

  /// 当前模式的中文名
  String get modeLabel => switch (_mode) {
        ThemeMode.light => '明亮',
        ThemeMode.system => '跟随系统',
        ThemeMode.dark => '暗黑',
      };
}

/// 全局单例 —— 交给 MaterialApp 用，避免额外引入状态管理库
final themeController = ThemeController();

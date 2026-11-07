/// 统一响应包装：后端所有接口返回 { code, message, data }
class ApiResult<T> {
  final int code;
  final String message;
  final T? data;

  ApiResult({required this.code, required this.message, this.data});

  bool get ok => code == 0;

  factory ApiResult.fromJson(
    Map<String, dynamic> json,
    T Function(dynamic)? parser,
  ) {
    final raw = json['data'];
    return ApiResult(
      code: json['code'] as int? ?? -1,
      message: json['message'] as String? ?? '',
      data: parser != null && raw != null ? parser(raw) : raw as T?,
    );
  }
}

/// 登录返回的用户信息
class UserInfo {
  final int userId;
  final String username;
  final String name;
  final String roleName;
  final String? avatar;

  UserInfo({
    required this.userId,
    required this.username,
    required this.name,
    required this.roleName,
    this.avatar,
  });

  factory UserInfo.fromJson(Map<String, dynamic> j) => UserInfo(
        userId: j['userId'] as int? ?? 0,
        username: j['username'] as String? ?? '',
        name: (j['name'] ?? j['nickname'] ?? '') as String,
        roleName: j['roleName'] as String? ?? '',
        avatar: j['avatar'] as String?,
      );
}

/// 登录结果
class LoginResult {
  final String accessToken;
  final UserInfo userInfo;

  LoginResult({required this.accessToken, required this.userInfo});

  factory LoginResult.fromJson(Map<String, dynamic> j) => LoginResult(
        accessToken: j['accessToken'] as String? ?? '',
        userInfo: UserInfo.fromJson(
          (j['userInfo'] as Map?)?.cast<String, dynamic>() ?? {},
        ),
      );
}

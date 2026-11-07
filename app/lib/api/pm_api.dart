import 'dart:io';

import 'package:dio/dio.dart';

import '../models/auth.dart';
import '../models/webterm.dart';

/// API 异常（携带后端 message，便于界面直接展示）
class ApiException implements Exception {
  final String message;
  final int? code;
  ApiException(this.message, [this.code]);
  @override
  String toString() => message;
}

/// PM 后端 API 客户端。
///
/// 后端约定（见 sb-framework）：
///   · 所有接口返回 { code, message, data }，code == 0 为成功
///   · 参数走 form-urlencoded（不是 JSON），对应后端 [FromForm]
///   · 鉴权用 JwtAdmin scheme：Authorization: Bearer <token>
///   · 管理端接口都在 /admin/* 前缀下
class PmApi {
  /// 服务地址，形如 http://192.168.10.199:8080（不带尾部斜杠）
  String baseUrl;

  String? _token;

  final Dio _dio;

  PmApi({required this.baseUrl, String? token})
      : _token = token,
        _dio = Dio(
          BaseOptions(
            connectTimeout: const Duration(seconds: 15),
            receiveTimeout: const Duration(seconds: 60),
            sendTimeout: const Duration(seconds: 60),
            // 不自动抛异常，统一由 _request 处理
            validateStatus: (_) => true,
          ),
        );

  String? get token => _token;
  set token(String? v) => _token = v;
  bool get loggedIn => _token != null && _token!.isNotEmpty;

  Options _opts({bool form = true}) => Options(
        headers: {
          if (_token != null) 'Authorization': 'Bearer $_token',
          'Content-Type': form
              ? 'application/x-www-form-urlencoded'
              : 'application/json',
        },
        responseType: ResponseType.json,
      );

  /// 统一请求 + 结果解包
  Future<T> _request<T>(
    String method,
    String path, {
    Map<String, dynamic>? params,
    Map<String, dynamic>? form,
    T Function(dynamic)? parser,
  }) async {
    final url = '$baseUrl$path';
    late Response<dynamic> resp;
    try {
      if (method == 'GET') {
        resp = await _dio.get(url, queryParameters: params, options: _opts());
      } else {
        resp = await _dio.post(
          url,
          // 直接传 Map，配合 Content-Type: x-www-form-urlencoded
          // （dio 会按该 Content-Type 自动序列化成 a=1&b=2，与后端 [FromForm] 对应）
          data: form,
          queryParameters: params,
          options: _opts(),
        );
      }
    } on DioException catch (e) {
      throw ApiException(_friendlyNetworkError(e));
    }

    // 后端未安装时返回 503 + setup_required
    if (resp.statusCode == 503) {
      throw ApiException('服务尚未安装或正在初始化');
    }
    if (resp.statusCode != 200) {
      throw ApiException('HTTP ${resp.statusCode}');
    }

    final body = resp.data;
    if (body is! Map) {
      throw ApiException('响应格式异常');
    }
    final json = body.cast<String, dynamic>();
    final code = json['code'] as int? ?? -1;
    if (code != 0) {
      throw ApiException(json['message'] as String? ?? '操作失败', code);
    }
    final data = json['data'];
    return parser != null ? parser(data) : data as T;
  }

  String _friendlyNetworkError(DioException e) {
    switch (e.type) {
      case DioExceptionType.connectionTimeout:
        return '连接超时，请检查服务器地址与网络';
      case DioExceptionType.receiveTimeout:
        return '响应超时，服务器可能繁忙';
      case DioExceptionType.connectionError:
        final msg = e.message ?? '';
        if (msg.contains('SocketException') || e.error is SocketException) {
          return '无法连接服务器，请检查地址与端口';
        }
        return '网络连接失败';
      default:
        return e.message ?? '网络请求失败';
    }
  }

  // ==================== 认证 ====================

  /// 登录。成功后会保存 token，后续请求自动带上。
  Future<LoginResult> login(String username, String password) async {
    final result = await _request<LoginResult>(
      'POST',
      '/admin/Auth/Login',
      form: {'username': username, 'password': password},
      parser: (d) => LoginResult.fromJson((d as Map).cast<String, dynamic>()),
    );
    _token = result.accessToken;
    return result;
  }

  Future<void> logout() async {
    try {
      await _request('POST', '/admin/Auth/Logout');
    } catch (_) {
      // 忽略登出失败（token 可能已过期）
    }
    _token = null;
  }

  /// 校验 token 是否仍有效
  Future<bool> verifyToken() async {
    if (!loggedIn) return false;
    try {
      await _request<dynamic>('GET', '/admin/App/Info');
      return true;
    } catch (_) {
      return false;
    }
  }

  // ==================== 终端会话 ====================

  /// 会话列表
  Future<List<CliSession>> listSessions({int page = 1, int pageSize = 50}) {
    return _request<List<CliSession>>(
      'GET',
      '/admin/CliSession/List',
      params: {'page': page, 'pageSize': pageSize},
      parser: (d) {
        final m = (d as Map).cast<String, dynamic>();
        return ((m['items'] as List?) ?? [])
            .map((e) => CliSession.fromJson((e as Map).cast<String, dynamic>()))
            .toList();
      },
    );
  }

  /// 新建会话
  Future<int> createSession({
    String? name,
    int cols = 120,
    int rows = 30,
    String? cwd,
    int? projectId,
  }) {
    return _request<int>(
      'POST',
      '/admin/CliSession/Create',
      form: {
        if (name != null) 'name': name,
        'cols': cols,
        'rows': rows,
        if (cwd != null) 'cwd': cwd,
        if (projectId != null) 'projectId': projectId,
      },
      parser: (d) {
        if (d is int) return d;
        if (d is Map) return (d['id'] as int?) ?? 0;
        return 0;
      },
    );
  }

  Future<void> destroySession(int id) =>
      _request('POST', '/admin/CliSession/Destroy', form: {'id': id});

  Future<void> deleteSession(int id) =>
      _request('POST', '/admin/CliSession/Del', form: {'id': id});

  /// WebSocket 终端地址（token 走 query，因为 WS 不便自定义 header）
  String webtermSocketUrl(int sessionId) {
    final ws = baseUrl.replaceFirst(RegExp(r'^http'), 'ws');
    final t = Uri.encodeComponent(_token ?? '');
    return '$ws/webterm/ws/$sessionId?token=$t';
  }

  // ==================== 文件管理 ====================

  /// 默认打开路径（来自系统设置）
  Future<DefaultPath> defaultPath() => _request<DefaultPath>(
        'GET',
        '/admin/FileManager/DefaultPath',
        parser: (d) => DefaultPath.fromJson((d as Map).cast<String, dynamic>()),
      );

  /// 列目录
  Future<DirectoryListing> listDir(String path, {bool showHidden = false}) {
    return _request<DirectoryListing>(
      'GET',
      '/admin/FileManager/List',
      params: {'path': path, 'showHidden': showHidden},
      parser: (d) =>
          DirectoryListing.fromJson((d as Map).cast<String, dynamic>()),
    );
  }

  /// 读文件（编辑用）
  Future<FileContent> readFile(String path) => _request<FileContent>(
        'GET',
        '/admin/FileManager/Read',
        params: {'path': path},
        parser: (d) => FileContent.fromJson((d as Map).cast<String, dynamic>()),
      );

  /// 保存文件。expectedHash 用于并发保护（传读取时拿到的 hash）
  Future<void> saveFile(
    String path,
    String content, {
    String? expectedHash,
    bool createBackup = true,
  }) {
    return _request('POST', '/admin/FileManager/Save', form: {
      'path': path,
      'content': content,
      if (expectedHash != null) 'expectedHash': expectedHash,
      'createBackup': createBackup.toString(),
    });
  }

  Future<void> createEntry(String path, {bool isDirectory = false}) {
    return _request('POST', '/admin/FileManager/Create', form: {
      'path': path,
      'isDirectory': isDirectory.toString(),
    });
  }

  Future<void> renameEntry(String path, String newName) {
    return _request('POST', '/admin/FileManager/Rename',
        form: {'path': path, 'newName': newName});
  }

  /// 删除（默认进回收站）
  Future<String> deleteEntry(String path, {bool recursive = false}) {
    return _request<String>(
      'POST',
      '/admin/FileManager/Delete',
      form: {'path': path, 'recursive': recursive.toString()},
      parser: (d) => (d is Map ? d['message'] : null) as String? ?? '删除成功',
    );
  }

  Future<void> chmod(String path, String mode) {
    return _request('POST', '/admin/FileManager/Chmod',
        form: {'path': path, 'mode': mode});
  }

  /// 上传文件到目录
  Future<void> uploadFile(String dirPath, String filePath, String fileName) async {
    final form = FormData.fromMap({
      'dirPath': dirPath,
      'overwrite': 'true',
      'file': await MultipartFile.fromFile(filePath, filename: fileName),
    });
    late Response<dynamic> resp;
    try {
      resp = await _dio.post(
        '$baseUrl/admin/FileManager/Upload',
        data: form,
        options: Options(
          headers: {
            if (_token != null) 'Authorization': 'Bearer $_token',
          },
          responseType: ResponseType.json,
        ),
      );
    } on DioException catch (e) {
      throw ApiException(_friendlyNetworkError(e));
    }
    final body = resp.data;
    if (body is Map && (body['code'] as int? ?? -1) != 0) {
      throw ApiException(body['message'] as String? ?? '上传失败');
    }
  }

  /// 下载文件到本地临时路径，返回保存后的文件
  Future<File> downloadFile(String path, String savePath) async {
    try {
      final resp = await _dio.get(
        '$baseUrl/admin/FileManager/Download',
        queryParameters: {'path': path},
        options: Options(
          headers: {if (_token != null) 'Authorization': 'Bearer $_token'},
          responseType: ResponseType.bytes,
          followRedirects: true,
        ),
      );
      if (resp.statusCode != 200) {
        throw ApiException('下载失败 HTTP ${resp.statusCode}');
      }
      final f = File(savePath);
      await f.writeAsBytes(resp.data as List<int>);
      return f;
    } on DioException catch (e) {
      throw ApiException(_friendlyNetworkError(e));
    }
  }

  /// 回收站列表
  Future<Map<String, dynamic>> recycleList() => _request<Map<String, dynamic>>(
        'GET',
        '/admin/FileManager/RecycleList',
        parser: (d) => (d as Map).cast<String, dynamic>(),
      );

  /// 系统用户（chown 用，暂未在 UI 暴露，保留）
  Future<List<dynamic>> systemUsers() =>
      _request<List<dynamic>>('GET', '/admin/FileManager/SystemUsers');
}

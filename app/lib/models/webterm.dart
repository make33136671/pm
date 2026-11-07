/// 终端会话
class CliSession {
  final int id;
  final String name;
  final String status;
  final String? cwd;
  final String? shellPath;
  final int? projectId;
  final String? ownerUsername;
  final DateTime? startedAt;
  final DateTime? lastActiveAt;

  CliSession({
    required this.id,
    required this.name,
    required this.status,
    this.cwd,
    this.shellPath,
    this.projectId,
    this.ownerUsername,
    this.startedAt,
    this.lastActiveAt,
  });

  bool get running => status == 'Running';

  factory CliSession.fromJson(Map<String, dynamic> j) {
    DateTime? parse(dynamic v) =>
        v is String ? DateTime.tryParse(v) : null;
    return CliSession(
      id: j['id'] as int? ?? 0,
      name: j['name'] as String? ?? '',
      status: j['status'] as String? ?? '',
      cwd: j['cwd'] as String?,
      shellPath: j['shellPath'] as String?,
      projectId: j['projectId'] as int?,
      ownerUsername: j['ownerUsername'] as String?,
      startedAt: parse(j['startedAt']),
      lastActiveAt: parse(j['lastActiveAt']),
    );
  }
}

/// 文件/目录条目
class FileEntry {
  final String name;
  final String path;
  final bool isDirectory;
  final bool isSymlink;
  final int size;
  final String mode;
  final String owner;
  final String group;
  final String lastModified;
  final bool editable;
  final String extension;

  FileEntry({
    required this.name,
    required this.path,
    required this.isDirectory,
    required this.isSymlink,
    required this.size,
    required this.mode,
    required this.owner,
    required this.group,
    required this.lastModified,
    required this.editable,
    required this.extension,
  });

  factory FileEntry.fromJson(Map<String, dynamic> j) => FileEntry(
        name: j['name'] as String? ?? '',
        path: j['path'] as String? ?? '',
        isDirectory: j['isDirectory'] as bool? ?? false,
        isSymlink: j['isSymlink'] as bool? ?? false,
        size: j['size'] as int? ?? 0,
        mode: j['mode'] as String? ?? '-',
        owner: j['owner'] as String? ?? '-',
        group: j['group'] as String? ?? '-',
        lastModified: j['lastModified'] as String? ?? '',
        editable: j['editable'] as bool? ?? false,
        extension: j['extension'] as String? ?? '',
      );

  String get sizeText {
    if (isDirectory) return '-';
    if (size < 1024) return '$size B';
    if (size < 1024 * 1024) return '${(size / 1024).toStringAsFixed(1)} KB';
    if (size < 1024 * 1024 * 1024) {
      return '${(size / 1024 / 1024).toStringAsFixed(1)} MB';
    }
    return '${(size / 1024 / 1024 / 1024).toStringAsFixed(2)} GB';
  }
}

/// 目录列表
class DirectoryListing {
  final String path;
  final String parentPath;
  final List<Breadcrumb> breadcrumbs;
  final List<FileEntry> directories;
  final List<FileEntry> files;
  final bool truncated;

  DirectoryListing({
    required this.path,
    required this.parentPath,
    required this.breadcrumbs,
    required this.directories,
    required this.files,
    required this.truncated,
  });

  factory DirectoryListing.fromJson(Map<String, dynamic> j) {
    List<T> list<T>(dynamic v, T Function(Map<String, dynamic>) f) =>
        (v as List? ?? [])
            .map((e) => f((e as Map).cast<String, dynamic>()))
            .toList();
    return DirectoryListing(
      path: j['path'] as String? ?? '/',
      parentPath: j['parentPath'] as String? ?? '',
      breadcrumbs: list(j['breadcrumbs'], Breadcrumb.fromJson),
      directories: list(j['directories'], FileEntry.fromJson),
      files: list(j['files'], FileEntry.fromJson),
      truncated: j['truncated'] as bool? ?? false,
    );
  }

  /// 目录在前、文件在后
  List<FileEntry> get all => [...directories, ...files];
}

class Breadcrumb {
  final String name;
  final String path;
  Breadcrumb({required this.name, required this.path});
  factory Breadcrumb.fromJson(Map<String, dynamic> j) => Breadcrumb(
        name: j['name'] as String? ?? '',
        path: j['path'] as String? ?? '',
      );
}

/// 文件内容（编辑用）
class FileContent {
  final String path;
  final String name;
  final String content;
  final int size;
  final bool isBinary;
  final bool tooLarge;
  final bool editable;
  final String lineEnding;
  final int lineCount;
  final String mode;
  final String hash;

  FileContent({
    required this.path,
    required this.name,
    required this.content,
    required this.size,
    required this.isBinary,
    required this.tooLarge,
    required this.editable,
    required this.lineEnding,
    required this.lineCount,
    required this.mode,
    required this.hash,
  });

  factory FileContent.fromJson(Map<String, dynamic> j) => FileContent(
        path: j['path'] as String? ?? '',
        name: j['name'] as String? ?? '',
        content: j['content'] as String? ?? '',
        size: j['size'] as int? ?? 0,
        isBinary: j['isBinary'] as bool? ?? false,
        tooLarge: j['tooLarge'] as bool? ?? false,
        editable: j['editable'] as bool? ?? false,
        lineEnding: j['lineEnding'] as String? ?? 'LF',
        lineCount: j['lineCount'] as int? ?? 0,
        mode: j['mode'] as String? ?? '-',
        hash: j['hash'] as String? ?? '',
      );
}

/// 默认打开路径（系统设置）
class DefaultPath {
  final String path;
  final bool fallback;
  final String message;

  DefaultPath({
    required this.path,
    required this.fallback,
    required this.message,
  });

  factory DefaultPath.fromJson(Map<String, dynamic> j) => DefaultPath(
        path: j['path'] as String? ?? '/',
        fallback: j['fallback'] as bool? ?? false,
        message: j['message'] as String? ?? '',
      );
}

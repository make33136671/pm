using FreeSql;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sb.Apps.Pm.Models.FileManager;
using SB.Framework.Core.Attribute;
using SB.Framework.Core.Util;

namespace Sb.Apps.Pm.Services.FileManager
{
    /// <summary>
    /// 回收站：删除进回收站、查看、恢复、永久删除、清空、自动清理。
    ///
    /// 设计要点：
    ///   1. **同盘 move**：回收站目录放 ContentRootPath 下，与业务数据同盘，
    ///      `Directory.Move` / `File.Move` 是原子的 O(1) 操作，不是复制。
    ///   2. **数据库记账**：原始路径可能超长且要记录删除人/时间，所以落表
    ///      `sb_pm_file_recycle`，回收站内的存储名用 `<Id>_<原文件名>`。
    ///   3. **配额与过期**：超配额或超保留期时，从最旧的开始清。
    ///   4. 恢复时若原位置已被占用，自动加时间戳后缀，不覆盖已有文件。
    /// </summary>
    [AutoDependencyInjection]
    public class RecycleBinService
    {
        private readonly IFreeSql _fsql;
        private readonly FileManagerOptions _options;
        private readonly PathGuard _guard;
        private readonly IHostEnvironment _env;
        private readonly ILogger<RecycleBinService> _logger;

        public RecycleBinService(IFreeSql fsql, FileManagerOptions options, PathGuard guard,
            IHostEnvironment env, ILogger<RecycleBinService> logger)
        {
            _fsql = fsql;
            _options = options;
            _guard = guard;
            _env = env;
            _logger = logger;
        }

        /// <summary>回收站根目录的绝对路径。</summary>
        public string RootDir => Path.IsPathRooted(_options.RecycleBinPath)
            ? _options.RecycleBinPath
            : Path.Combine(_env.ContentRootPath, _options.RecycleBinPath);

        public bool Enabled => _options.RecycleBinEnabled;

        /// <summary>
        /// 把文件/目录移入回收站。调用方负责已完成路径校验。
        /// </summary>
        /// <returns>回收站记录；若回收站关闭返回 null（调用方应改为直接删除）。</returns>
        public FileRecycleItem? MoveToRecycleBin(string fullPath, string actor, bool isDirectory, long size)
        {
            Directory.CreateDirectory(RootDir);

            var name = Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar));
            var item = new FileRecycleItem
            {
                OriginalPath = fullPath,
                Name = name,
                IsDirectory = isDirectory,
                Size = size,
                DeletedAt = AppTime.UtcNow,
                DeletedBy = actor,
                StorageName = string.Empty,
            };

            // 注意：ExecuteAffrows() 不回填自增主键，必须用 ExecuteIdentity()。
            // 之前用 ExecuteAffrows 导致 Id 恒为 0，存储名变成 '0_xxx'，
            // 列表返回的 id 为空、恢复时按空 StorageName 找不到行（实测踩坑）。
            var id = (int)_fsql.Insert(item).ExecuteIdentity();
            if (id <= 0)
                throw new FileManagerException("写入回收站记录失败");

            var storage = $"{id}_{name}";
            var dest = Path.Combine(RootDir, storage);
            item.Id = id;
            item.StorageName = storage;

            try
            {
                if (isDirectory) Directory.Move(fullPath, dest);
                else File.Move(fullPath, dest);

                // 只有移动成功才落 StorageName，失败时整行删除（见 catch）
                _fsql.Update<FileRecycleItem>(id)
                    .Set(r => r.StorageName, storage)
                    .ExecuteAffrows();
            }
            catch
            {
                // 移动失败就撤销记录，避免出现「记录在但文件不在」的幽灵条目
                _fsql.Delete<FileRecycleItem>(id).ExecuteAffrows();
                throw;
            }

            EnforceQuotaAsync().ConfigureAwait(false);
            return item;
        }

        /// <summary>回收站列表。</summary>
        public RecycleBinListDto List()
        {
            var rows = _fsql.Select<FileRecycleItem>()
                .Where(r => r.StorageName != "")
                .OrderByDescending(r => r.DeletedAt)
                .ToList();

            return new RecycleBinListDto
            {
                QuotaMb = _options.RecycleBinQuotaMB,
                Count = rows.Count,
                TotalSize = rows.Sum(r => r.Size),
                Items = rows.Select(r => new RecycleBinItemDto
                {
                    Id = r.StorageName,
                    OriginalPath = r.OriginalPath,
                    Name = r.Name,
                    Size = r.Size,
                    IsDirectory = r.IsDirectory,
                    DeletedAt = AppTime.UtcNow.Kind == DateTimeKind.Utc
                        ? DateTime.SpecifyKind(r.DeletedAt, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ssZ")
                        : r.DeletedAt.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    DeletedBy = r.DeletedBy,
                }).ToList(),
            };
        }

        /// <summary>
        /// 恢复。原位置被占用时自动加 `_restored_<时间戳>` 后缀，不覆盖。
        /// </summary>
        public FileOperationResultDto Restore(string storageName, string? restoreToPath, string actor)
        {
            var row = FindRow(storageName) ?? throw new FileManagerException("回收站条目不存在（可能已被清理）");

            var src = Path.Combine(RootDir, row.StorageName);
            var target = string.IsNullOrWhiteSpace(restoreToPath)
                ? row.OriginalPath
                : _guard.ResolveForCreate(restoreToPath);

            if (File.Exists(target) || Directory.Exists(target))
            {
                var dir = Path.GetDirectoryName(target)!;
                var ext = row.IsDirectory ? string.Empty : Path.GetExtension(target);
                var stem = row.IsDirectory ? Path.GetFileName(target) : Path.GetFileNameWithoutExtension(target);
                target = Path.Combine(dir, $"{stem}_restored_{AppTime.UtcNow:yyyyMMddHHmmss}{ext}");
            }

            var parent = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);

            _guard.ResolveForCreate(target);

            try
            {
                if (row.IsDirectory) Directory.Move(src, target);
                else File.Move(src, target, overwrite: true);
            }
            catch (Exception ex)
            {
                throw new FileManagerException($"恢复失败：{ex.Message}");
            }

            _fsql.Delete<FileRecycleItem>(row.Id).ExecuteAffrows();
            _logger.LogInformation("回收站恢复 {Name} -> {Target} by {Actor}", row.Name, target, actor);
            return new FileOperationResultDto { Path = _guard.ToDisplayPath(target), Message = "恢复成功" };
        }

        /// <summary>永久删除单条。</summary>
        public void PurgeOne(string storageName, string actor)
        {
            var row = FindRow(storageName) ?? throw new FileManagerException("回收站条目不存在");
            DeleteOnDisk(Path.Combine(RootDir, row.StorageName), row.IsDirectory);
            _fsql.Delete<FileRecycleItem>(row.Id).ExecuteAffrows();
            _logger.LogWarning("回收站永久删除 {Name}（{Path}） by {Actor}", row.Name, row.OriginalPath, actor);
        }

        /// <summary>清空回收站。</summary>
        public int PurgeAll(string actor)
        {
            var rows = _fsql.Select<FileRecycleItem>().ToList();
            foreach (var row in rows)
            {
                try { DeleteOnDisk(Path.Combine(RootDir, row.StorageName), row.IsDirectory); }
                catch (Exception ex) { _logger.LogWarning(ex, "清空回收站时删除 {Name} 失败", row.Name); }
            }
            _fsql.Delete<FileRecycleItem>().Where(_ => true).ExecuteAffrows();
            _logger.LogWarning("回收站已清空（{Count} 条） by {Actor}", rows.Count, actor);
            return rows.Count;
        }

        /// <summary>按保留期清理过期条目。可挂后台任务定时调。</summary>
        public int PurgeExpired()
        {
            if (_options.RecycleBinRetentionDays <= 0) return 0;
            var deadline = AppTime.UtcNow.AddDays(-_options.RecycleBinRetentionDays);
            var rows = _fsql.Select<FileRecycleItem>().Where(r => r.DeletedAt < deadline).ToList();
            foreach (var row in rows)
            {
                try
                {
                    DeleteOnDisk(Path.Combine(RootDir, row.StorageName), row.IsDirectory);
                    _fsql.Delete<FileRecycleItem>(row.Id).ExecuteAffrows();
                }
                catch (Exception ex) { _logger.LogWarning(ex, "清理过期回收站条目 {Name} 失败", row.Name); }
            }
            if (rows.Count > 0) _logger.LogInformation("回收站清理过期条目 {Count} 条", rows.Count);
            return rows.Count;
        }

        /// <summary>超配额时从最旧开始清。</summary>
        private void EnforceQuota()
        {
            if (_options.RecycleBinQuotaMB <= 0) return;
            var limit = _options.RecycleBinQuotaMB * 1024L * 1024L;

            var rows = _fsql.Select<FileRecycleItem>().OrderByDescending(r => r.DeletedAt).ToList();
            var total = rows.Sum(r => r.Size);
            if (total <= limit) return;

            // 从最旧的开始删，直到回到配额内
            foreach (var row in rows.AsEnumerable().Reverse())
            {
                if (total <= limit) break;
                try
                {
                    DeleteOnDisk(Path.Combine(RootDir, row.StorageName), row.IsDirectory);
                    _fsql.Delete<FileRecycleItem>(row.Id).ExecuteAffrows();
                    total -= row.Size;
                    _logger.LogInformation("回收站超额，清理最旧条目 {Name}（{Size} 字节）", row.Name, row.Size);
                }
                catch (Exception ex) { _logger.LogWarning(ex, "回收站超额清理 {Name} 失败", row.Name); }
            }
        }

        private Task EnforceQuotaAsync()
        {
            try { EnforceQuota(); }
            catch (Exception ex) { _logger.LogWarning(ex, "回收站配额检查失败（不影响删除结果）"); }
            return Task.CompletedTask;
        }

        private FileRecycleItem? FindRow(string storageName)
        {
            var name = Path.GetFileName(storageName);
            return _fsql.Select<FileRecycleItem>().Where(r => r.StorageName == name).First();
        }

        private void DeleteOnDisk(string path, bool isDirectory)
        {
            if (isDirectory)
            {
                if (Directory.Exists(path))
                {
                    foreach (var f in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                    {
                        try { File.SetAttributes(f, FileAttributes.Normal); } catch { }
                    }
                    Directory.Delete(path, recursive: true);
                }
            }
            else if (File.Exists(path))
            {
                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);
            }
        }
    }
}
